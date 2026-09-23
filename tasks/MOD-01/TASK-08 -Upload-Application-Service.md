# TASK-08 — Upload Application Service

## 目的

實作 Upload Application Service，將目前已完成的 Upload Module 元件串成完整的上傳流程。

## 實作範圍

Upload Service 應依序完成：

```text
Validate Files
→ Create Batch
→ Store Original Files
→ Create Images
→ Create ProcessingJobs
→ Persist Database
→ Commit
→ Enqueue ProcessingJobs
→ Return Upload Result
```

## 必要條件

- Application Layer 只能依賴既有 abstraction。
- Queue 必須透過 `IProcessingQueue`，不得直接使用 `Channel<T>` 或 `ChannelProcessingQueue`。
- Database commit 必須完成後才能 enqueue。
- 所有 async operation 必須傳遞 `CancellationToken`。
- 回傳 Application DTO，不得包含 HTTP / Controller concern。
- 不修改既有 Batch、Image、ProcessingJob Domain Model。
- 不自行新增 Queue retry、Outbox 或完整 compensation mechanism。

## 中文註解要求

本 TASK 新增或修改的核心程式碼，必須加入**詳細、易懂的繁體中文註解**。

註解目的不是逐行翻譯程式碼，而是協助開發者理解：

- 這段程式碼為什麼存在
- 在 Upload 流程中扮演什麼角色
- 為什麼執行順序不能改
- 與其他元件之間如何互動
- 發生失敗時會有什麼影響

### 優先加註解的位置

以下位置應優先加入中文說明：

- Upload Service 主流程
- Validation 呼叫前後
- Batch 建立
- Storage 寫入
- Image 建立
- ProcessingJob 建立
- Database Commit
- Queue Enqueue
- CancellationToken 傳遞
- Exception / Failure handling
- Unit Test 的 Arrange / Act / Assert 目的

例如：

```csharp
// 必須先完成資料庫 Commit，再將 Job 放入 Processing Queue。
// 否則 Background Worker 可能先取到 Job，卻查不到尚未成功寫入資料庫的資料。
await _unitOfWork.SaveChangesAsync(cancellationToken);

// 資料已成功持久化後，才允許背景處理流程開始執行。
await _processingQueue.EnqueueAsync(job, cancellationToken);
```

### 註解風格

應避免沒有資訊量的註解，例如：

```csharp
// 建立 Batch
var batch = ...
```

應改成說明「目的或原因」，例如：

```csharp
// 一次 Upload Request 會以 Batch 作為同一批圖片的識別單位，
// 後續查詢處理進度時也會透過 BatchId 聚合這批圖片。
var batch = ...
```

註解應保持清楚、簡潔，不需要每一行程式碼都加註解。

---

## Error Handling

至少處理：

- Validation failure
- Storage failure
- Database failure
- Queue failure
- Cancellation

Queue enqueue 若在 DB commit 後失敗，本 TASK 先允許 exception 向上傳遞；補償機制留待後續 TASK。

## Tests

至少測試：

1. Successful upload
2. Validation failure → 不執行 Storage / DB / Queue
3. Storage failure → 不 enqueue
4. Database failure → 不 enqueue
5. Queue failure → exception 正常向上傳遞
6. CancellationToken 可正確中止流程

測試程式碼同樣應加入中文註解，尤其要說明：

- 為何建立這個測試情境
- Arrange 中的重要設定
- Act 實際驗證哪個行為
- Assert 為何能證明規格成立

## Acceptance Criteria

- Upload Application Service 完成
- Batch / Image / ProcessingJob 可正確建立並持久化
- DB commit 發生在 enqueue 之前
- Queue 只透過 `IProcessingQueue`
- Clean Architecture dependency 不被破壞
- 核心程式碼具有詳細、易懂的繁體中文註解
- 測試具有足以理解測試目的的中文註解
- `dotnet build` 通過
- `dotnet test` 全部通過
- Existing tests 無 regression

## 實作前

依 `pre-implementation-review` Skill：

1. 讀取 `docs/INDEX.md`
2. 載入 TASK-08 必要 Context
3. 檢查既有 Validation / Storage / Persistence / Queue contracts
4. 列出預計修改檔案與測試
5. 確認哪些核心流程需要加入中文註解
6. 有衝突則 STOP；無衝突則回覆 READY