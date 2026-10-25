# TASK-08 — Startup Recovery 與 Crash Reconciliation

**Task ID:** MOD-02-TASK-08  
**Module:** MOD-02 Processing  
**Task Name:** Startup Recovery 與 Crash Reconciliation  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

在舊程序已停止的明確前提下核對持久證據，安全恢復同Run而不重複副作用。

## 2. Dependencies / Delivery State

- [TASK-07](TASK-07-Retry-Timeout-Cancellation.md)：相關 Scope、契約及測試證據已交付。

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
重點來源：MOD-02 §1.11、§1.18～§1.22；SYS 已核准MVP執行限制；Database-Schema §9 ownership；MOD-04最終F-02。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Infrastructure/Persistence/PhotoPlatformDbContext.cs`
- `src/PhotoPlatform.Application/Interfaces/IProcessingQueue.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Processing/StartupRecoveryService.cs、部署前提與result reconciliation抽象
- 擴充 Infrastructure/Persistence/ProcessingExecutionStore.cs 的 T4及恢復查詢
- 新增 UnitTests/Processing/RecoveryTests.cs、IntegrationTests/Processing/CrashRecoveryTests.cs

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Application 分類crash與准入；Infrastructure核對durable資料/T4；Domain不判liveness；host順序交TASK-09。

## 7. Implementation Requirements

1. 同工作集合僅一個執行Instance。部署控制須確認舊受管理process退出/instance Terminated且無其他執行者；health timeout/readiness false/PID檔/grace到期均不足。
2. 未確認停止前提：不回收Processing、不開新Claim/補排程，安全回報deployment prerequisite；不自行kill或設liveness偵測API。
3. 啟動掃描先核對Binding/Job/Run/checkpoint/History及副作用。Pending無Run可後續T1建Initial；Pending/Pending Run沿原輸入；Completed不重排；Failed只走人工Retry。
4. T4僅安全恢復Processing/Active：可重算Running→Interrupted、checkpoint→Pending、Job→Pending、同Run→Pending、primary投影同transaction；RunNo/RetryOrdinal/NextRetryAt/首次StartedAt/成功結果保留。
5. 只讀/純計算且原input版本可重建才可重算；兼容SQL result重用；commit未知先查DB；artifact依OperationId/receipt/Inspect核對。Unknown副作用保持阻擋，不先改Interrupted再覆寫Success。
6. 未知副作用可保持Processing/Active與未確認Running，這不是runtime liveness證明，也不可交Pending scanner回收。
7. 下一次T1建立新Resume invocation，不增加Retry；已Completed checkpoint只在結果相容時跳過。RetryWaiting保留期限，即使重啟配置改變也不重設。
8. VERSION_ANALYSIS無Running的持久Waiting Run屬TASK-10專用分流：保持原等待資格/deadline，不按泛用T4回收或建立新Run；尚未交付TASK-10時不能啟用該執行路徑。
9. 完成分類後交TASK-09先啟動Consumer再分批通知；未知Binding/資料矛盾只阻擋相應範圍，不修復整庫。

## 8. Database / Migration / Transaction Boundary

T4及核對後補T3均透過既有writer；無新Migration/owner/lease。外部副作用不在SQL rollback保證內。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

DB unavailable不猜資料；terminal Job/Active Run等矛盾回報、不盲目關Run。恢復失敗保留可核對狀態；原token取消不授權破壞性cleanup。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 停止證據有效/缺失/僅health timeout；停止前提不足零Claim/回收。
- 純計算、已提交SQL、commit未知、artifact Complete/Missing/Unknown依核准adapter證據分類。
- RetryWaiting/已完成checkpoint/未知Binding/矛盾History、waiting Run分流。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實SQL fault injection涵蓋T1前/中/後、Step後T3前、T3commit不明、T3成功通知前、下一T2前、RetryWaiting shutdown、T5後enqueue前。
- 確認Resume沿同Run/ordinal，舊Log保留Interrupted且新Attempt唯一，delta不重複。
- 未知副作用/缺證據零新執行；測試替身的Inspect不當真實Storage驗收，後者留TASK-12/13。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 安全者可恢復，未知者不被誤判Success/Failed或重跑。
- [ ] 停止證據與副作用確認分開檢查。
- [ ] 恢復不新增Run、不消耗新Retry，不回收Completed/Failed。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 核心Recovery與SQL crash tests通過；具體部署證據接點及未交付adapter限制清楚列出。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

Recovery/T4與crash tests一組；feat(mod-02): reconcile interrupted processing runs。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

自動kill、rolling overlap、多Instance fencing、artifact cleanup/reapply、資料修復API。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

