# TASK-13 — Processing 核心與交接 Acceptance

**Task ID:** MOD-02-TASK-13  
**Module:** MOD-02 Processing  
**Task Name:** Processing 核心與交接 Acceptance  
**Status:** PLANNED_WITH_DEPENDENCIES  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

分開驗證核心execution可靠性及真實Workflow；不將一種證據冒充另一種完成。

## 2. Dependencies / Delivery State

- [TASK-01](TASK-01-Processing-State-Model.md)：相關 Scope、契約及測試證據已交付。
- [TASK-02](TASK-02-Execution-Persistence-Schema.md)：相關 Scope、契約及測試證據已交付。
- [TASK-03](TASK-03-Primary-Binding-Admission.md)：相關 Scope、契約及測試證據已交付。
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

- 核心驗收依 TASK-01～11；真實 DuplicateDetection/Naming 額外依 [TASK-12](TASK-12-Module-Adapters-Naming-Handoff.md)。完整 Analysis/Full 驗收屬 TASK-14，不反向阻擋本 TASK 核心階段。


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
重點來源：MOD-02 §1.22、§4.1～§4.4、§5/§7；SYS §4；Database-Schema §2～§6/§9。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `tests/PhotoPlatform.IntegrationTests/Fixtures/UploadAcceptanceFixture.cs`
- `tests/PhotoPlatform.IntegrationTests/Persistence/UploadPersistenceTests.cs`
- `tests/PhotoPlatform.IntegrationTests/Api/UploadApiHost.cs`
- `tests/PhotoPlatform.UnitTests/Infrastructure/ChannelProcessingQueueTests.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 IntegrationTests/Processing/ProcessingAcceptanceTests.cs、CrashWindowAcceptanceTests.cs
- 新增 IntegrationTests/Fixtures/ProcessingAcceptanceFixture.cs、ProcessingTestHost與受控fault injection
- 必要時新增UI就緒後的E2E tests；不得為此自建未授權UI或引入新package

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

測試橫跨API/Application/Infrastructure/Domain；不新增產品功能。Core可用受控test adapter，真實Workflow必須使用所屬模組實作。

## 7. Implementation Requirements

1. 核心驗收前置TASK01～11；真實DuplicateDetection/Naming驗收額外依TASK12。此為條件依賴，不形成TASK12反向依賴；Analysis/Full真實驗收屬TASK14。
2. 核心測試包含Upload成功Commit但通知遺失/部分失敗→Pending scanner→T1→execution→T3，證明DB/Storage不被補償刪除。
3. 覆蓋T1/T2多Consumer競爭、T1逐寫入點rollback、T3commit outcome不明、通知失敗、跨Job相同result保護與Primary aggregate無重複/遺失delta。
4. Shutdown同Run/ordinal Resume、RetryWaiting期限保留、ManualRetry上限/歷史、未知Binding/副作用零執行、舊程序停止未確認零回收。
5. VERSION_ANALYSIS Waiting讓出Worker、到期優先、restart不重設、重複wake唯一；不測未核准VISUAL等待政策。
6. 同Batch一個未知Primary阻擋全部Primary但其他Batch前進；secondary不改aggregate；新增Image使舊確認失效。
7. 真實Workflow驗收需附來源版本/DB/Storage/provider及adapter清單；fake EXIF/Hash/Artifact結果不能當完整Workflow PASS。
8. UI/Playwright user flow須待實際UI/測試環境及所需授權存在；API/host測試不能標成Playwright E2E PASS，未具備時明列未完成。
9. 建立可追溯矩陣：案例→規格→實際執行命令→passed/failed/skipped→測試基準。以前226 Unit PASS只是MOD01歷史，不作新Processing證據。
10. 測試故障不自動重設真實DB或改規格；若修正產品缺陷另依批准Scope處理。

## 8. Database / Migration / Transaction Boundary

無新Migration。真實SQL驗證新DB與原MOD01 baseline升級、完整已交付migration chain。隔離fixture可參考既有建立/清理方法，但執行前需再核對測試Connection String來源及生成DB範圍，不輸出secret。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

故障注入區分Environment/Dependency/Compilation/TestFailure/IntegrationConflict；未執行標Not Run，不偽稱PASS；cleanup不得清理其他資料庫/正式Storage。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 執行新增Processing Unit Tests及MOD01受影響回歸，不把全部任務重新Review。
- 用可控制時間避免真實長等待；成功/失敗/取消/邊界情境皆可重現。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 必須真實SQL Server，多連線/多scope/故障注入；禁止EF InMemory替代Claim/transaction證據。
- Core階段與真實Workflow階段獨立報告，後者等待TASK12。
- 隔離SQL/Storage資源建立清理先驗證且另行取得必要執行授權；不自動操作非測試環境。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 核心矩陣每個必要不變量有對應證據，尚缺項明列。
- [ ] 真實Workflow與UI E2E未完成時不得宣稱完整MOD02 DoD達成。
- [ ] MOD01既有API/Queue/transaction/mapping保持回歸通過。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 核心階段可單獨簽收；整份TASK完成需其真實Workflow及適用E2E門檻成立。任一未執行不標完成，Analysis/Full仍另依TASK14。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

核心acceptance與真實Workflow acceptance按階段分開提交；test(mod-02): verify processing integration acceptance。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

新產品功能、修復未核准契約、實作UI、正式DB操作、完整Analysis/Full驗收（TASK14）、重新全面Review MOD01。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

