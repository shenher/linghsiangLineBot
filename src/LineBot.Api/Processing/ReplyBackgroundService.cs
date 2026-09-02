using LineBot.Api.Ai;
using LineBot.Api.Knowledge;
using LineBot.Api.Line;
using LineBot.Api.Moderation;
using Microsoft.Extensions.Options;

namespace LineBot.Api.Processing;

/// <summary>
/// 開發計劃 Phase 3 的核心：從 <see cref="ReplyQueueChannel"/> 拿出工作，
/// 在「45 秒總時間預算」內完成「組 system prompt → 呼叫 AI（含降階）→ 離題判斷／黑名單 → 送出回覆」，
/// 逾時或全部 AI provider 失敗時改送固定的「AI維護中請稍後再試」，絕不使用 Push 補送。
///
/// 本服務用到的所有相依服務都註冊為 Singleton（見 Program.cs），因此可以直接建構子注入，
/// 不需要依微軟官方文件建議的「BackgroundService 消費 Scoped 服務時要另外開 IServiceScope」那套做法
/// ——那是給 Scoped 服務用的，這裡完全不涉及 Scoped 生命週期。
/// </summary>
public sealed class ReplyBackgroundService : BackgroundService
{
    private readonly ReplyQueueChannel _queue;
    private readonly IAiResponderChain _aiChain;
    private readonly IKnowledgeService _knowledge;
    private readonly IBlocklistStore _blocklist;
    private readonly ILineMessagingClient _lineClient;
    private readonly KnowledgeOptions _knowledgeOptions;
    private readonly ModerationOptions _moderationOptions;
    private readonly ProcessingOptions _processingOptions;
    private readonly ILogger<ReplyBackgroundService> _logger;

    public ReplyBackgroundService(
        ReplyQueueChannel queue,
        IAiResponderChain aiChain,
        IKnowledgeService knowledge,
        IBlocklistStore blocklist,
        ILineMessagingClient lineClient,
        IOptions<KnowledgeOptions> knowledgeOptions,
        IOptions<ModerationOptions> moderationOptions,
        IOptions<ProcessingOptions> processingOptions,
        ILogger<ReplyBackgroundService> logger)
    {
        _queue = queue;
        _aiChain = aiChain;
        _knowledge = knowledge;
        _blocklist = blocklist;
        _lineClient = lineClient;
        _knowledgeOptions = knowledgeOptions.Value;
        _moderationOptions = moderationOptions.Value;
        _processingOptions = processingOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ReadAllAsync 會一直等到有新工作或服務停止為止，不會忙等（busy-wait）。
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            // 刻意不 await：多筆訊息要能平行處理，不能讓後面的使用者排隊等前一則訊息的 AI 呼叫做完
            // （尤其地端 fallback 最多可能花到 25 秒，序列處理很容易讓後面的 reply token 直接過期）。
            // 每個工作內部都有自己獨立的例外處理與時間預算，不會互相影響。
            _ = ProcessJobSafelyAsync(job, stoppingToken);
        }
    }

    private async Task ProcessJobSafelyAsync(ReplyJob job, CancellationToken appStoppingToken)
    {
        var elapsedSinceReceived = DateTimeOffset.UtcNow - job.ReceivedAtUtc;
        var remainingBudget = _processingOptions.TotalTimeBudget - elapsedSinceReceived;

        if (remainingBudget <= TimeSpan.Zero)
        {
            // 極端情況：工作在佇列裡排隊排到時間預算都用完了才被拿出來處理（例如佇列嚴重塞車）。
            _logger.LogWarning("工作在進入處理前就已超過 {Budget} 秒總時間預算，直接嘗試回覆固定訊息", _processingOptions.TotalTimeBudgetSeconds);
            await TrySendReplyAsync(job.ReplyToken, FixedReplies.MaintenanceMessage, appStoppingToken);
            return;
        }

        using var jobCts = CancellationTokenSource.CreateLinkedTokenSource(appStoppingToken);
        jobCts.CancelAfter(remainingBudget);

        try
        {
            await ProcessJobAsync(job, jobCts.Token);
        }
        catch (OperationCanceledException) when (appStoppingToken.IsCancellationRequested)
        {
            // 是整個應用程式要關閉了（例如容器換版），不是單一工作逾時，這種情況不強行送出任何東西。
            _logger.LogWarning("背景處理因服務關閉而中止");
        }
        catch (OperationCanceledException)
        {
            // 45 秒總時間預算用完（jobCts 自己觸發的取消）：改嘗試送出固定的維護中訊息。
            // 這裡用 appStoppingToken 而非已經取消的 jobCts.Token，確保「送這通固定訊息」本身不會被同一個逾時卡住。
            _logger.LogWarning("背景處理超過 {Budget} 秒總時間預算，改回覆固定訊息", _processingOptions.TotalTimeBudgetSeconds);
            await TrySendReplyAsync(job.ReplyToken, FixedReplies.MaintenanceMessage, appStoppingToken);
        }
        catch (Exception ex)
        {
            // 任何其他未預期例外都不能讓背景服務的整個迴圈掛掉，一律記錄並嘗試回覆固定訊息。
            _logger.LogError(ex, "背景處理發生未預期例外");
            await TrySendReplyAsync(job.ReplyToken, FixedReplies.MaintenanceMessage, appStoppingToken);
        }
    }

    private async Task ProcessJobAsync(ReplyJob job, CancellationToken cancellationToken)
    {
        var knowledgeMarkdown = await _knowledge.GetKnowledgeMarkdownAsync(cancellationToken);
        var systemPrompt = SystemPromptBuilder.Build(_knowledgeOptions.StoreName, knowledgeMarkdown);

        string rawModelOutput;
        try
        {
            rawModelOutput = await _aiChain.GenerateAsync(job.UserMessageText, systemPrompt, cancellationToken);
        }
        catch (AiUnavailableException ex)
        {
            // 依硬性限制：所有 AI 路徑都失敗時，回固定訊息，不做任何 Push 補送。
            _logger.LogError(ex, "所有 AI provider 皆失敗，改回覆固定的維護中訊息");
            await TrySendReplyAsync(job.ReplyToken, FixedReplies.MaintenanceMessage, cancellationToken);
            return;
        }

        var parsed = AiJsonReplyParser.Parse(rawModelOutput);

        string finalReplyText;
        if (parsed.OnTopic)
        {
            // 計數器規則是「連續」離題才算數，只要問到一次相關問題就要歸零。
            await _blocklist.ResetOffTopicCounterAsync(job.SubjectId, cancellationToken);
            finalReplyText = parsed.ReplyText;
        }
        else
        {
            var registration = await _blocklist.RegisterOffTopicAsync(job.SubjectId, cancellationToken);
            finalReplyText = _moderationOptions.OffTopicMessage;

            if (registration.JustBlocked)
            {
                // 依開發計劃：把觸發封鎖的訊息內容寫進 log，方便日後回頭檢查誤判率。
                _logger.LogWarning(
                    "使用者 {SubjectId} 連續離題達 {Count} 次並觸發封鎖，本次訊息內容：{Message}",
                    job.SubjectId, registration.ConsecutiveOffTopicCount, job.UserMessageText);
            }
        }

        await TrySendReplyAsync(job.ReplyToken, finalReplyText, cancellationToken);
    }

    private async Task TrySendReplyAsync(string replyToken, string text, CancellationToken cancellationToken)
    {
        try
        {
            var success = await _lineClient.ReplyAsync(replyToken, text, cancellationToken);
            if (!success)
            {
                // 依硬性限制：連這則訊息都送不出去時（通常是 replyToken 已過期或已用過），僅記錄 log，
                // 絕對不嘗試任何形式的補送（本專案完全不實作 Push Message）。
                _logger.LogWarning("回覆送出失敗（常見原因：replyToken 已過期），不會嘗試任何補送");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("送出回覆時被取消（可能是服務正在關閉），本次未送出任何內容");
        }
    }
}
