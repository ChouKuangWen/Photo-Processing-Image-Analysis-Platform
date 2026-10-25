# TASK-10 — VERSION_ANALYSIS 持久依賴等待

**Task ID:** MOD-02-TASK-10  
**Module:** MOD-02 Processing  
**Task Name:** VERSION_ANALYSIS 持久依賴等待  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

只實作已核准VERSION_ANALYSIS的T2-W等待、恢復檢查與T2原子喚醒。

## 2. Dependencies / Delivery State

- [TASK-06](TASK-06-Outcome-History-Aggregates-Events.md)：相關 Scope、契約及測試證據已交付。
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
重點來源：MOD-02最終dependency wait/T2-W；MOD-08最終F-06；Database-Schema §9 DependencyWait。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Infrastructure/Persistence/PhotoPlatformDbContext.cs`
- `src/PhotoPlatform.Application/Interfaces/IProcessingQueue.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Processing/DependencyWaitCoordinator.cs 及Recommendation dependency reader abstraction
- 新增 Infrastructure/Processing/DependencyWaitScanner.cs、Infrastructure/Persistence/DependencyWaitStore.cs
- 擴充Processing composition與TASK-08的等待分流；新增UnitTests/Processing/DependencyWaitTests.cs、IntegrationTests/Processing/DependencyWaitPersistenceTests.cs

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Application核對依賴資格；Infrastructure以DB UTC/短交易和scanner協調；Domain不新增Job狀態；API不提供等待調整端點。

## 7. Implementation Requirements

1. 僅在先前Step完成、Run Active/Job Processing、VERSION_ANALYSIS checkpoint Pending且無Running時開始等待，不先Claim未準備好的Step。
2. 首次T2-W保存DB UTC started/deadline=start+本次配置（預設120秒>0）、Waiting。等待不占Worker、不建Running、不耗Retry。
3. 依賴scanner每5秒核對，與Pending scanner職責分開：這裡可處理Processing/Active的特定等待資格，但不當作新JobClaim或一般Processing回收。
4. 鎖Job/Run/checkpoint，核對deadline/依賴/無Running；ready時Waiting→Resolved、checkpoint Processing、唯一Running/Attempt在同T2 commit，才調adapter。
5. DB now>=deadline時優先Expired，checkpoint Failed/DEPENDENCY_NOT_READY Permanent、Run/Job Failed與primary delta同管理交易；無invocation不偽造History，commit後通知。
6. Started/deadline不因配置變更、重複通知、scanner或重啟重設；停機時間算入等待。Resolved/Expired時間證據保留。
7. Shutdown後確認舊程序已停才重驗等待；無Running Waiting保持原Run與deadline，不T4泛用回收、不新Run。未知副作用仍阻擋。
8. 依賴必須同snapshot/version且Completed，缺分不當0；永久失敗依MOD08處理為Permanent Failed。Concrete Recommendation輸入查詢由所屬模組交付，核心可用測試reader。
9. Waiting與執行互斥由SQL條件更新及唯一索引保護。跨Batch向前掃描，不能占滿Worker等其他Job。
10. 不對VISUAL_SIMILARITY啟用此契約；N-02的budget/timeout/排程不得引用本120秒自行決定。

## 8. Database / Migration / Transaction Boundary

使用TASK-02等待三欄，無新Migration/等待表/JobStatus。T2-W與T2/T3共用既有writer及Primary門檻。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

等待超時是管理性Permanent DEPENDENCY_NOT_READY，不是Step timeout transient；取消不消耗Retry。DB未知不能假ready或重設deadline。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 未就緒建立一次期限、重複掃描不重設、ready/到期同時發生到期優先。
- 等待無Running/不占Worker/不計Retry；版本錯誤/永久依賴失敗；VISUAL明確不適用。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實SQL多scanner同時wake僅一Running，Waiting/Processing互斥。
- 重啟/配置更改/停機跨deadline期限不變；Expired關Run/Job/aggregate原子且無偽造Log。
- 與StartupRecovery及Pending scanner共同host測試不重複T1/T2；其他Batch可前進。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 僅VERSION_ANALYSIS生效；N-02仍未決。
- [ ] 等待資格/期限/結果持久可重驗，無Worker starvation或重複wake。
- [ ] 真實Recommendation adapter未交付的限制明列。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 核心T2-W與SQL concurrency驗證通過；不因此解除Analysis/Full阻擋。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

Wait store/scanner/coordinator及測試一組；feat(mod-02): persist recommendation dependency waits。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

VISUAL等待、部分候選snapshot、重新排序Workflow、無期限等待、新Job狀態、Recommendation演算法。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

