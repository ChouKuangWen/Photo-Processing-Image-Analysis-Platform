# TASK-12 — Integration, E2E & MOD-01 Backend Acceptance

**模組：** MOD-01 Upload Module  
**任務編號：** TASK-12  
**任務名稱：** Integration, E2E & MOD-01 Backend Acceptance  
**文件狀態：** Development Task  
**前置任務：** TASK-11 Batch Status & Observability  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core / SQL Server  

---

# 1. 任務目的

TASK-12 是 MOD-01 Backend Acceptance；不代表整個產品或後續 Processing Module 完成。

本任務不再新增新的 Upload 業務能力。

主要目標：
- 驗證 TASK-01 ～ TASK-11 的整合結果。
- 驗證真實 HTTP → Application → Storage → Database → Queue 流程。
- 驗證主要 failure path。
- 驗證規格、實作與測試一致。
- 完成 MOD-01 Backend Acceptance Checklist。
- 確認 MOD-01 可以交付並進入下一個 Module。

共同失敗邊界依 System-Level §1.26 / MOD-01 §1.8：Transaction 未建立只補償 Storage、不 Rollback；已建立但 Commit 未成功則嘗試 Rollback + Compensation；Commit 成功後不 Rollback、不 Compensation（含 enqueue failure / cancellation）。不改 TASK-08 正常流程。

# 2. 核心原則

TASK-12 是：

```text
Integration
+
End-to-End
+
Acceptance
```

不是 Feature Expansion。

若發現缺少功能，應判斷為 Bug / Specification Gap / Missing Contract，再回到正確 TASK 或上層規格處理。

# 3. Acceptance Scope

MOD-01 必須完整驗證：

```text
Upload API
    ↓
Validation
    ↓
Storage
    ↓
Database Transaction
    ↓
Batch / Image / ProcessingJob
    ↓
Commit
    ↓
Processing Queue
    ↓
Batch Status Query
    ↓
Observability
```

# 4. Success Path E2E

至少包含：
- 單檔 Upload。
- 多檔 Upload。

驗證：
1. POST /api/v1/images/upload 回傳 202 與既有 success / data envelope。
2. Batch 建立。
3. Image 建立。
4. 原始檔案存在 Storage。
5. ProcessingJob 建立。
6. DB transaction 已 Commit。
7. Job 被送入 Processing Queue。
8. GET /api/v1/images/batches/{batchId}/status 查得正式 aggregate 結果。
9. Response / Log 不洩漏敏感 internal detail。

# 5. Validation Failure E2E

依已核准規格選擇 invalid extension / invalid signature / oversize / empty request 等案例。

驗證：
- API 回傳預期 client error。
- 不建立 Batch / Image / ProcessingJob。
- 不留下 Storage file。
- 不 Enqueue。
- response 安全。

# 6. Storage Failure Before Transaction

模擬：

```text
A Save Success
B Save Failure
```

驗證：
- A 被 Compensation。
- 若 Transaction 尚未建立，不得誤呼叫 Rollback。
- DB 無新 Batch / Image / Job。
- Queue 無新 Job。
- failure 可安全觀察。

# 7. Persistence Failure E2E

模擬：

```text
Storage Save Success
Begin Transaction
Database Save Failure
```

驗證：
- Transaction Rollback。
- Storage Compensation。
- DB 無新資料。
- Queue 無新工作。
- original failure 保留。
- cleanup failure 不覆蓋 original failure。

# 8. Commit Failure E2E

模擬：

```text
SaveChanges Success
Commit Failure
```

驗證：
- 未 Commit 資料不視為成功。
- 必要 rollback / cleanup 執行。
- Storage Compensation。
- Queue 不執行。
- API / log 行為符合規格。

# 9. Queue Failure After Commit

模擬：

```text
Database Commit Success
Queue Enqueue Failure
```

驗證：
- 不 Rollback。
- 不 Storage Compensation。
- Batch / Image / ProcessingJob 保留。
- Original File 保留。
- Batch Status 可查到持久化資料。
- Queue failure 被安全記錄。
- 不宣稱自動 recovery。

# 10. Partial Queue Enqueue Failure

```text
Job A Enqueue Success
Job B Enqueue Failure
```

驗證：
- 已 Commit DB 全部保留。
- Storage 全部保留。
- 不刪除 Job A。
- 不刪除 Job B DB record。
- 不 Rollback。
- failure 可被追蹤。

# 11. Cancellation E2E

## 11.1 Cancellation Before Commit

- cancellation 向下傳遞。
- 未 Commit DB 不保留。
- 對本次成功存檔逐一嘗試 compensation；cleanup failure 安全記錄，不覆蓋 original exception。
- Queue 不執行。

## 11.2 Cancellation After Commit / During Enqueue

依 TASK-09 最終核准規格。

不得因 cancellation 刪除已 Commit Storage / DB 資料。

# 12. Database Isolation Verification

所有 transaction-related integration tests 必須使用真實 SQL Server。

重要驗證使用新的 DbContext 重新查詢，避免 Change Tracker 誤判。

# 13. Storage Isolation Verification

Storage tests 必須使用獨立 temporary directory。

不得使用真實使用者圖片資料夾或依賴固定開發機路徑。

# 14. Queue Isolation Verification

每個測試使用獨立 queue / fixture。

不得依賴測試順序或 arbitrary delay。

# 15. Existing Data Protection

測試：

```text
Existing committed Batch A
+
New Upload B fails
```

驗證：
- Batch A 不受影響。
- B 不留下未 Commit persistence。
- Compensation 只處理 B 的 Storage Objects。

# 16. Concurrent Request Smoke Test

建立最低限度並行驗證。

驗證：
- BatchId 不衝突。
- Storage key 不衝突。
- Image / Job identity 正常。
- Request 間不互相 rollback。
- Compensation 不刪除其他 request 的檔案。

本 TASK 不宣稱完成高併發 / 壓力測試。

# 17. API Contract Verification

確認：
- Route 與規格一致。
- Request content type 正確。
- Success HTTP status 正確。
- Error status mapping 正確。
- Response DTO 不暴露 internal entity。
- Error response 不暴露 internal exception detail。

# 18. Observability Verification

確認主要事件具安全追蹤能力：
- Upload failure
- Rollback
- Compensation failure
- Commit
- Queue enqueue failure

log 不得包含 Stack Trace、SQL Detail、OS absolute path、JWT、Authorization Header 或 raw binary。

# 19. Regression Suite

必須執行：

```text
dotnet restore
dotnet build
dotnet test
```

不得只執行 TASK-12 新增測試。

# 20. Flaky Test Check

Integration / E2E tests 不得依賴：
- Thread.Sleep
- 固定 timing 猜測
- test execution order
- shared mutable global state

若涉及 async queue，使用 deterministic synchronization / bounded timeout。

# 21. Acceptance Matrix

| Scenario | API | Storage | DB | Queue | Status Query | Logging |
|---|---|---|---|---|---|---|
| Single Upload Success | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Multi Upload Success | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Validation Failure | ✓ | ✓ | ✓ | ✓ | - | ✓ |
| Storage Failure | ✓ | ✓ | ✓ | ✓ | - | ✓ |
| DB Save Failure | ✓ | ✓ | ✓ | ✓ | - | ✓ |
| Commit Failure | ✓ | ✓ | ✓ | ✓ | - | ✓ |
| Queue Failure After Commit | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Cancellation Before Commit | ✓ | ✓ | ✓ | ✓ | - | ✓ |

補充驗證 workflow 必填 / 非法值、failure category mapping、error.traceId、zero-count progress、failure events 不重複。Queue failure 後以 fixture 已知 BatchId 查詢，不為測試擴充 API error 欄位。使用 deterministic synchronization / bounded timeout，不新增 production feature 或 automatic recovery。

# 22. Documentation Verification

檢查：
- System-Level Specification
- MOD-01 Specification
- TASK-01 ～ TASK-12
- API contract
- Database schema references

是否與最終 implementation 一致。

若是 policy / contract 差異，必須回到正確 System-Level / Module-Level 文件修正。

# 23. 不得實作的內容

TASK-12 不包含：
- 新 feature
- Background Worker
- Image Analysis
- Metadata Extraction
- Naming
- Retry Scheduler
- Outbox Pattern
- Message Broker
- Cloud Storage
- Load Testing Platform
- Production deployment
- Frontend

# 24. Deliverables

```text
1. Integration / E2E Tests
2. MOD-01 Acceptance Result
3. Regression Test Result
4. Known Limitations
5. Remaining Risks
6. Handoff Notes for next Module
```

# 25. Acceptance Criteria

- [ ] Single-file Upload E2E 通過。
- [ ] Multi-file Upload E2E 通過。
- [ ] Validation failure 無 side effect。
- [ ] Storage failure compensation 正確。
- [ ] Database failure rollback + compensation 正確。
- [ ] Commit failure cleanup 正確。
- [ ] Queue failure after Commit 保留 DB / Storage。
- [ ] Cancellation 行為符合 TASK-09。
- [ ] Batch Status 可查詢已 Commit 資料。
- [ ] Logging 符合安全規則。
- [ ] Existing committed data 不受 failed request 影響。
- [ ] Concurrent request smoke test 通過。
- [ ] SQL Server integration tests 通過。
- [ ] Storage integration tests 使用 temporary directory。
- [ ] API integration tests 通過。
- [ ] 無新增 flaky timing dependency。
- [ ] `dotnet build` 成功。
- [ ] 完整 `dotnet test` 成功。
- [ ] TASK-01～TASK-11 無 regression。
- [ ] MOD-01 規格與最終 implementation 一致。

# 26. Definition of Done

TASK-12 完成代表 MOD-01 Backend Acceptance 通過。Frontend / Dashboard UI、Worker 真正 processing、Image Analysis、Retry / Recovery runtime 為後續 Module / dependency acceptance；不得僅因這些尚未完成而判 backend BLOCKED，也不將其標記通過或宣稱整個產品完成。

系統已能：

```text
接收 Upload
→ 驗證
→ 儲存
→ 建立 DB records
→ 建立 Processing Jobs
→ Commit
→ Enqueue
→ 查詢 Batch 狀態
→ 追蹤主要失敗
```

這不代表後續 Background Processing Module 已完成。

# 27. Agent Implementation Rules

1. 實作前先完成 `pre-implementation-review`。
2. TASK-12 必須以 TASK-09～TASK-11 最終核准規格為基準。
3. 不得在 Acceptance 階段自行創造新的 business behavior。
4. 發現規格缺口必須回報正確責任層級。
5. Transaction 測試必須使用真實 SQL Server。
6. Storage 測試必須隔離。
7. E2E 測試不得依賴不穩定 timing。
8. 必須執行完整 regression suite。
9. 完成後提供 MOD-01 最終 Acceptance Result。

# 28. 完成回報格式

```text
TASK-12 / MOD-01 Acceptance Result

1. Modified Files
2. E2E Scenarios Added
3. Integration Scenarios Added
4. Acceptance Matrix Result
5. Regression Test Result
6. dotnet build Result
7. dotnet test Result
8. Specification Consistency Result
9. Known Limitations
10. Remaining Risks
11. MOD-01 Backend Status: PASS / BLOCKED; dependency acceptance: deferred
12. Handoff Notes for Next Module
```
