# TASK-14 — Analysis／Full 完整 Workflow 整合

**Task ID:** MOD-02-TASK-14  
**Module:** MOD-02 Processing  
**Task Name:** Analysis／Full 完整 Workflow 整合  
**Status:** BLOCKED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

記錄完整Analysis/Full未來整合範圍及解除條件；當前不得開始程式開發。

## 2. Dependencies / Delivery State

- [TASK-10](TASK-10-Recommendation-Dependency-Wait.md)：相關 Scope、契約及測試證據已交付。
- [TASK-12](TASK-12-Module-Adapters-Naming-Handoff.md)：相關 Scope、契約及測試證據已交付。
- [TASK-13](TASK-13-Core-Workflow-Acceptance.md)：相關 Scope、契約及測試證據已交付。

- **已存在**：第 4 節為 MOD-01/Repository 基線，不代表本 TASK 新功能已存在。
- **本 TASK 新增**：第 7 節範圍；前置 TASK 的類別與方法不得另建重複版本。
- **後續負責**：第 7、11、15 節指定的後續整合／驗收不能在此冒稱完成。
- **待決策／外部依賴**：N-01 引擎/版本、N-02 VISUAL 候選調度仍未決；不阻擋無關核心，不能以測試替身解除真實 Workflow 依賴。


- BLOCKED：N-01、N-02 及 MOD-03～08 必要交付未齊；本文件不提供未核准部分的實作許可。

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
重點來源：SYS最終F-01～F-08/N-02；MOD-02最終F-07/F-08/N-02；Database-Schema §9/N-02；MOD03～08最終契約；集中Review N-01/N-02。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Domain/Enums/WorkflowType.cs`
- `src/PhotoPlatform.Application/Interfaces/IProcessingQueue.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 解除BLOCKED並經新Review後，才細化Application/Processing/Adapters/的Analysis/Full整合檔案
- 預期新增IntegrationTests/Processing/AnalysisWorkflowTests.cs、FullWorkflowTests.cs；不是現在建立的授權
- Migration/等待Schema如需變動須由核准決策確定，現在不預設檔案或欄位

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

MOD02只協調已交付模組；MOD03～08 owns算法/profile/result。N-01/N-02由Architecture Owner決策，不由Agent填補。

## 7. Implementation Requirements

1. N-01仍未決：Image decoder/ICC engine、精確package/native版本及平台矩陣；不得指定替代引擎、假ManifestChecksum或未核准預設ProfileVersion。
2. N-02仍未決：VISUAL候選pHash未齊的持久等待/釋放Worker/喚醒/timeout政策；不得挪用VERSION_ANALYSIS 120秒、不先改Checkpoint等待限制。
3. 其他依賴：MOD03 Metadata、MOD05 Hash/EXACT、MOD06 pHash/VISUAL、MOD07 Quality、MOD08 Recommendation；Full另需MOD04 Naming及持久input/receipt。各結果mapping/migrations/adapter驗收版本均需交付。
4. 只保留已核准順序：Analysis為EXIF→Geo→SHA256→EXACT→pHash→VISUAL→Quality→VERSION_ANALYSIS；Full追加NAMING/FILE_RENAME。正式Step識別以TASK04/規格為準，不增刪/重排。
5. 完整同版本candidate snapshot才可正式VISUAL結果，不能partial hash集合冒充完整；JobClaim不替代result ownership/unique/rowversion。
6. 沿既有T1～T5及VERSION_ANALYSIS T2-W；未核准的VISUAL等待不寫入實作。若核准後影響Schema/核心接口，先做影響分析並更新受影響TASK，不反向改規格。
7. 解除條件：N-01/N-02明確核准並同步正式規格；全部必要模組及input交付；TASK13核心簽收、相關真實Workflow交接成立；完成新的Pre-Implementation Review並取得實作批准。
8. 解鎖後補齊可執行的detail/test向量；目前不凍結未核准容量/timeout/演算法/新API。規格已有性能及golden/平台驗收門檻由所屬模組負責，不冒稱已PASS。

## 8. Database / Migration / Transaction Boundary

僅引用已核准結果及執行Schema。N-02可能影響waiting mapping，但本TASK不批准新增欄位/狀態/表；決策後才能安排Migration與SQL驗收。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

Unknown profile/input/候選狀態不執行、不作成功或缺分0；永久/暫時錯誤沿各模組正式Outcome，不能在BLOCKED期間臆定策略。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 目前僅定義驗收需求，不執行：Workflow順序、版本一致、缺adapter/input拒絕、核准Skipped。
- N-02新增測試必須等政策核准，不能現在寫假定deadline或排程測試。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 解鎖後真實SQL/Storage/已交付模組完整鏈，驗證結果owner/version/T3 atomicity與Full artifact recovery。
- 完整candidate就緒、容量/平台/profile驗收、取消/恢復及無Worker starvation，期望值由正式決策確定。
- TASK13核心test doubles不取代本TASK完整Analysis/Full測試。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 目前BLOCKED原因與外部依賴保持明確，未自行解除。
- [ ] 未來只有正式決策+交付+新Review+人工批准齊備才可開發。
- [ ] 實作後須真實端到端證據，不能僅以Build或pHash單項通過結案。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 當前是BLOCKED規劃文件，不是Completed；未來所有解除條件及完整驗收達成後才可變更狀態。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

目前不允許程式提交；解鎖後依核准影響範圍重定整合提交邊界，不使用混入未核准契約的巨型提交。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

自行決定N-01/N-02、建立部分snapshot、改Workflow順序、先做VISUAL等待Migration、完整Export/進階Monitoring、假成功adapter。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

