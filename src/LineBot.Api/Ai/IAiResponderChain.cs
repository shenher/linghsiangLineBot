namespace LineBot.Api.Ai;

/// <summary>
/// 依優先序嘗試多個 <see cref="IAiResponder"/>，任一個成功就回傳結果；全部失敗才對外拋出例外。
/// 這是 <see cref="ReplyBackgroundService"/> 真正會注入使用的服務，本身不直接實作 <see cref="IAiResponder"/>，
/// 避免它被 DI 用 IEnumerable&lt;IAiResponder&gt; 解析時把自己也算進去、造成無窮遞迴。
/// </summary>
public interface IAiResponderChain
{
    /// <exception cref="AiUnavailableException">所有已註冊的 provider 都失敗時拋出。</exception>
    Task<string> GenerateAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken);
}
