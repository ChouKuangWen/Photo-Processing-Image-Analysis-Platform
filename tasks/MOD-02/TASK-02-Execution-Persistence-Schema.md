# TASK-02 — 執行資料持久化與 Migration

**Task ID:** MOD-02-TASK-02  
**Module:** MOD-02 Processing  
**Task Name:** 執行資料持久化與 Migration  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

以 SQL Server 實現四個核准執行表，保留 MOD-01 原表與資料。

## 2. Dependencies / Delivery State

- [TASK-01](TASK-01-Processing-State-Model.md)：相關 Scope、契約及測試證據已交付。

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
重點來源：Database-Schema §2～§6、§9 DependencyWait；MOD-02 §2.1～§2.3、§1.18～§1.20。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Infrastructure/Persistence/PhotoPlatformDbContext.cs`
- `src/PhotoPlatform.Infrastructure/Persistence/Configurations/ProcessingJobConfiguration.cs`
- `src/PhotoPlatform.Infrastructure/Migrations/20260917025957_InitialUploadPersistence.cs`
- `tests/PhotoPlatform.IntegrationTests/Persistence/UploadPersistenceTests.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 src/PhotoPlatform.Domain/Entities/ImageProcessingBinding.cs
- 新增 src/PhotoPlatform.Infrastructure/Persistence/Configurations/{ProcessingRun,ProcessingStepCheckpoint,ProcessingLog,ImageProcessingBinding}Configuration.cs
- 修改 PhotoPlatformDbContext.cs；新增 Infrastructure/Migrations/ 下 additive Migration 及更新 PhotoPlatformDbContextModelSnapshot.cs
- 新增 tests/PhotoPlatform.IntegrationTests/Processing/ProcessingSchemaTests.cs；必要時新增隔離 fixture
- 精準調整 UploadPersistenceTests.cs 的完整 DB 規模斷言，不刪除原三表契約測試

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Domain 僅補 binding representation；Infrastructure 擁有 EF／DDL；Application／API 不接觸 SQL。測試使用真實 SQL Server。

## 7. Implementation Requirements

1. 逐欄對照正式 Schema 的型別、長度、Unicode、nullability、default、PK、FK、CHECK、索引；不得僅依 Entity 自動推導。
2. processing_runs：UNIQUE(JobId,RunNo)、RunNo 1～4 與 Reason 配對、每 Job 最多一個 Pending／Active 未關閉 Run。
3. processing_step_checkpoints：UNIQUE(RunId,Step)、UNIQUE(RunId,StepOrder)、StepOrder>0、RetryOrdinal 0～3、Status／ResultStatus CHECK；版本及 fingerprint 不填未知 default。
4. processing_logs：BIGINT IDENTITY、AttemptNo>0、ResumeNo>=0、DurationMs NULL 或 >=0、兩組 invocation unique、每 checkpoint 最多一個 Running；依 §5 加 Image／Job Timestamp DESC,Id DESC 查詢索引及 checkpoint FK 索引。
5. Log 不加 physical Step／RunId／RetryCount 欄位；Step／Run 經 checkpoint 查詢。FK 值彼此業務一致性仍由交易 writer 檢查。
6. image_processing_bindings：ImageId PK、PrimaryJobId UNIQUE、核准欄位與 NoAction FK；不為原 Job／Image 加 composite FK。
7. 納入 §9 已核准等待三欄，三者全 NULL 或全非 NULL、deadline>start；Waiting 僅 VERSION_ANALYSIS/Pending。跨表 Run／Job／無 Running 條件留給 TASK-10，不能以單列 CHECK 宣稱已保證。
8. 所有原三表 Mapping、unique、NoAction、既有 Migration 不變。檢查生成 diff 不含原表重建／未授權 ALTER／資料刪除。
9. 既有 Migration_CreatesApprovedTablesAndConstraints 測試假設一個 Migration、三表、三 FK；改為保留原三表 scoped 驗證並另外驗證新增 schema，不以刪除斷言消除回歸。

## 8. Database / Migration / Transaction Boundary

只新增 processing_runs、processing_step_checkpoints、processing_logs、image_processing_bindings，等待欄位隨本 Migration 建立。先確認目標測試 DB 無既存不相容表；有差異停止。建立 Migration 不等於授權套用正式 DB。不得自動 backfill 猜測 Primary Job。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

DB 拒絕非法 unique／CHECK／FK；安全回報，不暴露 SQL 或 credentials。遷移／既存資料衝突需人工確認，不 reset DB、不改原始 Migration。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 可驗證的 Entity 邊界沿用 TASK-01；Mapping metadata assertions 可作輔助，不能替代 SQL tests。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 使用隔離真實 SQL Server，驗證全新 DB migration chain 與只有 InitialUploadPersistence 且含 Batch/Image/Job 的 DB 升級。
- 讀 sys catalog 核對四表欄位／索引／filter／CHECK／FK／NoAction；原三表資料、38 欄等原有契約保持。
- 併發插入未關閉 Run／Running Log 只有一筆成立；terminal 歷史允許保留多筆。
- 驗證 RunNo／Reason、ordinal、duration、等待三欄及關聯的合法與非法案例；跨表語意不冒充 schema 已保障。
- 測試資料庫建立與清理先核對隔離；不以 EF Core InMemory 替代。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] Schema 與核准文件逐項對照，原表無非預期變更。
- [ ] 升級後 MOD-01 資料可讀寫；新增唯一性在 SQL Server 有實際證據。
- [ ] 無新的 production Migration baseline、資料修復或秘密設定。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] Mapping／Migration／受影響測試一起完成；實際 SQL 驗收未執行不得標完成。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

Mapping、additive Migration、snapshot 與 schema regression tests 同一提交；feat(mod-02): add processing execution persistence。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

T1～T5 寫入流程、模組結果表、診斷持久表、Job owner/lease/rowversion、新 provider。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

