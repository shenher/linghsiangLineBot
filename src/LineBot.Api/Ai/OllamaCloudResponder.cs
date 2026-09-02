using Microsoft.Extensions.Options;

namespace LineBot.Api.Ai;

/// <summary>優先序 1：Ollama Cloud，使用免費額度，需要 API Key。</summary>
public sealed class OllamaCloudResponder : IAiResponder
{
    private readonly HttpClient _httpClient;
    private readonly OllamaCloudOptions _options;
    private readonly ILogger<OllamaCloudResponder> _logger;

    public string Name => "OllamaCloud（雲端）";

    public OllamaCloudResponder(
        IHttpClientFactory httpClientFactory,
        IOptions<OllamaSettings> settings,
        ILogger<OllamaCloudResponder> logger)
    {
        _httpClient = httpClientFactory.CreateClient(OllamaHttpClientNames.Cloud);
        _options = settings.Value.Cloud;
        _logger = logger;
    }

    public Task<string> GenerateAsync(string userMessage, string systemPrompt, CancellationToken cancellationToken)
        => OllamaChatClient.SendChatAsync(
            _httpClient,
            _options.Model,
            systemPrompt,
            userMessage,
            Name,
            TimeSpan.FromSeconds(_options.TimeoutSeconds),
            OllamaFormats.Json,
            _logger,
            cancellationToken);
}
