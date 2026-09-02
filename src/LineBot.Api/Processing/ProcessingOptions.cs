namespace LineBot.Api.Processing;

/// <summary>對應 appsettings.json 的 "Processing" 區塊。</summary>
public sealed class ProcessingOptions
{
    public const string SectionName = "Processing";

    /// <summary>
    /// 從收到 Webhook 事件開始算起，整個「產生回覆並送出」流程的總時間預算（秒）。
    /// 開發計劃固定訂為 45 秒——因為不使用 Push Message，所有回覆都必須在 Reply Token 過期前送出，
    /// 而 Token 有效期社群實測約 1 分鐘，45 秒是留了安全餘裕後的數字。
    /// </summary>
    public int TotalTimeBudgetSeconds { get; set; } = 45;

    public TimeSpan TotalTimeBudget => TimeSpan.FromSeconds(TotalTimeBudgetSeconds);
}
