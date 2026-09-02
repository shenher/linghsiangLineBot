namespace LineBot.Api.Moderation;

/// <summary>對應 appsettings.json 的 "Admin" 區塊，保護管理用端點（目前只有解封 API）。</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    /// <summary>
    /// 呼叫管理端點時，必須在 X-Admin-Api-Key 標頭帶上這組值。機密資訊，正式環境用環境變數注入。
    /// 未設定時，管理端點會直接拒絕所有請求（回 503），避免「忘記設定」變成「金鑰形同虛設」。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
