using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LineBot.Api.Ai.Models;
using Polly;
using Polly.Timeout;

namespace LineBot.Api.Ai;

/// <summary>
/// Ollama Cloud 與地端 Ollama 共用的 HTTP 呼叫實作。
/// 兩個 Responder（<see cref="OllamaCloudResponder"/>、<see cref="LocalOllamaResponder"/>）
/// 只差 HttpClient 的 BaseAddress／Authorization 標頭與時間預算，實際「怎麼呼叫、怎麼判斷成功失敗」
/// 完全相同，因此抽成這個靜態類別共用，避免同一段邏輯維護兩份。
/// </summary>
internal static class OllamaChatClient
{
    /// <summary>
    /// 呼叫 /api/chat 並回傳模型的文字內容。
    /// 失敗一律轉成 <see cref="AiProviderFailedException"/>（哪種原因見 <see cref="AiFailureReason"/>），
    /// 讓 <see cref="AiResponderChain"/> 可以用同一種方式判斷「該不該降階」，不用分別處理各種例外型別。
    /// </summary>
    public static async Task<string> SendChatAsync(
        HttpClient httpClient,
        string model,
        string systemPrompt,
        string userMessage,
        string providerName,
        TimeSpan timeout,
        JsonElement? structuredOutputFormat,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var request = new OllamaChatRequest
        {
            Model = model,
            Stream = false,
            Format = structuredOutputFormat,
            Messages =
            [
                new OllamaChatMessage("system", systemPrompt),
                new OllamaChatMessage("user", userMessage),
            ],
        };

        // 用 Polly 的 Timeout 策略幫這個 provider 的時間預算把關（雲端 15 秒／地端 25 秒，可調整）。
        // 這裡刻意「不」加入 Retry 策略：開發計劃的降階設計（cloud 失敗馬上換 local）本質上已經是一種重試，
        // 若在單一 provider 內部再重試一次，只會多消耗時間、擠壓整體 45 秒的回覆時間預算。
        var pipeline = new ResiliencePipelineBuilder().AddTimeout(timeout).Build();

        try
        {
            return await pipeline.ExecuteAsync(
                async token =>
                {
                    using var response = await httpClient.PostAsJsonAsync("chat", request, token);
                    return await ParseSuccessOrThrowAsync(response, providerName, token);
                },
                cancellationToken);
        }
        catch (TimeoutRejectedException ex)
        {
            // Polly 的 Timeout 策略在時間到時，是拋出這個型別，而不是 OperationCanceledException。
            throw new AiProviderFailedException(
                providerName, AiFailureReason.Timeout, $"{providerName} 呼叫逾時（時間預算 {timeout.TotalSeconds} 秒）", ex);
        }
        catch (HttpRequestException ex)
        {
            // 連 HTTP 回應都沒收到：DNS 解析失敗、對方拒絕連線、TLS 握手失敗等連線層級的問題。
            throw new AiProviderFailedException(
                providerName, AiFailureReason.ConnectionFailed, $"{providerName} 連線失敗：{ex.Message}", ex);
        }
    }

    private static async Task<string> ParseSuccessOrThrowAsync(
        HttpResponseMessage response, string providerName, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new AiProviderFailedException(
                providerName, AiFailureReason.RateLimited, $"{providerName} 回應 429（額度或速率已達上限）");
        }

        if ((int)response.StatusCode >= 500)
        {
            throw new AiProviderFailedException(
                providerName, AiFailureReason.ServerError, $"{providerName} 回應 HTTP {(int)response.StatusCode}");
        }

        if (!response.IsSuccessStatusCode)
        {
            // 其餘非成功狀態碼（如 400 參數錯誤、401 憑證錯誤）不在開發計劃列出的四種降階條件內，
            // 但這個 provider 既然回應不了正確結果，保守起見同樣視為失敗、觸發降階比讓整個請求掛掉更安全。
            throw new AiProviderFailedException(
                providerName, AiFailureReason.UnexpectedError, $"{providerName} 回應非預期狀態碼 HTTP {(int)response.StatusCode}");
        }

        OllamaChatResponse? parsed;
        try
        {
            parsed = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new AiProviderFailedException(
                providerName, AiFailureReason.UnexpectedError, $"{providerName} 回應內容無法解析為 JSON", ex);
        }

        var content = parsed?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AiProviderFailedException(providerName, AiFailureReason.EmptyResponse, $"{providerName} 回應內容為空字串");
        }

        return content;
    }
}
