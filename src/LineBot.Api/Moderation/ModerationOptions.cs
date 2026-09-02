namespace LineBot.Api.Moderation;

/// <summary>對應 appsettings.json 的 "Moderation" 區塊。</summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    /// <summary>
    /// 連續離題達到這個次數（含）就封鎖。開發計劃的驗收標準是 3 次，開放調整以因應未來需求變化。
    /// </summary>
    public int BlockAfterConsecutiveOffTopicCount { get; set; } = 3;

    /// <summary>離題時要回覆的固定訊息。</summary>
    public string OffTopicMessage { get; set; } = "僅回答拎香相關資訊";

    /// <summary>
    /// SQLite 資料庫檔案路徑。若不是絕對路徑，視為相對於 <see cref="AppContext.BaseDirectory"/>
    /// （容器內建議掛載到 volume，確保容器重建後黑名單資料不會消失，見 Phase 6 的 docker-compose.yml）。
    /// </summary>
    public string DatabasePath { get; set; } = "data/blocklist.db";
}
