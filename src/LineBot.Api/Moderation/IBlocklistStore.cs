namespace LineBot.Api.Moderation;

/// <summary>
/// 黑名單／連續離題計數的存放介面。實作見 <see cref="SqliteBlocklistStore"/>。
///
/// 重要前提（開發計劃 Phase 5）：LINE Messaging API 沒有「封鎖使用者」的端點，
/// 所謂封鎖純粹是本服務自己維護的名單——命中黑名單時，Webhook endpoint 驗證簽章後直接回 200、
/// 不呼叫 AI、不顯示 loading、也不送任何回覆，對使用者而言就是「已讀不回」。
/// </summary>
public interface IBlocklistStore
{
    /// <summary>查詢這個使用者目前是否在黑名單中。</summary>
    Task<bool> IsBlockedAsync(string subjectId, CancellationToken cancellationToken);

    /// <summary>
    /// 使用者這次的提問被判定為「與店家無關」時呼叫。
    /// 會把連續離題計數 +1；若達到 3 次（含）就同時標記為封鎖。
    /// </summary>
    /// <returns>更新後的連續離題次數，以及這次呼叫是否讓使用者「剛好」被封鎖。</returns>
    Task<OffTopicRegistrationResult> RegisterOffTopicAsync(string subjectId, CancellationToken cancellationToken);

    /// <summary>
    /// 使用者這次的提問被判定為「與店家相關」時呼叫，把連續離題計數歸零。
    /// 計數規則是「連續」——只要中間問過一次相關問題就要重新從 0 開始算。
    /// </summary>
    Task ResetOffTopicCounterAsync(string subjectId, CancellationToken cancellationToken);

    /// <summary>管理用解封：清除黑名單狀態並把連續離題計數歸零。</summary>
    Task UnblockAsync(string subjectId, CancellationToken cancellationToken);
}

/// <param name="ConsecutiveOffTopicCount">更新後的連續離題次數。</param>
/// <param name="JustBlocked">這次呼叫是否讓使用者從「未封鎖」變成「已封鎖」（計數剛好達到 3）。</param>
public readonly record struct OffTopicRegistrationResult(int ConsecutiveOffTopicCount, bool JustBlocked);
