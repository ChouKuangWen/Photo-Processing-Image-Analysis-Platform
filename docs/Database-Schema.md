# 共用 Database Schema — 規格修訂草稿

日期：2026-10-12。這是已核准 ownership 下的 physical mapping 文件，不是已執行 Migration、實際資料庫驗證或所有未決 Schema 的批准。

## 1. 來源、責任與安全邊界

- 規格固定來源：develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90`。
- MOD-01 交接實作來源：feature `a1bb29e1d7cf704a202adbe1c14741b6441575be`，尚未合併。
- 既有 MOD-01 Spec、TASK-06、ProcessingJobConfiguration／InitialUploadPersistence Migration／ModelSnapshot 已核對；其中只有 Batch／Image／ProcessingJob，沒有 ProcessingLogs／ImageAnalysis／Group／Recommendation 表。
- 本環境沒有 PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING／ConnectionStrings__PhotoPlatform 可用設定；未驗證實際 DB、資料量或已部署 Migration。沒有讀取秘密值、連線或進行遷移。
- 本文件管理 physical mapping，MOD-02 管理 Job／Log 語意，MOD-09 管理 Query；業務規則仍在模組／System Spec，不放入 Schema。
- SQL Server provider 不變。Guid → uniqueidentifier；DateTimeOffset／nullable → datetimeoffset(7)；timestamp／rowversion 不可用作日期時間。
- Unicode 檔名／CameraModel／LocationName／ErrorMessage 使用 nvarchar；StoredPath／MimeType／SHA256／Status／Workflow／ErrorCode 沿用既有 varchar。
- 不修改 MOD-01。若新增欄位、改既有映射或資料遷移需影響 MOD-01，先回報理由與影響並取得批准；本草稿不授權改原始碼或執行 Migration。

## 2. 已確認的 MOD-01 physical mapping

以下表格與 Keys／Index／DeleteBehavior 逐項沿用 MOD-01 §2.2～§2.5；本文件不能反向變更該基線。
兩個 FK 各自有效不代表 ProcessingJob.BatchId 與 Image.BatchId 必然相同，Application 必須驗證正確關聯；D-07已核准在CreateJob／binding與執行交易驗證一致性，不新增MOD-01未核准的composite FK。

### Batches

### Table: `batches`

| 欄位 | 型別 | Nullable | Key / Index | Default | 說明 |
|---|---|---:|---|---|---|
| Id | UNIQUEIDENTIFIER | No | PK | — | Batch ID |
| TotalCount | INT | No | — | 0 | 圖片總數 |
| ProcessedCount | INT | No | — | 0 | 已處理數量 |
| SuccessCount | INT | No | — | 0 | 成功數量 |
| FailedCount | INT | No | — | 0 | 失敗數量 |
| Status | VARCHAR(30) | No | Index | — | Batch 狀態 |
| CreatedAt | DATETIMEOFFSET(7) | No | — | — | 建立時間 |
| CompletedAt | DATETIMEOFFSET(7) | Yes | — | NULL | 完成時間 |

Relationship：

```text
Batches 1 ─── N Images
```

---

### Images

### Table: `images`

| 欄位 | 型別 | Nullable | Key / Index | Default | 說明 |
|---|---|---:|---|---|---|
| Id | BIGINT | No | PK | Identity | Image ID |
| BatchId | UNIQUEIDENTIFIER | No | FK, Index | — | 所屬 Batch |
| OriginalFileName | NVARCHAR(255) | No | — | — | 原始檔名 |
| StoredPath | VARCHAR(500) | No | — | — | Storage Reference |
| NewFileName | NVARCHAR(255) | Yes | — | NULL | 新檔名 |
| FileSize | BIGINT | No | — | — | 檔案大小 |
| MimeType | VARCHAR(100) | No | — | — | MIME Type |
| SHA256 | VARCHAR(64) | Yes | Index | NULL | SHA-256 |
| TakenAt | DATETIMEOFFSET(7) | Yes | Index | NULL | 拍攝時間 |
| CameraModel | NVARCHAR(100) | Yes | — | NULL | 相機型號 |
| ISO | INT | Yes | — | NULL | ISO |
| ShutterSpeed | VARCHAR(50) | Yes | — | NULL | 快門速度 |
| Aperture | VARCHAR(50) | Yes | — | NULL | 光圈 |
| Latitude | DECIMAL(10,7) | Yes | — | NULL | 緯度 |
| Longitude | DECIMAL(10,7) | Yes | — | NULL | 經度 |
| LocationName | NVARCHAR(150) | Yes | — | NULL | 地點名稱 |
| Status | VARCHAR(30) | No | Index | — | Image 狀態 |
| CreatedAt | DATETIMEOFFSET(7) | No | — | — | 建立時間 |
| UpdatedAt | DATETIMEOFFSET(7) | No | — | — | 更新時間 |

Relationship：

```text
Images N ─── 1 Batches
```

---

### ProcessingJobs

### Table: `processing_jobs`

| 欄位 | 型別 | Nullable | Key / Index | Default | 說明 |
|---|---|---:|---|---|---|
| Id | BIGINT | No | PK | Identity | Job ID |
| ImageId | BIGINT | No | FK, Index | — | Image ID |
| BatchId | UNIQUEIDENTIFIER | No | FK, Index | — | Batch ID |
| Workflow | VARCHAR(30) | No | — | — | Workflow |
| Status | VARCHAR(30) | No | Index | — | Job 狀態 |
| RetryCount | INT | No | — | 0 | 重試次數 |
| CreatedAt | DATETIMEOFFSET(7) | No | — | — | 建立時間 |
| StartedAt | DATETIMEOFFSET(7) | Yes | — | NULL | 開始時間 |
| CompletedAt | DATETIMEOFFSET(7) | Yes | — | NULL | 完成時間 |
| ErrorCode | VARCHAR(50) | Yes | — | NULL | 錯誤代碼 |
| ErrorMessage | NVARCHAR(MAX) | Yes | — | NULL | 錯誤訊息 |

Relationships：

```text
ProcessingJobs N ─── 1 Images
ProcessingJobs N ─── 1 Batches
```

---

### Keys / Indexes / Constraints

### Batches

```text
PK:
Batches.Id
```

### Images

```text
PK:
Images.Id

FK:
Images.BatchId → Batches.Id

INDEX:
Images.BatchId
Images.SHA256
Images.Status
Images.TakenAt
```

### ProcessingJobs

```text
PK:
ProcessingJobs.Id

FK:
ProcessingJobs.ImageId → Images.Id

FK:
ProcessingJobs.BatchId → Batches.Id

UNIQUE:
ProcessingJobs.ImageId + ProcessingJobs.Workflow

INDEX:
ProcessingJobs.ImageId
ProcessingJobs.BatchId
ProcessingJobs.Status
```

---

### Foreign Key DeleteBehavior

以下三條關聯必須明確設定 `DeleteBehavior.NoAction`，不得依賴 EF Core default behavior：

- Batch → Images（Images.BatchId → Batches.Id）。
- Image → ProcessingJobs（ProcessingJobs.ImageId → Images.Id）。
- Batch → ProcessingJobs（ProcessingJobs.BatchId → Batches.Id）。



## 3. ProcessingRuns（D-05 已核准）

logical name ProcessingRuns；physical name `processing_runs`，沿用MOD-01 snake_case table命名，不改既有表。

| 欄位 | Type | Nullable／Default／Key |
|---|---|---|
| Id | BIGINT IDENTITY | NOT NULL PK |
| JobId | BIGINT | NOT NULL FK→processing_jobs.Id／NoAction |
| RunNo | INT | NOT NULL；初始1、人工2～4 |
| Reason | VARCHAR(20) | NOT NULL；Initial／ManualRetry |
| Status | VARCHAR(20) | NOT NULL；Pending／Active／Completed／Failed |
| WorkflowVersion | VARCHAR(50) | NOT NULL；workflow-mvp-v1，依SYS/MOD02最終Step列表，不填未知default |
| CreatedAt | DATETIMEOFFSET(7) | NOT NULL |
| StartedAt | DATETIMEOFFSET(7) | NULL；首次Active才設定，Resume保留 |
| CompletedAt | DATETIMEOFFSET(7) | NULL；terminal才設定 |
| TraceId | VARCHAR(100) | NULL；來源可核對，Worker trace不冒稱HTTP trace |

UNIQUE(JobId,RunNo)，CHECK RunNo BETWEEN 1 AND 4；Reason=Initial限RunNo1，ManualRetry限2～4。
每Job只能有一個未關閉Run：filtered UNIQUE(JobId) WHERE Status IN ('Pending','Active')。這將原單一Active限制延伸至人工Retry準備的Pending Run，防止兩個未關閉Run；不新增Job欄位。
Run state／Job投影／人工Retry交易見MOD-02 §1.18～§1.19；不在本章重新定義業務轉換。

## 4. ProcessingStepCheckpoints（D-05）

physical name `processing_step_checkpoints`。

| 欄位 | Type | Nullable／Key |
|---|---|---|
| Id | BIGINT IDENTITY | NOT NULL PK |
| RunId | BIGINT | NOT NULL FK→processing_runs.Id／NoAction |
| Step | VARCHAR(50) | NOT NULL |
| StepOrder | INT | NOT NULL，>0 |
| RuleVersion | VARCHAR(50) | NOT NULL；所屬MOD最終規則版本，未知引擎版本阻擋不填default |
| InputFingerprint | VARCHAR(64) | NOT NULL；原始內容SHA256+versioned canonical input JSON之SHA256，詳細邊界見§9 |
| Status | VARCHAR(20) | NOT NULL；Pending／Processing／RetryWaiting／Completed／Failed |
| RetryOrdinal | INT | NOT NULL，初始0，CHECK 0～3 |
| NextRetryAt | DATETIMEOFFSET(7) | NULL |
| ResultReference | VARCHAR(500) | NULL；受控已提交結果識別，不是任意path |
| ResultStatus | VARCHAR(20) | NULL；Success／Warning／Skipped |
| ErrorCode | VARCHAR(50) | NULL；受控code |
| ErrorClassification | VARCHAR(20) | NULL；Transient／Permanent；未知不猜分類 |
| StartedAt | DATETIMEOFFSET(7) | NULL；該checkpoint首次開始 |
| CompletedAt | DATETIMEOFFSET(7) | NULL；terminal才設定 |

UNIQUE(RunId,Step)、UNIQUE(RunId,StepOrder)。Status／ResultStatus取值須CHECK；Job／Image／Batch透過Run與既有Job取得，不增加JobMapping。
結果重用依F-01～F-08與各MOD最終章；有欄位不代表已實作adapter/遷移。新增Dependency等待欄位與互斥constraint見§9。

## 5. ProcessingLogs（D-06 單一 History writer）

physical name `processing_logs`；MOD-02單一writer管理，MOD-09讀取／轉發；不是Audit／Outbox／durable queue。

| 欄位 | Type | Nullable／Key |
|---|---|---|
| Id | BIGINT IDENTITY | NOT NULL PK |
| StepCheckpointId | BIGINT | NOT NULL FK→processing_step_checkpoints.Id／NoAction |
| JobId | BIGINT | NOT NULL FK→processing_jobs.Id／NoAction |
| ImageId | BIGINT | NOT NULL FK→images.Id／NoAction |
| AttemptNo | INT | NOT NULL，>0；checkpoint內實際invocation序號 |
| RetryOrdinal | INT | NOT NULL，CHECK 0～3；邏輯槽位 |
| ResumeNo | INT | NOT NULL，CHECK >=0；同槽位恢復序號 |
| Status | VARCHAR(20) | NOT NULL；Running／Success／Warning／Failed／Interrupted／Skipped |
| StartedAt | DATETIMEOFFSET(7) | NOT NULL |
| CompletedAt | DATETIMEOFFSET(7) | NULL |
| DurationMs | BIGINT | NULL；有值時>=0，未知不是0 |
| ErrorCode | VARCHAR(50) | NULL |
| ErrorMessage | NVARCHAR(MAX) | NULL；受控安全訊息 |
| Timestamp | DATETIMEOFFSET(7) | NOT NULL；record建立時間，不因terminal更新改排序key |
| TraceId | VARCHAR(100) | NULL |

UNIQUE(StepCheckpointId,AttemptNo)、UNIQUE(StepCheckpointId,RetryOrdinal,ResumeNo)；filtered UNIQUE(StepCheckpointId) WHERE Status='Running'。
需維持外鍵與實際checkpoint→Run→Job→Image的一致性，由writer在同一交易核對，不改原Job／Image的constraint。
Step／RunId為checkpoint關聯與DTO投影，不另存physical Step／RunId；舊DTO retryCount映射RetryOrdinal，不新增physical RetryCount欄位。
不以UNIQUE(ImageId,Step)限制attempt；terminalStatus immutable，新的Resume保留舊History。
查詢索引使用 (ImageId,Timestamp DESC,Id DESC)、(JobId,Timestamp DESC,Id DESC) 與Checkpoint FK；BatchQuery以Job.BatchId關聯。新增索引限本Log表，不擅自改MOD-01索引。
BIGINT／generation／FK／Status與這些執行表為已核准設計，不是已建立DB或已可遷移。現有DB若有GUID Log／其他表仍須先回報impact；本次不執行Migration。

## 6. ImageProcessingBindings（D-07）

physical name `image_processing_bindings`，不新增或修改MOD-01 Image／Job欄位。

| 欄位 | Type | Nullable／Key |
|---|---|---|
| ImageId | BIGINT | NOT NULL PK／FK→images.Id／NoAction |
| BatchId | UNIQUEIDENTIFIER | NOT NULL FK→batches.Id／NoAction |
| PrimaryJobId | BIGINT | NOT NULL UNIQUE／FK→processing_jobs.Id／NoAction |
| PrimaryWorkflow | VARCHAR(30) | NOT NULL；沿用既有Workflow名稱 |
| PrimaryStatus | VARCHAR(30) | NOT NULL；Pending／Processing／Completed／Failed |
| UpdatedAt | DATETIMEOFFSET(7) | NOT NULL |

binding建立與Status投影更新核對PrimaryJob.ImageId／BatchId／Workflow一致。PK／UNIQUE不代表這些跨表值自動相同。
PrimaryJobId／Workflow綁定後不改；未知來源先阻擋。原TotalCount、BatchQuery、JobMapping／unique／NoAction不變；業務delta與Batch狀態見MOD-02 §1.15。

## 7. MOD-03～MOD-10 最終文件狀態

Metadata/Naming/versioned hash/EXACT/VISUAL/Quality/Recommendation與diagnostic語意已依F-01～F-08核准同步§9；解碼/ICC引擎與精確版本N-01仍未定，pHash manifest不能發佈。Export無專用表、延後新能力且未通過完整實作准入；Migration/DB相容性/所有實作驗收未執行。

---

## 9. F-01～F-08 最終結果與input mapping（規格，非Migration）

### 共通型別、ownership與canonical input

Guid→uniqueidentifier；Id為BIGINT identity（Request/Operation/Snapshot為GUID）；時間datetimeoffset(7)、日期不是rowversion。FK NoAction，不加MOD01 composite FK/欄位/索引。Unicode展示/raw text用nvarchar，hash/profile/error受控ASCII用varchar/char。Outcome nullable直到Completed；Completed/Failed有CompletedAt，Pending/Processing無CompletedAt；Failed有ErrorCode，Completed必要result/reference非NULL。所有新結果table有rowversion與ProcessingOwnerCheckpointId nullable FK→processing_step_checkpoints.Id，作SQL條件更新/同image-step-version所有權，不取代Run/History authority。
result-key唯一+ProcessingOwnerCheckpointId核對持久checkpoint防多Worker同時同key副作用；不同版本結果可並存，primary/current projection只有T3授權更新。Result Processing不能因clock timeout被其他Worker偷取；僅既有舊process停止確認/恢復契約可處理孤立owner，未知副作用阻擋。提交result更新必須同rowversion+owner條件成功，再同T3閉checkpoint/History/Run/Job，不能先寫結果再猜claim。
ContentVersion=原始內容SHA256（64小寫hex），不假設MOD01存storage version。InputFingerprint=SHA256(versioned canonical input JSON UTF8)：固定schemaVersion、property ordinal ordering、整數/decimal invariant不科學記法、string unicode原值不額外casefold、無BOM/空白、UTC instant O格式、nullable明確null；array按凍結業務order；includeImageId/ContentVersion/WorkflowVersion/RuleVersion與Result/Input references，excluded runtime time/trace/attempt。相同input必須同fingerprint，變更input不是Shutdown Resume。RuleVersion是受控版本識別，不包含secret。ResultReference為受控table-name/id/version不超500，不是任意storage path。

### Metadata（MOD03）

image_metadata_results：Id BIGINT PK、ImageId BIGINT FK、SourceSHA256 CHAR64、MetadataRuleVersion VARCHAR50、InputFingerprint CHAR64，UNIQUE(ImageId,InputFingerprint,MetadataRuleVersion)；Status VARCHAR20 Pending/Processing/Completed/Failed，Outcome VARCHAR20 nullable Success/Warning、RawTakenAt NVARCHAR64 nullable、ExifOffsetMinutes SMALLINT nullable、ResolvedTakenAt datetimeoffset nullable（Completed必有）、TimeSource VARCHAR30 nullable、OffsetKnown bit nullable（Completed必有）、Orientation tinyint nullable CHECK1..8、Latitude/Longitude decimal(10,7) nullable，完整有效pair才同時非NULL；GpsValidity VARCHAR20、GeoOutcome VARCHAR30、LocationName NVARCHAR150 nullable、FieldProvenance NVARCHAR(MAX) versioned JSON，CHECK ISJSON；ErrorCode VARCHAR100 nullable、CreatedAt/CompletedAt、rowversion/owner。
JSON schemaVersion=1、allowed欄位cameraModel(raw/normalized/validity)、iso、aperture、exposureTime(raw numerator/denominator或normalized decimal)、GPS refs/raw、time source/raw/validity與orientationRaw；validity限Valid/Missing/Invalid/Fallback，不保存任意EXIF dump/client payload。Completed結果按MOD03有效性規則；缺GPS与服务故障分开。

### Naming（MOD04）

naming_requests：Id GUID PK、BatchId GUID FK、Template NVARCHAR1000、RuleVersion VARCHAR50、CollisionPolicy VARCHAR30沿用既有enum、InputFingerprint CHAR64、CreatedAt。
naming_request_items：Id BIGINT PK、RequestId GUID FK、ImageId BIGINT FK、JobId BIGINT FK UNIQUE、Ordinal INT CHECK>=0、Sequence BIGINT CHECK>=0、OriginalExtension NVARCHAR20、ResolvedInput NVARCHAR(MAX) ISJSON/schemaVersion1、TargetName NVARCHAR255；UNIQUE(RequestId,ImageId)、UNIQUE(RequestId,Ordinal)。ResolvedInput只有已核對template tokens/原名/ext/時間source/location/order/sequence snapshot，不存無來源default。每Item.Job與Image/Batch同交易核對，Run input-reference對應Item，不創造BatchJob。
naming_operations：OperationId GUID PK、ItemId BIGINT FK UNIQUE、ExpectedContentVersion CHAR64、TargetReference VARCHAR500（opaque ASCII storage reference）、Receipt NVARCHAR(MAX) nullable ISJSON、Status VARCHAR30 Prepared/ArtifactCreated/Committed/NeedsReconciliation、rowversion、CreatedAt/CompletedAt；同operation payload immutable；Committed必有可Inspect核對receipt。
naming_artifact_reservations：TargetKey NVARCHAR500 normalized、OperationId GUID FK UNIQUE，TargetKey UNIQUE NONCLUSTERED；Unicode目標不能誤用varchar損失。TargetKey按已選provider/case policy canonicalization，receipt保留原display filename；不以SQL默认collation代替provider collision规则，唯一键使用明确binary比较的canonical key。新表調整Unicode mapping是沿用共用規則，不動MOD01。
沒有Compensated自動刪除狀態；cleanup/保留/reapply未核准，不自動清理reservation/artifact。

### Hash／EXACT／VISUAL（MOD05/06）

image_hash_results：Id BIGINT PK、ImageId FK、ContentVersion CHAR64、HashRuleVersion VARCHAR50、SHA256 CHAR64 nullable、FileSize BIGINT CHECK>=0、Status/error/CreatedAt/CompletedAt/rowversion/owner，UNIQUE(ImageId,ContentVersion,HashRuleVersion)；Completed hash64小寫hex。
image_perceptual_hashes：Id BIGINT PK、ImageId FK、ContentVersion CHAR64、ProfileVersion VARCHAR50、HashHex VARCHAR16 nullable（phash-dct64-v1為16）、BitLength INT、ManifestChecksum CHAR64、Status/error/CreatedAt/CompletedAt/rowversion/owner，UNIQUE(ImageId,ContentVersion,ProfileVersion)。Profile未凍結不寫假的manifest版本；hash完成独立於Quality。
exact_groups：Id GUID PK、FileSize BIGINT、SHA256 CHAR64 UNIQUE(FileSize,SHA256)，集合唯一前提是單部署單資料集合。exact_group_members：GroupId FK、ImageId FK、HashResultId FK、UNIQUE(GroupId,ImageId)。Membership snapshot/result reference保留版本，晚到候選不覆寫原Job History。
visual_snapshots：Id GUID PK、ProfileVersion/RuleVersion VARCHAR50、CandidateFingerprint CHAR64、Status VARCHAR20 Pending/Processing/Completed/Failed、ErrorCode、CreatedAt/CompletedAt/rowversion/owner。visual_memberships保存SnapshotId/GroupId/ImageId/HashResultId，UNIQUE(SnapshotId,ImageId)；同snapshot single-link每圖僅一component，孤立圖用明確singleton/result表示。
visual_edges：SnapshotId FK、ImageLo/ImageHi FK、ContentVersionLo/Hi CHAR64、ProfileVersion VARCHAR50、Score decimal(18,8) CHECK0..1、CHECK ImageLo<ImageHi、UNIQUE(SnapshotId,ImageLo,ImageHi)，只保存>=groupThreshold。snapshot candidates版本受控input-reference；candidate/edgeCount<=本次1000/50000配置才可發布，超限Failed/staging不作正式結果。current snapshot reference使用MOD06自己的singleton result-pointer/rowversion原子切換，不改MOD01表；歷史不清理。低threshold Query只讀hash，不新增edge/pair。

### Quality／Recommendation（MOD07/08）

quality_results：Id BIGINT PK、ImageId FK、ContentVersion CHAR64、QualityRuleVersion VARCHAR50、MetadataResultId FK、RawMetrics/ComponentResults NVARCHAR(MAX) versionedJSON、QualityScore decimal(9,6)、Grade VARCHAR20、Status/error/CreatedAt/CompletedAt/rowversion/owner；UNIQUE(ImageId,ContentVersion,QualityRuleVersion,MetadataResultId)，metadata reference是input一部分，避免provenance更新重用錯結果。Complete結果有六位score、API兩位，不能用shared row存在Skip；current ImageAnalysis只是projection。
recommendation_results：Id GUID PK、SnapshotId FK、QualityRuleVersion/RecommendationRuleVersion VARCHAR50、InputFingerprint CHAR64、Status VARCHAR20 Pending/Processing/Completed/Failed、Freshness VARCHAR20 Current/Stale、RankedResults/InputReferences NVARCHAR(MAX) versionedJSON、CreatedAt/CompletedAt/rowversion/owner；UNIQUE(SnapshotId,QualityRuleVersion,RecommendationRuleVersion)，凍結每member QualityResultId/pHash版本，缺input不當0。freshness不是Job狀態；版本變化不改舊完成History。
API/Query only references Completed與version一致的result；Failed/Processing/unknown不冒充None/NoMatch。

### DependencyWait checkpoint擴充（MOD02/08）

processing_step_checkpoints增加DependencyWaitStartedAt datetimeoffset nullable、DependencyWaitDeadlineAt datetimeoffset nullable、DependencyWaitStatus VARCHAR20 nullable Waiting/Resolved/Expired。三者全NULL或全非NULL；deadline>start。Waiting限VERSION_ANALYSIS且checkpoint.Pending，Run.Active/Job.Processing、無RunningLog；Resolved才可checkpoint.Processing，Expired→checkpoint.Failed/DEPENDENCY_NOT_READY、Run/Job Failed。cross-table條件由同SQL管理transaction核對，不能以單列CHECK聲稱已保證。
首次wait使用DB UTC加本次可設定120秒；deadline不因重啟/配置/補調度重設；保留Resolved/Expired/start/deadline記錄。T2-W/T2與Running唯一index協調互斥，錯誤到期沒有實際invocation不偽造History。

### 未驗證／延後

以上是已核准規格形狀與可推導型別/transaction細節，不是Migration/DDL執行。實際DB舊表/資料/索引/相容性仍未核對；若牽動MOD01現有mapping須先另報。N-01 engine/精確版本阻擋pHash完整manifest，golden bytes/table/expected-output及效能量測由後續實作產生與驗收，未PASS。Export/Audit無新表，進階metric不持久化。

## 最後一致性檢查發現：N-02候選pHash就緒的調度契約

正式VISUAL_SIMILARITY要求完整同版本候選snapshot，Workflow將其放在Quality前；其他Image仍可能在Pending或尚未pHash。F-06核准的T2-W dependency wait目前只限VERSION_ANALYSIS，沒有定義VISUAL候選未齊時的等待／釋放Worker／恢復／到期。不能使用partial hash集合冒充完整snapshot，也不能占滿所有Worker等待其他Job。
這是規格缺口而非已證實runtime deadlock。保持Analysis/Full完整實作准入BLOCKED，核心TASK規劃與Naming不因本缺口阻擋。推薦下一次針對N-02核准把VISUAL候選等待納入既有T2-W（讓出Worker、持久deadline、版本核對、無Running/不消耗Retry、ready後原子開始）；VISUAL等待budget與逾時政策需集中決定，不能擅自挪用Recommendation120秒或改Step順序。本輪不實作、不建立TASK、不改已核准checkpoint wait範圍。
