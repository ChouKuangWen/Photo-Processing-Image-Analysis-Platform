# TASK-09 — Transaction & Compensation

**模組：** MOD-01 Upload Module  
**任務編號：** TASK-09  
**任務名稱：** Transaction & Compensation  
**文件狀態：** Development Task  
**前置任務：** TASK-08 Upload Application Service  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core / SQL Server  

---

# 1. 任務目的

TASK-09 的目標是補齊 Upload Module 在失敗情境下的資料一致性處理。

TASK-08 已完成 Upload Application Service 的主要 orchestration 流程，包含：

1. 驗證上傳檔案。
2. 儲存原始圖片至 Storage。
3. Begin Database Transaction，建立 Batch。
4. 建立 Image。
5. 建立 ProcessingJob。
6. 寫入 Database。
7. Commit Transaction。
8. 將已建立的 ProcessingJob 加入 Processing Queue。

TASK-09 不重新設計上述成功流程。

本任務專注處理：

- Database Transaction 的失敗回滾。
- Storage 已寫入但 Database 失敗時的補償刪除。
- 多檔上傳時部分成功、部分失敗的清理。
- Commit 前後責任邊界。
- Commit 後 Queue enqueue 失敗時的處理原則。
- 失敗情境下不得留下不一致或無法追蹤的 Upload 資料。

本任務完成後，Upload 流程應具備明確的失敗處理與補償策略。

---

# 2. 背景

Upload Module 同時操作兩種不同資源：

- Database
- File Storage

Database 可以透過 Transaction 執行 Commit / Rollback。

File Storage 不屬於 Database Transaction，因此：

```text
Storage.SaveAsync()
```

成功後，即使之後：

```text
Database Transaction Rollback
```

已寫入 Storage 的檔案仍然存在。

因此 Upload 流程不能只依靠 Database Transaction。

需要額外建立 Compensation 機制，在流程失敗時主動刪除已經寫入但不應保留的檔案。

---

# 3. 核心一致性原則

Upload Module 採用以下一致性原則。

## 3.1 Commit 前

依 System-Level §1.26 / MOD-01 §1.8：Validation 失敗無 Storage / Database side effect。

- Transaction 尚未建立（含 Storage Save / BeginTransaction 失敗）：逐一嘗試 Storage Compensation，不呼叫 Database Rollback。
- Transaction 已建立、Commit 尚未成功：先嘗試 Database Rollback，再逐一嘗試 Storage Compensation。
- 不 Enqueue；cleanup 失敗不阻止其他 cleanup、不覆蓋 original exception，必須安全記錄。

---

## 3.2 Commit 後

Database Transaction Commit 成功後：

```text
Batch
Image
ProcessingJob
```

已成為正式的持久化資料。

此時不得因 Queue enqueue 失敗而回頭：

- 刪除已 Commit 的 Batch。
- 刪除已 Commit 的 Image。
- 刪除原始圖片。
- 假裝 Database Transaction 可以 Rollback。

Commit 是 Database Transaction 的責任邊界。

---

# 4. Upload Transaction Boundary

維持 TASK-08 正常流程，不重新設計：

```text
Validate all files
Save all files to Storage（記錄每次成功回傳的 StoredPath）
Begin Database Transaction
Create Batch / Images
SaveChanges → Image Identity 回填
Create ProcessingJobs
SaveChanges
Commit Transaction
Enqueue ProcessingJobs
Return Upload Result
```

SQL Transaction 僅涵蓋 Batch / Image / ProcessingJob 持久化；Validation / Storage Save 在 Begin 前，Storage / Queue 不屬於 SQL Transaction。

---

# 5. Compensation 範圍

Compensation 僅處理：

> 本次 Upload Request 已經成功寫入 Storage，但因 Commit 前發生錯誤而不應留下的檔案。

系統必須記錄本次 Upload 已成功儲存的檔案資訊。

概念上：

```csharp
var storedFiles = new List<string>(); // 沿用 SaveAsync 回傳的 StoredPath，不新增 DTO
```

每次 Storage Save 成功後：

```csharp
storedFiles.Add(storedFile);
```

若 Commit 前發生 Exception：

```text
Rollback Database（僅已建立交易）
↓
Delete storedFiles
↓
向上傳遞明確分類並保留原始錯誤
```

不得掃描整個資料夾進行推測式刪除。

Compensation 必須只作用於本次 Upload 已確認成功寫入的檔案。

---

# 6. Compensation 執行順序

Commit 前發生錯誤時，處理順序應為：

```text
Original Exception
        ↓
Database Rollback（僅已建立交易）
        ↓
Storage Compensation
        ↓
Propagate classified failure preserving Original Exception
```

Rollback 與 Compensation 均屬於 cleanup 行為。

Cleanup 失敗不得覆蓋原始 Upload Exception。

例如：

```text
Storage Save Success
Database Save Failed
        ↓
Rollback Success
        ↓
Storage Delete Failed
```

最終主要錯誤仍應代表：

```text
Database Save Failed
```

Storage Delete Failed 應被記錄，供後續追蹤與清理。

---

# 7. 多檔 Upload 的失敗情境

假設一次 Upload：

```text
Image A
Image B
Image C
```

如果：

```text
A Storage Save → Success
B Storage Save → Success
C Storage Save → Failed
```

則整個 Upload Request 視為失敗。

必須：

```text
No Database Rollback（Storage 階段尚未建立交易）

Delete A

Delete B
```

不得：

- 保留 A / B 的 Database 資料。
- 將 A / B 存檔當作成功結果故意保留；必須逐一嘗試刪除，Delete 失敗則安全記錄，不承諾外部失敗時仍能保證零 orphan。
- 建立部分成功的 Batch。
- Enqueue A / B 的 ProcessingJob。

目前 MOD-01 Upload 採用：

> Request-level atomic behavior before Commit.

也就是 Commit 前任何必要步驟失敗，都視為整個 Upload Request 失敗。

---

# 8. Database Rollback

TASK-08 已建立 Transaction abstraction。

TASK-09 應沿用既有：

```text
IUploadTransaction
```

或目前專案中等價的 Transaction abstraction。

不得在 Application Layer 直接依賴：

```text
SqlTransaction
DbTransaction
```

等 Infrastructure implementation。

僅已取得 Transaction 且 Commit 尚未成功時才呼叫 Rollback。Rollback 必須：

1. 回滾 Database Transaction。
2. 清理由本次失敗流程造成的 EF Core tracking 狀態。
3. 不得影響已經 Commit 的其他 Request。

---

# 9. EF Core Tracking 清理

Database Rollback 不代表 DbContext Change Tracker 自動回復至完全乾淨狀態。

因此 Rollback 後需要確保本次 Upload 建立的：

```text
Batch
Image
ProcessingJob
```

不會繼續留在錯誤的 Added / Modified tracking 狀態。

應沿用 TASK-08 已建立的 persistence / transaction 行為。

不得因 TASK-09 再建立另一套重複的 DbContext cleanup 機制。

---

# 10. Storage Compensation Contract

優先沿用既有 Storage abstraction。

例如既有：

```csharp
IFileStorageService
```

若目前 contract 已具備：

```csharp
SaveAsync(...)
DeleteAsync(...)
```

TASK-09 不新增另一個 Storage interface。

Compensation 使用既有：

```text
DeleteAsync
```

完成。

如果現有 Delete contract 不足以安全執行 Compensation，才允許以最小變更調整 interface。

不得因本任務導入新的 Storage Provider 或新的 Storage architecture。

---

# 11. Queue 與 Transaction 的邊界

Processing Queue enqueue 發生於：

```text
Database Commit
```

之後。

原因是 Worker 不應取得尚未 Commit 的 ProcessingJob。

禁止：

```text
Enqueue
↓
Database Commit
```

避免 Worker 先取出 Job，但 Database 尚查不到該 Job。

正確順序：

```text
Database Commit
↓
Enqueue
```

---

# 12. Commit 後 Queue Enqueue 失敗

這是本任務最重要的邊界之一。

如果：

```text
Database Commit → Success
Queue.EnqueueAsync → Failed
```

此時：

```text
Batch
Image
ProcessingJob
Original File
```

都已經是正式資料。

不得執行 Storage Compensation。

不得嘗試 Database Rollback。

原因：

> Database Transaction 已經 Commit，Transaction 已結束。

TASK-09 的最低要求是：

1. 保留已 Commit 資料。
2. 將 enqueue failure 明確記錄。
3. Exception 不得被吞掉。
4. 不得刪除原始圖片。
5. 不得造成「DB 已存在，但檔案被刪除」的新不一致。

---

# 13. Queue Failure 的恢復責任

Commit 後 Queue failure：保留 DB / Storage、Failure 可觀察、Exception 向上傳遞。Commit 後 cancellation 同樣不得 Rollback 或 Storage Compensation。

ProcessingJob 是持久化紀錄，不代表目前已有 automatic requeue、retry scheduler、每個 Pending Job 的 startup recovery 或 durable queue recovery。Recovery 屬後續 Module；本 Task 不實作 Worker / Outbox / Scheduler。

---

# 14. Logging

使用 Microsoft.Extensions.Logging / ILogger<T>（Application 可新增所需最小 Microsoft.Extensions.Logging.Abstractions 依賴；不導入 provider），遵守 MOD-01 §1.12 Logging Ownership / Security。Application 不引用 Infrastructure，本階段不安裝或綁定 Serilog provider。

TASK-09 唯一擁有 Transaction failure、Rollback failure、Storage Compensation failure、Queue Enqueue failure 與 Upload acceptance failure stage 所需最小安全事件。TASK-10 只記錄 HTTP boundary / classification / TraceId；TASK-11 重用並補齊觀測基線，不重複 failure events。

僅允許必要 identifier / logical key / failure stage / sanitized type or code。Key 沿用 original/{guid}；禁止 raw Exception.Message、Stack Trace、SQL Detail、OS absolute path、JWT、Authorization Header、binary、secret。不得直接傳 raw exception object 給 logger 展開。

儲存 partial-file cleanup 的責任仍留在既有 Storage implementation，不建立第二套 cleanup 或 logging abstraction。

---

# 15. Exception 原則

TASK-09 不建立龐大的新 Exception hierarchy。

依 MOD-01 §1.12 Application → API Failure Classification，在操作邊界提供 category / stage 並保留 original exception。具體最小分類載體型別留待實作；TASK-09 建立供 TASK-10 使用的分類，不以 CLR type 猜來源、不引入 HTTP 型別。原始 exception 可保存在載體內，但 raw message / stack 不得輸出至 Log / API。

主要原則：

```text
原始業務 / Infrastructure Exception
```

優先於 cleanup exception。

例如：

```text
Database Exception
+
Compensation Delete Exception
```

不得只回傳：

```text
Delete Failed
```

而遺失真正造成 Upload 失敗的 Database Exception。

---

# 16. Cancellation

CancellationToken 必須繼續向：

- Storage operation
- Database operation
- Queue operation

傳遞。

但進入必要 cleanup 時：

```text
Rollback
Compensation
```

不得因原 Request CancellationToken 已取消，就直接跳過所有清理。

Cleanup 應使用適當策略確保有合理機會完成。

不得因 Request 被取消而留下明顯 orphan file。

---

# 17. Idempotency

Compensation delete 應盡可能具備 idempotent behavior。

例如：

```text
Delete File A
```

如果檔案已不存在，不應因此造成新的重大失敗。

具體行為依現有 `IFileStorageService.DeleteAsync` contract 為準。

TASK-09 不建立完整 Upload Request Idempotency Key 機制。

---

# 18. 不得實作的內容

TASK-09 不包含：

- Upload Controller。
- Multipart HTTP parsing。
- API endpoint。
- Batch Status API。
- Background Worker。
- 圖片 metadata extraction。
- Image analysis。
- Naming Module。
- Retry Scheduler。
- Dead Letter Queue。
- Distributed Transaction。
- Two-Phase Commit。
- Outbox Pattern。
- Saga Framework。
- Cloud Storage。
- Message Broker。
- Redis Queue。
- RabbitMQ。
- Kafka。

不得為了處理 Transaction & Compensation 擴大架構。

---

# 19. 建議修改範圍

本 TASK 應優先修改既有程式，而不是建立平行架構。

可能涉及：

```text
PhotoPlatform.Application
└─ Upload
   └─ UploadService

PhotoPlatform.Application
└─ Abstractions
   ├─ IUploadTransaction
   ├─ IUploadPersistence
   └─ IFileStorageService

PhotoPlatform.Infrastructure
├─ Persistence
│  ├─ UploadPersistence
│  └─ UploadTransaction
│
└─ Storage
   └─ LocalFileStorage

Tests
├─ UnitTests
└─ IntegrationTests
```

實際名稱以目前 repository 為準。

Agent 必須先讀取現有實作，不得僅依此文件假設檔名或 namespace。

---

# 20. 實作要求

## 20.1 UploadService

UploadService 必須明確區分：

```text
Pre-Commit Failure
```

與：

```text
Post-Commit Failure
```

Pre-Commit Failure：

```text
Rollback（僅已建立交易）
+
Storage Compensation
```

Post-Commit Failure：

```text
No Rollback
No Storage Compensation
```

---

## 20.2 Stored File Tracking

UploadService 或適當 scope 必須追蹤：

```text
本次 Request 已成功 Save 的 Storage Objects
```

不得依賴：

- Database query 推測。
- directory scan。
- 檔名 pattern 猜測。

---

## 20.3 Compensation

Compensation 應：

1. 嘗試清理所有已記錄的 Storage Objects。
2. 單一 Delete 失敗不得阻止其他檔案繼續清理。
3. 記錄每一個 cleanup failure。
4. cleanup failure 不覆蓋 original exception。

---

# 21. Unit Tests

至少涵蓋以下案例。

## 21.1 第一個 Storage Save 失敗

```text
Storage Save → Failed
```

驗證：

- Database 未 Commit。
- 不 Enqueue。
- 不嘗試刪除不存在的檔案。

---

## 21.2 第二個 Storage Save 失敗

```text
Image A Save → Success
Image B Save → Failed
```

驗證：

- A 被 Compensation Delete。
- Storage 失敗時交易尚未建立，不呼叫 Database Rollback。
- 不 Enqueue。

---

## 21.3 第一階段 Database Save 失敗

```text
Storage Saves → Success
Database SaveChanges → Failed
```

驗證：

- Rollback。
- 所有本次已儲存檔案被 Delete。
- 不 Enqueue。

---

## 21.4 第二階段 Database Save 失敗

```text
Batch/Image Save → Success
ProcessingJob Save → Failed
```

驗證：

- Rollback。
- Batch / Image / ProcessingJob 不得 Commit。
- Storage files 全部 Compensation。
- 不 Enqueue。

---

## 21.5 Commit 失敗

```text
SaveChanges → Success
Commit → Failed
```

驗證：

- 執行失敗 cleanup。
- Storage files 被 Compensation。
- 不 Enqueue。

---

## 21.6 Compensation 部分失敗

```text
Delete A → Failed
Delete B → Success
```

驗證：

- B 仍然會被嘗試 Delete。
- Original Exception 被保留。
- Delete A failure 被記錄。

---

## 21.7 Commit 後第一個 Queue Enqueue 失敗

```text
Commit → Success
Queue.Enqueue → Failed
```

驗證：

- 不 Rollback。
- 不 Storage Compensation。
- Database 資料仍存在。
- Original File 仍存在。
- Queue failure 可被觀察。

---

## 21.8 Commit 後部分 Queue Enqueue 成功

```text
Job A Enqueue → Success
Job B Enqueue → Failed
```

驗證：

- 不刪除 Job A。
- 不刪除 Job B Database record。
- 不刪除任何 Original File。
- 不嘗試 Rollback 已 Commit Transaction。
- Failure 被記錄 / 回傳至上層。

---

## 21.9 Request Cancellation Before Commit

驗證：

- Cancellation 被傳遞。
- Database 不 Commit。
- 已儲存 Storage files 仍進行必要 Compensation。

---

補充 Unit Tests：第一個 Storage failure 不 Rollback；BeginTransaction failure 清理已存檔但不 Rollback；Commit 後 cancellation 保留資料；failure category / stage 與安全 logging；original exception 可由分類載體取得且不被 cleanup 覆蓋。既有測試只因分類 contract 更新必要斷言，不改成功流程。

# 22. Integration Tests

使用真實 SQL Server integration test。

不得使用 EF Core InMemory 作為 Transaction 驗證替代。

至少建立以下案例。

## 22.1 Rollback Removes Database Records

在 Commit 前模擬失敗。

使用新的 DbContext 查詢：

```text
Batch
Image
ProcessingJob
```

驗證資料不存在。

---

## 22.2 Commit Persists Records

正常 Upload Commit。

使用新的 DbContext 查詢：

```text
Batch
Image
ProcessingJob
```

驗證資料存在。

---

## 22.3 Rollback Does Not Affect Existing Data

先建立一筆已 Commit 的既有資料。

再執行新的失敗 Upload。

驗證：

```text
舊資料仍存在
新 Upload 資料不存在
```

---

# 23. Storage Integration Test

如果目前 LocalFileStorage integration test infrastructure 已存在，新增：

```text
Save A
Save B
Trigger Failure
Compensate
```

最後確認：

```text
A 不存在
B 不存在
```

測試必須使用獨立 temporary directory。

不得操作開發者真實圖片資料夾。

---

# 24. Acceptance Criteria

TASK-09 完成必須符合以下條件：

- [ ] Commit 前依三階段政策處理；只有已建立交易才嘗試 Rollback。
- [ ] Commit 前失敗會清理本次已成功儲存的 Storage files。
- [ ] 不接受部分成功；逐一嘗試清除本次成功存檔，清理失敗安全記錄。
- [ ] Compensation delete 單一失敗不會停止其他 cleanup。
- [ ] Cleanup exception 不覆蓋 original exception。
- [ ] Database Commit 後不再執行 Rollback。
- [ ] Database Commit 後 Queue failure 不會刪除 Storage file。
- [ ] Queue 只在 Commit 成功後 enqueue。
- [ ] Rollback 後 EF Core tracking state 不污染後續操作。
- [ ] Unit Tests 全部通過。
- [ ] SQL Server Integration Tests 全部通過。
- [ ] 現有 TASK-01 ～ TASK-08 測試不得 regression。
- [ ] `dotnet build` 成功。
- [ ] `dotnet test` 成功。

---

# 25. Definition of Done

TASK-09 完成代表：

```text
Upload Module 已具備
Database Transaction
+
Storage Compensation
+
明確的 Commit Boundary
+
Queue Failure Boundary
```

系統可以正確處理：

```text
Upload Success
Upload Failure Before Commit
Storage Failure
Database Failure
Commit Failure
Compensation Failure
Queue Failure After Commit
```

而不因單一錯誤造成不可預期的 Database / Storage 交叉不一致。

---

# 26. Agent Implementation Rules

開始實作前：

1. 先閱讀 `docs/INDEX.md`。
2. 依 INDEX 載入 TASK-09 必要的 System-Level / MOD-01 / Database / Storage / Queue Context。
3. 閱讀 TASK-08 最終實作。
4. 確認目前 `UploadService`、`IUploadPersistence`、`IUploadTransaction`、`IFileStorageService` 與 Queue contract 的真實 API。
5. 不得自行假設不存在的 method。
6. 不得先修改程式再補理解。
7. 若 TASK-09 與已核准規格發生衝突，停止實作並回報衝突來源。
8. 若只是 implementation detail 且規格已有唯一合理答案，可直接依既有架構實作。
9. 優先最小修改，不進行與 TASK-09 無關的 refactor。
10. 完成後執行完整 build / test。

---

# 27. 完成回報格式

Agent 完成 TASK-09 後，回報：

```text
TASK-09 Implementation Result

1. Modified Files
2. Transaction Changes
3. Compensation Behavior
4. Queue Failure Behavior
5. Unit Tests Added / Updated
6. Integration Tests Added / Updated
7. dotnet build Result
8. dotnet test Result
9. Remaining Risks / Notes
```

不得只回報：

```text
TASK-09 completed
```

必須提供可供後續 TASK-10 Review 使用的 implementation summary。
