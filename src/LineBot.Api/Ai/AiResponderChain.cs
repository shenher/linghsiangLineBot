namespace LineBot.Api.Ai;

/// <summary>
/// <see cref="IAiResponderChain"/> 的實作：依 DI 註冊順序（Program.cs 裡先註冊 Cloud、再註冊 Local）
/// 依序嘗試每一個 <see cref="IAiResponder"/>，第一個成功的結果就直接回傳，不會再呼叫後面的 provider。
/// </summary>
public sealed class AiResponderChain : IAiResponderChain
{
    private readonly IReadOnlyList<IAiResponder> _responders;
    private readonly ILogger<AiResponderChain> _logger;

    public AiResponderChain(IEnumerable<IAiResponder> responders, ILogger<AiResponderChain> logger)
    {
        _responders = responders.ToArray();
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken)
    {
        List<string>? failures = null;

        foreach (var responder in _responders)
        {
            // 若整體 45 秒時間預算已經用完（Phase 3 的外層 CancellationToken 被取消），
            // 就不用再浪費時間嘗試下一個 provider，直接讓 OperationCanceledException 往上拋。
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await responder.GenerateAsync(userMessage, systemPrompt, cancellationToken);
            }
            catch (AiProviderFailedException ex)
            {
                // 每次降階都要記錄：這是日後判斷「Ollama Cloud 免費額度到底夠不夠用、該不該升級付費方案」的依據。
                _logger.LogWarning(
                    "AI provider [{Provider}] 這次失敗（原因：{Reason}）：{Message}",
                    ex.ProviderName,
                    ex.Reason,
                    ex.Message);

                (failures ??= []).Add($"{ex.ProviderName}（{ex.Reason}）");
            }
        }

        var detail = failures is { Count: > 0 } ? string.Join("、", failures) : "沒有任何已註冊的 AI provider";
        throw new AiUnavailableException($"所有 AI provider 皆無法產生回覆：{detail}");
    }
}
