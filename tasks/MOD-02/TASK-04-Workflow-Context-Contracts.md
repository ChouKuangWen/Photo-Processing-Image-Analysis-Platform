# TASK-04 — Workflow、Input Context 與 Adapter 契約

**Task ID:** MOD-02-TASK-04  
**Module:** MOD-02 Processing  
**Task Name:** Workflow、Input Context 與 Adapter 契約  
**Status:** PLANNED  
**Architecture:** Clean Architecture  
**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Planning Branch:** `feature/mod-02-processing`

本文件只定義開發 Scope，不是 Code、Build、Tests、Migration、Commit 或 Push 的執行授權。狀態 PLANNED 不等於 READY FOR IMPLEMENTATION。
開始實作前依 AGENTS.md 與 pre-implementation-review skill 核對當前來源、前置交付與具體 signatures，通過後等待人工批准；遇正式規格衝突停止受影響工作並回報，不以 TASK 覆蓋規格。
新檔名是責任定位建議；若前置 TASK 已建立同責任型別須沿用。不得擅加 NuGet、public API、欄位或替代架構。

---

## 1. Goal / Scope

提供凍結 Workflow、可核對持久輸入、Outcome 及模組交接契約，不實作其他模組演算法。

## 2. Dependencies / Delivery State

- [TASK-01](TASK-01-Processing-State-Model.md)：相關 Scope、契約及測試證據已交付。
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
重點來源：SYS 最終 F-01～F-08；MOD-02 §1.17、§1.18、最終 F-07/F-08；Database-Schema §9；MOD-03 最終 F-01/F-05、MOD-04 最終 F-02。
正式規格同題的最終核准章取代歷史待決文字；舊「MOD-01未合併」描述為歷史審查基準，目前已合併，不當成缺陷。來源若已變更，重新審查受影響範圍。

## 4. Existing Components / Reuse

- `src/PhotoPlatform.Domain/Enums/WorkflowType.cs`
- `src/PhotoPlatform.Application/Interfaces/IProcessingQueue.cs`
- `src/PhotoPlatform.Application/Interfaces/IFileStorageService.cs`
- `src/PhotoPlatform.Domain/Entities/Image.cs`

不得重做 MOD-01 TASK-01～12；保留既有 Upload 接受邊界及 Queue signature。MOD-01 過去測試紀錄不直接證明新 Processing 能力。

## 5. Expected Files

- 新增 Application/Interfaces/IWorkflowOrchestrator.cs、Step adapter／Context provider／結果核對抽象
- 新增 Application/Processing/ 下 WorkflowDefinition、ProcessingContext、StepOutcome、canonical input 元件
- 新增 Application/Interfaces/IImageContentReader.cs（依 MOD-04 已核准介面，若前置模組已建立則沿用）
- 新增 UnitTests/Processing/WorkflowContextTests.cs、CanonicalInputTests.cs；持久 reference 測試置 IntegrationTests/Processing/

路徑縮寫約定：Domain/Application/Infrastructure/API 分別位於 `src/PhotoPlatform.Domain`、`src/PhotoPlatform.Application`、`src/PhotoPlatform.Infrastructure`、`src/PhotoPlatform.Api`；
UnitTests/IntegrationTests 位於 `tests/PhotoPlatform.UnitTests`、`tests/PhotoPlatform.IntegrationTests`。新增／修改只限核准 Scope，不要求無理由搬移既有檔案。

## 6. Layer Responsibilities

Domain 沿用 Workflow enum；Application 定義協調契約與 canonical input；Infrastructure 後續實作 reader／persistence adapter，不將 Stream 路徑或 SQL 暴露給 Domain；API 不新增輸入欄位。

## 7. Implementation Requirements

1. WorkflowVersion 固定 workflow-mvp-v1。Naming：EXIF_PARSING→GEO_LOOKUP→NAMING→FILE_RENAME；DuplicateDetection：SHA256_CALCULATION→EXACT_DUPLICATE_CHECK。
2. Analysis：EXIF_PARSING→GEO_LOOKUP→SHA256_CALCULATION→EXACT_DUPLICATE_CHECK→PHASH_GENERATION→VISUAL_SIMILARITY→QUALITY_ANALYSIS→VERSION_ANALYSIS；Full 為 Analysis 後 NAMING→FILE_RENAME。VALIDATION 僅准入、COMPLETED 僅 lifecycle，不建假 checkpoint。
3. Resolver 可描述已核准 Workflow，不等於其 runtime 已啟用；Analysis/Full 實作准入仍受 N-01/N-02 限制。未知 Workflow／engine／profile／RuleVersion／input 不補 default。
4. 每 Run 凍結 stepOrder/source/input references。Resume 與 ManualRetry 使用原業務 input／版本，非改參數重分析。開發前逐項列出從核准表重建輸入的方法；fingerprint 不是可逆 input storage。
5. ContentVersion 為原內容 SHA256 64小寫hex。InputFingerprint 依 Schema §9 canonical JSON UTF8：固定 schemaVersion、property ordinal order、invariant 數字、無BOM/空白、UTC O時間、明確null、array按凍結order，排除 runtime time/trace/attempt。
6. ResultReference 為受控 table/id/version，長度<=500；核對同 input/version 且已提交，不以 row存在／History成功／任意path當可重用結果。
7. MissingGPS 為明確 GEO_LOOKUP Skipped；InvalidGPS／Geo耗盡 Warning；auth/config Permanent Failed。Naming collision Skip 與空 Recommendation snapshot 的 Skip 必須有模組明確證據；任意 Skip 不可算必要 Step 完成。
8. 沿用 MOD-04 IImageContentReader.OpenOriginalAsync(ImageId,token)／ContentHandle(Stream,Length,ContentVersion)，caller dispose、不可假設 seek；原圖 hash 核對不是 EXACT grouping。具體 reader 未交付時依賴仍存在。
9. Step adapter 回 Outcome／受控結果及副作用核對資訊，不自做 transient retry／History、不持有跨 operation SQL transaction。Application contract 不帶 DbContext／SqlConnection。
10. Concrete signatures 須先契約審查；若凍結 input 無法以核准 mapping 可重建，停止受影響 adapter 並回報，不自行加 Context JSON 欄位。

## 8. Database / Migration / Transaction Boundary

### R3 已批准的縮限純 Outcome 子範圍（2026-10-30）

正式 Signature 與完整不變量以 [MOD-02 StepOutcome R3 最終核准契約](../../docs/modules/MOD-02-Processing-Spec.md#最終核准契約--stepoutcome-r3-純型別2026-10-30) 為準，不複製第二套 Signature。本補充記錄人工定稿授權，不等於程式實作授權，且不將整份 TASK-04 標記完成。

- 預計新增 `Application/Processing/StepEvidenceIdentity.cs`、`IStepEvidence.cs`、`StepFailure.cs`、`ResultMaterial.cs`、`CompletionProposal.cs`、`StepOutcome.cs`；純測試預計為 `UnitTests/Processing/StepOutcomeTests.cs`。若實際採同責任檔案組織，仍不得擴大 Public API。
- 沿用 ProcessingErrorClassification、ProcessingLogStatus、ProcessingResultStatus；不改 Domain、不新增 enum。StepOutcome 是單次觀察、CompletionProposal 是待核對提案，皆不代表 T3 已提交或取得完成權。
- Material 保留 Prepared/ExistingCandidate/NoNewResult。Prepared 未提交；ExistingCandidate 須核對已提交的同 input/version/owner 結果；NoNewResult 須具體 Step policy 許可，不能任意省略必要結果或製造假結果。
- Returned factory 僅允許 Success/Success、Warning/Warning、Skipped/Skipped；建構及公開表面不得讓外部繞過驗證。nested private constructor 存取可用非公開協作解決，不開放任意 Returned 建構。
- CompletionProposal 及 NeedsReconciliation 的具名 Evidence 必要；Failed 的 StepFailure 必要、模組 Evidence 可 null，但協調層持久 Invocation/Input 核對不可省略。Evidence Identity 僅表示目前 Step 業務輸入，不是 Invocation 或完整集合來源證明；集合來源由後續具體 Evidence 核對。
- StepFailure 無 SafeMessage，只承載受控 ErrorCode/既有分類；純型別驗證 enum、非空 ASCII 與50字元上限，實際 code 白名單/分類映射由後續 Step policy 負責，不新增未核准值。
- Geo 耗盡維持 Failed History，協調層核准 fallback 後同 T3 完成 Warning；不新增成功 invocation。Cancellation/Unknown 不自動重試、Failed 或 Interrupted。
- 純測試覆蓋：三種 Material、必要參數與 nullable Failed Evidence、合法/未知 enums、字串及hash邊界、三組 Returned factory 與所有錯配、不可變公開表面。具體 code/Step policy、集合證據、T3 原子性及 fallback 執行不以 fake 驗收。
- 後續實作須先完成獨立 pre-implementation-review 並獲人工實作批准；驗證 Restore/Solution Build、相關及全部 Unit Tests。無 SQL 寫入，不要求為純型別執行 SQL Integration Tests。新增手寫註解使用白話繁體中文 `//`；每個新測試有測試目的/測試情境/預期結果三行註解。
- 本子範圍不定稿 IProcessingStepAdapter 或 IGeoFallbackPreparer，不實作 Concrete Payload/Evidence、Persistent Context、Claim/T3、Retry/Recovery，不新增 Migration/NuGet。F-04-01、N-01、N-02 維持；§7 原有完整 Context/Adapter 要求仍是後續未完成範圍。

本 TASK 不新增 Migration。引用 Run/checkpoint 與各模組核准 input/result；本階段可用測試 adapter，正式 input provider 未交付不能假定完整 Workflow 可執行。

共用不變量：T1 是 Claim/Run/checkpoint/Running 同 commit；T2 是同 Run 下一 invocation；T3 是 result/History/checkpoint/必要終態與 aggregate；
T4 僅安全 Startup Recovery；T5 僅人工 Retry。T2-W 僅核准的 VERSION_ANALYSIS 等待。不是本 TASK 擁有的交易不得另實作第二條路徑。
不新增原 Job owner/lease/rowversion，不更換 SQL Server，不建立 Outbox。Application/Domain 不直接使用 DbContext/SQL。

## 9. Errors / Concurrency / Cancellation / Recovery

缺 Context／版本／Naming input 保持 prerequisite 阻擋，沒有新 JobStatus；Cancellation 向下傳遞，副作用安全性不能只靠 cancellation 成功推論。

- Channel 是通知，DB 是狀態真相；Enqueue 不授予執行權。
- 同一工作集合單 Instance、多 Worker；memory lock 不能替代 SQL 條件更新／唯一性。
- 未知輸入／副作用／歷史矛盾先阻擋，不猜 Success、不盲目 Failed 再 Retry。
- SQL transaction 不跨外部 I/O、delay 或完整 Workflow；Commit 後通知失敗不重跑。
- N-01/N-02、cleanup/reapply 等未核准事項保持原限制。

## 10. Unit Tests

- 四 Workflow 精確 Step order；未知 enum/name 拒絕；VALIDATION/COMPLETED 無 checkpoint。
- canonical JSON 的文化、null、排序、Unicode、UTC 測試向量；改業務 input 改 fingerprint，改 trace/attempt 不改。
- 結果版本／來源不符拒絕重用；缺 Template、未核准引擎零 invocation；明確 Skip 與任意 Skip 分開。

測試名稱需表達 Given/When/Expected 行為；覆蓋成功、拒絕、邊界及適用的取消。不用真實 SQL、外部 API 或 GCS 作 Unit Test 前提。

## 11. Integration Tests

- 本 TASK 核心為純契約，無新 SQL 寫入；若實作持久 reference 查詢，必須以真實 SQL 驗證錯關聯／版本拒絕及查詢無副作用。
- Concrete 模組 input/result 重建與 Storage 驗證留 TASK-12，不能以 fake provider 宣稱已通過。

涉及 SQL Claim、unique、transaction、Migration、並行與持久恢復的驗收，必須使用真正 SQL Server，禁止 EF Core InMemory 替代。
執行前核對隔離 Connection String 來源、生成測試 DB/Storage 範圍與清理目標，不輸出秘密；環境問題依 sqlserver-integration-test skill，不藉改 production code 繞過。
上述是未來驗收要求，本次 TASK 文件生成不執行任何測試或 Migration。

## 12. Acceptance Criteria

- [ ] 可追溯到核准 Workflow／input／Outcome，未新增業務預設。
- [ ] 列明已存在、測試替身與尚未交付的 concrete adapters。
- [ ] 契約可供 T1～T5 使用但不承諾未核准的 public API 或 Schema。

## 13. Definition of Done

- [ ] 實作前 Review 與人工批准可追溯，實作基準與前置交付已確認。
- [ ] 本 TASK Acceptance Criteria 有實際測試證據；未執行/跳過與失敗分開報告。
- [ ] 在授權範圍執行 Restore/Build/相關 Unit Tests，記錄命令、版本與結果；必要 SQL Integration Tests 有隔離環境證據。
- [ ] 未破壞 MOD-01 API/Queue/原表 Mapping；沒有未核准套件、Schema、業務預設。
- [ ] 沒有未解決的本 TASK 規格衝突；外部依賴與剩餘限制明列。
- [ ] Diff 僅含本 TASK 核准程式／測試／必要 Migration，無 secret。
- [ ] Workflow／Context 核心與測試完成；真實來源缺失明列，不阻擋不依賴其演算法的核心工作。

PLANNED/DEPENDENCY_PENDING/PLANNED_WITH_DEPENDENCIES/BLOCKED 均不是 PASS；文件建立不勾選上述項目。

## 14. Git Commit Boundary

Workflow/context/adapter contracts 與純測試一組；feat(mod-02): define workflow execution contracts。

完成後先提交變更摘要與測試結果供人工 Review；沒有另行授權不得自動 Commit/Push/Merge/Rebase 或切換分支。

## Phase B-1a 人工核准補充：原圖型輸入文件契約

本章記錄僅限 Specification 的批准，不是 Production／Tests／Mapping／Migration／SQL Writer 的實作授權，也不修改本 TASK 的 PLANNED 狀態或宣稱 TASK-04 COMPLETE。

### 核准來源與範圍

- [MOD-02 B-1a 最終補充](../../docs/modules/MOD-02-Processing-Spec.md#最終核准補充--phase-b-1a-原圖型持久輸入契約)：原圖選定、兩個完整 Canonical Schema、T1／T2／Resume／ManualRetry 與 Reader／Fail-Closed 責任。
- [Database-Schema B-1a 補充](../../docs/Database-Schema.md#phase-b-1a-核准補充原圖型輸入綁定)：processing_original_inputs 及 processing_original_step_inputs 的 PK／FK／CHECK／不可變與交易核對規則。
- 同 Job 共用原圖 ContentVersion／ContentLength／WorkflowVersion；Job＋Step 保存個別 RuleVersion／InputFingerprint，EXIF 另存 UploadCreatedAt，SHA 必須 NULL。不建立通用 Context JSON。
- 首次 T1 只凍結 Workflow 所需的原圖型輸入，與 Claim／Run／首個 invocation 同提交；不提前建立後續 Running。T2、Resume、ManualRetry 沿原綁定；不改既有 T1～T5 邊界。
- Canonical v1 維持既有格式；schemaVersion 自動輸出，EXIF uploadCreatedAt 是業務輸入，不受 runtime timestamp 排除規則排除；不重複加入 fileSize。原內容 hash 與業務 InputFingerprint 不混用。
- Reader 必須確保核對 bytes 與 Adapter 消費 bytes 相同；純 Interface／ContentHandle 不等於具體 Storage 保證已交付，不改 Public Signature，不宣稱 MOD-01 Upload 已保存 SHA256。

### 後續准入與待決事項

1. 文件定稿不取代後續縮限實作的 pre-implementation-review 與人工批准。原 TASK「本 TASK 不新增 Migration」是先前 scope；本輪只核准新增表的規格，尚未批准任何 Migration 實作，後續須另界定 TASK／授權範圍。
2. Frozen Binding 遺失不自動 backfill；內容／版本不符不換目前來源，不僅憑來源不符把 Running 改為 Failed／Interrupted，不新增狀態或擴充既有 API ErrorCode。
3. 診斷 ErrorCode、回報接點、重新檢查／排程政策、未關閉 Invocation 的持久處置仍待決，受影響路徑維持 Implementation Blocker；沒有新 Failure／Retry 預設。
4. MOD-03 Metadata frozen time／來源、MOD-04 Reader 同內容、MOD-05 Hash frozen version／FileSize 的文件對齊須另行授權；本輪只列待辦，不修改其他模組。
5. F-04-01 仍有 B-1b／B-1c 及其他 Step input reconstruction 缺口；N-01／N-02 不解除。Reader、Provider、SQL Writer、演算法與真實恢復驗收均未由此章交付。
6. 後續驗收應涵蓋共同內容一致、兩個 Canonical 固定向量、T1 原子性／並行 Claim 不覆寫、原輸入恢復／繼承、missing/mismatch fail-closed；SQL 約束須真實 SQL Server 測試，不以純型別或 Test Double 宣稱完成。本輪不新增或執行測試。

## B-1b-01 核准補充：Metadata／GEO 文件契約

本節只記錄人工批准的 Specification，不授權 Code、Tests、Entity／Mapping、Migration、Provider、Writer 或 API，不改本 TASK 完成狀態。
正式來源：MOD-03 B-1b-01 定義 EXIF／GEO authority、Canonical v1、Manifest、結果矩陣與 API Pending；Database-Schema 同名補充定義兩張新表及 EXIF authority 調整；MOD-02 同名補充定義交易／恢復責任。上述補充優先於舊同題待決描述，不以本 TASK 覆蓋正式規格。

- EXIF Completed 不可覆寫；GEO 有獨立版本／fingerprint／owner，processing_geo_inputs 固定原來源。所有新增表仍僅是規格，不宣稱 SQL 已交付。
- 首次 GEO T2 綁定與 Running 原子提交；Commit 後外部 I/O。Resume／ManualRetry 沿原輸入，不按最新時間選取，不 upsert 改來源。
- Completed GEO ErrorCode=NULL；TransientExhausted 保留原 Failed History，與 Warning 結果及 Completed／Warning checkpoint 同 T3，不增加成功 Invocation。
- 同 key Failed Result 不重開、不移交 owner，不新增 Result Attempt；ManualRetry 資格／上限與 T1～T5 不變。
- Missing／Mismatch／Commit Unknown 採 Fail-Closed，不自動 backfill 或關閉 Running。診斷碼／解除阻擋／排程與實際 Provider Manifest 仍為 Implementation Blocker。
- MOD-04 Naming 固定消費同鏈 Completed GEO；MOD-07 QUALITY 消費不可變 EXIF，地名不計六項分數；QUALITY T2 binding 未完成。
- MOD-03 Pending／Failed 欄位映射與跨 Run 展示為 API Contract Pending；不改 Route／Response／HTTP Status／Public ErrorCode，不以 IMAGE_NOT_FOUND 代替未就緒。
- 不修改 MOD-01、Run／Checkpoint、StepOutcome R3；F-04-01 整體、N-01／N-02、B-1b-02／B-1c 仍保留，後續實作須獨立 Review 及人工批准。

## B-1b-02 核准補充：QUALITY Input 文件契約

本節記錄核心契約的文件批准，優先補足先前「QUALITY T2 binding 尚待定義」描述，不表示實作或驗收完成。來源為 MOD-07 B-1b-02 的 Canonical／Profile、Database-Schema 同名表定義及 MOD-02 同名交易契約；不以 TASK 覆蓋正式規格。

- 新規格 processing_quality_inputs 每 Job 固定 MetadataResultId／QualityRuleVersion／InputFingerprint，不存原圖、Path、Metadata JSON，不增加 owner／rowversion／Retry Counter。
- 目前核准 Workflow 的 QUALITY／GEO MetadataResultId 必須相同且符合原 Job EXIF 鏈；不是僅同 ImageId，不讀最新來源。
- 首次 T2 來源核對、binding／fingerprint 與 Running 原子提交；確認後才分析，不跨外部 I/O。Resume／ManualRetry 沿原來源，不改 T1～T5。
- 原 Run 未開始 QUALITY 時，不虛構引用或選最新規則；原版本選擇缺持久依據即為 Implementation Blocker，尚未批准新的版本選擇政策。
- Canonical v1 使用 MOD-07 的十個固定欄位，schemaVersion 自動輸出；不含 GEO 地名、runtime 識別或任意 JSON。Quality 公式不變。
- Completed 結果須完整來源／版本／owner 證據才可重用；唯一鍵不等於完整 fingerprint，不跨未核准 WorkflowVersion 等價重用；Failed 同 key 無核准處置即阻擋，不新增 Result Attempt。
- N-01／N-02／F-04-01 其他 Step、Reader、診斷／未關閉 invocation、跨 Run 未凍結規則與 Failed 結果處置仍保留。新增表僅是 Specification，未交付 EF／SQL；不開始 B-1b-03／B-1c，不標 TASK-04 COMPLETE。

## B-1b-03 核准補充：Naming／Artifact 文件契約

本節記錄人工批准的文件範圍，優先補足先前Naming input未定及雙Step Skip描述；正式業務／Canonical以MOD-04 B-1b-03、四表以Database-Schema、交易以MOD-02同名補充為準，不以TASK取代Specification。不是Production／Migration實作授權。
- Request與完整靜態Items在首次T1前同準備交易保存，Preview零寫入；缺合法原輸入即阻擋，不改MOD-01或補Template。原集合／順序／Sequence／版本不可變。
- Item允許尚未形成的ResolvedInput／TargetName為NULL；NAMING T2凍結具名來源，T3保存TargetName與Item Reservation。Naming結果與ArtifactDisposition分離，不以假值補欄位。
- FILE_RENAME僅Create分支的T2核對NAMING Completed／Success、固定TargetName與Item Reservation，原子建立唯一Prepared Operation、一次性連結Reservation、保存fingerprint／owner與實際Running；確認Commit才允許Storage Create。前段NAMING Completed／Skipped只核對原Item Skip證據及本次合法Checkpoint／History，不要求Reservation／Operation／Receipt、不呼叫Create、不偽造Invocation。
- FILE_RENAME T3分支：Artifact成功經Inspect核對Operation／immutable payload／ExpectedContentVersion／Receipt／TargetReference／owner＋rowversion及合法Checkpoint／History後，同交易保存結果與必要Primary投影；前段Skip沿核准StepOutcome／Checkpoint契約提交Skipped，不建假Operation；確定後發碰撞核對CollisionEvidence、無本Operation Artifact、無未結束Create及原owner＋rowversion，Receipt／TargetReference必須NULL，依既有Policy提交Skipped或Permanent Failed，不改NAMING原Success。證據不足保持NeedsReconciliation，不換Operation／目標；Adapter證據不足仍阻擋。NAMING已因候選耗盡或Fail政策Permanent Failed時，不啟動FILE_RENAME、不建Operation或FILE_RENAME T3／成功History。
- Request、NAMING、FILE_RENAME三份Canonical與成員子schema按MOD-04固定欄位／Ordinal；schemaVersion自動，日期使用原來源offset，原檔名Token無副檔名，不納入runtime／Path或未產生TargetName。
- 100穩定候選限Operation前；後發確定碰撞Skip政策Skipped、其他Permanent Failed；無核准無副作用證據即NeedsReconciliation，不猜碰撞。前段Skip可使兩StepSkipped，後段不覆寫NAMING History，不偽造Artifact／成功Invocation。
- T2確認提交才外部I/O；T3／unknown／Inspect核對原持久證據，SQL不跨Storage；Resume／ManualRetry不改來源、不建第二Artifact，不改T5資格。
- Provider Manifest／Storage證據與驗收／CollisionEvidence映射／reconciliation／Failed跨Run／Request接點／跨Step版本等仍為Implementation Blockers；F-04-01、N-01／N-02未解除，不開始B-1c，不標TASK-04 COMPLETE。

## 15. Out of Scope

實作 EXIF／SHA256 duplicate／pHash／Naming 演算法、任意通用 Context 欄位、修改 Upload input、決定 N-01/N-02。

本 TASK 不授權修改正式規格、重新全面 Review MOD-01、開始其他 TASK 或自行決定 N-01/N-02。既有 Pkcs 安全警告仍屬正式部署／公開服務前必須處理的技術債，本 TASK 不擴大安全審查或自行升級。

