# LINE 官方帳號 AI 客服機器人 — 開發計劃

> 目標：以 ASP.NET Core Web API 實作 LINE 官方帳號的 AI 自動回覆機器人，
> 部署於自有 Mac mini，AI 推論以「免費雲端優先、地端降階」為原則，零額外訊息費用。

---

## 0. 前提與限制

### 已具備資源
- 自有網域（已設定 Cloudflare DNS）
- Mac mini（Docker + Ollama 本機推論）
- 家用固定 IP、router port forwarding 已完成

### 硬性限制（設計時必須遵守）
| 限制 | 說明 |
|---|---|
| **不使用 Push Message** | Push 會扣官方帳號月額度／計費。本專案一律只用 Reply Message |
| **Reply Token 一次性且有時效** | 社群實測約 1 分鐘內有效，LINE 官方未公布精確秒數 |
| **Webhook 必須 HTTPS** | LINE 平台只會打 HTTPS endpoint |
| **失敗一律回固定訊息** | 所有 AI 路徑都失敗時，回傳 `AI維護中請稍後再試`，不做任何 Push 補送 |
| **離題一律回固定訊息** | 與店家無關的問題回傳 `僅回答拎香相關資訊`，連續 3 次即封鎖 |

### 技術選型註記
- LINE **沒有官方 .NET SDK**（官方僅支援 Java / PHP / Python / Node.js / Go / Ruby）
- 社群 .NET 套件維護狀況不穩，且 Context7 未收錄任何 .NET 版 LINE SDK，無法驗證版本
- **決策：直接以 `HttpClient` 手刻**。本專案只需要 3 個端點，手刻反而好維護

### 需要的 LINE API 端點
| 用途 | Method | URL |
|---|---|---|
| 回覆訊息 | POST | `https://api.line.me/v2/bot/message/reply` |
| 顯示載入動畫 | POST | `https://api.line.me/v2/bot/chat/loading/start` |
| （不使用）推播 | ~~POST~~ | ~~`/v2/bot/message/push`~~ |

官方文件：
- 建立聊天機器人 https://developers.line.biz/zh-hant/docs/messaging-api/building-bot/
- 載入動畫 https://developers.line.biz/en/docs/messaging-api/use-loading-indicator/
- API Reference https://developers.line.biz/en/reference/messaging-api/

---

## Phase 0 — LINE 平台設定（不寫程式，約 30 分鐘）

- [ ] LINE Developers Console 建立 Provider
- [ ] 建立 Messaging API Channel（或由既有官方帳號啟用 Messaging API）
- [ ] 取得並保存 `Channel Secret`（Basic settings 頁籤）
- [ ] 取得並保存 `Channel Access Token`（long-lived，Messaging API 頁籤）
- [ ] **停用「自動回應訊息」與「加入好友歡迎訊息」**（否則會與 AI 回覆衝突）

**驗收**：兩組憑證已存入 user-secrets 或 `.env`，未進版控。

---

## Phase 1 — Webhook 骨架與簽章驗證

### 交付內容
- ASP.NET Core Web API 專案（Minimal API 或 Controller 皆可）
- `POST /webhook` endpoint
- `X-Line-Signature` 簽章驗證（HMAC-SHA256，以 Channel Secret 為 key，比對 raw request body）
- Webhook event 解析（先只處理 `message` / `text` 類型，其他型別直接忽略並回 200）
- Echo bot：原封不動回傳用戶輸入

### 實作重點
- 簽章驗證必須用 **raw body bytes**，不能用反序列化後再序列化的字串
- 驗證失敗回 `401`，不要回 200
- 無論處理結果如何，**對 LINE 一律快速回 200**（LINE 不在乎你的回應內容）
- 測試期間可用 `cloudflared tunnel` 或 `ngrok` 暫時對外

### 建議專案結構
```
LineBot.Api/
├── Endpoints/WebhookEndpoint.cs
├── Line/
│   ├── LineSignatureValidator.cs
│   ├── LineMessagingClient.cs      # reply / loading 兩個方法
│   └── Models/                     # WebhookEvent, ReplyRequest ...
└── appsettings.json
```

### 驗收標準
- 手機傳「哈囉」→ 機器人回「哈囉」
- 用錯誤簽章打 `/webhook` → 回 401
- LINE Console 的 Webhook「Verify」按鈕顯示成功

---

## Phase 2 — AI 抽象層與降階 fallback（核心）

### 設計
```csharp
public interface IAiResponder
{
    string Name { get; }
    Task<string> GenerateAsync(string userMessage, string systemPrompt, CancellationToken ct);
}
```

實作兩個：

| 實作 | 優先序 | 端點 | 說明 |
|---|---|---|---|
| `OllamaCloudResponder` | 1 | `https://api.ollama.com` | 使用免費額度，需 API Key |
| `LocalOllamaResponder` | 2 | `http://localhost:11434` | Mac mini 本機，無限制但較慢 |

### 降階邏輯（被動判斷）
`AiResponderChain` 依序嘗試，遇到以下情況即降階至下一個：
- HTTP `429`（額度／速率上限）
- HTTP `5xx`
- Timeout / 連線失敗
- 回應為空字串

**全部失敗** → 拋出 `AiUnavailableException`，由上層轉為固定訊息（見 Phase 3）。

### 注意事項
- Ollama Cloud 與 local Ollama **API 介面相同**，只差 base URL 與是否帶 API Key，
  故兩個 Responder 可共用同一個 HTTP 呼叫實作，僅注入不同設定
- 使用 **Polly** 處理 retry 與 timeout（.NET 標準做法）
- 每次降階都要 **log 記錄**（哪個 provider、什麼原因），這是日後判斷是否該升級付費方案的依據

### 單元測試（必做）
- [ ] Cloud 回 429 → 確實改呼叫 Local
- [ ] Cloud 回 200 → **不會**呼叫 Local
- [ ] 兩者皆失敗 → 拋出 `AiUnavailableException`
- [ ] Cloud timeout → 降階且未超出總時間預算

---

## Phase 3 — 回應速度處理（不使用 Push）

### 時間預算設計
因為不用 Push，所有回覆都必須在 Reply Token 有效期內完成。設定**總預算 45 秒**：

```
t=0s   收到 webhook
       → 驗證簽章
       → 呼叫 loading animation（loadingSeconds: 60）
       → 立即回 HTTP 200 給 LINE
       → 將任務丟進背景佇列

t=0s   背景開始處理
       ├─ 嘗試 OllamaCloudResponder（timeout 15s）
       ├─ 失敗 → 嘗試 LocalOllamaResponder（timeout 25s）
       └─ 仍失敗或總時間超過 45s
             → 回覆固定訊息「AI維護中請稍後再試」

t≤45s  以 Reply Token 送出結果
```

### 實作重點
- **Loading Animation**
  - `POST /v2/bot/chat/loading/start`，body：`{ "chatId": "<userId>", "loadingSeconds": 60 }`
  - `loadingSeconds` 必須是 5 的倍數，範圍 5–60，預設 20
  - **僅支援一對一聊天**，群組／多人聊天室不支援 → 收到群組事件時跳過此步驟
  - 呼叫失敗不影響主流程，log 後繼續即可

- **背景處理**
  - 用 `Channel<T>` + `BackgroundService`，或 `IHostedService` 搭配佇列
  - 每個任務綁一個 `CancellationTokenSource(TimeSpan.FromSeconds(45))`

- **失敗處理（依需求：不做 Push 補送）**
  ```csharp
  catch (AiUnavailableException) or when 超時
      → await _line.ReplyAsync(replyToken, "AI維護中請稍後再試");
  ```
  - 若連這則固定訊息都送不出去（Reply Token 已過期）→ **僅記錄 log，不做任何補送**
  - 不實作 Push，不留 Push 的程式碼路徑

### 驗收標準
- 正常情況：傳訊息 → 看到「輸入中」動畫 → 收到 AI 回覆
- 手動停掉 Ollama Cloud（改錯 API Key）→ 仍能收到回覆（來自地端）
- 手動停掉兩邊 → 收到「AI維護中請稍後再試」
- 全程未呼叫任何 push 端點（可用 log 或 LINE Console 訊息則數確認）

---

## Phase 4 — 業務知識載入（讀取指定 md 檔）

### 設計
程式啟動時與每次請求前，讀取一個固定路徑的 Markdown 檔案，內容作為 system prompt 的一部分注入。

```
appsettings.json
{
  "Knowledge": {
    "FilePath": "business.md",
    "ReloadOnChange": true
  }
}
```

路徑相對於應用程式根目錄（`AppContext.BaseDirectory`），容器內即 `/app/business.md`。

### 實作重點
- **不要每次請求都讀檔**。用 `IMemoryCache` 或單例快取，
  搭配 `File.GetLastWriteTimeUtc()` 比對；檔案有變更才重新讀取
  - 好處：老婆改完 md 檔存檔，**不用重啟容器**就會生效
  - 或使用 `FileSystemWatcher`（Docker volume 下有時不可靠，建議用 mtime 比對較穩）
- 檔案編碼固定 **UTF-8**，注意 BOM 處理
- 檔案不存在或讀取失敗 → log 警告，使用內建的預設 system prompt，**不要讓服務起不來**
- **控制長度**：地端小模型 context window 有限，md 檔建議控制在 2000 字以內。
  程式應在超長時 log 警告

### System Prompt 組裝範例
```
你是「<店名>」的客服助理。請依據以下店家資訊回答顧客問題。

<以下為 business.md 內容>
{knowledgeMarkdown}
</以上為 business.md 內容>

規則：
- 只根據上述資訊回答，資訊中沒有的內容請回覆「這部分我需要幫您確認，稍後由專人回覆」
- 語氣親切、簡潔，回覆長度控制在 3 句話以內
- 不要編造價格、成分或營業時間
```

### business.md 建議結構（交給老婆填寫）
```markdown
# 店家資訊
## 營業時間
## 地址與交通
## 產品項目與價格
## 成分與過敏原說明
## 訂購方式
## 配送範圍與運費
## 常見問題
```

### 驗收標準
- 修改 `business.md` 中的營業時間 → 不重啟服務，下一次提問即反映新內容
- 問 md 檔中沒有的問題 → 回覆「需要幫您確認」而非胡亂編造
- 刪除 md 檔 → 服務仍正常運作（使用預設 prompt）

---

## Phase 5 — 離題偵測與惡意使用者封鎖

### 需求
- 顧客詢問與拎香無關的問題 → 一律回覆固定訊息：`僅回答拎香相關資訊`
- **連續** 3 次（含）以上離題 → 將該 userId 加入封鎖名單，之後不再回覆

### 重要前提：LINE 沒有「封鎖使用者」的 API
LINE Messaging API **無法從機器人端封鎖使用者**。所謂「封鎖」只能是伺服器端自行實作的黑名單：
收到該 userId 的 webhook 時，驗證簽章後直接回 200 並結束，不做任何回覆、不呼叫 AI、不顯示載入動畫。
對使用者而言就是「機器人已讀不回」。

### 離題判斷方式
交給 AI 判斷，要求模型回傳結構化結果：

```
在回答前，先判斷使用者的問題是否與店家資訊相關。
以 JSON 格式回覆，不要有其他文字：
{ "onTopic": true/false, "reply": "你的回覆內容" }

若 onTopic 為 false，reply 欄位可留空。
```

**這一步的風險必須正視**：地端小模型（7B–8B）的結構化輸出可靠度不高，
可能回傳非 JSON、或誤判正常問題為離題。因此：

- 解析失敗時 **一律 fail-open**（視為 onTopic = true），寧可多回答也不要誤封顧客
- 誤判成本不對稱：多回一句無關訊息只是浪費；誤封一位真實顧客則是實質的生意損失
- Ollama 支援 OpenAI 相容的 structured outputs（JSON Schema），
  雲端路徑可使用；地端則需視所選模型支援程度，實測後決定

### 計數規則
```
使用者傳訊息
  ├─ 在黑名單 → 回 200，結束（不回覆）
  ├─ onTopic = true  → 計數器歸零，正常回答
  └─ onTopic = false → 計數器 +1
        ├─ 計數 < 3 → 回覆「僅回答拎香相關資訊」
        └─ 計數 ≥ 3 → 加入黑名單，回覆「僅回答拎香相關資訊」後不再回應
```

- 計數器必須是**連續**的：中間只要問過一次相關問題就歸零
- 解析失敗（fail-open）視同 onTopic = true，計數器歸零

### 資料儲存
- 用 SQLite（`Microsoft.Data.Sqlite` 或 EF Core），DB 檔以 volume 掛載
- 表結構：`UserId (PK)`、`ConsecutiveOffTopicCount`、`IsBlocked`、`BlockedAt`、`LastMessageAt`
- **不要用純記憶體**：容器重啟後黑名單就消失，惡意使用者可以無限重來

### 解封機制（必做）
誤判一定會發生，必須留解封手段，擇一即可：
- 提供一個帶 API Key 保護的管理 endpoint：`DELETE /admin/blocklist/{userId}`
- 或直接用 SQLite CLI 改資料（最簡單，但需要你本人操作）

建議同時把封鎖事件寫入 log（userId、時間、觸發封鎖的 3 則訊息內容），
方便日後回頭檢查誤判率。

### 驗收標準
- 連續問 3 個無關問題 → 前 3 次都收到「僅回答拎香相關資訊」，第 4 次起無回應
- 問 2 次無關 → 問 1 次相關（正常回答）→ 再問 2 次無關 → **不會被封鎖**（計數已歸零）
- 重啟容器後，已封鎖的使用者仍在黑名單中
- 呼叫解封 endpoint 後，該使用者恢復正常對話
- AI 回傳非 JSON 時 → 不封鎖、不計數，仍嘗試正常回覆

---

## Phase 6 — 部署至 Mac mini

### 交付內容
- [ ] `Dockerfile`（multi-stage build，runtime 用 `mcr.microsoft.com/dotnet/aspnet`）
- [ ] `docker-compose.yml`，與現有的 Docker 網站併存
- [ ] `business.md` 以 **volume 掛載**至 `/app/business.md`（不要 COPY 進 image，否則改內容要重 build）
- [ ] SQLite 資料檔（黑名單）以 **volume 掛載**，確保容器重建後資料保留
- [ ] Cloudflare DNS + 反向代理，確保 webhook URL 為 HTTPS
- [ ] LINE Console 填入正式 webhook URL 並 Verify

### 注意事項
- 容器要連到 host 上的 Ollama：macOS Docker Desktop 用 `http://host.docker.internal:11434`
- 憑證（Channel Secret / Access Token / Ollama API Key）用環境變數注入，不要進 image
- 建議設定 `restart: unless-stopped`
- LINE Console 的 Security settings 可設定 IP 白名單，但家用 IP 若會變動則不建議啟用

### 驗收標準
- 從外網手機（行動網路，非家中 Wi-Fi）傳訊息，能正常收到回覆
- 重啟 Mac mini 後服務自動恢復
- 修改 `business.md` 後生效，無需 rebuild

---

## 執行建議

**不要一次叫 Claude Code 做完全部**。建議節奏：

1. Phase 1 完成並在手機上驗證通過 → 才進 Phase 2
2. Phase 2 的單元測試務必寫完 → 才進 Phase 3
3. 每個 Phase 結束時 commit，並記錄「Claude Code 在哪裡需要你介入修正」

最後一點是額外收穫：這份介入記錄可以直接成為你評估 Claude Code 實際能力的第一手材料。

---

## 待確認事項

- [ ] 該官方帳號的方案類型（免費 / 輕用量 / 中用量），影響未來若真要用 Push 的成本評估
- [ ] Ollama Cloud 免費額度的實際觸頂頻率 —— 上線後看 log 統計降階次數再決定是否升級 Pro
- [ ] 地端要跑哪個模型（Mac mini 16GB RAM 建議 7B–8B 量化模型，需實測繁中回覆品質）
