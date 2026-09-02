using Microsoft.Extensions.Options;

namespace LineBot.Api.Ai;

/// <summary>優先序 2：Mac mini 本機的 Ollama，無額度限制但速度較慢，雲端不可用時的備援。</summary>
public sealed class LocalOllamaResponder : IAiResponder
{
    private readonly HttpClient _httpClient;
    private readonly OllamaLocalOptions _options;
    private readonly ILogger<LocalOllamaResponder> _logger;

    public string Name => "LocalOllama（地端）";

    public LocalOllamaResponder(
        IHttpClientFactory httpClientFactory,
        IOptions<OllamaSettings> settings,
        ILogger<LocalOllamaResponder> logger)
    {
        _httpClient = httpClientFactory.CreateClient(OllamaHttpClientNames.Local);
        _options = settings.Value.Local;
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
