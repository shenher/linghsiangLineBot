using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using LineBot.Api.Ai;
using LineBot.Api.Ai.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineBot.Api.Tests.Ai;

/// <summary>
/// 針對 OllamaChatClient（Cloud／Local 共用的 HTTP 呼叫邏輯）做整合層級的驗證，
/// 重點是確認 Polly 的 Timeout 策略真的有生效——這是開發計劃 Phase 2「Cloud timeout → 降階」
/// 這個驗收條件在「實際發送 HTTP 請求」這一層的對應測試（AiResponderChainTests 驗證的是鏈上的降階邏輯本身）。
/// </summary>
public class OllamaChatClientTests
{
    private static HttpClient CreateClient(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://fake-ollama.test/api/") };

    [Fact]
    public async Task CallTakesLongerThanBudget_ThrowsTimeoutFailure()
    {
        // Handler 故意拖 2 秒才回應，但我們只給 200 毫秒的時間預算，驗證真的會提早中斷而不是傻等 2 秒。
        using var handler = new DelayingHttpMessageHandler(
            TimeSpan.FromSeconds(2),
            () => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(handler);

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<AiProviderFailedException>(() =>
            OllamaChatClient.SendChatAsync(
                client,
                model: "test-model",
                systemPrompt: "system",
                userMessage: "user",
                providerName: "TestProvider",
                timeout: TimeSpan.FromMilliseconds(200),
                structuredOutputFormat: null,
                logger: NullLogger.Instance,
                cancellationToken: CancellationToken.None));
        stopwatch.Stop();

        Assert.Equal(AiFailureReason.Timeout, exception.Reason);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"應該在時間預算附近就中斷，實際耗時 {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Http429_ThrowsRateLimitedFailure()
    {
        using var handler = new DelayingHttpMessageHandler(
            TimeSpan.Zero,
            () => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<AiProviderFailedException>(() =>
            OllamaChatClient.SendChatAsync(
                client, "test-model", "system", "user", "TestProvider",
                TimeSpan.FromSeconds(5), structuredOutputFormat: null, NullLogger.Instance, CancellationToken.None));

        Assert.Equal(AiFailureReason.RateLimited, exception.Reason);
    }

    [Fact]
    public async Task EmptyContent_ThrowsEmptyResponseFailure()
    {
        using var handler = new DelayingHttpMessageHandler(TimeSpan.Zero, () =>
        {
            var payload = new OllamaChatResponse
            {
                Done = true,
                Message = new OllamaResponseMessage { Role = "assistant", Content = "" },
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        });
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<AiProviderFailedException>(() =>
            OllamaChatClient.SendChatAsync(
                client, "test-model", "system", "user", "TestProvider",
                TimeSpan.FromSeconds(5), structuredOutputFormat: null, NullLogger.Instance, CancellationToken.None));

        Assert.Equal(AiFailureReason.EmptyResponse, exception.Reason);
    }

    [Fact]
    public async Task SuccessfulResponse_ReturnsContent()
    {
        using var handler = new DelayingHttpMessageHandler(TimeSpan.Zero, () =>
        {
            var payload = new OllamaChatResponse
            {
                Done = true,
                Message = new OllamaResponseMessage { Role = "assistant", Content = "你好，這是測試回覆" },
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        });
        using var client = CreateClient(handler);

        var result = await OllamaChatClient.SendChatAsync(
            client, "test-model", "system", "user", "TestProvider",
            TimeSpan.FromSeconds(5), structuredOutputFormat: null, NullLogger.Instance, CancellationToken.None);

        Assert.Equal("你好，這是測試回覆", result);
    }
}
