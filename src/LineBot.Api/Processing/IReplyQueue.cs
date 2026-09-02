namespace LineBot.Api.Processing;

/// <summary>
/// Webhook endpoint 與背景服務之間的工作佇列。
/// Webhook endpoint 只負責「寫入」（Enqueue），<see cref="ReplyBackgroundService"/> 負責「讀出並處理」。
/// </summary>
public interface IReplyQueue
{
    /// <summary>將一個回覆工作放進佇列，立即返回，不等待處理完成。</summary>
    ValueTask EnqueueAsync(ReplyJob job, CancellationToken cancellationToken);
}
