using System.Text.Json.Serialization;

namespace LineBot.Api.Line.Models;

/// <summary>
/// LINE Webhook 送過來的最外層 JSON 結構。
/// 官方文件：https://developers.line.biz/en/reference/messaging-api/#request-body
/// </summary>
public sealed class WebhookPayload
{
    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    [JsonPropertyName("events")]
    public List<WebhookEvent> Events { get; set; } = [];
}

/// <summary>
/// 單一事件。本專案（依開發計劃 Phase 1）只處理 type == "message" 且 message.type == "text" 的事件，
/// 其他型別（如 follow、unfollow、貼圖、圖片訊息等）一律忽略、直接回 200。
/// </summary>
public sealed class WebhookEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 回覆用的一次性 Token，社群實測約 1 分鐘內有效。只能用一次，過期或用過就不能再拿來 Reply。
    /// follow/unfollow 等事件也會帶 replyToken，但本專案不處理那些事件類型。
    /// </summary>
    [JsonPropertyName("replyToken")]
    public string? ReplyToken { get; set; }

    [JsonPropertyName("source")]
    public EventSource? Source { get; set; }

    [JsonPropertyName("message")]
    public EventMessage? Message { get; set; }

    /// <summary>事件時間戳（Unix milliseconds），目前僅供 log 使用。</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

/// <summary>
/// 事件來源。type 可能是 "user"（一對一聊天）、"group"（群組）或 "room"（多人聊天室）。
/// 只有 "user" 時 userId 一定存在；group / room 事件底下的 userId 則不一定會帶（要看該成員是否已加好友）。
/// </summary>
public sealed class EventSource
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("groupId")]
    public string? GroupId { get; set; }

    [JsonPropertyName("roomId")]
    public string? RoomId { get; set; }

    /// <summary>是否為一對一聊天（決定能不能呼叫 loading animation，該功能僅支援一對一）。</summary>
    [JsonIgnore]
    public bool IsOneOnOne => Type == "user";
}

/// <summary>訊息內容本體，本專案只讀取 type == "text" 時的 Text 欄位。</summary>
public sealed class EventMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
