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

## Phase B-1a 核准補充：原圖型輸入綁定

本節為人工核准的規格定義，不是已建立資料表、Migration 或執行授權。僅補 EXIF_PARSING／SHA256_CALCULATION 的結果前持久來源；業務凍結、Canonical 欄位與 Fail-Closed 規則見 [MOD-02 B-1a](modules/MOD-02-Processing-Spec.md#最終核准補充--phase-b-1a-原圖型持久輸入契約)。保留既有表、Mapping、Migration 及資料，不重寫歷史章節。

### processing_original_inputs

| 欄位 | SQL Type | Nullability／Key／規則 |
|---|---|---|
| JobId | BIGINT | NOT NULL；PK；FK → processing_jobs.Id，NoAction |
| ContentVersion | CHAR(64) | NOT NULL；恰64字元小寫 SHA256 |
| ContentLength | BIGINT | NOT NULL；CHECK >= 0 |
| WorkflowVersion | VARCHAR(50) | NOT NULL；非空；必須是核准且可解析版本 |

同一 Job 僅一份原圖版本。ImageId 透過 Job 定位，不另存 Storage Path、原圖 bytes 或重複 ImageId。

### processing_original_step_inputs

| 欄位 | SQL Type | Nullability／Key／規則 |
|---|---|---|
| JobId | BIGINT | NOT NULL；FK → processing_original_inputs.JobId，NoAction |
| Step | VARCHAR(50) | NOT NULL；限定 EXIF_PARSING／SHA256_CALCULATION |
| RuleVersion | VARCHAR(50) | NOT NULL；非空；原核准且可解析版本 |
| UploadCreatedAt | DATETIMEOFFSET(7) | EXIF 必填；SHA256 必須 NULL |
| InputFingerprint | CHAR(64) | NOT NULL；恰64字元小寫 SHA256 |

PK(JobId,Step)，不另設 identity。必要 CHECK：Step 精確符合兩種值；(Step=EXIF_PARSING AND UploadCreatedAt IS NOT NULL) OR (Step=SHA256_CALCULATION AND UploadCreatedAt IS NULL)。不得讓 SQL UNKNOWN 放行錯誤 NULL 組合。

兩表 hash 欄位都必須檢查實際完整長度與僅含0～9／a～f，不得接受 CHAR padding、空白、大寫或依預設大小寫不敏感 collation 放行。Step／hash 的精確字元比對須使用可保證上述語意的檢查；WorkflowVersion／RuleVersion 必須拒絕空白值，真正核准版本解析由 Application／writer 核對，SQL 非空檢查不能授予執行權。

### 不可變性與跨表交易責任

- 首次 T1 與 Claim／Run／首個 invocation 同交易保存 parent 與所需 Step contracts；只保存 Workflow 實際包含的原圖型 Step。不能提前建立後續 Step 的 Running Log。
- 來源建立後不可更新替換；重複 Claim 只可核對相同值，不使用 upsert 改綁。PK／FK 防止重複與孤立引用，不代表 SQL 已自行保證不可變或跨表業務一致。
- writer 在同交易核對 Job／Image／Batch 關聯、Workflow 所需 Step 齊全、Run.WorkflowVersion 與 checkpoint 的 RuleVersion／InputFingerprint／順序一致。Step 的原圖 ContentVersion／Length 一律取同 Job parent。
- T2／同 Run Resume／同 Job ManualRetry 沿原綁定，不重新選版本；沿 Run.JobId＋Step 查詢，不新增 Run／Checkpoint 欄位。原圖 ContentLength 必須與 Reader 完整內容及接受時 Image.FileSize 核對；不一致不得靜默修改 MOD-01 資料。
- InputFingerprint 依 MOD-02 本章兩個具名 Canonical v1 Schema 計算；EXIF UploadCreatedAt 為參與 hash 的業務時間，SHA 不含此欄位。schemaVersion 由 serializer 輸出，不新增資料庫欄位。
- 不新增 Context JSON、Retry Counter、JobStatus／CheckpointStatus 或 API；不以結果表或 ResultReference 代替原圖輸入綁定。
- 後續實作若獲批准，須新增式 Migration 與 SQL 約束／升級驗證，不修改既有 Migration；本輪不建立或執行 Migration。
- 缺綁定不自動 backfill，來源 mismatch 不換成新內容；不得僅憑 mismatch 關閉 Running。診斷代碼／回報接點／未關閉 Invocation 持久處置未定，相關路徑仍阻擋實作。F-04-01 其他輸入缺口、N-01／N-02 未解除。

## B-1b-01 核准補充：EXIF／GEO Authority 與 GEO Input Binding

本節優先於 §9 同題的共享 Metadata 欄位定義；僅為 Specification，非已建立表或 Migration 授權。保留 MOD-01 原表及現有 Run／Checkpoint Mapping，不修改既有 Migration。

### image_metadata_results Authority 調整

保留 §9 的 Id／ImageId、SourceSHA256、MetadataRuleVersion、InputFingerprint、Status／Outcome、EXIF 欄位、原始 FieldProvenance、時間、owner／rowversion，以及 UNIQUE(ImageId,InputFingerprint,MetadataRuleVersion)。Completed EXIF 的業務內容、來源與完成證據不可覆寫。
GeoOutcome／LocationName 不再屬本表正式 Result Authority，移至 image_geo_results；不保留雙重可更新權威。這不是刪除實際 SQL 欄位的授權。GPS Valid 為合法成對座標；Missing／Invalid 正規化座標均 NULL，原始非法值／reference 保留於受控 EXIF provenance。

### image_geo_results

| 欄位 | 型別／Nullability／Key |
|---|---|
| Id | BIGINT NOT NULL PK |
| MetadataResultId | BIGINT NOT NULL FK → image_metadata_results.Id，NoAction |
| GeoRuleVersion | VARCHAR(50) NOT NULL，非空、核准且可解析 |
| InputFingerprint | CHAR(64) NOT NULL，精確小寫 SHA256 |
| Status | VARCHAR(20) NOT NULL |
| Outcome | VARCHAR(20) NULL |
| GeoOutcome | VARCHAR(30) NULL |
| LocationName | NVARCHAR(150) NULL |
| ErrorCode | VARCHAR(100) NULL |
| CreatedAt | DATETIMEOFFSET(7) NOT NULL |
| CompletedAt | DATETIMEOFFSET(7) NULL |
| ProcessingOwnerCheckpointId | BIGINT NULL FK → processing_step_checkpoints.Id，NoAction |
| rowversion | SQL rowversion |

UNIQUE(MetadataResultId,GeoRuleVersion,InputFingerprint)。ImageId／原內容透過不可變 EXIF 定位，不重複存放。FK 存在不證明 EXIF Completed 或版本一致。

CHECK 必須以明確 IS NULL／IS NOT NULL 及精確字串比較，僅允許：
- Pending／Processing：Outcome、GeoOutcome、LocationName、ErrorCode、CompletedAt 全為 NULL。
- Completed／Success／Resolved：LocationName 有效且非空、ErrorCode NULL、CompletedAt 非 NULL。
- Completed／Skipped／MissingGPS：LocationName=UnknownLoc、ErrorCode NULL、CompletedAt 非 NULL。
- Completed／Warning／InvalidGPS 或 TransientExhausted：LocationName=UnknownLoc、ErrorCode NULL、CompletedAt 非 NULL。
- Failed／Outcome NULL／AuthFailure 或 ConfigurationFailure：LocationName NULL、ErrorCode 非空、CompletedAt 非 NULL。
不得由 SQL UNKNOWN 放行不合法組合。Hash 限完整64字元0～9/a～f，拒絕 padding／空白／大寫；版本不得為空白。精確錯誤碼批准與版本解析由模組／writer 核對，不以長度或 NOT NULL 代表業務核准。
Pending owner 可 NULL；Processing／Completed／Failed owner 必須非 NULL。owner FK 不證明合法寫入權；交易核對 GEO checkpoint、原 invocation、Job／Image、來源／版本及 rowversion。Completed／Failed 保留提交 owner，跨 Job 消費不更換 owner。Completed 業務結果不可覆寫；同 key Failed 不自動重開或移交，不新增 Result Attempt 表。PK／FK／CHECK 不自行保證上述不可變與跨表語意。

### processing_geo_inputs

| 欄位 | 型別／Nullability／Key |
|---|---|
| JobId | BIGINT NOT NULL PK／FK → processing_original_inputs.JobId，NoAction |
| MetadataResultId | BIGINT NOT NULL FK → image_metadata_results.Id，NoAction |
| GeoRuleVersion | VARCHAR(50) NOT NULL，非空、核准且可解析 |
| InputFingerprint | CHAR(64) NOT NULL，精確小寫 SHA256 |

版本／hash CHECK 同上。不加入 Context JSON、Path 或 retry counter；原 EXIF MetadataRuleVersion／fingerprint／SourceSHA256 由不可變來源查回。
首次 GEO T2 沿核准執行鏈選定來源，核對 EXIF 已提交、同 Image／原內容／Workflow；綁定與 Running Invocation 同交易保存或回滾。既有綁定只能比對完全一致，不 upsert 改來源。Resume／ManualRetry 沿 Run.JobId 找原綁定；Binding 不授予 Result owner。
Canonical 欄位、Null 與順序以 MOD-03 B-1b-01 補充為準，schemaVersion 由 serializer 產生，不加資料庫欄位。Completed GEO ErrorCode 一律 NULL，TransientExhausted 的錯誤碼只存原 Failed History；結果／History／checkpoint 同 T3。
缺綁定不 backfill、mismatch 不換最新、commit unknown 先核對持久證據，不僅憑核對失敗關閉 Running。診斷碼／解除阻擋／Provider Manifest／API 展示仍待決；新增表仍僅存在於規格，未解除 F-04-01、N-01／N-02。

## B-1b-02 核准補充：QUALITY 持久輸入

本節是人工核准的 Specification，優先補足 B-1b-01 中 QUALITY input binding 尚未定義的同題描述；不表示 EF Entity／Mapping 或 SQL Server 表已交付。保留既有 quality_results 唯一鍵與 MOD-01、Run／Checkpoint 結構，不修改既有 Migration。

### processing_quality_inputs

| 欄位 | 型別／Nullability／Key |
|---|---|
| JobId | BIGINT NOT NULL，PK／FK → processing_original_inputs.JobId，NoAction |
| MetadataResultId | BIGINT NOT NULL，FK → image_metadata_results.Id，NoAction |
| QualityRuleVersion | VARCHAR(50) NOT NULL，非空、受控且須可解析 |
| InputFingerprint | CHAR(64) NOT NULL，精確64字元小寫 SHA256 |

JobId PK 保證每 Job 一組綁定，不另增 identity、rowversion、Result Owner、Retry Counter 或重複 Unique。不保存原圖 bytes、Storage Path、完整 Metadata JSON。版本 CHECK 拒絕空白；hash CHECK 僅接受完整0～9/a～f，拒絕 padding、空白、大寫及非法長度，不能依大小寫不敏感比較放行。
建立後不可修改／覆寫／換最新來源；重複建立只允許完全一致核對，不 upsert 改綁。PK／FK 不保證來源 Completed、內容正確或不可更新；由 T2 核對跨表契約。

對目前包含 QUALITY_ANALYSIS 的核准 Workflow，同 Job 的 processing_quality_inputs.MetadataResultId 必須等於 processing_geo_inputs.MetadataResultId，且兩者均符合原 Job 的核准 EXIF 結果鏈；同 ImageId 或最新 EXIF 不能替代。不新增無 GEO 的 QUALITY 執行路徑，也不擅加未核准的 composite FK。
原 ContentVersion／ContentLength／WorkflowVersion 由 processing_original_inputs 查回；MetadataRuleVersion、Metadata input fingerprint、有效性／Orientation／provenance 由不可變 EXIF 查回。Canonical 欄位及順序依 MOD-07 B-1b-02 補充，不另存重複 JSON 或 schemaVersion。
首次 QUALITY T2 同交易核對來源／版本／前置 Step、保存或核對 binding 與 checkpoint fingerprint、建立 Running Invocation；整筆提交或回滾，確認 Commit 才執行。完整 Storage I/O／分析不包在 SQL transaction。

### 與結果及版本來源的界線

quality_results 保留 UNIQUE(ImageId,ContentVersion,QualityRuleVersion,MetadataResultId)，不新增 Result Attempt。此鍵不等於完整 InputFingerprint；Completed 候選仍須核對來源、版本、原 Owner Checkpoint 與完整輸入證據，不將不同 WorkflowVersion 自動視為相同業務輸入。
Binding 不授予結果寫入權；owner 是原生產者，不是所有消費者。T3 仍核對 owner＋rowversion，與 History／Checkpoint 原子提交；Failed 不冒充完成，同 key Failed 無核准處置時 Fail-Closed。
已有 binding 的 Resume／ManualRetry 沿同 Job 原輸入。原 Run 尚未開始 QUALITY 且無 binding 時，不虛構 MetadataResultId、不選最新 QualityRuleVersion；若原 Workflow 缺可重建的持久版本依據，該路徑為 Implementation Blocker。本節不新增版本選擇表、預設或政策。
N-01 profile、N-02、F-04-01 其他輸入缺口、Reader 同 bytes、Missing／Mismatch 診斷及未關閉 invocation 處置仍未完成。未來實作須另獲授權並以新增式 Migration／真實 SQL 測試驗證，本輪不建立或執行 Migration。

## B-1b-03 核准補充：Naming／Artifact 四表生命週期

本節優先於§9舊Naming表同題的必填時機／Reservation關聯／操作狀態描述；僅Specification，不代表EF或SQL已交付。保留四表，不新增第五表，不改MOD-01或既有Migration。業務與具名Canonical／CollisionEvidence以MOD-04 B-1b-03為準；所有FK採NoAction。

### naming_requests／naming_request_items

Request保留Id GUID PK、BatchId FK、Template NVARCHAR(1000)、RuleVersion VARCHAR(50)、CollisionPolicy VARCHAR(30)、InputFingerprint CHAR(64)、CreatedAt；新增SequenceStart BIGINT NOT NULL CHECK>=0。Request與完整Items首次T1前同一準備交易保存，靜態集合不可增刪改序；fingerprint可由原成員完整重建。
Item保留Id BIGINT PK、RequestId／ImageId／JobId FK、UNIQUE(JobId)、UNIQUE(RequestId,ImageId)、UNIQUE(RequestId,Ordinal)、Ordinal INT>=0、Sequence BIGINT>=0、OriginalExtension NVARCHAR(20)；新增OriginalFileName NVARCHAR(255) NOT NULL（不含副檔名）。靜態欄位非NULL且不可變，Ordinal連續及Sequence=checked(Start+Ordinal)、Job／Image／Batch一致性由準備交易核對，不聲稱FK可保證。

| Item新增／調整欄位 | 型別／Nullability／限制 |
|---|---|
| ResolvedInput | NVARCHAR(MAX) NULL；具名schemaVersion1，非NULL須ISJSON |
| TargetName | NVARCHAR(255) NULL；僅正式命名成功結果才填入 |
| NamingStatus | VARCHAR(20) NOT NULL；Pending／Processing／Completed／Failed |
| NamingOutcome | VARCHAR(20) NULL；Success／Skipped |
| InputFingerprint | CHAR(64) NULL；NAMING T2凍結 |
| ProcessingOwnerCheckpointId | BIGINT NULL FK→processing_step_checkpoints.Id；NAMING owner |
| rowversion | rowversion，SQL併發核對 |
| CompletedAt | DATETIMEOFFSET(7) NULL；NAMING終態時間 |
| ErrorCode | VARCHAR(100) NULL；既有核准錯誤碼 |
| CollisionEvidence | NVARCHAR(MAX) NULL；非NULL須ISJSON，MOD-04具名schemaVersion1 |
| ArtifactDisposition | VARCHAR(20) NULL；Created／Skipped／Failed，獨立於NamingOutcome |

CHECK：Pending的ResolvedInput／InputFingerprint／TargetName／Outcome／CompletedAt／ErrorCode／owner為NULL。Processing須ResolvedInput／fingerprint／owner非NULL，TargetName／Outcome／CompletedAt／ErrorCode為NULL。Completed／Success須來源、fingerprint、owner、TargetName、CompletedAt非NULL；Completed／Skipped須來源、fingerprint、owner、CompletedAt、CollisionEvidence非NULL且TargetName NULL。Completed的ErrorCode NULL。Failed須來源、fingerprint、owner、CompletedAt、ErrorCode非NULL，Outcome／TargetName NULL；非碰撞失敗不強制CollisionEvidence。未知列舉及不完整組合拒絕，NULL分支須明確，不能靠SQL UNKNOWN放行。
ArtifactDisposition Created只能對應Naming Completed／Success；Skipped只能對應Naming Completed／Success或Skipped；Failed只能對應Naming Completed／Success，表示後續Artifact失敗，不代替Naming Failed。NULL不代表成功。實際Operation／Receipt及Skip證據由交易核對；跨表一致性不是CHECK可單獨完成。Item狀態不照抄Checkpoint Recovery狀態，不清除凍結輸入。Completed命名業務內容／owner不可改，後續只更新獨立ArtifactDisposition並核對rowversion。

### naming_operations

保留OperationId GUID PK、ItemId BIGINT NOT NULL FK UNIQUE、ExpectedContentVersion CHAR(64) NOT NULL、rowversion、CreatedAt／CompletedAt。TargetReference VARCHAR(500)改為NULLable，Receipt NVARCHAR(MAX)保持NULLable且非NULL須ISJSON。Status VARCHAR(30) NOT NULL保留Prepared／ArtifactCreated／Committed／NeedsReconciliation，新增受控Skipped／Failed。
新增ProviderId VARCHAR(50)、NamespaceId VARCHAR(100)、TargetPolicyVersion VARCHAR(50)、InputFingerprint CHAR(64)、ProcessingOwnerCheckpointId BIGINT FK→processing_step_checkpoints.Id，均NOT NULL；owner屬FILE_RENAME。新增ErrorCode VARCHAR(100) NULL、CollisionEvidence NVARCHAR(MAX) NULL（非NULL須ISJSON、具名schema）。Provider／Namespace為受控邏輯識別，不是秘密或實體Path；TargetName沿不可變Item讀取，不重複新增欄位。
CHECK：Prepared的TargetReference／Receipt／CompletedAt／ErrorCode NULL；ArtifactCreated須核對後的TargetReference／Receipt非NULL、CompletedAt／ErrorCode NULL；Committed須TargetReference／Receipt／CompletedAt非NULL、ErrorCode NULL。Skipped須CompletedAt／CollisionEvidence非NULL、TargetReference／Receipt／ErrorCode NULL；碰撞Failed須CompletedAt／ErrorCode／CollisionEvidence非NULL、TargetReference／Receipt NULL。非碰撞Failed亦須CompletedAt／ErrorCode且不得以unknown冒充已確認失敗，其具體安全處置仍受既有阻擋限制。NeedsReconciliation無終態CompletedAt，不以缺Receipt推論無Artifact；已有核對證據不得任意抹除。Failed／Skipped／Committed均不可自行重開。
immutable request payload是OperationId／ItemId／ExpectedContentVersion、原Item目標與來源、Provider／Namespace／TargetPolicy／TargetKey／fingerprint；結果欄位不在payload內，僅由核准T3／恢復核對更新。FK／UNIQUE不保證owner授權或不可變，須owner＋rowversion及原Invocation核對。Failed跨Run重取權未核准，不因T5存在就允許覆寫。

### naming_artifact_reservations

新增ItemId BIGINT NOT NULL PK／FK→naming_request_items.Id；OperationId GUID改為NULLable FK→naming_operations.OperationId，UNIQUE採WHERE OperationId IS NOT NULL的filtered index。TargetKey NVARCHAR(500) NOT NULL保持UNIQUE NONCLUSTERED，使用明確binary比較的provider-normalized logical scope key，含provider／namespace識別且不能默默截斷或套預設collation。
NAMING T3建立Item Reservation與TargetName／結果同提交；FILE_RENAME T2才將OperationId由NULL一次性綁定。ItemId／Operation.ItemId／Reservation.ItemId同交易核對；不循環要求先有Operation才能保存命名目標。TargetKey建立後不可改，OperationId綁定後不可換；不刪除／重建reservation繞過碰撞。Provider target policy的原Manifest必須可重建，未交付時阻擋，不能依目前配置解讀舊key。

所有hash非NULL時須精確64字元小寫0-9/a-f，拒絕padding／非法長度／大小寫寬鬆比對；必要版本、reference與名稱拒絕空白及超長。JSON格式CHECK不是Evidence已核准或來源一致性的證明。T2／T3與Owner、History、Primary投影依MOD-02／MOD-04補充，SQL不跨Storage I/O；ResultReference分Item與Operation，不以row存在冒充Completed。N-01／N-02／F-04-01及Storage證據／Recovery等Remaining Blockers保留，未授權Migration。

## B-1c-00 核准補充：Frozen Rule Manifest Binding

本節依 System-Level B-1c-00 定義 Option A 的最小持久引用，優先補足舊文「未開始 Step 缺少持久規則選擇」同題缺口，不改 B-1a／B-1b 的具名輸入表或結果唯一鍵。僅 Schema Specification，尚未建立 EF Entity、Mapping、Migration 或 SQL 資料表。

### processing_rule_manifest_bindings

| 欄位 | 型別／Nullability | 約束與用途 |
|---|---|---|
| JobId | BIGINT NOT NULL | PK；FK → processing_jobs.Id，DeleteBehavior.NoAction |
| WorkflowVersion | VARCHAR(50) NOT NULL | 核准且可解析的 Workflow 版本 |
| ManifestId | VARCHAR(100) NOT NULL | 受控發布的不可變 Manifest 身分，不是可變 alias |
| ManifestChecksum | CHAR(64) NOT NULL | SHA256(原始發布 UTF-8 bytes)，精確小寫 hex |

CHECK 必須拒絕空白 WorkflowVersion／ManifestId；版本／識別不得截斷或補預設。ManifestChecksum 須精確 64 字元且只允許 ASCII 0-9、a-f，以明確 binary 語意驗證，不能因寬鬆 collation 接受大寫、非 ASCII 或 padding。CHAR 的型別寬度不等於格式驗證。所有欄位非 NULL；不新增 Status、Owner、rowversion、Retry／Attempt Counter、結果引用、JSON 或原圖資料。
每 Job 最多一筆，整份 Binding 建立後不可變，包括 WorkflowVersion；同 Job 重複請求只允許四欄完全一致，不用 UPDATE／upsert 換版本。多 Job 可共用同 Manifest，因此 ManifestId 不設 UNIQUE。ManifestId 永遠不得對應不同 checksum；受控發布／解析流程及交易核對必須拒絕此衝突，不能將一般索引或本表 PK 當成全域發布一致性證明。
WorkflowVersion 必須與 Job 所屬 Workflow、Run、processing_original_inputs 及 Manifest 內容一致。既有 processing_jobs 保存 Workflow 類型而非 WorkflowVersion；本節不為 Job 新增欄位，所稱 Job 版本是本 Binding 固定的版本。跨表／資源一致性由核准交易及解析流程驗證，PK／FK／CHECK 不能證明不可更新、核准來源、內容存在或規則可執行。

首次 T1 與原 Claim／Active Run／第一個 Running Invocation、B-1a 必要綁定同交易 INSERT 或一致核對；競爭者不得寫出矛盾版本。T1 前完成唯讀完整 Manifest 驗證，SQL transaction 不跨長時間資源載入或 Storage I/O。T2 取原 Manifest 規則並核對既有 Step Binding，不預造上游結果 FK。Resume 沿原 Run，ManualRetry 新 Run 沿同 Job Binding；缺失／不符／Commit Unknown 先核對原證據並 Fail-Closed，不自動 backfill、重選或關閉 Running。
完整 Manifest 及子資源採 System-Level 所定受控不可變發布／歷史保留，不將內容複製成任意 Context JSON。相同 WorkflowVersion 不等於相同規則組合；相同 Result Unique Key 不等於相同完整 InputFingerprint。既有 Canonical 欄位及結果鍵不變，不加入 Result Attempt。
實際歷史保存／解析／Application／Repository／Registry 接點、N-01／N-02、EXACT／VISUAL／VERSION 集合來源、診斷及 F-04-01 Recovery 仍待交付或決策。後續 Schema 實作須另獲批准並以新增式 Migration 與真實 SQL 測試驗證，本輪不授權遷移。

## B-1c-01 核准補充：EXACT Job-scoped Input 與結果

本節補足§9及 B-1c-00 尚未定義的 EXACT Snapshot／NoMatch 持久形狀；同題以本節及 MOD-05 B-1c-01 為準。僅 Specification，不代表 Entity／Mapping／Migration／SQL 已交付。三表所有 FK 為 NoAction，不改既有 exact_groups UNIQUE(FileSize,SHA256) 或 exact_group_members UNIQUE(GroupId,ImageId)。

### processing_exact_inputs

| 欄位 | 型別／Nullability | 約束 |
|---|---|---|
| SnapshotId | UNIQUEIDENTIFIER NOT NULL | PK |
| JobId | BIGINT NOT NULL | UNIQUE，FK→processing_original_inputs.JobId |
| HashResultId | BIGINT NOT NULL | FK→image_hash_results.Id，自身來源 |
| ExactRuleVersion | VARCHAR(50) NOT NULL | 原 Job Manifest 的核准版本 |
| ScopeVersion | VARCHAR(50) NOT NULL | 不可變核准集合範圍政策 |
| CandidateSelectionVersion | VARCHAR(50) NOT NULL | 不可變核准匹配候選政策 |
| CandidateCount | BIGINT NOT NULL | CHECK >=0 |
| InputFingerprint | CHAR(64) NOT NULL | 精確小寫 SHA256 |

Header 合併 Job→Snapshot Binding，每 Job 至多一份；全部在首次合法 EXACT T2 保存並不可改綁，不另建 Job Binding 表。Job／Image／Hash／B-1a／Manifest／WorkflowVersion 一致性由同交易核對；Header 無 Result Owner、完成狀態、Retry Counter 或 Context JSON。來源欄位由原引用重建，不重複保存原圖或完整 hash JSON。

### processing_exact_input_candidates

SnapshotId UNIQUEIDENTIFIER NOT NULL FK→processing_exact_inputs.SnapshotId；ImageId BIGINT NOT NULL FK→images.Id；HashResultId BIGINT NOT NULL FK→image_hash_results.Id。PK(SnapshotId,ImageId)，UNIQUE(SnapshotId,HashResultId)。
Detail 與 Header 同 T2 原子保存，之後不可增刪改；引用的 Completed Hash 業務內容與版本亦不可變且須保留。每筆排除本 Image，限同集合、Completed、FileSize＋SHA256 完整相等且版本相容的來源；ImageId 必須等於 HashResult.ImageId。多版本選擇歧義 Fail-Closed。Header.CandidateCount 等於完整 Detail 數，不靠 FK／CHECK 宣稱跨表資格、計數或不可變性已保證。

### exact_check_results

| 欄位 | 型別／Nullability | 約束 |
|---|---|---|
| Id | BIGINT identity NOT NULL | PK |
| SnapshotId | UNIQUEIDENTIFIER NOT NULL | UNIQUE，FK→processing_exact_inputs.SnapshotId |
| Status | VARCHAR(20) NOT NULL | CHECK 僅 Completed |
| Outcome | VARCHAR(20) NOT NULL | CHECK Match／NoMatch |
| GroupId | UNIQUEIDENTIFIER NULL | FK→exact_groups.Id |
| MatchedCount | BIGINT NOT NULL | CHECK >=0 |
| ProcessingOwnerCheckpointId | BIGINT NOT NULL | FK→processing_step_checkpoints.Id，原 EXACT 生產者 |
| ErrorCode | VARCHAR(100) NULL | CHECK IS NULL |
| CreatedAt | DATETIMEOFFSET(7) NOT NULL | 結果列建立時間 |
| CompletedAt | DATETIMEOFFSET(7) NOT NULL | 合法 T3 完成時間 |
| rowversion | rowversion | SQL 併發核對，不作業務版本 |

本表只在成功 T3 插入 Completed 結果，這是§9共通結果生命週期的具名縮限，不新增 Domain 狀態。CHECK 明確要求 (Outcome=Match AND GroupId IS NOT NULL AND MatchedCount>0) 或 (Outcome=NoMatch AND GroupId IS NULL AND MatchedCount=0)，不得靠 SQL UNKNOWN 放行；不建立 Pending／Processing 或 Failed Result Attempt。失敗保留於原 Invocation／Checkpoint 契約，不用假 NoMatch 存錯誤。
T3 核對 MatchedCount=Header.CandidateCount、完整輸入與 Owner；Match 的 Group.FileSize／SHA256、Membership HashResult 與凍結來源一致。NoMatch 必須對應已凍結空集合，無 Group；CHECK 只能保護本列形狀，完整性與提交證據需同交易核對。Result／必要 Group、Membership／Checkpoint／History／投影同 Commit，Completed 及 Owner 不覆寫。ResultReference 依 MOD-05 B-1c-01，不額外保存通用 JSON。
必要版本拒絕空白／超長，hash 精確64小寫 ASCII hex且採明確 binary 驗證；不截斷、不填 latest/default。Canonical 由 MOD-05 具名主／子 Schema 重建，Detail 依 ImageId ASC，不以目前 Group 成員重算。
首次 T2 採短 Serializable transaction 對完整 Match Predicate 查詢及鍵範圍保護，空集合也保存 Header；不分頁截斷、不跨 Storage I/O。晚到來源不改舊 Snapshot。實際索引、鎖競爭、交易時間與高併發驗證仍阻擋對應實作，不能僅靠 isolation 名稱宣稱完整性已驗收。Resume／ManualRetry／Commit Unknown 依 MOD-02 同名補充；不直接跨 Job 重用 EXACT 完成結果，不變更 N-01／N-02 或其他來源限制。

## 最後一致性檢查發現：N-02候選pHash就緒的調度契約

正式VISUAL_SIMILARITY要求完整同版本候選snapshot，Workflow將其放在Quality前；其他Image仍可能在Pending或尚未pHash。F-06核准的T2-W dependency wait目前只限VERSION_ANALYSIS，沒有定義VISUAL候選未齊時的等待／釋放Worker／恢復／到期。不能使用partial hash集合冒充完整snapshot，也不能占滿所有Worker等待其他Job。
這是規格缺口而非已證實runtime deadlock。保持Analysis/Full完整實作准入BLOCKED，核心TASK規劃與Naming不因本缺口阻擋。推薦下一次針對N-02核准把VISUAL候選等待納入既有T2-W（讓出Worker、持久deadline、版本核對、無Running/不消耗Retry、ready後原子開始）；VISUAL等待budget與逾時政策需集中決定，不能擅自挪用Recommendation120秒或改Step順序。本輪不實作、不建立TASK、不改已核准checkpoint wait範圍。
