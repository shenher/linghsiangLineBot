using System.Text.Json;

namespace LineBot.Api.Ai;

/// <summary>
/// 本專案固定要求模型以「離題判斷 + 回覆內容」的 JSON 格式作答（見 Phase 5、appsettings 的 system prompt 樣板），
/// 所以每一次呼叫都固定帶上 Ollama 的寬鬆 JSON 模式（"format": "json"），提高小模型輸出合法 JSON 的機率。
/// 官方文件：https://github.com/ollama/ollama/blob/main/docs/capabilities/structured-outputs.mdx
///
/// 這裡選擇「寬鬆 JSON 模式」而非帶完整 JSON Schema，是因為開發計劃 Phase 5 已明確提醒：
/// 地端 7B–8B 小模型對嚴格 Schema 的支援度不一定穩定，與其要求嚴格符合 Schema 導致地端模型直接失敗，
/// 不如只要求「合法 JSON」，實際欄位是否存在改由應用層（見 AiJsonReplyParser）用 fail-open 的方式寬容解析。
/// </summary>
internal static class OllamaFormats
{
    public static readonly JsonElement Json = JsonSerializer.SerializeToElement("json");
}
