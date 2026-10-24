# TASK-11 — Batch Status & Observability

**模組：** MOD-01 Upload Module  
**任務編號：** TASK-11  
**任務名稱：** Batch Status & Observability  
**文件狀態：** Development Task  
**前置任務：** TASK-10 Upload API  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core / SQL Server  

---

# 1. 任務目的

TASK-11 的目標是讓 Upload Module 的執行狀態可被查詢與追蹤。

本任務負責：
- 提供 Batch 狀態查詢能力。
- 提供必要的 Upload / Queue / Failure 結構化 logging。
- 建立最小可觀測性基線。
- 讓 TASK-12 可以驗證完整 Upload 狀態。

本任務不負責 Background Worker、Retry Scheduler、Metrics Dashboard、外部 APM 或完整 distributed tracing。

共同失敗邊界依 System-Level §1.26 / MOD-01 §1.8：Transaction 未建立只補償 Storage、不 Rollback；已建立但 Commit 未成功則嘗試 Rollback + Compensation；Commit 成功後不 Rollback、不 Compensation（含 enqueue failure / cancellation）。不改 TASK-08 正常流程。

# 2. Responsibility Boundary

TASK-11 分為：

```text
A. Batch Status Query
B. Minimal Observability
```

# 3. Batch Status Source of Truth

Batch / Image / ProcessingJob 的持久化狀態以 Database 為準。

Queue 是 runtime scheduling mechanism，不得作為 Batch 狀態唯一來源。

# 4. Batch Status Query

正式 endpoint：

```http
GET /api/v1/images/batches/{batchId}/status
```

TASK-11 首次提供 read-only query；MOD-09 後續重用 / 擴充同一 endpoint 與 contract，不建立第二套 API。

# 5. Query Response

依 MOD-01 §3.6 / MOD-09 §3.2，回傳 success / data envelope；data 包含：

```text
batchId
totalCount
processedCount
successCount
failedCount
progressPercentage
status
```

不以 Image / Job 清單替代，不新增 CreatedAt、ImageId、JobId 等 response 欄位。

# 6. Batch Aggregate Status

正式 aggregate 已存在：讀取持久化 Batch counts / status，不新增 lifecycle、不從 Image / Job 建立第二套規則。Processing 狀態更新屬 MOD-02。

Progress 使用既有 Decimal 與 ProcessedCount / TotalCount × 100，限制 0–100；TotalCount == 0 時為 0，不建立新 precision / rounding model。

---

# 7. Query Layer

優先建立 read-only Application query contract。

Application Layer 不應直接回傳 EF Core Entity。

API response 應使用 DTO / response model。

# 8. Not Found

不存在 BatchId：

```http
404 Not Found
```

沿用現有 API error contract。

# 9. Minimal Observability

透過重用 TASK-09 / TASK-10 機制與事件，整體基線至少覆蓋：

```text
UploadAccepted
UploadFailedBeforeTransaction
TransactionStarted
TransactionRolledBack
TransactionCommitted
StorageCompensationStarted
StorageCompensationFailed
QueueEnqueueFailed
```

TASK-09 唯一記錄 transaction / rollback / compensation / queue failure 及最小 acceptance failure events；TASK-10 擁有 HTTP boundary / classification / TraceId。TASK-11 不重複發送上述 failure events，只補足基線、query correlation 與缺少的觀察能力。

# 10. Logging Security

允許：
- BatchId
- ImageId
- JobId
- FailureStage
- Logical Storage Key
- Sanitized Exception Type / Error Code

禁止：
- Raw binary
- JWT / Authorization Header
- SQL Detail
- Stack Trace
- OS absolute path
- 未清理的 Exception.Message

# 11. Logical Storage Key

Storage logging 只能記錄 logical key，例如：

```text
original/{guid}
```

不得記錄 physical absolute path。

# 12. Logging Layer Boundary

重用 Microsoft.Extensions.Logging / ILogger<T> 與 TASK-09 已有事件，不建立平行 abstraction、不在此階段安裝或綁定 Serilog。

Infrastructure 不重複記錄 TASK-09 已擁有的 failure event，也不得透過 raw exception object 展開敏感 provider 資訊。

Application 不得引用 Infrastructure。

# 13. Correlation

沿用 TASK-10 已建立的 ASP.NET Core request TraceIdentifier / error.traceId，補齊 query correlation。

若沒有，不需建立完整 distributed tracing framework；可沿用 ASP.NET Core request trace identifier 作最低基線。

# 14. Queue Failure Observability

對：

```text
Commit Success
Queue Enqueue Failed
```

驗證 TASK-09 已記錄 BatchId、JobId、FailureStage、Error classification；補齊同一事件的必要欄位，不重複發送。

不得宣稱已有自動 recovery。

# 15. Compensation Failure Observability

驗證 TASK-09 擁有的每筆 Compensation failure 安全 log，不建立第二個記錄者。

單一 delete failure 不得阻止後續 cleanup。

# 16. Query Performance

避免明顯 N+1 Query。

可依既有架構使用 projection / Include / join。

不得因此重構整個 repository architecture。

# 17. API Tests

至少涵蓋：
- Existing Batch → 200。
- Missing Batch → 404。
- Response sanitization。
- 正式 aggregate 欄位 / envelope、TotalCount == 0 回傳 0、公式與 0–100 邊界。

# 18. Logging Tests

至少涵蓋：
- Pre-Transaction Storage Failure。
- Transaction Rollback。
- Compensation Partial Failure。
- Queue Enqueue Failure。

# 19. Integration Tests

至少涵蓋：
- Query Persisted Batch。
- Query Missing Batch。
- Queue failure 後 DB record 仍可查詢。

所有 persistence 驗證應使用真實 SQL Server 與新的 DbContext。

# 20. 不得實作的內容

TASK-11 不包含：
- Worker
- Retry execution
- Recovery scheduler
- Dead Letter Queue
- Prometheus / Grafana
- OpenTelemetry backend
- Alerting
- Admin retry API
- Dashboard UI

# 21. 建議修改範圍

```text
PhotoPlatform.Application
├─ Queries
├─ DTOs
└─ Services

PhotoPlatform.Infrastructure
└─ Persistence query implementation（若架構需要）

PhotoPlatform.Api
└─ Batch / Upload status endpoint

Tests
├─ UnitTests
└─ IntegrationTests
```

實際檔名、namespace 以 repository 為準。

# 22. Acceptance Criteria

- [ ] 可依 BatchId 查詢持久化 Upload 狀態。
- [ ] 狀態來源以 Database 為準。
- [ ] 不存在 Batch 回傳 404。
- [ ] API 不回傳 EF Core Entity。
- [ ] 不自行建立未核准的 Batch state machine。
- [ ] Upload / Transaction / Compensation / Queue failure 具最小 structured logging。
- [ ] Logging 不洩漏 SQL detail / stack trace / absolute path / raw exception message。
- [ ] Queue failure 可追蹤但不宣稱自動 recovery。
- [ ] Query 無明顯 N+1。
- [ ] Unit Tests 全部通過。
- [ ] Integration Tests 全部通過。
- [ ] `dotnet build` 成功。
- [ ] `dotnet test` 成功。
- [ ] TASK-01～TASK-10 不 regression。

# 23. Definition of Done

TASK-11 完成後：

```text
Upload Request
    ↓
Batch / Images / Jobs Persisted
    ↓
可查詢
+
關鍵失敗可追蹤
```

但不代表 Background Processing 已完成。

# 24. Agent Implementation Rules

1. 實作前先完成 `pre-implementation-review`。
2. 依 `docs/INDEX.md` 載入必要 Context。
3. 不得推翻 TASK-09 Transaction / Compensation 邊界。
4. 不得重新定義 TASK-10 API error contract。
5. 先確認 System-Level 是否已有 Batch Status 定義。
6. 未有規格時，不自行創造 aggregate Batch state。
7. Logging 必須符合 System-Level 安全規則。
8. 優先最小修改，不引入大型 Observability framework。
9. 完成後執行完整 build / test。

# 25. 完成回報格式

```text
TASK-11 Implementation Result

1. Modified Files
2. Batch Status Query Design
3. API Endpoint
4. Logging Events Added
5. Security / Sanitization Behavior
6. Unit Tests Added / Updated
7. Integration Tests Added / Updated
8. dotnet build Result
9. dotnet test Result
10. Remaining Risks / Notes
```
