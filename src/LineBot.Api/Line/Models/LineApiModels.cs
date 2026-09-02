using System.Text.Json.Serialization;

namespace LineBot.Api.Line.Models;

/// <summary>
/// POST https://api.line.me/v2/bot/message/reply 的請求 body。
/// 本專案刻意不實作 Push Message，全站只有這一種送訊息的方式。
/// </summary>
public sealed class ReplyMessageRequest
{
    [JsonPropertyName("replyToken")]
    public string ReplyToken { get; set; } = string.Empty;

    /// <summary>LINE 一次 Reply 最多可帶 5 則訊息，本專案固定只送 1 則純文字。</summary>
    [JsonPropertyName("messages")]
    public List<TextMessage> Messages { get; set; } = [];
}

/// <summary>純文字訊息物件。type 固定為 "text"，是 LINE Messaging API 的訊息物件格式之一。</summary>
public sealed class TextMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    public TextMessage()
    {
    }

    public TextMessage(string text)
    {
        Text = text;
    }
}

/// <summary>
/// POST https://api.line.me/v2/bot/chat/loading/start 的請求 body。
/// 官方文件：https://developers.line.biz/en/docs/messaging-api/use-loading-indicator/
/// 僅支援一對一聊天（chatId 為對方 userId）；群組/多人聊天室不支援，呼叫會失敗。
/// </summary>
public sealed class LoadingAnimationRequest
{
    [JsonPropertyName("chatId")]
    public string ChatId { get; set; } = string.Empty;

    /// <summary>必須是 5 的倍數，範圍 5–60，超過這段時間動畫會自動停止。</summary>
    [JsonPropertyName("loadingSeconds")]
    public int LoadingSeconds { get; set; } = 60;
}
