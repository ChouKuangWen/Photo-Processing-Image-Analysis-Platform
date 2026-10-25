# TASK-12 — 真實模組 Adapter 與 Naming Input Handoff

**Task ID:** MOD-02-TASK-12  
**Module:** MOD-02 Processing  
**Task Name:** 真實模組 Adapter 與 Naming Input Handoff  
**Status:** DEPENDENCY_PENDING  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

在所屬模組交付後接通DuplicateDetection及Naming真實Workflow；MOD02只負責adapter與execution handoff。

## 2. Dependencies / Delivery State

- [TASK-04](TASK-04-Workflow-Context-Contracts.md)：相關 Scope、契約及測試證據已交付。
- [TASK-05](TASK-05-Atomic-Claim-Invocation-Start.md)：相關 Scope、契約及測試證據已交付。
- [TASK-06](TASK-06-Outcome-History-Aggregates-Events.md)：相關 Scope、契約及測試證據已交付。
- [TASK-07](TASK-07-Retry-Timeout-Cancellation.md)：相關 Scope、契約及測試證據已交付。
- [TASK-08](TASK-08-Startup-Recovery-Reconciliation.md)：相關 Scope、契約及測試證據已交付。
- [TASK-09](TASK-09-Worker-Pending-Scanner-DI.md)：相關 Scope、契約及測試證據已交付。
- [TASK-10](TASK-10-Recommendation-Dependency-Wait.md)：相關 Scope、契約及測試證據已交付。
- [TASK-11](TASK-11-Processing-API-Prerequisite-Diagnostics.md)：相關 Scope、契約及測試證據已交付。

- **已存在**：第 4 節為 MOD-01/Repository 基線，不代表本 TASK 新功能已存在。
- **本 TASK 新增**：第 7 節範圍；前置 TASK 的類別與方法不得另建重複版本。
- **後續負責**：第 7、11、15 節指定的後續整合／驗收不能在此冒稱完成。
- **待決策／外部依賴**：N-01 引擎/版本、N-02 VISUAL 候選調度仍未決；不阻擋無關核心，不能以測試替身解除真實 Workflow 依賴。
- 本 TASK 為 DEPENDENCY_PENDING：MOD-03/04/05 的真實交付與 Storage contracts 依第 7 節分 Workflow 確認。



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
重點來源：MOD-02 §1.18～§1.22及最終F-07；MOD-03最終F-01/F-05、MOD-04最終F-02、MOD-05最終契約；Database-Schema §9。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Application/Interfaces/IFileStorageService.cs`
- `src/PhotoPlatform.Application/Services/UploadService.cs`
- `src/PhotoPlatform.Domain/Entities/Image.cs`
- `src/PhotoPlatform.Infrastructure/Persistence/PhotoPlatformDbContext.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Processing/Adapters/ 下Metadata/Geo/Hash/Exact/Naming/Artifact adapters
- 新增或接入 Infrastructure/Persistence/ 下所屬模組的result transaction adapters；沿用已交付reader/artifact services
- 新增 IntegrationTests/Processing/DuplicateWorkflowTests.cs、NamingWorkflowTests.cs、ModuleResultAtomicityTests.cs

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

MOD02 Application協調，Infrastructure使所屬模組result參與T3；MOD03/04/05 owns算法/input/result；API不擴充Upload欄位；Domain不得塞各演算法到Worker。

## 7. Implementation Requirements

1. 外部依賴：MOD03版本化Metadata/Geo Outcome與result writer；MOD04持久NamingRequest/Item、原圖reader、artifact Create/Inspect/receipt及reservation；MOD05版本化SHA256/EXACT結果與SQL ownership。交付版本/介面/測試證據需可核對。
2. 可先按Workflow完成子範圍：DuplicateDetection依MOD05+reader；Naming依MOD03/04+reader。任一依賴未交付保持DEPENDENCY_PENDING，不以新fake production adapter取代。
3. reader核對原內容SHA256，不捏造Storage version；跨image-step-version result key/owner/rowversion與T3共同驗證。JobClaim成功不代表共享結果執行權。
4. NamingRequest與Item已持久且對應Image/Job/Batch、固定Ordinal/Sequence/OriginalExtension/ResolvedInput/RuleVersion；Run可從受控reference重建，無Template不Claim、不修改Upload input。
5. MOD04流程Prepared+reservation→Create artifact→Inspect receipt/source/target→T3 result/checkpoint/History/Primary投影；SQL與Storage不是一個rollback transaction。
6. 同OperationId同payload結果一致、不同payload拒絕；provider conditional no-overwrite。已建artifact未T3須核對後補T3，不再建；Unknown維持NeedsReconciliation/阻擋。
7. 原bytes/StoredPath不變；只有Primary可投影NewFileName及既有全域Metadata，secondary保存獨立版本結果。
8. Shutdown同Run/input/OperationId；ManualRetry新Run沿原Naming input並Inspect重用，不重新分配第二份artifact；collision Skip只限模組明確Item終態，NAMING/FILE_RENAME按核准Skipped完成。
9. 各模組不能自行疊加transient retry/History writer。metadata+Geo各checkpoint保留；MissingGPS/InvalidGPS/耗盡/auth-config分類核對。
10. 先核對provider case/Unicode TargetKey normalization與receipt證據；不以memory Exists→Create或SQL預設collation代替collision契約。未交付的細節由所屬模組核准，不在MOD02補規則。

## 8. Database / Migration / Transaction Boundary

Metadata/Naming/hash/EXACT表及Migration由所屬模組TASK交付，不在本TASK重複建表。引用Schema §9，result更新與T3同SQL transaction；operation prepared/reservation依MOD04先行持久。缺必要mapping停止受影響整合。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

來源/版本/receipt不明不guess Success或Failed再Retry；不得自動cleanup/reapply/刪artifact或原圖。Storage錯誤與SQL保存重試分開。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- adapter Outcome、版本/reference拒絕、缺input零呼叫、primary/secondary投影。
- 同operation retry、顯式collision Skip、auth/config fatal；adapter不內建第二個retry loop。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實SQL+隔離Storage+真實已交付adapter：DuplicateDetection與Naming各自全鏈驗收；不以fake成果宣稱真實Workflow完成。
- 結果/owner/rowversion/checkpoint/History/aggregate共同commit，各寫入點故障rollback；同key並行不重複副作用。
- artifact已建立但SQL未保存、receipt Unknown、來源變動、collision/no-overwrite、shutdown/ManualRetry重用；原檔不變。
- Naming input在新context/程序恢復後可重建，不只memory引用；未交付項維持pending。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 分別列出DuplicateDetection/Naming是否有真實通過證據。
- [ ] 無Template/未知receipt零副作用；原Upload契約不變。
- [ ] 沒有以核心test double解除外部依賴。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 僅在全部所選Workflow依賴及真實驗收完成後結案；部分交付記錄子範圍，不把整個TASK標完成。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

按Workflow分開提交adapter與對應tests，例如 integrate duplicate workflow／integrate naming handoff；不得混入其他模組算法。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

自行實作MOD03/04/05算法與持久input API、pHash/Analysis/Full、原檔rename、cleanup/reapply、解決N-01/N-02。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

