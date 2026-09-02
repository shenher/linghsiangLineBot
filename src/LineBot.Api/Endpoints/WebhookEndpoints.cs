using System.Text.Json;
using LineBot.Api.Line;
using LineBot.Api.Line.Models;
using LineBot.Api.Moderation;
using LineBot.Api.Processing;

namespace LineBot.Api.Endpoints;

/// <summary>
/// 註冊 <c>POST /webhook</c>，LINE 平台會把所有事件（訊息、加好友、封鎖…）都送到這個單一端點。
/// </summary>
public static class WebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhook", HandleWebhookAsync);
        return app;
    }

    private static async Task<IResult> HandleWebhookAsync(
        HttpRequest request,
        LineSignatureValidator signatureValidator,
        IBlocklistStore blocklist,
        ILineMessagingClient lineClient,
        IReplyQueue queue,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        // === 步驟 1：讀取「原始」body bytes ===
        // 一定要在做任何 JSON 解析之前先把 body 讀成 byte[]，簽章驗證必須用這份未經加工的原始資料，
        // 反序列化再序列化回去的字串，位元組不保證跟原始請求一模一樣（屬性順序、跳脫字元都可能不同）。
        using var bodyStream = new MemoryStream();
        await request.Body.CopyToAsync(bodyStream, cancellationToken);
        var rawBody = bodyStream.ToArray();

        // === 步驟 2：驗證簽章 ===
        var signature = request.Headers["X-Line-Signature"].ToString();
        if (!signatureValidator.IsValid(rawBody, signature))
        {
            logger.LogWarning(
                "Webhook 簽章驗證失敗，來源 IP：{RemoteIp}",
                request.HttpContext.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        // === 步驟 3：解析事件 ===
        WebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<WebhookPayload>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            // 簽章已驗證通過但 body 卻不是合法 JSON，理論上不該發生。
            // 依規範「無論處理結果如何，對 LINE 一律快速回 200」，這裡也不例外，只記錄 log。
            logger.LogWarning(ex, "Webhook 簽章驗證通過，但 body 不是合法 JSON");
            return Results.Ok();
        }

        if (payload?.Events is not { Count: > 0 })
        {
            return Results.Ok();
        }

        // === 步驟 4：逐一處理事件 ===
        // 每個事件各自獨立 try/catch，避免其中一則訊息處理出錯（例如格式異常）連累同一批的其他事件。
        foreach (var webhookEvent in payload.Events)
        {
            try
            {
                await HandleSingleEventAsync(webhookEvent, blocklist, lineClient, queue, logger, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "處理單一 Webhook 事件時發生未預期例外");
            }
        }

        // === 步驟 5：一律回 200 ===
        // LINE 不在乎回應內容，只要求盡快收到 2xx；真正的 AI 回覆已經丟進背景佇列非同步處理。
        return Results.Ok();
    }

    private static async Task HandleSingleEventAsync(
        WebhookEvent webhookEvent,
        IBlocklistStore blocklist,
        ILineMessagingClient lineClient,
        IReplyQueue queue,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // 只處理「文字訊息」事件，其餘型別（貼圖、圖片、加好友、封鎖…）直接忽略。
        if (webhookEvent.Type != "message" ||
            webhookEvent.Message is not { Type: "text", Text.Length: > 0 } message ||
            webhookEvent.ReplyToken is not { Length: > 0 } replyToken ||
            webhookEvent.Source is not { } source)
        {
            return;
        }

        // 辨識「這是誰」：一對一聊天用 userId；群組/多人聊天室拿不到 userId 時退回用 groupId/roomId
        // （開發計劃沒有明確定義群組情境下的黑名單規則，這是保守解讀，確保至少該聊天室可被辨識與計數）。
        var subjectId = source.UserId ?? source.GroupId ?? source.RoomId;
        if (subjectId is null)
        {
            logger.LogWarning("事件缺少可辨識的 Source Id（userId/groupId/roomId 皆為空），略過");
            return;
        }

        // 黑名單命中：驗證簽章後直接結束，不呼叫 AI、不顯示 loading、不送任何回覆——對使用者就是已讀不回。
        if (await blocklist.IsBlockedAsync(subjectId, cancellationToken))
        {
            return;
        }

        if (source.IsOneOnOne)
        {
            // Loading 動畫僅支援一對一聊天。用上限 60 秒，搭配 Phase 3 的 45 秒總時間預算仍有餘裕，
            // 呼叫失敗也不影響後續流程（ILineMessagingClient 內部已處理）。
            await lineClient.StartLoadingAsync(subjectId, loadingSeconds: 60, cancellationToken);
        }

        var job = new ReplyJob(
            ReplyToken: replyToken,
            SubjectId: subjectId,
            UserMessageText: message.Text!,
            IsOneOnOne: source.IsOneOnOne,
            ReceivedAtUtc: DateTimeOffset.UtcNow);

        await queue.EnqueueAsync(job, cancellationToken);
    }
}
