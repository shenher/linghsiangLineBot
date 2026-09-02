namespace LineBot.Api.Processing;

/// <summary>
/// 一個「需要在背景產生 AI 回覆並送出」的工作單位。
/// Webhook endpoint 收到訊息後，只做「驗簽章 + 顯示 loading + 組出這個物件丟進佇列」就立刻回 200，
/// 真正花時間的 AI 生成與送出回覆都在 <see cref="ReplyBackgroundService"/> 裡非同步進行。
/// </summary>
/// <param name="ReplyToken">用來回覆的一次性 Token（有效期極短，約 1 分鐘）。</param>
/// <param name="SubjectId">
/// 用來辨識「這是誰」的識別碼，作為離題計數／黑名單的主鍵。
/// 一對一聊天用 userId；群組或多人聊天室在拿不到 userId 時，退回用 groupId／roomId，
/// 讓「同一個聊天室」至少還能被辨識與計數（開發計劃未明確定義群組情境下的黑名單行為，此為保守解讀）。
/// </param>
/// <param name="UserMessageText">使用者傳入的文字內容。</param>
/// <param name="IsOneOnOne">是否為一對一聊天（true 才代表 SubjectId 是真正的 userId，可用於未來若有需要區分身分的情境）。</param>
/// <param name="ReceivedAtUtc">收到 Webhook 事件的時間（UTC），用來計算 45 秒總時間預算是否超支。</param>
public sealed record ReplyJob(
    string ReplyToken,
    string SubjectId,
    string UserMessageText,
    bool IsOneOnOne,
    DateTimeOffset ReceivedAtUtc);
