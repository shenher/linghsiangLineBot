namespace LineBot.Api.Ai;

/// <summary>
/// 對應 appsettings.json 的 "Ollama" 區塊。Cloud 與 Local 共用同一套 Ollama Chat API 介面，
/// 只差 BaseUrl、要不要帶 API Key、以及各自的時間預算，因此兩者的 HTTP 呼叫邏輯共用同一份實作
/// （見 <see cref="OllamaChatClient"/>），這裡只是把「哪裡不同」拆成兩組設定值。
/// </summary>
public sealed class OllamaSettings
{
    public const string SectionName = "Ollama";

    public OllamaCloudOptions Cloud { get; set; } = new();

    public OllamaLocalOptions Local { get; set; } = new();
}

public sealed class OllamaCloudOptions
{
    /// <summary>
    /// Ollama Cloud 的官方 API 根路徑。
    /// 注意：開發計劃文件（line-bot-plan.md）中寫的是 https://api.ollama.com，
    /// 但依官方文件 https://docs.ollama.com/api/introduction 核實，正確路徑其實是 https://ollama.com/api，
    /// 這裡已按官方文件校正。
    /// </summary>
    public string BaseUrl { get; set; } = "https://ollama.com/api/";

    /// <summary>Ollama Cloud 的 API Key，會放進 Authorization: Bearer 標頭。機密資訊，正式環境用環境變數注入。</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>要使用的雲端模型名稱，依 Ollama Cloud 實際提供的免費模型調整。</summary>
    public string Model { get; set; } = "gpt-oss:20b";

    /// <summary>這個 provider 分配到的時間預算（秒）。對應開發計劃 Phase 3 的 15 秒。</summary>
    public int TimeoutSeconds { get; set; } = 15;
}

public sealed class OllamaLocalOptions
{
    /// <summary>
    /// 地端 Ollama 的 API 根路徑。本機開發用 http://localhost:11434/api/；
    /// 部署到 Mac mini 上的 Docker 容器時，因為容器與 host 是不同網路命名空間，
    /// 要改成 http://host.docker.internal:11434/api/ 才能連到 host 上跑的 Ollama（見 docker-compose.yml 註解）。
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/api/";

    /// <summary>要使用的地端模型名稱，建議 7B–8B 量化模型（Mac mini 16GB RAM 可負擔的大小）。</summary>
    public string Model { get; set; } = "llama3.1:8b";

    /// <summary>這個 provider 分配到的時間預算（秒）。對應開發計劃 Phase 3 的 25 秒。</summary>
    public int TimeoutSeconds { get; set; } = 25;
}
