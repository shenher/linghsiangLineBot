namespace LineBot.Api.Tests.Ai;

/// <summary>
/// 測試用的 HttpMessageHandler：故意拖延一段時間才回應，藉此驗證 Polly 的 Timeout 策略
/// 是否真的能在時間預算到了之後把呼叫中斷，而不是傻等 HttpClient 的預設 timeout（100 秒）。
/// </summary>
internal sealed class DelayingHttpMessageHandler : HttpMessageHandler
{
    private readonly TimeSpan _delay;
    private readonly Func<HttpResponseMessage> _responseFactory;

    public DelayingHttpMessageHandler(TimeSpan delay, Func<HttpResponseMessage> responseFactory)
    {
        _delay = delay;
        _responseFactory = responseFactory;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 一定要把 cancellationToken 傳進 Task.Delay，這樣 Polly 觸發逾時取消時，
        // 這個「假的慢速呼叫」才會真的被中斷，而不是繼續空等到 _delay 跑完。
        await Task.Delay(_delay, cancellationToken);
        return _responseFactory();
    }
}
