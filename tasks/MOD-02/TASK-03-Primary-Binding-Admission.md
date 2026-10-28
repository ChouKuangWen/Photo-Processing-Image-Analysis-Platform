# TASK-03 — Primary Binding 與 Batch 准入

**Task ID:** MOD-02-TASK-03  
**Module:** MOD-02 Processing  
**Task Name:** Primary Binding 與 Batch 准入  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

確認原 Upload Job 的主工作身分，建立可重驗的 Batch 准入及 aggregate 規則。

## 2. Dependencies / Delivery State

- [TASK-02](TASK-02-Execution-Persistence-Schema.md)：相關 Scope、契約及測試證據已交付。

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
重點來源：MOD-02 §1.15、U-03、§3.6；Database-Schema §2、§6；MOD-09 最終 F-08。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Application/Services/UploadService.cs`
- `src/PhotoPlatform.Domain/Entities/Batch.cs`
- `src/PhotoPlatform.Domain/Entities/Image.cs`
- `src/PhotoPlatform.Infrastructure/Persistence/BatchStatusPersistence.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Interfaces/IProcessingPrerequisiteService.cs 與 DTOs/ 下 prerequisite 結果
- 新增 Infrastructure/Persistence/ProcessingBindingPersistence.cs
- 新增 Domain/Policies/PrimaryAggregatePolicy.cs；必要時擴充 Batch.cs、Image.cs 的受控投影行為
- 新增 UnitTests/Processing/PrimaryBindingTests.cs 與 IntegrationTests/Processing/PrimaryBindingPersistenceTests.cs
- F-03-01 例外：修改 UploadService.cs、IUploadPersistence.cs、UploadPersistence.cs，以及直接受影響的 Upload Unit／Persistence／Acceptance Tests 與測試替身；不修改原三表 Mapping 或 Migration。

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Domain 計算已確認狀態的貢獻規則；Application 協調交接核對；Infrastructure 鎖定／查詢／建立 binding；API 診斷留 TASK-11。

## 7. Implementation Requirements

1. F-03-01 核准：原 Upload Job 的 Workflow 可為四種之一，不強制 Full。新 Upload 在原 SQL Transaction 內依序 Save Batch／Images、Save Jobs 取得 Id、直接從本次 Jobs 建立並 Save Bindings、Commit、Enqueue。允許最小修改 UploadService／IUploadPersistence／UploadPersistence 與測試；Binding 加入 tracked 清理集合，沿用 Commit 前回滾／Storage 補償與 Commit 後資料保留規則。
   持久 Binding 是原始身分證據；歷史缺 Binding 一律阻擋，不查候選猜測、不 backfill、不手動選主。Assessment 不建立 Binding；既有 Binding 仍須完整核對。
2. Binding 建立須核對 Job.ImageId／BatchId／Workflow 與 Image.BatchId；PrimaryJobId／Workflow 綁定後不因 secondary／Retry 改變。
3. Primary Claim／恢復／aggregate 更新前，在相關短交易核對該 Batch 全部接受 Image 的完整 binding 集合及計數。不能以記憶體 ready flag 或只取前100筆理由替代完整核對。
4. 新增 Image 使先前集合確認不再足夠。未知 binding 的 Image 所有 Job 不執行；整個 Batch 的 Primary 阻擋，但已確認其他 Batch 繼續；已確認 secondary 不額外套全 Batch aggregate 門檻。
5. 貢獻 Pending/Processing=(0,0,0)、Completed=(1,1,0)、Failed=(1,0,1)。舊→新 delta 由持久資料計算；重複完成不再加計，Failed→Pending 減 Processed／Failed。
6. TotalCount 不變，Processed=Success+Failed 且介於0與TotalCount。空 Batch 保持 Pending、零進度、不發 BatchCompleted；非空全 Pending 為 Pending，部分終止或 Processing 為 Processing，全終止有成功為 Completed、全失敗為 Failed。
7. Retry 後若仍有其他終態 Image 則 Batch Processing；CompletedAt 在離開終態清空、再次進終態設定。Secondary 不改 Upload aggregate／Image 全域 Processing Status。
8. 分離受控 binding establishment 與純只讀 prerequisite assessment；Query 不建立 binding、不修復。傳回核准原因分類，完整核對失敗不能假 ready。
9. 本 TASK 建立規則與 persistence 核對接點；T1/T3/T4/T5 的實際 aggregate 更新由後續交易 writer 在同一 transaction 使用，禁止此處先單獨 Commit delta。

## 8. Database / Migration / Transaction Boundary

使用原三表及 image_processing_bindings，無新 Migration。建立 binding 與關聯核對在短交易；跨交易的過期准入結果不能授權 Claim。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

未知／矛盾資料保持原 Status，安全診斷不輸出 SQL／路徑。拒絕建立 secondary 的 HTTP 503 交 TASK-11；不新增修復/unblock API。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 各 Primary 狀態 delta、重複完成、Failed Retry、secondary、空 Batch、全失敗／混合成功。
- 多候選、不一致、缺證據不能選主；既有 Query 計算契約不變。
- 核對三次 Save 的順序、第三次 Save 失敗與取消時的回滾／補償，Commit 後 Queue 失敗不得撤銷 Binding。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 真實 SQL Server 驗證併發 binding 建立不選出兩個主 Job。
- 核對完整集合；新增 Image 後舊確認不能繼續放行；錯關聯／計數矛盾阻擋，其他 Batch 不受影響。
- 唯讀 assessment 前後資料不變；所有 aggregate 寫入整合留 TASK-05～08 再驗證。
- 四種 Workflow 經真正 Upload 交易建立 Binding；Binding 寫入失敗時四表一起回滾，DbContext 清理後重用不殘留 Binding；新 Upload 來源不使用歷史候選推測。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 可確認的 MOD-01 單一原 Job 能建立穩定 binding。
- [ ] 未知證據零執行／零猜測修復；secondary 不污染 aggregate。
- [ ] SQL admission 與 Domain delta 規則可由後續 writer 共用。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] 准入及貢獻規則完成，不宣稱 runtime Primary 執行已完成。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

Binding／准入／aggregate policy 與測試一組；feat(mod-02): establish primary binding admission。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## 15. Out of Scope

重新設計 Upload（僅允許 §7 核准的同交易 Binding 整合例外）、全庫 backfill／修復、第二套 Batch Query、獨立提交 aggregate、變更 TotalCount。不得新增 Migration、資料表、NuGet 或 API。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

