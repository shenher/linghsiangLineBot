using System.Security.Cryptography;
using System.Text;
using LineBot.Api.Moderation;
using Microsoft.Extensions.Options;

namespace LineBot.Api.Endpoints;

/// <summary>
/// 管理用端點：目前只有一個解封 API，對應開發計劃 Phase 5「解封機制（必做）」。
/// LINE 沒有辦法從機器人端把使用者「加回好友」或撤銷封鎖，所以這個黑名單完全是本服務自己的狀態，
/// 誤判時只能靠這個端點，或直接用 SQLite CLI 改資料庫（見開發計劃的備案說明）。
/// </summary>
public static class AdminEndpoints
{
    private const string ApiKeyHeaderName = "X-Admin-Api-Key";

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/admin/blocklist/{userId}", HandleUnblockAsync);
        return app;
    }

    private static async Task<IResult> HandleUnblockAsync(
        string userId,
        HttpRequest request,
        IOptions<AdminOptions> adminOptions,
        IBlocklistStore blocklist,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var configuredKey = adminOptions.Value.ApiKey;
        if (string.IsNullOrEmpty(configuredKey))
        {
            // 沒設定 API Key 時寧可直接拒絕所有請求，也不要「忘記設定」變成「管理端點對外無密碼開放」。
            logger.LogWarning("管理端點被呼叫，但尚未設定 Admin:ApiKey，已拒絕此次請求");
            return Results.Problem(
                "管理端點尚未設定 API Key，請設定 appsettings 的 Admin:ApiKey 或環境變數 ADMIN_API_KEY 後再試",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var providedKey = request.Headers[ApiKeyHeaderName].ToString();
        if (!IsValidApiKey(providedKey, configuredKey))
        {
            logger.LogWarning(
                "管理端點驗證失敗，來源 IP：{RemoteIp}", request.HttpContext.Connection.RemoteIpAddress);
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.BadRequest(new { message = "userId 不可為空" });
        }

        await blocklist.UnblockAsync(userId, cancellationToken);
        return Results.Ok(new { message = $"已解除封鎖：{userId}" });
    }

    /// <summary>用固定時間比較（<see cref="CryptographicOperations.FixedTimeEquals"/>）驗證 API Key，避免時序攻擊。</summary>
    private static bool IsValidApiKey(string providedKey, string configuredKey)
    {
        if (string.IsNullOrEmpty(providedKey))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedKey);
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);

        // 長度不同時 FixedTimeEquals 會直接拋例外，這裡先手動排除；長度本身外洩的時序資訊風險極低，可接受。
        if (providedBytes.Length != configuredBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }
}
