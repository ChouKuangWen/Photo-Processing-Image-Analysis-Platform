# TASK-01 — Processing 狀態模型與不變量

**Task ID:** MOD-02-TASK-01  
**Module:** MOD-02 Processing  
**Task Name:** Processing 狀態模型與不變量  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

建立可獨立測試的 Job／Run／Checkpoint／History 狀態模型；不實作資料庫 Claim 或背景執行。

## 2. Dependencies / Delivery State

- 無 MOD-02 前置開發 TASK；先完成本 TASK 實作前契約核對。

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
重點來源：MOD-02 §1.5、§1.10、§1.18～§1.20；Database-Schema §3～§5。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Domain/Entities/ProcessingJob.cs`
- `src/PhotoPlatform.Domain/Enums/ProcessingJobStatus.cs`
- `src/PhotoPlatform.Domain/Enums/WorkflowType.cs`
- `tests/PhotoPlatform.UnitTests/Domain/ProcessingJobTests.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 修改 src/PhotoPlatform.Domain/Entities/ProcessingJob.cs，保留 constructor、既有欄位與初始值
- 新增 src/PhotoPlatform.Domain/Entities/{ProcessingRun,ProcessingStepCheckpoint,ProcessingLog}.cs
- 新增 src/PhotoPlatform.Domain/Enums/ 下必要的 Run／Checkpoint／Log enums，以及 Domain policies
- 新增 tests/PhotoPlatform.UnitTests/Domain/ProcessingStateTests.cs 與各新 Entity 測試

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Domain 定義狀態、識別與受控行為；Application 後續使用，不加入 EF、HTTP、Hosting；Infrastructure／API 本 TASK 不修改。

## 7. Implementation Requirements

1. Job 僅 Pending／Processing／Completed／Failed；一般 Pending→Processing→Completed/Failed，合資格人工 Failed→Pending；Processing→Pending 僅管理 Recovery 入口，不能暴露成任意狀態 setter。
2. Run 僅 Pending／Active／Completed／Failed，Reason 僅 Initial／ManualRetry。Initial RunNo=1；人工 RunNo=2～4；Recovery 沿用 Run，不建立 Recovery Reason。Completed／Failed Run 不重開。
3. Checkpoint 僅 Pending／Processing／RetryWaiting／Completed／Failed；ResultStatus 為 NULL／Success／Warning／Skipped。允許轉換逐項對照 §1.20；terminal checkpoint 不重開。
4. Log 僅 Running→Success／Warning／Failed／Interrupted／Skipped 之一；terminal 不重寫。Timestamp 不因終態更新改變；無法量測的 DurationMs 為 NULL，不捏造 0。
5. 區分 AttemptNo（checkpoint 內正整數 invocation 序號）、RetryOrdinal（0～3 邏輯槽位）、ResumeNo（同槽位由 0 開始）、Job.RetryCount（跨 Step／Run 累計首次開始的重試槽位）。Domain 模型不以 Log 筆數推算實際呼叫次數。
6. 首次 StartedAt 與 terminal CompletedAt 規則一致；同 Run Resume 保留首次 StartedAt。Job 初始 Pending、RetryCount=0、錯誤與執行時間 NULL 不變。
7. 狀態行為可表達已驗證的資格，但不能把 Domain 方法成功視為 SQL 執行權。DB 再檢查與 atomicity 交 TASK-05～08；人工 Retry 的完整分類／cap orchestration 交 TASK-07。

## 8. Database / Migration / Transaction Boundary

本 TASK 無 Migration、不加入 DbSet。Entity 資料形狀對齊核准 Schema，但不增加原 processing_jobs 欄位。資料庫唯一性與跨表不變量由 TASK-02／05 後續實現。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

非法 transition 必須可映射至 INVALID_STATE_TRANSITION；不在 Domain 決定 HTTP response。Shutdown 不是永久失敗。Recovery 資格需外部證據，Domain 不偵測程序 liveness。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 四種 Workflow 原 constructor 回歸及未知 enum 拒絕。
- 各狀態的合法／非法 transition 表格測試；Completed Job 不回 Pending。
- RunNo 1／4 邊界及 0／5 拒絕、Reason 配對；RetryOrdinal 0／3 及越界；AttemptNo>0、ResumeNo>=0。
- 同槽位 Resume 不代表新增 Retry；首次 StartedAt 不覆寫；Log 終態不可二次轉換。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 不適用：本 TASK 無 Persistence；不以 Unit Test 宣稱 SQL concurrency 已通過。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 既有 Upload Job 建立契約不變。
- [ ] 狀態規則有成功、失敗與邊界測試，未新增 Job 狀態或可任意設定 Status 的 API。
- [ ] 新 Domain 型別沒有 EF／ASP.NET Core／Channel 相依。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 純 Domain 模型及 Unit Tests 完成；不得宣稱 Worker、SQL Claim 或 runtime Recovery 完成。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

單一狀態模型與測試提交；建議訊息 feat(mod-02): define processing lifecycle state models。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

EF Mapping／Migration、SQL Claim、完整 Retry executor、Worker、API、模組演算法。N-01／N-02 不影響本 TASK。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

