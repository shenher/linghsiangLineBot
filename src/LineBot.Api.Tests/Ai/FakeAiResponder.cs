using LineBot.Api.Ai;

namespace LineBot.Api.Tests.Ai;

/// <summary>
/// 測試用的假 IAiResponder，不牽涉真正的 HTTP 呼叫，用來單獨驗證 <see cref="AiResponderChain"/>
/// 「該不該降階到下一個 provider」的邏輯是否正確。
/// </summary>
internal sealed class FakeAiResponder : IAiResponder
{
    private readonly Func<CancellationToken, Task<string>> _behavior;

    public string Name { get; }

    public int CallCount { get; private set; }

    public FakeAiResponder(string name, Func<CancellationToken, Task<string>> behavior)
    {
        Name = name;
        _behavior = behavior;
    }

    /// <summary>建立一個「呼叫後直接成功回傳指定文字」的假 responder。</summary>
    public static FakeAiResponder Success(string name, string result) =>
        new(name, _ => Task.FromResult(result));

    /// <summary>建立一個「呼叫後拋出指定降階原因」的假 responder。</summary>
    public static FakeAiResponder Fails(string name, AiFailureReason reason) =>
        new(name, _ => throw new AiProviderFailedException(name, reason, $"{name} 模擬失敗：{reason}"));

    /// <summary>建立一個「先花一小段時間才拋出 Timeout 降階原因」的假 responder，模擬呼叫逾時。</summary>
    public static FakeAiResponder TimesOut(string name, TimeSpan delayBeforeFailing) =>
        new(name, async _ =>
        {
            await Task.Delay(delayBeforeFailing);
            throw new AiProviderFailedException(name, AiFailureReason.Timeout, $"{name} 模擬逾時");
        });

    public Task<string> GenerateAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken)
    {
        CallCount++;
        return _behavior(cancellationToken);
    }
}
