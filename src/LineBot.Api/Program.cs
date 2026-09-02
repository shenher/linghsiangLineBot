using System.Net;
using System.Net.Http.Headers;
using LineBot.Api.Ai;
using LineBot.Api.Endpoints;
using LineBot.Api.Knowledge;
using LineBot.Api.Line;
using LineBot.Api.Moderation;
using LineBot.Api.Processing;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// =====================================================================================
// 憑證設定：正式環境用 Kestrel 直接監聽 443 並掛載 pfx 憑證
// -------------------------------------------------------------------------------------
// 這一段的寫法參考自 shenher/linghsiang 專案（ASP.NET Core MVC，部署在同一台 Mac mini、
// 由 Kestrel 直接對外提供 HTTPS，不透過額外的反向代理終止 TLS）的 Program.cs，並轉換成
// .NET 10 Web API（Minimal Hosting）的寫法。
//
// 轉換說明：WebApplicationBuilder 與 IWebHostBuilder.ConfigureKestrel 這一層屬於「Generic Host」
// 的基礎設施，ASP.NET Core MVC 專案與 Minimal API／Web API 專案是共用的，所以憑證載入這段邏輯
// 本身幾乎不需要改寫；真正需要轉換的是原專案接下來的 AddControllersWithViews／Cookie 驗證／
// MapControllerRoute 等 MVC 專屬設定——本專案完全沒有 View 或後台登入頁面，
// 全部改用下方的 Minimal API 端點（app.MapXxx(...)，實作見 Endpoints/ 資料夾）取代。
// =====================================================================================
if (!builder.Environment.IsDevelopment())
{
    var certPath = Path.Combine(AppContext.BaseDirectory, "certs", "cert.pfx");
    var configPassword = builder.Configuration["CertificatePassword"];

    // 憑證密碼優先讀取 appsettings 的 CertificatePassword；
    // 未設定時退回讀取環境變數 CERT_PASSWORD（Docker Compose 由 .env 檔注入，見 docker-compose.yml）。
    // 兩者都沒有就直接讓服務啟動失敗，避免「用空密碼默默啟動一個打不開的憑證」這種難以排查的狀況。
    var certPassword = !string.IsNullOrWhiteSpace(configPassword)
        ? configPassword
        : Environment.GetEnvironmentVariable("CERT_PASSWORD")
          ?? throw new InvalidOperationException(
              "憑證密碼未設定。請在 appsettings 設定 'CertificatePassword'，或設定環境變數 CERT_PASSWORD。");

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Listen(IPAddress.Any, 443, listenOptions =>
        {
            listenOptions.UseHttps(certPath, certPassword);
        });
    });
}

// =====================================================================================
// 服務註冊（Dependency Injection）
// =====================================================================================

builder.Services.AddOpenApi();

// ---- 各 Phase 的設定區塊（Options Pattern，一一對應 appsettings.json 的區段）----
builder.Services.Configure<LineOptions>(builder.Configuration.GetSection(LineOptions.SectionName));
builder.Services.Configure<OllamaSettings>(builder.Configuration.GetSection(OllamaSettings.SectionName));
builder.Services.Configure<KnowledgeOptions>(builder.Configuration.GetSection(KnowledgeOptions.SectionName));
builder.Services.Configure<ModerationOptions>(builder.Configuration.GetSection(ModerationOptions.SectionName));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<ProcessingOptions>(builder.Configuration.GetSection(ProcessingOptions.SectionName));

// ---- Phase 1：LINE 簽章驗證與 Messaging API Client ----
builder.Services.AddSingleton<LineSignatureValidator>();
builder.Services.AddHttpClient<ILineMessagingClient, LineMessagingClient>((sp, http) =>
{
    var line = sp.GetRequiredService<IOptions<LineOptions>>().Value;
    http.BaseAddress = new Uri("https://api.line.me/");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", line.ChannelAccessToken);
});

// ---- Phase 2：AI 抽象層與降階 chain ----
// Cloud／Local 用具名 HttpClient 各自設定 BaseAddress／驗證標頭，實際 HTTP 呼叫邏輯共用 OllamaChatClient。
builder.Services.AddHttpClient(OllamaHttpClientNames.Cloud, (sp, http) =>
{
    var cloud = sp.GetRequiredService<IOptions<OllamaSettings>>().Value.Cloud;
    http.BaseAddress = new Uri(cloud.BaseUrl);
    if (!string.IsNullOrWhiteSpace(cloud.ApiKey))
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cloud.ApiKey);
    }
});
builder.Services.AddHttpClient(OllamaHttpClientNames.Local, (sp, http) =>
{
    var local = sp.GetRequiredService<IOptions<OllamaSettings>>().Value.Local;
    http.BaseAddress = new Uri(local.BaseUrl);
});

// 注意註冊順序：AiResponderChain 是用 IEnumerable<IAiResponder> 依「DI 註冊順序」依序嘗試，
// Cloud 一定要比 Local 先註冊，才符合開發計劃「雲端優先、地端降階」的優先序。
builder.Services.AddSingleton<OllamaCloudResponder>();
builder.Services.AddSingleton<IAiResponder>(sp => sp.GetRequiredService<OllamaCloudResponder>());
builder.Services.AddSingleton<LocalOllamaResponder>();
builder.Services.AddSingleton<IAiResponder>(sp => sp.GetRequiredService<LocalOllamaResponder>());
builder.Services.AddSingleton<IAiResponderChain, AiResponderChain>();

// ---- Phase 4：業務知識載入 ----
builder.Services.AddSingleton<IKnowledgeService, KnowledgeService>();

// ---- Phase 5：離題偵測與黑名單 ----
builder.Services.AddSingleton<IBlocklistStore, SqliteBlocklistStore>();

// ---- Phase 3：背景佇列與處理服務 ----
builder.Services.AddSingleton<ReplyQueueChannel>();
builder.Services.AddSingleton<IReplyQueue>(sp => sp.GetRequiredService<ReplyQueueChannel>());
builder.Services.AddHostedService<ReplyBackgroundService>();

var app = builder.Build();

// 啟動防呆：先把店家知識檔案讀過一次。
// KnowledgeService 內部已經處理好「檔案不存在／讀取失敗就退回預設 prompt」，這裡呼叫純粹是想讓
// 這類警告在「服務啟動當下」的 log 就先被看到，而不是等第一位顧客傳訊息、背景服務才第一次觸發讀取。
await app.Services.GetRequiredService<IKnowledgeService>().GetKnowledgeMarkdownAsync(CancellationToken.None);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    // 未預期例外一律轉成統一的 JSON 錯誤格式，不要把例外堆疊洩漏給呼叫端。
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "internal_server_error" });
    }));
    app.UseHsts();
}

app.UseHttpsRedirection();

// 這是純 JSON API、沒有任何會渲染 HTML 的頁面，因此只加上與 API 情境相關的安全性標頭
// （X-Frame-Options、CSP 等主要是防禦「被嵌入惡意網頁」的攻擊面，對純 API 沒有意義，故不加）。
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

// ---- Endpoints ----
app.MapWebhookEndpoints();
app.MapAdminEndpoints();

// 簡單的健康檢查端點，方便 docker-compose healthcheck 或人工確認服務是否還活著。
app.MapGet("/", () => Results.Ok(new { status = "ok", service = "linghsiang-line-bot" }));

app.Run();
