namespace LineBot.Api.Line;

/// <summary>
/// 封裝本專案唯二會用到的 LINE Messaging API 端點：回覆訊息、顯示載入動畫。
/// 刻意不提供 Push Message 的方法——依開發計劃的硬性限制，本專案永遠不能呼叫 Push，
/// 從介面設計上直接讓「誤用 Push」這件事變得不可能發生，比事後 code review 檢查更可靠。
/// </summary>
public interface ILineMessagingClient
{
    /// <summary>
    /// 呼叫 /v2/bot/message/reply 送出純文字回覆。
    /// </summary>
    /// <returns>
    /// true 表示 LINE 回應 2xx（訊息確實送出）；
    /// false 表示送出失敗（常見原因：replyToken 已過期或已使用過一次）。
    /// 呼叫端（背景處理服務）需要這個結果來判斷「連固定訊息都送不出去」時只記 log、不做任何補送。
    /// </returns>
    Task<bool> ReplyAsync(string replyToken, string text, CancellationToken cancellationToken);

    /// <summary>
    /// 呼叫 /v2/bot/chat/loading/start 顯示「輸入中」動畫。僅支援一對一聊天。
    /// 呼叫失敗不會拋例外（依開發計劃：不影響主流程），內部已自行記錄 log。
    /// </summary>
    /// <param name="userId">對方的 userId，即 loading 動畫請求中的 chatId。</param>
    /// <param name="loadingSeconds">動畫顯示秒數，必須是 5 的倍數、介於 5–60。</param>
    Task StartLoadingAsync(string userId, int loadingSeconds, CancellationToken cancellationToken);
}
