using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LineBot.Api.Moderation;

/// <summary>
/// <see cref="IBlocklistStore"/> 的 SQLite 實作。
///
/// 為什麼用 SQLite 而不是純記憶體：開發計劃明確要求「不要用純記憶體」——
/// 容器重啟後黑名單就會消失，惡意使用者只要讓容器重啟（或等自然重啟）就能無限重來，等於沒有封鎖效果。
/// SQLite 資料庫檔案掛載在 Docker volume（見 Phase 6 的 docker-compose.yml），重建容器不影響資料。
///
/// 為什麼直接用 Microsoft.Data.Sqlite 手刻 SQL，而不是 EF Core：
/// 只有一張表、四種簡單查詢，EF Core 的 DbContext／Migration 對這個規模來說是不必要的重量級依賴，
/// 開發計劃本身也把「Microsoft.Data.Sqlite 或 EF Core」列為擇一即可的選項。
/// </summary>
public sealed class SqliteBlocklistStore : IBlocklistStore
{
    private readonly string _connectionString;
    private readonly ModerationOptions _options;
    private readonly ILogger<SqliteBlocklistStore> _logger;

    // 用來確保「建表」這個動作只需要成功執行一次；用 SemaphoreSlim 而非 lock，
    // 是因為裡面要 await 非同步的資料庫呼叫，一般的 lock 不允許在鎖定範圍內 await。
    private readonly SemaphoreSlim _schemaInitLock = new(1, 1);
    private bool _schemaInitialized;

    public SqliteBlocklistStore(IOptions<ModerationOptions> options, ILogger<SqliteBlocklistStore> logger)
    {
        _options = options.Value;
        _logger = logger;

        var fullPath = Path.IsPathRooted(_options.DatabasePath)
            ? _options.DatabasePath
            : Path.Combine(AppContext.BaseDirectory, _options.DatabasePath);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ConnectionString;
    }

    public async Task<bool> IsBlockedAsync(string subjectId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT IsBlocked FROM Blocklist WHERE UserId = $userId;";
        command.Parameters.AddWithValue("$userId", subjectId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long isBlocked && isBlocked != 0;
    }

    public async Task<OffTopicRegistrationResult> RegisterOffTopicAsync(string subjectId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var previousCount = 0;
        var wasBlocked = false;

        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT ConsecutiveOffTopicCount, IsBlocked FROM Blocklist WHERE UserId = $userId;";
            select.Parameters.AddWithValue("$userId", subjectId);

            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                previousCount = checked((int)reader.GetInt64(0));
                wasBlocked = reader.GetInt64(1) != 0;
            }
        }

        var newCount = previousCount + 1;
        // wasBlocked 理論上不會發生：已封鎖的使用者在 Webhook endpoint 那一層就會被攔截、根本不會走到這裡，
        // 這裡仍保留判斷是為了「防禦性寫法」，避免未來程式改動不小心繞過前面的黑名單檢查時，封鎖狀態被意外撤銷。
        var shouldBlock = wasBlocked || newCount >= _options.BlockAfterConsecutiveOffTopicCount;
        var justBlocked = !wasBlocked && shouldBlock;
        var nowIso = DateTimeOffset.UtcNow.ToString("O");

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO Blocklist (UserId, ConsecutiveOffTopicCount, IsBlocked, BlockedAt, LastMessageAt)
                VALUES ($userId, $count, $blocked, $blockedAt, $lastMessageAt)
                ON CONFLICT(UserId) DO UPDATE SET
                    ConsecutiveOffTopicCount = excluded.ConsecutiveOffTopicCount,
                    IsBlocked = excluded.IsBlocked,
                    BlockedAt = COALESCE(Blocklist.BlockedAt, excluded.BlockedAt),
                    LastMessageAt = excluded.LastMessageAt;
                """;
            upsert.Parameters.AddWithValue("$userId", subjectId);
            upsert.Parameters.AddWithValue("$count", newCount);
            upsert.Parameters.AddWithValue("$blocked", shouldBlock ? 1 : 0);
            upsert.Parameters.AddWithValue("$blockedAt", justBlocked ? nowIso : (object)DBNull.Value);
            upsert.Parameters.AddWithValue("$lastMessageAt", nowIso);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        if (justBlocked)
        {
            // 依開發計劃：封鎖事件要寫 log，方便日後回頭檢查誤判率。
            // （觸發封鎖的訊息內容由呼叫端 ReplyBackgroundService 一併記錄，這裡只記錄封鎖這件事本身。）
            _logger.LogWarning("使用者 {SubjectId} 連續離題達 {Count} 次，已加入黑名單", subjectId, newCount);
        }

        return new OffTopicRegistrationResult(newCount, justBlocked);
    }

    public async Task ResetOffTopicCounterAsync(string subjectId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // 刻意不去動 IsBlocked／BlockedAt：這個方法只會在 onTopic = true 時呼叫，
        // 而已封鎖的使用者根本不會走到這裡（Webhook endpoint 已提早攔截），所以這裡沒有「不小心解封」的風險，
        // 但仍保留 ON CONFLICT 只更新計數與時間戳，語意上更清楚地表達「這個方法不負責解封」。
        command.CommandText = """
            INSERT INTO Blocklist (UserId, ConsecutiveOffTopicCount, IsBlocked, BlockedAt, LastMessageAt)
            VALUES ($userId, 0, 0, NULL, $lastMessageAt)
            ON CONFLICT(UserId) DO UPDATE SET
                ConsecutiveOffTopicCount = 0,
                LastMessageAt = excluded.LastMessageAt;
            """;
        command.Parameters.AddWithValue("$userId", subjectId);
        command.Parameters.AddWithValue("$lastMessageAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UnblockAsync(string subjectId, CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Blocklist
            SET IsBlocked = 0, ConsecutiveOffTopicCount = 0, BlockedAt = NULL
            WHERE UserId = $userId;
            """;
        command.Parameters.AddWithValue("$userId", subjectId);

        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation(
            "管理端解封請求：UserId={SubjectId}，影響筆數={AffectedRows}", subjectId, affectedRows);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        if (_schemaInitialized)
        {
            return;
        }

        await _schemaInitLock.WaitAsync(cancellationToken);
        try
        {
            if (_schemaInitialized)
            {
                return;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken);

            await using (var pragma = connection.CreateCommand())
            {
                // WAL（Write-Ahead Logging）模式：讀取不會被寫入鎖住，較適合「背景服務持續寫入、
                // 管理端 API 偶爾讀取／解封」這種並發情境，也是 SQLite 官方建議的一般用途設定。
                pragma.CommandText = "PRAGMA journal_mode = 'WAL';";
                await pragma.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var createTable = connection.CreateCommand())
            {
                createTable.CommandText = """
                    CREATE TABLE IF NOT EXISTS Blocklist (
                        UserId TEXT PRIMARY KEY NOT NULL,
                        ConsecutiveOffTopicCount INTEGER NOT NULL DEFAULT 0,
                        IsBlocked INTEGER NOT NULL DEFAULT 0,
                        BlockedAt TEXT NULL,
                        LastMessageAt TEXT NOT NULL
                    );
                    """;
                await createTable.ExecuteNonQueryAsync(cancellationToken);
            }

            _schemaInitialized = true;
            _logger.LogInformation("黑名單資料庫已就緒：{ConnectionString}", _connectionString);
        }
        finally
        {
            _schemaInitLock.Release();
        }
    }
}
