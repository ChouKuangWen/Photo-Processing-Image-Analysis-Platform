# TASK-11 — Processing API 與最小 Prerequisite 診斷

**Task ID:** MOD-02-TASK-11  
**Module:** MOD-02 Processing  
**Task Name:** Processing API 與最小 Prerequisite 診斷  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

交付核准Create/Get/Retry API及唯讀prerequisite診斷，不擴大Monitoring。

## 2. Dependencies / Delivery State

- [TASK-03](TASK-03-Primary-Binding-Admission.md)：相關 Scope、契約及測試證據已交付。
- [TASK-07](TASK-07-Retry-Timeout-Cancellation.md)：相關 Scope、契約及測試證據已交付。
- [TASK-09](TASK-09-Worker-Pending-Scanner-DI.md)：相關 Scope、契約及測試證據已交付。

- **已存在**：第 4 節為 MOD-01/Repository 基線，不代表本 TASK 新功能已存在。
- **本 TASK 新增**：第 7 節範圍；前置 TASK 的類別與方法不得另建重複版本。
- **後續負責**：第 7、11、15 節指定的後續整合／驗收不能在此冒稱完成。
- **待決策／外部依賴**：N-01 引擎/版本、N-02 VISUAL 候選調度仍未決；不阻擋無關核心，不能以測試替身解除真實 Workflow 依賴。




## 3. Required Reading / Specification Sources

依序閱讀並核對版本：
1. `AGENTS.md`
2. `Requirements.md`
3. `docs/System-Level-Specification.md`
4. `docs/modules/MOD-02-Processing-Spec.md`
5. 本節涉及的相關 Module 最終契約。
6. `docs/Database-Schema.md` 與相關 API 章。
7. 本 TASK 及前置 TASK。
8. 第 4 節 Existing Code／Tests。

`docs/INDEX.md` 只導航；`docs/reviews/Specification-Revision-Review.md` 用於核對決策與歷史，不覆蓋正式契約。
重點來源：MOD-02 §1.17、§3.1～§3.6；SYS共用error contract；MOD-09最終F-08。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Api/Http/ApiExceptionMiddleware.cs`
- `src/PhotoPlatform.Api/Contracts/ApiError.cs`
- `src/PhotoPlatform.Api/UploadApiComposition.cs`
- `src/PhotoPlatform.Application/Interfaces/IBatchStatusQuery.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Interfaces/IProcessingService.cs、Services/ProcessingService.cs、對應DTO/受控exceptions
- 新增 API/Controllers/ProcessingJobsController.cs、ProcessingPrerequisitesController.cs
- 必要擴充ApiExceptionMiddleware.cs及ProcessingApiComposition.cs，不改Upload映射
- 新增UnitTests/Processing/ProcessingApiTests.cs、IntegrationTests/Processing/ProcessingApiPersistenceTests.cs

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

API只處理HTTP/mapping；Application驗證/調用T5等服務；Infrastructure查詢/unique判定；Domain規則不放Controller。

## 7. Implementation Requirements

1. 只建立POST /api/v1/processing/jobs、GET /api/v1/processing/jobs/{jobId}、POST /api/v1/processing/jobs/{jobId}/retry，request/response保持MOD02 §3。
2. Create驗證順序：request/workflow/id→Image存在→Batch存在→Image.BatchId一致→duplicate→binding→建立secondary；原規格預期400/404/409/503不得自行換碼。
3. 四種既有Job狀態duplicate均409 PROCESSING_JOB_ALREADY_EXISTS，不新增/不Enqueue/不重設。併發INSERT只將確認的(ImageId,Workflow) unique映射duplicate，其他SQL錯誤安全INTERNAL_ERROR。
4. secondary建立前binding必須確認；不改Upload aggregate。Create持久Job後commit才Enqueue；queue失敗保留Pending供補排程，不建立假的Running Run。
5. Retry使用TASK-07 T5，成功202同JobId/Pending；不符合資格409 INVALID_STATE_TRANSITION。Get不存在404 PROCESSING_NOT_FOUND。
6. 所有error沿success:false/error.code/message/traceId；MOD02 Queue error為500 QUEUE_ERROR，Worker unavailable為503 WORKER_UNAVAILABLE。MOD01 Queue failure仍INTERNAL_ERROR，不互相覆蓋。
7. GET /api/v1/processing/batches/{batchId}/prerequisites正常blocked回200 data含batchId/checkedAt/ready:false/reasons/resolutionHint/count/truncated；不存在404/BATCH_NOT_FOUND，provider無法核對503/PROCESSING_PREREQUISITE_UNAVAILABLE。
8. 完整集合判斷ready，最多顯示100理由，依reasonCode/ImageId/JobId穩定排序；原因限BINDING_MISSING/BINDING_AMBIGUOUS/RELATIONSHIP_MISMATCH/COLLECTION_UNCONFIRMED/CONTEXT_MISSING/SIDE_EFFECT_UNCONFIRMED/AGGREGATE_INCONSISTENT。
9. 診斷只讀，不選主/修復/排程/清理、不加診斷表或Status。驗證／准入拒絕時零Job/Run/checkpoint/aggregate/queue副作用；既有duplicate/mismatch優先順序不被503覆蓋。這不包含成功 Commit 後的 Queue 失敗：依第4、5項及 T5 契約保留已提交的 Job／Run／aggregate，不為錯誤回應回滾或補償。
10. 不將PLANNED或missing adapter視為Workflow成功；具體准入沿正式Context契約，未知外部HTTP情況不自行新建error code。

## 8. Database / Migration / Transaction Boundary

Create使用短交易、T5沿TASK-07；只讀Query不可SaveChanges。無Migration/新API欄位/Log查詢新契約。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

Cancellation傳遞，commit後不撤銷已成立資料；安全message不包含SQL、path、secret、raw exception。HTTP 202只代表接受，不表示背景完成。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 各validation順序/HTTP code/traceId與response形狀；四狀態duplicate；queue與binding錯誤。
- 診斷超100原因仍查完整集合、穩定排序、count/truncated；query零寫入/零enqueue。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實SQL同Image/Workflow併發Create僅一筆、敗者409；關聯不符拒絕、secondary不改aggregate。
- T5+API上限與狀態資格；commit後enqueue失敗Pending仍在。
- 原Upload/BatchStatus endpoints回歸，診斷讀寫隔離；測試host不啟用未交付Workflow。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 端點與錯誤對照完整，無跨API boundary誤改。
- [ ] 診斷無副作用、沒有force unblock。
- [ ] 真實SQL unique race有證據；不以mock SQL exception宣稱已驗證。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] Processing API與最小診斷完成；完整MOD09/SignalR不在完成聲明。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

API/diagnostic與測試一組；feat(mod-02): expose processing job APIs and prerequisites。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

修改MOD01 API、Auth/RBAC、修復/unblock/cleanup/Cancel API、完整History分頁/SignalR/dashboard。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

