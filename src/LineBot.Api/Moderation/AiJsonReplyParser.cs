using System.Text.Json;

namespace LineBot.Api.Moderation;

/// <summary>
/// 解析 AI 依 <see cref="LineBot.Api.Knowledge.SystemPromptBuilder"/> 要求輸出的
/// <c>{"onTopic": bool, "reply": string}</c> JSON 結構。
///
/// 開發計劃 Phase 5 明確提醒過這一步的風險：地端 7B–8B 小模型的結構化輸出可靠度不高，
/// 可能回傳非 JSON、或在 JSON 前後夾帶多餘文字（例如 ```json 這種 Markdown code fence）。
/// 因此這裡的解析策略「一律 fail-open」：
/// - 解析失敗 → 視為 onTopic = true（寧可多回答，也不要誤封真實顧客——這兩種錯誤的成本並不對稱）。
/// - 解析失敗時仍然要「盡量給出有意義的回覆」，做法是把模型的原始輸出文字直接當作回覆內容，
///   而不是回覆一句「我看不懂」——多數小模型即使沒有包成嚴謹 JSON，內容本身通常還是合理的答案。
/// </summary>
public static class AiJsonReplyParser
{
    public static ParsedReply Parse(string rawModelOutput)
    {
        var jsonSlice = ExtractJsonObject(rawModelOutput);
        if (jsonSlice is not null && TryParseStructured(jsonSlice, out var parsed))
        {
            return parsed;
        }

        // Fail-open：不管是「整段都不是 JSON」還是「JSON 格式不對／欄位缺漏」，一律視為 onTopic = true。
        return new ParsedReply(OnTopic: true, ReplyText: rawModelOutput.Trim());
    }

    /// <summary>
    /// 從文字中截出第一個 <c>{</c> 到最後一個 <c>}</c> 之間的片段。
    /// 用來容忍模型在 JSON 前後多加了說明文字，或用 Markdown code fence 包住 JSON 的情況。
    /// </summary>
    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }

    private static bool TryParseStructured(string json, out ParsedReply result)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("onTopic", out var onTopicElement))
            {
                result = default;
                return false;
            }

            bool onTopic = onTopicElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                // 有些小模型會把布林值寫成字串 "true"/"false"，寬容處理這種情況。
                JsonValueKind.String when bool.TryParse(onTopicElement.GetString(), out var parsedBool) => parsedBool,
                _ => throw new FormatException("onTopic 欄位型別無法辨識"),
            };

            var replyText = root.TryGetProperty("reply", out var replyElement) && replyElement.ValueKind == JsonValueKind.String
                ? replyElement.GetString() ?? string.Empty
                : string.Empty;

            // onTopic = true 但模型忘了填 reply：退回用整段原始輸出當回覆，避免送出空白訊息給顧客。
            if (onTopic && string.IsNullOrWhiteSpace(replyText))
            {
                replyText = json.Trim();
            }

            result = new ParsedReply(onTopic, replyText);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            result = default;
            return false;
        }
    }
}

/// <param name="OnTopic">這句話是否與店家資訊相關。</param>
/// <param name="ReplyText">
/// 要回給使用者的文字。當 OnTopic 為 false 時，呼叫端應改用固定的離題訊息，
/// 而不是直接使用這個欄位（模型在離題時通常會把它留空）。
/// </param>
public readonly record struct ParsedReply(bool OnTopic, string ReplyText);
