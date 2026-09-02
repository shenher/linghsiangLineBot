namespace LineBot.Api.Line;

/// <summary>
/// 對應 appsettings.json 的 "Line" 區塊。
/// 兩個值都是機密資訊：正式環境建議用環境變數或 user-secrets 覆蓋，不要寫進版控的 appsettings.json。
/// </summary>
public sealed class LineOptions
{
    /// <summary>appsettings 設定區段名稱，供 builder.Services.Configure 使用。</summary>
    public const string SectionName = "Line";

    /// <summary>
    /// Channel Secret：LINE Developers Console → Basic settings 頁籤取得。
    /// 用途：驗證 Webhook 請求的 X-Line-Signature（HMAC-SHA256 的 key）。
    /// </summary>
    public string ChannelSecret { get; set; } = string.Empty;

    /// <summary>
    /// Channel Access Token（long-lived）：LINE Developers Console → Messaging API 頁籤取得。
    /// 用途：呼叫 Messaging API（回覆訊息、顯示載入動畫）時的 Bearer Token。
    /// </summary>
    public string ChannelAccessToken { get; set; } = string.Empty;
}
