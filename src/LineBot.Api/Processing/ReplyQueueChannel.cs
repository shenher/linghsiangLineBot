using System.Threading.Channels;

namespace LineBot.Api.Processing;

/// <summary>
/// 用 <see cref="Channel{T}"/> 實作的記憶體內佇列。
/// 註冊為 Singleton：對外只暴露 <see cref="IReplyQueue"/>（寫入端）給 Webhook endpoint 用，
/// <see cref="ReplyBackgroundService"/> 則直接注入這個具體類別來取得 <see cref="Reader"/>（讀取端）。
///
/// 為什麼不用資料庫或外部佇列（如 Redis）：
/// 這是單一容器、單一 process 的小型服務，佇列內容也不需要跨重啟保存
/// （重啟期間漏掉的訊息，使用者頂多重傳一次，不像黑名單那樣「一定要保存」）。
/// Channel 是 .NET 內建、無外部相依、效能足夠的做法。
/// </summary>
public sealed class ReplyQueueChannel : IReplyQueue
{
    private readonly Channel<ReplyJob> _channel = Channel.CreateUnbounded<ReplyJob>(new UnboundedChannelOptions
    {
        // 只有一個 ReplyBackgroundService 實例在讀。
        SingleReader = true,
        // 可能同時有多個 Webhook 請求（多個 HTTP 連線）在寫入。
        SingleWriter = false,
    });

    /// <summary>供 <see cref="ReplyBackgroundService"/> 讀取工作項目。</summary>
    public ChannelReader<ReplyJob> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(ReplyJob job, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(job, cancellationToken);
}
