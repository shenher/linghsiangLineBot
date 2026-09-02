namespace LineBot.Api.Ai;

/// <summary>
/// 造成單一 AI provider 被視為「這次不可用、該降階」的原因分類。
/// 對應開發計劃 Phase 2 列出的四種降階條件，額外加上 <see cref="UnexpectedError"/> 涵蓋其餘未預期狀況，
/// 確保任何一個 provider 出狀況都會確實觸發降階，而不是讓例外整個往上炸掉、跳過還沒試過的備援 provider。
/// </summary>
public enum AiFailureReason
{
    /// <summary>HTTP 429，額度或速率上限。</summary>
    RateLimited,

    /// <summary>HTTP 5xx，伺服器端錯誤。</summary>
    ServerError,

    /// <summary>呼叫超過該 provider 被分配到的時間預算（雲端 15 秒／地端 25 秒，可於 appsettings 調整）。</summary>
    Timeout,

    /// <summary>連線失敗（DNS 解析失敗、對方拒絕連線等），連 HTTP 回應都沒收到。</summary>
    ConnectionFailed,

    /// <summary>HTTP 呼叫成功，但模型回傳的內容是空字串。</summary>
    EmptyResponse,

    /// <summary>其他未列在開發計劃內的狀況（如回應不是合法 JSON、非預期狀態碼），保守起見同樣視為該 provider 失敗。</summary>
    UnexpectedError,
}

/// <summary>
/// 代表「單一 AI provider 這次呼叫失敗」，由 <see cref="AiResponderChain"/> 攔截後決定要不要換下一個 provider。
/// 這是「預期內、會被上層處理」的例外，不代表程式有 bug。
/// </summary>
public sealed class AiProviderFailedException : Exception
{
    public string ProviderName { get; }

    public AiFailureReason Reason { get; }

    public AiProviderFailedException(string providerName, AiFailureReason reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderName = providerName;
        Reason = reason;
    }
}

/// <summary>
/// 代表「所有 AI provider 都已嘗試過且全部失敗」，由 <see cref="AiResponderChain"/> 拋出。
/// 上層（背景處理服務，Phase 3）收到這個例外時，會改回覆固定訊息「AI維護中請稍後再試」。
/// </summary>
public sealed class AiUnavailableException : Exception
{
    public AiUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
