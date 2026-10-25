# TASK-06 — T3 Outcome、History、Aggregate 與事件

**Task ID:** MOD-02-TASK-06  
**Module:** MOD-02 Processing  
**Task Name:** T3 Outcome、History、Aggregate 與事件  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

單一 writer 原子提交 Outcome／執行終態與 aggregate，再 best-effort 通知。

## 2. Dependencies / Delivery State

- [TASK-05](TASK-05-Atomic-Claim-Invocation-Start.md)：相關 Scope、契約及測試證據已交付。

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
重點來源：MOD-02 §1.14～§1.16、§1.18、§1.20～§1.22；Database-Schema §5、§9 ownership；MOD-09 §1.12。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Domain/Entities/Batch.cs`
- `src/PhotoPlatform.Domain/Entities/Image.cs`
- `src/PhotoPlatform.Infrastructure/Persistence/BatchStatusPersistence.cs`
- `src/PhotoPlatform.Application/Services/UploadService.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Processing/ProcessingOutcomeCoordinator.cs、事件 DTO／publisher abstraction
- 擴充 Infrastructure/Persistence/ProcessingExecutionStore.cs 的 T3 寫入與結果 transaction 協調
- 必要時擴充 Domain Batch/Image 受控投影行為，不改 mapping
- 新增 UnitTests/Processing/ProcessingOutcomeTests.cs、ProcessingEventTests.cs；IntegrationTests/Processing/OutcomeCommitTests.cs

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Application 單一 lifecycle writer 協調；Infrastructure 同 transaction 實現結果寫入與 state；Domain aggregate policy 沿用 TASK-03；API 不直呼 SignalR，MOD-09 後續轉送。

## 7. Implementation Requirements

1. T3 在同交易保存模組 SQL 結果/ResultReference、Running Log終態、checkpoint Completed/RetryWaiting/Failed；最後必要Step或fatal同時關Run/Job及更新primary delta。
2. 模組 owns result，MOD-02 owns History。結果更新必須通過 result key/owner/rowversion 條件；失敗不能繼續關閉 checkpoint。Concrete result writer 留 TASK-12，核心提供受控交易接點而非虛構結果表。
3. 同版本可重用結果須核對 reference，不能只看 row存在。未執行的後續 checkpoint 可保持 Pending，但 terminal Run 不允許執行，不偽造 Skipped History。
4. Geo 最後失敗 invocation 保留 Failed；fallback checkpoint 可 Completed/Warning，不把真實失敗改成 Success。具體錯誤分類與 delay executor 由 TASK-07交付；本 TASK 使用已核准 Outcome 決策測試。
5. 鎖定 Job/binding/Batch 從持久舊貢獻計算 delta；重複 completion 無變更，secondary 不改 counts；完成/失敗 Job 不留下 Active Run。
6. 保存重試為初次+最多三次，1/2/4秒，只重試相同保存內容、不重新執行模組、不增加 RetryOrdinal/JobRetryCount。Commit不明先讀durable identity；已提交接受原結果，不再套delta。
7. 保存持續失敗保留最後可確認狀態，停止下一Step，不發成功；memory Outcome跨重啟可能遺失，交 TASK-08 核對。
8. 統一發布 ProcessingStarted、ProcessingStepStarted、ProcessingStepCompleted、ProcessingStepFailed、ProcessingCompleted、ProcessingFailed、BatchProgressUpdated、BatchCompleted，事件識別與 payload 依 MOD-02 §1.16，接入 TASK-05 commit後端點。schemaVersion=1、eventId、occurredAt、traceId及持久識別依規格；Batch事件無單一識別時用NULL。
9. Warning fallback 可 ProcessingStepFailed(willRetry=false) 後 ProcessingStepCompleted(outcome=Warning)，不得用 ProcessingFailed 表示 Warning；Shutdown Interrupted 不發 ProcessingFailed。
10. 事件、ILogger failure 不回滾／不重跑／不覆蓋原錯誤；durable History 寫入失敗不能吞掉。未知原HTTP trace以Worker correlation代替，不冒稱已持久化原trace。

## 8. Database / Migration / Transaction Boundary

T3 short transaction 包含 SQL result/state/history/aggregate；Storage artifact 不在 SQL rollback 範圍。無新 Migration、Outbox、持久事件表；不得為測試新增 production result table。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

受控安全 code/message，不輸出原 exception、SQL、secret、絕對路徑。System logging best-effort，durable writer failure 必須阻止宣稱完成。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- Success/Warning/核准Skipped/fatal Outcome；任意Skip拒絕。
- 主/次 Job、零Batch、全失敗/混合/重複完成 delta；事件只在commit成功後、通知失敗無重算。
- 保存重試精確次數/延遲且adapter執行一次、retry counters不變；unknown duration為NULL。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實 SQL 驗證 T3 每寫入點 rollback、terminal Job與Run一起關閉、同時完成不同主Job不遺失Batch計數。
- commit回應遺失重讀identity、不重複delta/Log；History唯一/terminal invariant。
- 具體模組結果+T3同commit的真實SQL證據須在 TASK-12 補齊；本TASK測試替身不能宣稱各結果表已整合。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] T3 核心寫入原子、History可追溯、所有通知在commit後。
- [ ] 沒有模組自寫重複History／結果獨立commit／通知失敗重跑。
- [ ] 當前核心與尚缺的真實結果adapter驗證分開報告。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 核心 Outcome writer、aggregate、event contracts 完成；不把核心測試當完整Workflow驗收。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

Outcome writer/aggregate/events與測試一組；feat(mod-02): persist processing outcomes atomically。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

SignalR Hub、完整Monitoring、Outbox/Broker、模組演算法、Storage cleanup、用保存重試重新執行Step。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

