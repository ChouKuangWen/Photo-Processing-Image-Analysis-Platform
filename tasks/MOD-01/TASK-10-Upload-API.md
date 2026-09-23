# TASK-10 — Upload API

**模組：** MOD-01 Upload Module  
**任務編號：** TASK-10  
**任務名稱：** Upload API  
**文件狀態：** Development Task  
**前置任務：** TASK-09 Transaction & Compensation  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core / SQL Server  

---

# 1. 任務目的

TASK-10 的目標是將既有 Upload Application Service 暴露為 HTTP API。

本任務負責：
- 建立 Upload Controller / Endpoint。
- 接收 multipart/form-data 上傳。
- 將 HTTP Request 轉換為 Application Layer 所需輸入。
- 呼叫既有 Upload Application Service。
- 將成功結果映射為 HTTP 202 Accepted。
- 將已定義的 Application / Validation failure 映射為適當 HTTP Response。
- 維持 API Layer 與 Application / Infrastructure 的責任邊界。

本任務不重新實作 Validation、Storage、Persistence、Transaction、Compensation、Queue 或 Background Processing。

共同失敗邊界依 System-Level §1.26 / MOD-01 §1.8：Transaction 未建立只補償 Storage、不 Rollback；已建立但 Commit 未成功則嘗試 Rollback + Compensation；Commit 成功後不 Rollback、不 Compensation（含 enqueue failure / cancellation）。不改 TASK-08 正常流程。

# 2. API Responsibility

API Layer 僅負責：

```text
HTTP Request
    ↓
Request Binding
    ↓
Basic HTTP-level validation
    ↓
Application Request Mapping
    ↓
UploadService
    ↓
HTTP Response Mapping
```

Controller 不得直接操作 DbContext、Storage implementation、Processing Queue 或 EF Core Transaction。

# 3. Endpoint

正式 route：

```http
POST /api/v1/images/upload
```

依 MOD-01 §3，不使用其他建議 route。

---

# 4. Request Format

Content-Type：

```text
multipart/form-data
```

支援單檔與多檔。form fields 固定為 files 與 workflow；workflow 為必填字串。缺少、空白或非法值以 400 / INVALID_WORKFLOW 與既定 validation envelope 拒絕，不因 binding failure 落入 enum default value。

HTTP binding model 可使用 `IFormFile` / `IReadOnlyCollection<IFormFile>`，但 Application Layer 不應直接依賴 `IFormFile`。

# 5. Request Mapping

Controller 應將 HTTP-specific upload data 轉換為既有 Application contract。

可能包含：

```text
FileName
ContentType
Length
Stream
```

實際欄位以現有 contract 為準。不得建立第二套平行 Upload DTO，除非現有 contract 明確不足。

# 6. Stream Ownership

- Controller / ASP.NET Core 負責 HTTP Request body 生命週期。
- Application Service 不應假設 Request 結束後仍可讀取原始 HTTP stream。
- UploadService 回傳前，所有必要 Storage Save 應已完成。
- 不得將尚未持久化的 Request stream 直接交給 Background Worker。

# 7. Success Response

Upload 成功完成必要接受流程後：

```http
202 Accepted
```

回傳既有 success / data envelope；data 為 UploadResult 的 batchId、totalCount、status，不省略 envelope。

不得在 API response 洩漏 internal file path、SQL detail、stack trace、internal exception detail 或 Storage physical root。

# 8. HTTP Status Mapping

## 8.1 Invalid Request / Validation Failure

例如無檔案、不支援格式、大小超限、invalid signature 等已定義 Upload validation failure。

預設：

```http
400 Bad Request
```

若現有規格另有明確定義，依既有規格。

## 8.2 Unsupported Media Type

只有在 System-Level / MOD-01 已明確要求時才使用：

```http
415 Unsupported Media Type
```

不得在 TASK-10 自行創造新的錯誤分類。

## 8.3 Request Cancellation

Request cancellation 必須向下傳遞 `CancellationToken`。

cleanup behavior 由 TASK-09 定義。

## 8.4 Unexpected Server Failure

目前尚無 global exception handler；本 Task 建立共用安全 exception handler / middleware，依 MOD-01 Application category / stage 映射既定 envelope。建立 / 沿用 ASP.NET Core request TraceIdentifier，error.traceId 必須存在。

不得回傳 raw exception message、stack trace、SQL detail 或 absolute file path。

# 9. Error Response Contract

固定使用 System-Level / MOD-01：

```text
{ success: false, error: { code, message, traceId } }
```

不得改為 ProblemDetails；HTTP binding validation 亦使用相同 envelope。

| Category | HTTP | Code |
|---|---|---|
| Validation | 400 | 既有 Validation code（含 INVALID_WORKFLOW） |
| Upload acceptance Storage failure | 500 | STORAGE_ERROR |
| Database / Transaction / Queue / unexpected | 500 | INTERNAL_ERROR |
| Batch not found（TASK-11 重用） | 404 | BATCH_NOT_FOUND |

使用 MOD-01 正式 stage / category，不以 IOException 等 CLR type 猜來源；cleanup 不覆蓋 original classification。安全 message 不來自 raw exception，不輸出 stack、SQL detail、physical path。

Cancellation 傳遞 RequestAborted，disconnect 不要求送達 error response、不新增自訂 cancellation HTTP status；仍依 TASK-09 三階段邊界清理。

---

# 10. Controller Design

Controller 應維持 thin controller：

```text
Bind
Map
Call
Map response
```

禁止在 Controller：
- 建立 Batch / Image / ProcessingJob。
- SaveChanges。
- Enqueue。
- Delete Storage File。

# 11. Dependency Injection

Upload API 必須透過 DI 取得 Application Service / contract。

Controller 不得 new UploadService 或注入 concrete Infrastructure。API composition root 完成必要 Upload / Validation / Storage / DbContext / Persistence 註冊；DbContext / Persistence 維持 request isolation，Queue 保持既有 Singleton。Storage root、max file size、SQL connection 由設定提供，不寫死 production 值或 secret。API test host 屬本 Task；若選擇新增套件，仍須依 AGENTS 取得依賴批准，不假設套件已存在。

# 12. API Validation Boundary

HTTP Layer 可處理 Request body、multipart binding、檔案集合是否存在。

extension、MIME、signature、size、business constraints 等既有 Upload validation 仍由 Application / Validation component 負責。

# 13. Logging Boundary

TASK-10 只負責 API 層必要 request/result logging。

完整 MOD-01 observability baseline 屬 TASK-11。使用既有 ILogger<T>，只記 HTTP boundary、request/result classification、exception mapping、TraceId；不重複 TASK-09 transaction / compensation / queue failure events，不綁定 Serilog provider。

# 14. OpenAPI / Swagger

若專案目前已啟用 OpenAPI / Swagger，需確保 endpoint、multipart request、response status 可被正確描述。

不得為本 TASK 導入新的 API documentation framework。

# 15. Unit Tests

至少涵蓋：
- Empty Upload。
- Valid Single File。
- Valid Multiple Files。
- Known Validation Failure。
- Unexpected Exception。
- Cancellation token forwarding。
- Missing / invalid workflow 不落入 enum default。
- Failure category mapping、error.traceId、binding error envelope、安全輸出。

# 16. API Integration Tests

至少涵蓋：
- POST single file → 202。
- POST multiple files。
- Invalid file。
- Missing file。
- Internal failure sanitization。

# 17. 不得實作的內容

TASK-10 不包含：
- 新 Storage Provider
- Transaction / Compensation redesign
- Queue redesign
- Background Worker
- Batch Status API
- Full Observability
- E2E module acceptance suite
- Frontend upload UI

# 18. 建議修改範圍

```text
PhotoPlatform.Api
├─ Controllers
├─ Contracts
└─ Program / DI registration（僅必要調整）

PhotoPlatform.Application
└─ 既有 Upload contract（僅必要最小調整）

Tests
├─ UnitTests
└─ IntegrationTests
```

實際檔名、namespace、route 以 repository 為準。

# 19. Acceptance Criteria

- [ ] Upload endpoint 可接收 multipart/form-data。
- [ ] 支援單檔與多檔。form fields 固定為 files 與 workflow；workflow 為必填字串。缺少、空白或非法值以 400 / INVALID_WORKFLOW 與既定 validation envelope 拒絕，不因 binding failure 落入 enum default value。
- [ ] Controller 不直接操作 DB / Storage / Queue。
- [ ] HTTP-specific type 不滲透進 Application Layer。
- [ ] 正常 Upload 回傳 202 Accepted。
- [ ] Validation failure 有一致 HTTP mapping。
- [ ] Unexpected failure 不洩漏 internal detail。
- [ ] CancellationToken 正確向下傳遞。
- [ ] 沿用既有 error response contract。
- [ ] Unit Tests 全部通過。
- [ ] API Integration Tests 全部通過。
- [ ] `dotnet build` 成功。
- [ ] `dotnet test` 成功。
- [ ] TASK-01～TASK-09 不 regression。

# 20. Definition of Done

TASK-10 完成後：

```text
HTTP Client
    ↓
Upload API
    ↓
Upload Application Service
    ↓
Storage / Database / Queue
```

具有清楚責任邊界，API Layer 僅處理 transport concern。

# 21. Agent Implementation Rules

1. 實作前先完成 `pre-implementation-review`。
2. 依 `docs/INDEX.md` 載入必要 Context。
3. 先核對目前 API route / error contract / exception handling strategy。
4. 必須以 TASK-09 最終核准的 failure behavior 為依據。
5. 不得自行重新定義 Upload atomicity。
6. 不得在 Controller 重複 Application validation。
7. 優先最小修改。
8. 不進行與 Upload API 無關的 refactor。
9. 完成後執行完整 build / test。

# 22. 完成回報格式

```text
TASK-10 Implementation Result

1. Modified Files
2. Endpoint / Route
3. Request Mapping
4. Response Mapping
5. Error Mapping
6. Unit Tests Added / Updated
7. API Integration Tests Added / Updated
8. dotnet build Result
9. dotnet test Result
10. Remaining Risks / Notes
```
