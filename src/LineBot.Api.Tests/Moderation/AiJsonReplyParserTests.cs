using LineBot.Api.Moderation;

namespace LineBot.Api.Tests.Moderation;

/// <summary>
/// 對應開發計劃 Phase 5 驗收標準：「AI 回傳非 JSON 時 → 不封鎖、不計數，仍嘗試正常回覆」。
/// </summary>
public class AiJsonReplyParserTests
{
    [Fact]
    public void ValidJson_OnTopicTrue_ParsesReplyText()
    {
        var result = AiJsonReplyParser.Parse("""{"onTopic": true, "reply": "我們營業時間是 10:00-20:00"}""");

        Assert.True(result.OnTopic);
        Assert.Equal("我們營業時間是 10:00-20:00", result.ReplyText);
    }

    [Fact]
    public void ValidJson_OnTopicFalse_ReturnsFalse()
    {
        var result = AiJsonReplyParser.Parse("""{"onTopic": false, "reply": ""}""");

        Assert.False(result.OnTopic);
    }

    [Fact]
    public void JsonWrappedInMarkdownFence_StillParses()
    {
        var raw = "```json\n{\"onTopic\": true, \"reply\": \"沒問題\"}\n```";

        var result = AiJsonReplyParser.Parse(raw);

        Assert.True(result.OnTopic);
        Assert.Equal("沒問題", result.ReplyText);
    }

    [Fact]
    public void NotJsonAtAll_FailsOpenAsOnTopicAndUsesRawTextAsReply()
    {
        const string raw = "這是一段模型忘記包成 JSON、直接純文字回答的內容";

        var result = AiJsonReplyParser.Parse(raw);

        Assert.True(result.OnTopic);
        Assert.Equal(raw, result.ReplyText);
    }

    [Fact]
    public void MissingOnTopicField_FailsOpen()
    {
        var result = AiJsonReplyParser.Parse("""{"reply": "缺少 onTopic 欄位"}""");

        Assert.True(result.OnTopic);
    }
}
