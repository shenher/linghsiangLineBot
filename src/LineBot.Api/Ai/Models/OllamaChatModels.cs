using System.Text.Json;
using System.Text.Json.Serialization;

namespace LineBot.Api.Ai.Models;

/// <summary>
/// POST /api/chat 的請求 body。Ollama Cloud 與地端 Ollama 用的是同一份 API 格式
/// （官方文件：https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-chat-completion）。
/// </summary>
public sealed class OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<OllamaChatMessage> Messages { get; set; } = [];

    /// <summary>本專案一律用非串流模式，等模型產生完整內容後一次拿回，簡化錯誤處理與逾時判斷。</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    /// <summary>
    /// 結構化輸出用的 JSON Schema（Phase 5 離題判斷會用到）。
    /// 一般問答不需要這個欄位，設為 null 時序列化直接省略，避免送出多餘的 "format": null。
    /// </summary>
    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Format { get; set; }
}

/// <summary>對話訊息物件，role 為 "system" / "user" / "assistant" 其中之一。</summary>
public sealed class OllamaChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    public OllamaChatMessage()
    {
    }

    public OllamaChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}

/// <summary>/api/chat 的回應 body（非串流模式）。</summary>
public sealed class OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("message")]
    public OllamaResponseMessage? Message { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

public sealed class OllamaResponseMessage
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
