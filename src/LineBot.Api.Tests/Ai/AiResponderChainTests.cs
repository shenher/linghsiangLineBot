using System.Diagnostics;
using LineBot.Api.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace LineBot.Api.Tests.Ai;

/// <summary>
/// 對應開發計劃 Phase 2「單元測試（必做）」的四個項目。
/// </summary>
public class AiResponderChainTests
{
    private static AiResponderChain CreateChain(params IAiResponder[] responders) =>
        new(responders, NullLogger<AiResponderChain>.Instance);

    /// <summary>Cloud 回 429 → 確實改呼叫 Local。</summary>
    [Fact]
    public async Task Cloud429_FallsBackToLocal()
    {
        var cloud = FakeAiResponder.Fails("Cloud", AiFailureReason.RateLimited);
        var local = FakeAiResponder.Success("Local", "地端回覆內容");
        var chain = CreateChain(cloud, local);

        var result = await chain.GenerateAsync("使用者訊息", "system prompt", CancellationToken.None);

        Assert.Equal("地端回覆內容", result);
        Assert.Equal(1, cloud.CallCount);
        Assert.Equal(1, local.CallCount);
    }

    /// <summary>Cloud 回 200 → 不會呼叫 Local。</summary>
    [Fact]
    public async Task CloudSucceeds_DoesNotCallLocal()
    {
        var cloud = FakeAiResponder.Success("Cloud", "雲端回覆內容");
        var local = FakeAiResponder.Success("Local", "地端回覆內容（不應該出現）");
        var chain = CreateChain(cloud, local);

        var result = await chain.GenerateAsync("使用者訊息", "system prompt", CancellationToken.None);

        Assert.Equal("雲端回覆內容", result);
        Assert.Equal(1, cloud.CallCount);
        Assert.Equal(0, local.CallCount);
    }

    /// <summary>兩者皆失敗 → 拋出 AiUnavailableException。</summary>
    [Fact]
    public async Task BothFail_ThrowsAiUnavailableException()
    {
        var cloud = FakeAiResponder.Fails("Cloud", AiFailureReason.ServerError);
        var local = FakeAiResponder.Fails("Local", AiFailureReason.ConnectionFailed);
        var chain = CreateChain(cloud, local);

        var exception = await Assert.ThrowsAsync<AiUnavailableException>(
            () => chain.GenerateAsync("使用者訊息", "system prompt", CancellationToken.None));

        // 例外訊息應同時包含兩個 provider 的失敗原因，方便從 log 直接判斷發生了什麼事。
        Assert.Contains("Cloud", exception.Message);
        Assert.Contains("Local", exception.Message);
    }

    /// <summary>Cloud timeout → 降階且未超出總時間預算。</summary>
    [Fact]
    public async Task CloudTimesOut_FallsBackToLocalWithinTimeBudget()
    {
        // 用短延遲模擬「Cloud 逾時」，避免單元測試真的等到 15 秒的正式時間預算。
        var cloud = FakeAiResponder.TimesOut("Cloud", TimeSpan.FromMilliseconds(50));
        var local = FakeAiResponder.Success("Local", "地端回覆內容");
        var chain = CreateChain(cloud, local);

        // 模擬 Phase 3 背景服務加諸的 45 秒總時間預算，這裡等比縮小成 2 秒，確保降階邏輯不會忽略外層取消。
        using var overallBudget = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var stopwatch = Stopwatch.StartNew();
        var result = await chain.GenerateAsync("使用者訊息", "system prompt", overallBudget.Token);
        stopwatch.Stop();

        Assert.Equal("地端回覆內容", result);
        Assert.Equal(1, local.CallCount);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), "降階後的總耗時應遠小於外層時間預算");
    }
}
