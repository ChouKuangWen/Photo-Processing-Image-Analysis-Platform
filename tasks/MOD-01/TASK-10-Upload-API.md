# TASK-10 — Upload API

**模組：** MOD-01 Upload Module  
**任務編號：** TASK-10  
**任務名稱：** Upload API  
**文件狀態：** Implementation Complete（最新驗證見 §25；歷史阻擋見 §24）
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

- [x] Upload endpoint 可接收 multipart/form-data。
- [x] 支援單檔與多檔。form fields 固定為 files 與 workflow；workflow 為必填字串。缺少、空白或非法值以 400 / INVALID_WORKFLOW 與既定 validation envelope 拒絕，不因 binding failure 落入 enum default value。
- [x] Controller 不直接操作 DB / Storage / Queue。
- [x] HTTP-specific type 不滲透進 Application Layer。
- [x] 正常 Upload 回傳 202 Accepted。
- [x] Validation failure 有一致 HTTP mapping。
- [x] Unexpected failure 不洩漏 internal detail。
- [x] CancellationToken 正確向下傳遞。
- [x] 沿用既有 error response contract。
- [x] Unit Tests 全部通過。
- [x] API Integration Tests 全部通過。（使用者回報 51/51，見 §25）
- [x] `dotnet build` 成功。
- [x] `dotnet test` 成功。（使用者回報完整回歸 261/261，見 §25）
- [x] TASK-01～TASK-09 不 regression。（完整回歸 261/261，見 §25）

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

---

# 23. Completion Record — 2026-09-28

> 歷史紀錄：以下完成判定已由 §24 取代。使用者先前的 260/260 結果不代表本次 Storage 修正版本已通過完整回歸。

**結果：** IMPLEMENTATION COMPLETE。使用者已授權更新完成狀態；本次收尾只更新本文件，未修改程式或測試。

## 驗證來源

- 使用者於本機 VS Code PowerShell 執行完整 `dotnet test`：總計 260，成功 260，失敗 0，跳過 0，包含真實 SQL Server 整合測試。此完整通過結果由使用者回報，不是 Agent 本次執行。
- 使用者回報建置成功，有 6 個警告；未提供該次警告明細，6 個警告的內容與分類列為未確認。先前 Agent 輸出曾出現 NU1900（NuGet 弱點資料來源無法連線），不能據此認定本次 6 個警告全部相同。
- 既有測試清單為 UnitTests 209、IntegrationTests 51（含 API HTTP 26、smoke 1、SQL integration 24），合計 260；使用者本次提供的是總計結果，未提供逐專案輸出。
- 使用者確認 SQL Server 登入與測試連線問題已排除。
- Agent 本次檢查全部 4 個已追蹤變更及 9 個新增未追蹤檔案，並執行 `git diff --check`，無 whitespace error。未新增依賴套件，未變更 Application / Domain / Infrastructure、Schema 或 TASK-09 補償流程。

## Acceptance Criteria 逐項核對

| §19 項目 | 核對依據 |
|---|---|
| multipart/form-data endpoint | UploadController 的 POST /api/v1/images/upload 與 UploadHttpRequest.ReadAsync；真實 Kestrel HTTP 測試。 |
| 單檔、多檔、files / workflow | MultipartUpload_ReturnsAcceptedEnvelope；workflow 缺少、空白、非法、數字及重複值測試；WrongFileField_IsNotAcceptedAsFiles。 |
| Controller 不操作 DB / Storage / Queue | Controller 只進行 HTTP mapping、呼叫 IUploadService、回傳成功 envelope。 |
| HTTP type 不滲透 Application | FormUploadFile 位於 API，實作既有 IUploadFile；Application 未修改。 |
| 成功 202 | ApiSuccessResponse<UploadResult>；HTTP 測試驗證 success、data.batchId、totalCount、status。 |
| Validation HTTP mapping | 400 / INVALID_FILE、UNSUPPORTED_FORMAT、FILE_TOO_LARGE、INVALID_WORKFLOW；binding error 同一 envelope。 |
| Internal detail 不外洩 | 受控訊息、category mapping、安全 logging 測試；不輸出 raw exception；EF diagnostics 在 API composition 過濾。 |
| CancellationToken | Controller token forwarding、ClientDisconnect_CancelsApplicationToken；不新增 cancellation status。 |
| 共用 error contract | ApiExceptionMiddleware / ApiErrors；success:false、error.code/message/traceId；不使用 ProblemDetails。 |
| Unit Tests 全部通過 | 使用者於本機執行完整 260/260 測試結果。 |
| API Integration Tests 全部通過 | 同上，包含真實 HTTP 與 SQL / Storage API 整合案例。 |
| Build 成功 | 使用者於本機執行並回報；6 個警告明細未確認。 |
| dotnet test 成功 | 使用者於本機執行：260 passed / 0 failed / 0 skipped。 |
| TASK-01～TASK-09 無 regression | 沿用全部既有測試、未降低斷言；包含在使用者完整通過結果中。 |

## 其他契約與邊界核對

- StorageSave category → 500 / STORAGE_ERROR；Database / Transaction / Queue / unexpected → 500 / INTERNAL_ERROR，不從 IOException 型別猜來源。
- BATCH_NOT_FOUND 的共用 404 mapping / envelope 已有單元測試；本 Task 未實作 Batch Status endpoint，TASK-11 未開始。
- 沿用 request TraceIdentifier，缺少時建立；error.traceId 與 HTTP logging 使用相同值。
- UploadService、DbContext、UploadPersistence 為 Scoped；Queue、無 request state 的 Storage / Validation 為 Singleton。Composition_IsolatesPersistenceAndSharesQueue 驗證 isolation / queue lifetime。
- HTTP adapter 可重複從檔案開頭讀取，並釋放自己開啟的 stream；底層 request body 由 ASP.NET Core 管理。Controller 不將 request stream 交給 Worker。
- SQL integration 新增成功接受及 Commit 後 Queue failure 案例，驗證 DB / Storage 保留；未改補償語意。

## 變更檔案

新增：

- src/PhotoPlatform.Api/Contracts/ApiError.cs
- src/PhotoPlatform.Api/Controllers/UploadController.cs
- src/PhotoPlatform.Api/Http/ApiExceptionMiddleware.cs
- src/PhotoPlatform.Api/Http/FormUploadFile.cs
- src/PhotoPlatform.Api/Http/UploadHttpRequest.cs
- src/PhotoPlatform.Api/UploadApiComposition.cs
- tests/PhotoPlatform.UnitTests/Api/UploadApiTests.cs
- tests/PhotoPlatform.IntegrationTests/Api/UploadApiHost.cs
- tests/PhotoPlatform.IntegrationTests/Api/UploadApiTests.cs

修改：

- src/PhotoPlatform.Api/Program.cs
- tests/PhotoPlatform.UnitTests/PhotoPlatform.UnitTests.csproj（API ProjectReference）
- tests/PhotoPlatform.IntegrationTests/PhotoPlatform.IntegrationTests.csproj（API ProjectReference）
- tests/PhotoPlatform.IntegrationTests/Persistence/UploadPersistenceTests.cs（新增 API 整合案例）
- tasks/MOD-01/TASK-10-Upload-API.md（本次完成狀態與驗收紀錄）

## 剩餘限制

撤回先前「無已知 TASK-10 阻擋或未解規格衝突」的描述；本次 Review 修正、驗證阻擋與待決策事項見 §24。歷史 6 個警告明細仍未確認。Queue 仍為既有 bounded runtime queue，Worker / recovery 不屬本 Task；不宣稱背景處理或整個 MOD-01 已完成。

No commit was created. Waiting for manual code review and commit approval.

# 24. Review 3c 修正與重新驗證 — 2026-09-29

**歷史結論（2026-09-29）：** 3c 程式修正與受影響測試已完成；當時完整回歸受 SQL 登入問題阻擋，尚不能重新宣稱 TASK-10 Completed。此驗證阻擋已解除，最新完成判定見 §25；本節保留先前實際結果與驗證限制。

## 授權與變更

使用者批准將既有 Storage 的 3c 缺陷納入本次範圍。依 MOD-01 §1.10、TASK-05 §15 / §18.10 與 TASK-10 §9，清理失敗不得覆蓋原始失敗或取消，且不為測試新增檔案系統抽象。

- `src/PhotoPlatform.Infrastructure/Storage/LocalFileStorageService.cs`：以明確 try / catch / finally 管理目的串流；在 DisposeAsync 前保存 Copy / Flush 原始例外。已有原始失敗時忽略次要釋放例外並以原始 throw 傳播，包含 cancellation；只有 DisposeAsync 失敗時仍進入外層失敗處理，嘗試恢復來源位置及刪除本次建立的檔案。來源仍由呼叫端擁有，CreateNew 失敗不刪既有檔案；刪除仍為 best effort。
- `tests/PhotoPlatform.UnitTests/Infrastructure/LocalFileStorageServiceTests.cs`：加強取消 token 斷言；將原始失敗與來源位置恢復同時失敗的案例擴充至 cancellation，驗證同一例外、token、檔案清理與來源未被釋放。
- 本文件：更新目前狀態、撤回過時完成結論並保留歷史驗證來源。

## 本次 Agent 實際執行結果

| 驗證 | 結果 |
|---|---|
| `dotnet restore PhotoPlatform.sln` | 成功；3 個 NU1900（NuGet 弱點資訊來源無法取得） |
| `dotnet build PhotoPlatform.sln --no-restore` | 成功；0 error、3 個 NU1900 warning |
| LocalFileStorageServiceTests 篩選測試 | 41 通過、0 失敗、0 跳過 |
| 完整回歸：PhotoPlatform.UnitTests | 210 通過、0 失敗、0 跳過 |
| 完整回歸：PhotoPlatform.IntegrationTests | 27 通過、24 失敗、0 跳過 |
| 完整回歸合計 | 261 項；237 通過、24 失敗、0 跳過；未通過 |

完整回歸命令：`dotnet test PhotoPlatform.sln --no-build --no-restore --logger 'console;verbosity=quiet'`。最初 SQL 初始化出現網路／執行個體連線失敗；啟動既有 Docker Desktop 與 photoplatform-test-sql 後重跑，仍有上述 24 個失敗。進一步以單一 SQL 案例診斷，確認 InitializeAsync 階段為登入失敗；尚未執行案例斷言，屬目前 Agent 測試環境的登入阻擋，不能據此判定程式回歸失敗或通過。未輸出連線字串或密碼，未變更憑證。先前使用者本機 260/260 與 6 個警告僅保留為歷史結果。

## 驗證限制

實際測試涵蓋寫入失敗／取消及來源位置恢復失敗的例外優先順序，以及既有成功、清理、來源所有權與碰撞保護案例。以下僅完成控制流程核對，**未經確定性執行測試覆蓋**：Copy / Flush 與目的 FileStream.DisposeAsync 同時失敗（含取消），以及只有目的 DisposeAsync 失敗。未新增 IFileSystem、串流 factory 或其他 production 測試抽象。目的釋放失敗時仍會嘗試刪除，但不保證刪除一定成功。

## 延續唯讀 Review 的待決策／改善事項

- **上傳限制：規格未定義部分待決策。** 既定單檔驗證在 Application 解析後執行；Controller 保留 DisableRequestSizeLimit 與 MultipartBodyLengthLimit = long.MaxValue。檔案數量、整體 request 與表單解析期間的完整限制及部署上限仍需決策／確認；本次不新增限制或錯誤契約。
- **EF Core 日誌政策：待決策。** LogLevel.None 會停用該分類下所有日誌，包含警告與錯誤；既有安全訊息規範不等同明確要求全面停用。Application 階段與 API 分類事件不等於完整 EF 診斷，須決定保留哪些診斷及安全處理方式；本次不改 filter。
- **API adapter cleanup（3a / 3b）：改善項目與未定義行為。** 任一 Dispose 拋錯會中止迴圈，Controller using 的清理例外可能覆蓋原失敗；標準 FormFile 的 ReferenceReadStream 釋放僅標記已釋放，尚無目前正常 HTTP 路徑可重現的證據。對可拋錯 adapter stream 的清理策略及僅清理失敗的 API 結果仍待決策，本次不修改。

以上事項不因測試通過而視為已決策；3c 修正不代表它們已解決。未修改 API 契約、Schema、TASK-09 補償流程、AGENTS.md；未開始 TASK-11，未建立 commit。

# 25. SQL 環境阻擋解除與完成紀錄 — 2026-10-04

**目前結論：TASK-10 IMPLEMENTATION COMPLETE。** 使用者提供修正版本的完整驗證結果並授權更新完成紀錄；§24 的 SQL 登入與完整回歸阻擋已解除。本次只更新文件，不修改 production code 或測試。

## 最新驗證基準與來源

以下為使用者於本機 PowerShell 執行並回報的結果，不是 Agent 本次重新執行：

- HEAD：`f55a279da1f3bdfb8e501706e068557f956b461c`。
- 驗證時 `git status --porcelain`：clean。
- `dotnet build`：PASS，0 errors；6 × NU1900，原因為無法取得 `https://api.nuget.org/v3/index.json` 的套件弱點資料，不是 compilation failure。
- Integration Tests：51/51 PASS，0 failed，0 skipped。
- Full Regression：261/261 PASS，0 failed，0 skipped；包含 TASK-01～TASK-10 既有測試。
- 原本 24 個 Integration Test failures 已確認由 `PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING` 中的 sa 密碼與目前 Docker SQL Server 不一致造成。修正 test connection string 後，PowerShell SQL connection 驗證成功，Integration 與 Full Regression 全數通過。此處不記錄密碼或連線字串內容。

§24 的 237 passed / 24 failed 保留為先前環境阻擋紀錄，不代表目前版本仍有測試失敗；§23 的 260/260 保留為更早歷史結果，不作為本次修正版本的完成依據。

## 完成範圍與保留事項

最新完整回歸已滿足 §19 的 API Integration Tests、dotnet test 與 TASK-01～TASK-09 regression 驗證項目。TASK-10 前置驗證阻擋解除，可重新進行 TASK-11 pre-implementation review。

§24 的確定性測試覆蓋限制，以及 EF logging filter、upload limits、adapter cleanup 待決事項仍保留；不因測試通過而視為已解決。本次不處理 NU1900，不變更 API / Database contract、TASK-09 Transaction / Compensation boundary 或既有程式。Queue / Worker / recovery 的既有限制不變。

TASK-11 implementation 尚未開始，仍須 review 通過與使用者明確批准。未建立 commit 或 push。
