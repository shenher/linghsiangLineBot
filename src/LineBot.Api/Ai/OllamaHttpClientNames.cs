namespace LineBot.Api.Ai;

/// <summary>
/// IHttpClientFactory 具名 HttpClient 的名稱常數，Program.cs 註冊時與 Responder 解析時共用同一組常數，
/// 避免兩邊各自手打字串、打錯字就在執行期才爆炸。
/// </summary>
public static class OllamaHttpClientNames
{
    public const string Cloud = "OllamaCloud";
    public const string Local = "OllamaLocal";
}
