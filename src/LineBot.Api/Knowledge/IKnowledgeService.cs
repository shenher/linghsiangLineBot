namespace LineBot.Api.Knowledge;

/// <summary>讀取並快取店家知識 Markdown 檔案（開發計劃 Phase 4）。</summary>
public interface IKnowledgeService
{
    /// <summary>
    /// 取得目前的店家知識內容。
    /// 檔案不存在或讀取失敗時，回傳內建的預設說明文字，絕不會拋例外、絕不會讓服務因此起不來。
    /// </summary>
    Task<string> GetKnowledgeMarkdownAsync(CancellationToken cancellationToken);
}
