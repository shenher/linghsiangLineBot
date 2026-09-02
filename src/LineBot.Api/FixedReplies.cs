namespace LineBot.Api;

/// <summary>
/// 開發計劃明確規定的固定回覆文字。這些文字之所以放在程式碼常數而非 appsettings，
/// 是因為它們是「硬性限制」的一部分（見 line-bot-plan.md 開頭的限制表），不是可以隨意調整的營運文案。
/// </summary>
public static class FixedReplies
{
    /// <summary>
    /// 所有 AI 路徑都失敗時的固定回覆。
    /// 依硬性限制：這句話送不出去時（reply token 已過期）也絕對不做任何 Push 補送。
    /// </summary>
    public const string MaintenanceMessage = "AI維護中請稍後再試";
}
