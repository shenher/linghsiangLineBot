using System.Text;
using Microsoft.Extensions.Options;

namespace LineBot.Api.Knowledge;

/// <summary>
/// <see cref="IKnowledgeService"/> 的實作：把 business.md 的內容快取在記憶體裡，
/// 用 <see cref="File.GetLastWriteTimeUtc(string)"/> 比對檔案異動時間，只有真的變更過才重新讀檔。
///
/// 為什麼不用 FileSystemWatcher：開發計劃已經提醒過，Docker volume 掛載的檔案系統事件有時不可靠
/// （bind mount 在某些環境下不會正確觸發 inotify 事件），用「每次請求前比對 mtime」雖然多一次
/// <see cref="File.GetLastWriteTimeUtc(string)"/> 呼叫，但這個系統呼叫成本極低，且結果穩定可預期。
///
/// 註冊為 Singleton，內部用 <see cref="SemaphoreSlim"/> 序列化重新讀取的動作，
/// 避免多個並發請求同時偵測到「檔案變更了」而一起搶著讀檔。
/// </summary>
public sealed class KnowledgeService : IKnowledgeService
{
    private const string DefaultKnowledgeMarkdown =
        "（目前尚未提供店家詳細資訊。請客氣地告訴顧客：這部分需要幫忙確認，稍後會由專人回覆，不要自行編造任何店家資訊。）";

    private readonly KnowledgeOptions _options;
    private readonly ILogger<KnowledgeService> _logger;
    private readonly string _fullPath;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);

    private string _cachedContent = DefaultKnowledgeMarkdown;
    private DateTime _cachedWriteTimeUtc = DateTime.MinValue;
    private bool _hasLoadedOnce;

    public KnowledgeService(IOptions<KnowledgeOptions> options, ILogger<KnowledgeService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _fullPath = Path.IsPathRooted(_options.FilePath)
            ? _options.FilePath
            : Path.Combine(AppContext.BaseDirectory, _options.FilePath);
    }

    public async Task<string> GetKnowledgeMarkdownAsync(CancellationToken cancellationToken)
    {
        // ReloadOnChange = false 時，第一次讀完之後就永遠沿用快取，省去之後每次的檔案系統呼叫。
        if (_hasLoadedOnce && !_options.ReloadOnChange)
        {
            return _cachedContent;
        }

        await _reloadLock.WaitAsync(cancellationToken);
        try
        {
            // 進到鎖之後再檢查一次，避免多個並發呼叫在等鎖期間，鎖釋放後又重複讀一次檔。
            if (_hasLoadedOnce && !_options.ReloadOnChange)
            {
                return _cachedContent;
            }

            if (!File.Exists(_fullPath))
            {
                LogIfChangedToDefault();
                _cachedContent = DefaultKnowledgeMarkdown;
                _cachedWriteTimeUtc = DateTime.MinValue;
                _hasLoadedOnce = true;
                return _cachedContent;
            }

            var writeTimeUtc = File.GetLastWriteTimeUtc(_fullPath);
            if (_hasLoadedOnce && writeTimeUtc == _cachedWriteTimeUtc)
            {
                // 檔案存在、但異動時間跟上次讀到的一樣，代表內容沒變，不必重新讀取。
                return _cachedContent;
            }

            try
            {
                // 指定 UTF-8 讀取；StreamReader（File.ReadAllTextAsync 底層用的）預設就會自動偵測並跳過 BOM。
                var content = await File.ReadAllTextAsync(_fullPath, Encoding.UTF8, cancellationToken);

                if (content.Length > _options.RecommendedMaxLength)
                {
                    _logger.LogWarning(
                        "店家知識檔案 {Path} 長度為 {Length} 字，超過建議上限 {Max} 字，" +
                        "地端小模型的 context window 有限，建議精簡內容以確保回覆品質",
                        _fullPath, content.Length, _options.RecommendedMaxLength);
                }

                _cachedContent = content;
                _cachedWriteTimeUtc = writeTimeUtc;
                _hasLoadedOnce = true;
                _logger.LogInformation("已（重新）載入店家知識檔案 {Path}，長度 {Length} 字", _fullPath, content.Length);
            }
            catch (IOException ex)
            {
                // 讀取失敗（例如檔案正被寫入中、權限問題）：不讓服務因此掛掉，回退到內建預設 prompt。
                _logger.LogWarning(ex, "讀取店家知識檔案 {Path} 失敗，改用內建的預設 system prompt", _fullPath);
                _cachedContent = DefaultKnowledgeMarkdown;
                _cachedWriteTimeUtc = DateTime.MinValue;
                _hasLoadedOnce = true;
            }

            return _cachedContent;
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private void LogIfChangedToDefault()
    {
        if (_cachedContent != DefaultKnowledgeMarkdown)
        {
            _logger.LogWarning("找不到店家知識檔案 {Path}（可能尚未建立或已被刪除），改用內建的預設 system prompt", _fullPath);
        }
    }
}
