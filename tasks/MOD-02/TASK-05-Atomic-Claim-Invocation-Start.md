# TASK-05 — T1／T2 SQL Atomic Claim 與 Invocation Start

**Task ID:** MOD-02-TASK-05  
**Module:** MOD-02 Processing  
**Task Name:** T1／T2 SQL Atomic Claim 與 Invocation Start  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

以短 SQL transaction 取得一次執行準備權，原子保存 Job／Run／checkpoint／Running Log。

## 2. Dependencies / Delivery State

- [TASK-02](TASK-02-Execution-Persistence-Schema.md)：相關 Scope、契約及測試證據已交付。
- [TASK-03](TASK-03-Primary-Binding-Admission.md)：相關 Scope、契約及測試證據已交付。
- [TASK-04](TASK-04-Workflow-Context-Contracts.md)：相關 Scope、契約及測試證據已交付。

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
重點來源：MOD-02 §1.8、§1.18～§1.20、§1.22；Database-Schema §3～§6。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Infrastructure/Persistence/PhotoPlatformDbContext.cs`
- `src/PhotoPlatform.Infrastructure/Persistence/Configurations/ProcessingJobConfiguration.cs`
- `src/PhotoPlatform.Application/Interfaces/IProcessingQueue.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Interfaces/IProcessingExecutionStore.cs 及受控 Claim/Invocation 結果 DTO
- 新增 Infrastructure/Persistence/ProcessingExecutionStore.cs（或同責任小型元件）
- 新增 IntegrationTests/Processing/AtomicClaimTests.cs、InvocationStartTests.cs 與 fault injection 支援
- 新增 UnitTests/Processing/ClaimCoordinationTests.cs

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Application 協調並只接受已確認的 durable identity；Infrastructure 執行 SQL／鎖定／affected-row 檢查；Domain 提供狀態規則；API/Worker 後續接入。

## 7. Implementation Requirements

1. Queue snapshot 只取 ProcessingJob.Id；重新讀持久狀態，不能信任 Queue 的 Status／RetryCount／Error。
2. T1 鎖定 Job/未關閉Run、核對 binding/context，Pending→Processing、Initial Active Run 或 Pending Run→Active、checkpoint Processing、配置 AttemptNo/ResumeNo、新增 Running Log、必要 primary aggregate 同 commit。
3. 只有 Id 匹配且 DB Pending 的原子條件更新成功者可取得執行資格；Processing/Completed/Failed 重複通知無執行／覆寫副作用。UNIQUE(ImageId,Workflow) 不替代 Claim。
4. T2 僅 Job Processing＋Run Active＋eligible checkpoint；依 order／NextRetryAt／budget 鎖定後配置下一 invocation。同 Run Step 序列執行。
5. AttemptNo/ResumeNo 的配置與 INSERT 在鎖定 checkpoint 的交易內，不能裸 MAX+1。首次開始 RetryOrdinal>0 且 ResumeNo=0 才增加 Job.RetryCount。
6. 首次 Run/Job StartedAt 只設定一次；Log StartedAt/Timestamp 於本 invocation 設定；checkpoint 首次 StartedAt 若 NULL 才設定。
7. Commit 失敗／不明先以新 DbContext 查原 durable invocation identity；未確認提交不得調 adapter，不能靠重複 INSERT 猜測。
8. 實作時使用真實 SQL concurrency，不能只用 memory lock；每 Job 獨立 scope。交易外才執行 Step／Queue／外部 I/O。
9. T1/T2 started-event 的 commit後通知由 TASK-06 的統一 event port 接入；本 TASK 不以缺事件阻擋狀態正確性，也不提前發送。

## 8. Database / Migration / Transaction Boundary

T1/T2 更新原三表必要投影及新執行/binding表，無新 Migration。每次啟動是一筆原子 transaction；不能先提交 Job Claim 再建立 Run/Log。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

失去競爭是未取得執行權，不應改成 Job Failed。Cancellation/DB error 未確認 commit 不開始模組；已知矛盾保持阻擋並安全回報。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- stale Queue object 僅使用 Id；未取得權利／缺 prerequisite 時 adapter 呼叫次數0。
- RetryCount 首次槽位與 Resume 的差異；下一 Step／未到期 RetryWaiting 拒絕。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實 SQL 多連線同 Job Claim，僅一組 Active Run/Processing checkpoint/Running Log，只有勝者可執行。
- 每個 T1 寫入點 fault injection，確認整筆回滾；無孤立 Run/Log/aggregate。
- T2 併發啟動同 checkpoint、Attempt identity unique；terminal Job 不 Claim。
- 模擬 commit未提交及已提交但回應遺失，重新讀取確認，無雙重 invocation/RetryCount。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] T1/T2 invariant 在真實 SQL 成立。
- [ ] 缺 binding/context、DB錯誤、重複通知皆不導致分析副作用。
- [ ] 保持原 Job schema/Queue signature；尚未有 Worker 不冒稱 runtime 已啟用。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 原子 start persistence 及 SQL 測試通過；T3／Recovery 後續交付。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

T1/T2 store、協調測試與 SQL fault tests 一組；feat(mod-02): implement atomic processing claims。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

read-check-write Claim、長交易、Job lease/owner、T3 結果寫入、T4回收、T5人工 Retry、重新設計 Queue。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

