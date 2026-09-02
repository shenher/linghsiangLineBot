# linghsiangLineBot

拎香LINE AI機器人

以 .NET 10 ASP.NET Core Web API 實作的 LINE 官方帳號 AI 客服機器人。
完整設計理念與各階段驗收標準請見 [`line-bot-plan.md`](./line-bot-plan.md)；本文件只說明「怎麼跑起來」。

## 專案結構

```
src/
├── LineBot.Api/           # 主程式（.NET 10 Minimal API）
│   ├── Line/               # Phase 1：簽章驗證、LINE Messaging API client、Webhook models
│   ├── Ai/                 # Phase 2：IAiResponder、Ollama Cloud/Local、降階 chain
│   ├── Processing/         # Phase 3：背景佇列、45 秒時間預算處理
│   ├── Knowledge/          # Phase 4：business.md 快取、system prompt 組裝
│   ├── Moderation/         # Phase 5：離題判斷、SQLite 黑名單、管理端點
│   └── Endpoints/          # Minimal API 端點：/webhook、/admin/blocklist/{userId}
└── LineBot.Api.Tests/      # xUnit 單元測試
business.md.example         # 店家知識檔案範本（實際內容放在 data/business.md，不進版控）
Dockerfile / docker-compose.yml / .env.example   # Phase 6：部署設定
```

## 本機開發

需要 .NET 10 SDK。

```bash
# 還原、建置
dotnet restore
dotnet build

# 執行單元測試
dotnet test

# 設定機密值（本機開發用 user-secrets，避免寫進 appsettings.json）
dotnet user-secrets set "Line:ChannelSecret" "xxx" --project src/LineBot.Api
dotnet user-secrets set "Line:ChannelAccessToken" "xxx" --project src/LineBot.Api
dotnet user-secrets set "Ollama:Cloud:ApiKey" "xxx" --project src/LineBot.Api
dotnet user-secrets set "Admin:ApiKey" "xxx" --project src/LineBot.Api

# 啟動（Development 環境不會啟用 443 + pfx 憑證那段邏輯，走 dotnet 內建的開發用連接埠）
dotnet run --project src/LineBot.Api
```

準備 `src/LineBot.Api/bin/Debug/net10.0/data/business.md`（可直接複製 `business.md.example` 的內容填寫）
讓 AI 有店家資訊可以回答；沒有這個檔案時服務仍會正常啟動，只是會用內建的預設 prompt。

本機若要測試 AI 降階邏輯，需要：
- 一組可用的 Ollama Cloud API Key（`Ollama:Cloud:ApiKey`），或
- 本機跑 `ollama serve`（預設 `http://localhost:11434`）

外部 LINE 平台要打進本機的 Webhook，可用 `cloudflared tunnel` 或 `ngrok` 暫時對外（見開發計劃 Phase 1）。

## 部署（Docker，Mac mini）

1. 複製 `.env.example` 為 `.env`，填入 `LINE_CHANNEL_SECRET`、`LINE_CHANNEL_ACCESS_TOKEN`、
   `OLLAMA_CLOUD_API_KEY`、`ADMIN_API_KEY`、`CERT_PASSWORD`。
2. 準備 `certs/cert.pfx`（正式環境 Kestrel 直接用這份憑證監聽 443，見 `Program.cs` 開頭的憑證設定區塊）。
3. 準備 `data/business.md`（店家知識，可不建立，會用預設 prompt）。
4. `docker compose up --build -d`

**注意**：`docker-compose.yml` 把 host 的 `8443` 對應到容器的 `443`，因為 Mac mini 上可能已經有其他網站
容器佔用了 host 的 `443`（見該檔案內註解）。請在 Cloudflare／router 另外設定一條轉發規則，
把 LINE Bot 要用的網域（例如 `linebot.<你的網域>`）指到 host 的 `8443`，再到 LINE Developers Console
填入 `https://linebot.<你的網域>/webhook` 並按 Verify。

修改 `data/business.md` 後不需要重建容器，下一次提問即會反映新內容（見 Phase 4 的快取機制）。

## 管理端點

```bash
# 解除封鎖（見 Phase 5：LINE 沒有官方的封鎖 API，黑名單完全是本服務自行維護的狀態）
curl -X DELETE https://linebot.<你的網域>/admin/blocklist/<userId> \
  -H "X-Admin-Api-Key: <ADMIN_API_KEY>"
```
