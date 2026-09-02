namespace LineBot.Api.Ai;

/// <summary>
/// 單一 AI 推論來源的抽象介面（開發計劃 Phase 2 的核心設計）。
/// 目前有兩個實作：<see cref="OllamaCloudResponder"/>（優先）與 <see cref="LocalOllamaResponder"/>（降階備援），
/// 由 <see cref="AiResponderChain"/> 依序嘗試。
/// </summary>
public interface IAiResponder
{
    /// <summary>供 log 與例外訊息辨識用的名稱，例如「OllamaCloud（雲端）」。</summary>
    string Name { get; }

    /// <summary>
    /// 呼叫這個 provider 產生回覆。
    /// </summary>
    /// <param name="userMessage">使用者傳入的訊息文字。</param>
    /// <param name="systemPrompt">組裝好的 system prompt（含店家知識與回覆規則，見 Phase 4）。</param>
    /// <exception cref="AiProviderFailedException">
    /// 遇到 HTTP 429、5xx、逾時、連線失敗、或回應為空字串時拋出，代表這個 provider「這次」不可用，
    /// <see cref="AiResponderChain"/> 收到後會改嘗試下一個 provider，而不會讓整個請求失敗。
    /// </exception>
    Task<string> GenerateAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken);
}
