# TASK-07 — Retry、Timeout、Cancellation 與人工 Retry

**Task ID:** MOD-02-TASK-07  
**Module:** MOD-02 Processing  
**Task Name:** Retry、Timeout、Cancellation 與人工 Retry  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

集中實作 Step transient retry、operation timeout、shutdown 分類與 T5 人工 Retry。

## 2. Dependencies / Delivery State

- [TASK-06](TASK-06-Outcome-History-Aggregates-Events.md)：相關 Scope、契約及測試證據已交付。

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
重點來源：MOD-02 §1.10、§1.18～§1.22、§3.3～§3.6；MOD-03 最終 F-01/F-05。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Domain/Entities/ProcessingJob.cs`
- `src/PhotoPlatform.Domain/Enums/ProcessingJobStatus.cs`
- `src/PhotoPlatform.Application/Interfaces/IProcessingQueue.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Processing/RetryPolicy.cs、StepExecutionCoordinator.cs、ManualRetryService.cs
- 擴充 Infrastructure/Persistence/ProcessingExecutionStore.cs 的 T5
- 新增 UnitTests/Processing/RetryPolicyTests.cs、CancellationTests.cs；IntegrationTests/Processing/ManualRetryTests.cs

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Application 分類/安排delay並用TASK-05/06持久交易；Infrastructure T5；Domain invariant沿用；HTTP mapping留 TASK-11。

## 7. Implementation Requirements

1. 每Step每Run槽位0/1/2/3，delay0/1/2/4秒；子模組不另做transient loop。四槽位不是跨shutdown的實際invocation上限。
2. 408/429/5xx、operation timeout、temporary I/O/Storage/Geo可重試；400/401/403/404、invalid/corrupted image、永久missing file、invalid request/config不可自動重試。未知分類不能猜成transient。
3. 失敗T3保存NextRetryAt=失敗完成時間+1/2/4秒及下一ordinal；Job/Run維持Processing/Active，RetryWaiting無Running；到期T2才開始。
4. MissingGPS不呼叫Geo且Skipped；InvalidGPS Warning；Geo四槽位耗盡UnknownLoc/Warning繼續；auth/config Permanent Failed。Shutdown不是Geo fallback。
5. Operation timeout預設Geo15秒、其他120秒，皆>0可設定；只在舊operation停止或副作用已確認後才進下一槽位。不合作取消仍活躍時不啟動第二次。
6. Host shutdown停止新Claim、取消queue/delay/step，等待已取消operation停止，grace預設30秒>0；不加Retry、不判Permanent Failed，grace到期不是停止證據。
7. T5只接受Failed且最後必要Step終止分類Transient、輸入/版本仍可核對。最多新增RunNo2/3/4；其他資格回INVALID_STATE_TRANSITION。
8. T5鎖Job/Run/binding，確認舊Run Failed終態，新增Pending ManualRetry Run、Job Failed→Pending、清空當前StartedAt/CompletedAt/Error、套primary delta；CreatedAt/Workflow/累計RetryCount/歷史不清空。
9. 若Failed Job卻Active Run，只在持久Failed checkpoint/Log證據足夠時按規格處理；矛盾/缺證據拒絕，不先關閉活躍Run掩蓋錯誤。Commit後才Enqueue，失敗由Pending補排程接手，不再建Run。
10. 同槽位Resume增加AttemptNo/ResumeNo不增Retry；保存重試沿TASK-06，不混入StepRetry。必要cleanup不全依原取消token、不覆蓋原exception、不做Upload compensation。

## 8. Database / Migration / Transaction Boundary

T3保存retry決策、T2啟動到期槽位、T5建立人工Run，各用短transaction；delay/外部operation/Enqueue在交易外。無Migration。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

API拒絕碼依原規格由TASK-11映射；既有Commit成功後取消不撤銷完成。原輸入變動不是Retry，不重分析。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- transient/permanent分類表、0～3槽位、精確delay、耗盡。
- Geo五類結果、timeout/host shutdown區分、非合作取消零重疊。
- 人工Retry上限、錯狀態、未知分類/輸入變更拒絕；Resume/保存retry不增加Job counter。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實SQL驗證兩個人工Retry請求只建一個Pending Run、primary delta只減一次，舊History保留。
- T5各寫入點失敗全回滾；commit後enqueue失敗保留新Run供補排程。
- RetryWaiting期限重讀不重設，NextRetryAt未到不啟動；Failed/Active矛盾無證據不修復。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 自動StepRetry、保存retry、ShutdownResume、ManualRetry四者不混淆。
- [ ] Retry無新Job／新Workflow／換template，持久history及aggregate正確。
- [ ] Timeout不造成同Step operation重疊。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] Retry/Timeout/T5與對應單元及SQL測試通過；啟動恢復由TASK-08完成。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

Retry與T5及測試一組；feat(mod-02): coordinate retries and cancellation。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

使用者Cancel API、新Retry上限政策、無限重試、Completed重分析、重新上傳/刪檔補償。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

