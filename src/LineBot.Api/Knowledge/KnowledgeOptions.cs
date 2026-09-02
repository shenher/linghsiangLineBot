namespace LineBot.Api.Knowledge;

/// <summary>對應 appsettings.json 的 "Knowledge" 區塊。</summary>
public sealed class KnowledgeOptions
{
    public const string SectionName = "Knowledge";

    /// <summary>
    /// 店家知識 Markdown 檔案路徑。若不是絕對路徑，會視為相對於 <see cref="AppContext.BaseDirectory"/>
    /// （容器內即 /app/business.md，對應開發計劃 Phase 6 的 volume 掛載路徑）。
    /// </summary>
    public string FilePath { get; set; } = "business.md";

    /// <summary>
    /// 是否每次都比對檔案異動時間、有變才重新讀取。設為 false 時只會在服務啟動後第一次用到時讀一次，
    /// 之後永遠沿用記憶體中的快取（適合完全不會改內容的部署情境，可省去每次都問檔案系統的開銷）。
    /// </summary>
    public bool ReloadOnChange { get; set; } = true;

    /// <summary>組裝進 system prompt 時要用的店名。</summary>
    public string StoreName { get; set; } = "拎香";

    /// <summary>
    /// 建議的內容長度上限（字元數）。地端小模型的 context window 有限，超過這個長度只會記警告 log，
    /// 不會拒絕啟動或截斷內容——截斷店家資訊可能造成答案缺漏，交由老闆娘自行決定要不要精簡。
    /// </summary>
    public int RecommendedMaxLength { get; set; } = 2000;
}
