using System.Net.Http.Json;
using LineBot.Api.Line.Models;

namespace LineBot.Api.Line;

/// <summary>
/// <see cref="ILineMessagingClient"/> 的實作。
/// 這個類別本身不處理 HttpClient 的 BaseAddress／Authorization 設定，
/// 那些是在 Program.cs 用 builder.Services.AddHttpClient(...) 統一設定（見該檔案註解），
/// 這裡只放「呼叫哪個路徑、body 長什麼樣子、怎麼判斷成功失敗」的邏輯。
/// </summary>
public sealed class LineMessagingClient : ILineMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<LineMessagingClient> _logger;

    public LineMessagingClient(HttpClient httpClient, ILogger<LineMessagingClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> ReplyAsync(string replyToken, string text, CancellationToken cancellationToken)
    {
        var request = new ReplyMessageRequest
        {
            ReplyToken = replyToken,
            Messages = [new TextMessage(text)],
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v2/bot/message/reply", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // 常見會落在這裡的原因：replyToken 已過期（社群實測約 1 分鐘）或已經被用掉一次。
            // 這是預期內會發生的情況，不算系統錯誤，用 Warning 等級記錄即可，不用 Error。
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "LINE Reply API 回應非成功狀態碼：{StatusCode}，內容：{Body}",
                response.StatusCode,
                body);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "呼叫 LINE Reply API 時發生例外");
            return false;
        }
    }

    public async Task StartLoadingAsync(string userId, int loadingSeconds, CancellationToken cancellationToken)
    {
        var request = new LoadingAnimationRequest
        {
            ChatId = userId,
            LoadingSeconds = loadingSeconds,
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v2/bot/chat/loading/start", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "LINE Loading Animation API 回應非成功狀態碼：{StatusCode}，內容：{Body}",
                    response.StatusCode,
                    body);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 依開發計劃：loading 動畫呼叫失敗不影響主流程，這裡吞掉例外、只記 log。
            _logger.LogWarning(ex, "呼叫 LINE Loading Animation API 時發生例外，已忽略並繼續處理");
        }
    }
}
