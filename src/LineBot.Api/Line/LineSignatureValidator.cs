using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace LineBot.Api.Line;

/// <summary>
/// 驗證 LINE Webhook 請求的 X-Line-Signature 標頭。
///
/// LINE 平台在送出 Webhook 時，會用「Channel Secret 當 HMAC-SHA256 的 key」對 request body 做簽章，
/// 並把結果做 Base64 編碼後放進 X-Line-Signature 標頭。我們收到請求時要用同樣的方式重新計算一次簽章，
/// 兩者相同才代表這個請求真的來自 LINE，而不是任何人假造 POST 過來的。
///
/// 重點（開發計劃 Phase 1 明確要求）：
/// 1. 一定要用「原始（raw）body bytes」計算，不能先反序列化成物件、再序列化回字串——
///    因為 JSON 屬性順序、空白、跳脫字元只要跟原始 bytes 有一點點不同，簽章就會對不起來。
/// 2. 用 <see cref="CryptographicOperations.FixedTimeEquals"/> 而非直接比較位元組陣列，
///    避免時序攻擊（timing attack）洩漏簽章比對到第幾個 byte 才失敗。
/// </summary>
public sealed class LineSignatureValidator
{
    private readonly LineOptions _options;

    public LineSignatureValidator(IOptions<LineOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// 驗證簽章是否正確。
    /// </summary>
    /// <param name="rawRequestBody">HTTP 請求的原始 body bytes（尚未做任何解析）。</param>
    /// <param name="signatureHeaderValue">X-Line-Signature 標頭的原始值（Base64 字串）。</param>
    public bool IsValid(ReadOnlySpan<byte> rawRequestBody, string? signatureHeaderValue)
    {
        if (string.IsNullOrEmpty(signatureHeaderValue))
        {
            return false;
        }

        if (string.IsNullOrEmpty(_options.ChannelSecret))
        {
            // Channel Secret 沒設定時一律視為驗證失敗，避免「空 key 也能算出一個簽章」這種誤用情境。
            return false;
        }

        var keyBytes = Encoding.UTF8.GetBytes(_options.ChannelSecret);
        var computedHash = HMACSHA256.HashData(keyBytes, rawRequestBody);

        // 把送過來的 Base64 字串轉回 bytes 再比對，比「兩邊都轉成字串比對」更嚴謹，
        // 也能順便擋掉「簽章字串本身不是合法 Base64」的畸形請求。
        Span<byte> receivedHash = stackalloc byte[64];
        if (!Convert.TryFromBase64String(signatureHeaderValue, receivedHash, out var bytesWritten))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(computedHash, receivedHash[..bytesWritten]);
    }
}
