# TASK-09 — BackgroundService、Pending Scanner 與 DI

**Task ID:** MOD-02-TASK-09  
**Module:** MOD-02 Processing  
**Task Name:** BackgroundService、Pending Scanner 與 DI  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

把核心執行接到現有Bounded Channel，以受控Consumer與持久Pending補排程運作。

## 2. Dependencies / Delivery State

- [TASK-08](TASK-08-Startup-Recovery-Reconciliation.md)：相關 Scope、契約及測試證據已交付。

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
重點來源：MOD-02 §1.7～§1.11、§1.18；SYS Processing/Queue及單Instance限制；MOD-01 §1.8 Upload acceptance boundary。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Infrastructure/Processing/ChannelProcessingQueue.cs`
- `src/PhotoPlatform.Infrastructure/Processing/ProcessingQueueOptions.cs`
- `src/PhotoPlatform.Api/UploadApiComposition.cs`
- `src/PhotoPlatform.Api/Program.cs`
- `tests/PhotoPlatform.UnitTests/Infrastructure/ChannelProcessingQueueTests.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Infrastructure/Processing/ProcessingWorker.cs、PendingJobScanner.cs、ProcessingOptions.cs
- 新增 API/ProcessingApiComposition.cs；修改 Program.cs 接入經准入控制的host
- 新增 UnitTests/Processing/WorkerTests.cs、PendingScannerTests.cs；IntegrationTests/Processing/ProcessingHostTests.cs

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Infrastructure BackgroundService負責host/scope/排程；Application執行核心use cases；API composition註冊；Domain無Hosting/Channel依賴。

## 7. Implementation Requirements

1. 只使用既有IProcessingQueue與ProcessingJob item，不新增Queue DTO、不改signature、不重建第二個Singleton Queue。
2. Worker Dequeue僅用Id，獨立DI scope/DbContext呼叫T1；未取得權利不Execute。各Job可並行，同Run Step序列；MaxConcurrency由有效配置限制，不用無界Task.Run。
3. Startup依TASK-08完成停止前提/DB/binding/恢復分類後才開Claim；先有可消費Worker，再啟動分批producer，避免超capacity阻塞host啟動。
4. Pending scanner啟動完整分批、runtime預設5秒/每批100，皆>0。CreatedAt/Id排序逐批向前遍歷，一輪cursor為runtime進度，不永遠重掃同一批舊Pending。
5. 只通知已Commit Pending、binding/context有效、NextRetryAt已到或NULL者；不以runtime scanner回收Processing，也不以記憶體enqueued marker作durable唯一性。
6. 補排程Enqueue預設timeout5秒>0，滿載/取消/失敗保留DB/Storage，下輪重試；不Drop。這個timeout不修改MOD-01 Upload等待或Request token。
7. DB unavailable不猜狀態/不Enqueue，Readiness不接受新工作，延後/停止掃描而非產生無界tasks。Readiness不是舊process停止證據；具體host整合不改Upload response contract。
8. Shutdown停止新Claim、取消Dequeue/delay/step並遵守TASK-07 grace/無重疊規則；每scope確實dispose，Singleton不持有scoped DbContext。
9. 生產執行只接受完整有效adapter/input；未交付Workflow不使用no-op或fake成功。測試host可注入fake adapter；不能因載入composition就無條件啟用全部Workflow。
10. 配置來源/命名在實作前審查，不新增secret或新套件；Queue Capacity仍預設100/Wait。MaxConcurrency=4是規格範例，不擅自宣稱唯一核准Production容量。

## 8. Database / Migration / Transaction Boundary

使用TASK-05～08交易，scanner讀取與Enqueue分離；無Migration。Job.Processing的retry continuation不交Pending scanner重新T1；同Run到期由T2協調。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

單Job失敗不終止其他已准入工作；DB/host prerequisites失敗阻擋Claim。取消未Commit可回滾，Commit後保持durable truth。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- Bounded queue滿載與取消、重複/過期通知、MaxConcurrency、每Job scope dispose。
- 超過100筆/超capacity補排程、cursor前進、timeout保留下一輪資格、某Batch阻擋不餓死其他Batch。
- shutdown不再Claim，既有成功不退回；invalid正數配置拒絕。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實SQL+測試host驗證Upload已Commit但enqueue失敗的Pending可補排程且只Claim一次。
- Worker/Scanner同時通知、多Consumer、不同Job獨立DbContext；DB不可用不做猜測執行。
- 啟動順序及超capacity不死鎖；不依賴真實模組演算法的測試須標為核心整合。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 現有Queue/Upload契約未改，沒有unbounded排程。
- [ ] 每個operation僅由成功T1/T2授權；Scanner不授予執行權。
- [ ] host受單Instance/Context gates保護。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] host/DI/scanner核心驗收通過；不宣稱缺adapter的Workflow已可用。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

HostedService/Scanner/DI與host tests一組；feat(mod-02): host processing workers and pending reconciliation。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

改Upload API/queue timeout、runtime回收Processing、多Instance部署、自動migration、實作其他模組、完整health dashboard。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

