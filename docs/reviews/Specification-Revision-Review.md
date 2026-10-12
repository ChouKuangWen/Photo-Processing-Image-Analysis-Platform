# MOD-02～MOD-10 最終文件審核報告

日期：2026-10-12。關卡：**DOCUMENT REVIEW — 本輪結束；非實作授權／非測試PASS**。

## 1. 審核基準與授權

- origin/develop：`8ff1dd983af2ffa68dfc93893f9cca61fadb2c90`。
- MOD-01 feature：`a1bb29e1d7cf704a202adbe1c14741b6441575be`，僅交接證據，未合併。
- working HEAD：`8e28b678085b9863da88c3ca77a346fc49a4a76e`，未切換／未改。
- F-01～F-08推薦方向與MVP延後限制已核准，最後F04/F05/F06/D13補充已同步。只修改13份授權規格/審閱文件，不修改MOD-01/旗下TASK/原始碼/Migration/AGENTS/Requirements，未建立TASK，未Checkout/Commit/Push/Merge。
- 13份文件對目前HEAD/index均untracked；相對固定develop為11修改、2新增。空git diff不代表沒有文件成果；本次用Git blob逐檔核對。/tmp輔助檔不屬Repo成果，後续提交不得包含，核對未重跑修改腳本。

## 2. 分範圍最終准入

| 範圍 | 文件判定 | 前置／限制 |
|---|---|---|
| MOD-02核心TASK拆分 | **可規劃/拆分，僅文件範圍** | 已有SQLClaim/T1～T5/T2-W、Run/checkpoint/History/retry/recovery/binding/aggregate、adapter/diagnostic契約。實作未授權，本輪未建TASK；真實交接須核對feature及最新驗收 |
| Naming | **可作後續TASK規劃，完整執行仍有交接阻擋條件** | 需持久Naming input/template/順序與完整Primary binding；Upload Naming/Full若無input不得猜。Create/Inspect/storage receipt/Crash/並行待實作驗收 |
| DuplicateDetection | **文件契約可規劃** | SHA256/EXACT/input/唯一key/晚到候選明確；SQL/storage/source版本實作尚未驗證 |
| Analysis | **完整實作准入BLOCKED** | N-01引擎/精確版本與N-02候選pHash未齊的調度契約；不能以所有F核准假稱完整流程可實作 |
| Full | **完整實作准入BLOCKED** | 同Analysis的N-01/N-02，加Naminginput交接條件；原檔不搬、不默認template |
| MOD-09最小diagnostic/metrics | **文件契約可規劃** | read-only新路由、runtime與DB指標分開；provider/SQL/handler未驗收 |
| MOD-10 Export | **延後新能力；未通過完整實作准入** | 原規格保留，CSV/filter/snapshot/formula與完整驗收待後續Export範圍；不阻擋MOD02核心 |

准入只指可把核心規格拆成將來的TASK，不等於feature已合併、MOD01驗收PASS、完整Workflow runtime READY或新功能可發布。

## 3. 已同步内容与最後一致性檢查

1. F01 Metadata：獨立表/offset可信instant/UploadCreatedAt不計completeness/Orientation只分析；MissingGPS Skipped、InvalidGPS Warning、Geo耗盡Warning、auth/config Permanent Failed及GET只讀。
2. F02 Naming：read/Create/Inspect、持久Request/Item/Operation/reservation，Item綁圖片Job、固定序號/extension/collision100；保留原檔/StoredPath/no-overwrite，禁止cleanup/reapply；舊identifier只是兼容名。
3. F03 hash/snapshot：versioned結果、EXACT unique/晚到候選、single-link true edge/不作可删證據；1000/50000/2暫限/無部分正式發布；Query最多999完整計算後pageSize100、2秒deadline。
4. F04 profile：已完整寫gray/alpha/ICC意圖/area resize/DCT Q16/zigzag/median/tie/encoding/rounding、golden manifest類別及獨立holdout門檻。引擎/版本N01未決；固定係數實際檔案/checksum/golden bytes/output/matrix由後續實作驗收，未偽造。
5. F05 Quality：JPEG q=sum63 AC/63、100*clamp((100-q)/99,0,1)，DC不計；DQT缺失/非法/無唯一luma mapping不補值；PNG100只是本項啟發式；有效原始六Metadata判定、可選缺失不計／必需資料未就緒分開；Grade連續區間/6位計算2位輸出。
6. F06 wait：120秒可設定，第一次未就緒在DB UTC持久start/deadline；重啟/補調度/配置不重設，now>=deadline先Expire；Waiting無Running，Resolved+Running同T2提交，Expired關Run/Job且無invocation不偽造History。
7. F07正式Workflow/版本/Skip与lifecycle同步SYS/MOD02～08；檢查發現VISUAL依賴scope不在已批准VERSION wait內（N02），未默改Step順序。
8. F08 diagnosis：200 blocked與write503分開；reason安全/整Batch確認/零寫入，queue通知/active/retry/resume runtime counter不當DBJob真相。
9. 共用Schema：型別/Unicode/date/unique/ownership/input/resultReference/等待欄位與模块語意同步，不改MOD01 mapping；Export/Audit無新表、無多租戶欄位。

檢查結論：核心/Naming/Metadata/Quality/diagnostic文件契約已收斂；完整Analysis/Full仍有以下局部架構缺口，不宣稱全MOD無衝突或整體READY。

## 4. Issue最終狀態（文件層，不是runtime驗收）

| Issue | 程度 | 最終狀態／剩餘事項 |
|---|---|---|
| C-01 | Major | Recovery管理例外、一般Job與Run/checkpoint狀態及原子順序文件衝突解除 |
| C-02 | Major | History BIGINT/欄位/owner/Query一致；實際DB舊GUID/資料遷移相容性未驗證 |
| C-03 | Major | MOD01 datetimeoffset/Unicode/unique/NoAction基線保留，獨立結果mapping同步；未執行Migration |
| C-04 | Major | Dependency rule錯圖已修正，Infrastructure實作Application/Domain contracts |
| C-05 | Major | 共用安全error envelope/validation/503交接与409duplicate/mismatch同步，不改MOD01 Upload錯誤 |
| R-01 | Major | Pending補排程/Recovery兩類/crash-window規格解除；Delivery/負載未驗證 |
| R-02 | Major | 單Instance舊process確認停止、單資料集合部署前提明列；不宣稱technical isolation/multiInstance |
| R-03 | Major | 四邏輯槽位/Shutdown/人工Retry/Geo分類/120秒等待持久規則解除；timeouts/defaults未量測 |
| R-04 | Major | Naming OperationId/Inspect與T3/result consistency收斂；實際Storage/SQL crash驗收未做，未知副作用仍阻擋；N02未定 |
| R-05 | Major | SQL原子JobClaim/Run/Running與result-key/owner條件更新文件完整；Race測試未做 |
| R-06 | Major | 單Historywriter/提交後best-effort通知/API分页与模組Outcome同步；無Outbox必達承諾 |
| R-07 | Major | Workflow/Context凍結，完整Analysis/Full仍N01/N02；未全解除 |
| R-08 | Major | Primarybinding/secondary隔離/delta/Batch終態/空Batch/diagnostic同步；真實交接證據不足仍阻擋，不猜主Job |
| R-09 | Risk | runtime metrics與有界capacity/query定義完成；效能與backpressure待量測，非已證实deadlock |
| R-10 | Major | 獨立scope/DbContext與queuedEntity非authority、T1/T2/T3/T2-W同步；實作adapter與SQL未驗證 |
| R-11 | Major | duplicate409/Failed transient Retry/History/validation明确；capacity重執行/Completed重分析/reapply延後且禁止默支持 |
| A-01 | Minor | TASK11/12最新验收待確認，未標PASS；舊TASK10紀錄保留為歷史證據 |
| N-01 | Major | **待決／Analysis與Full阻擋**：解碼器/ICC引擎/精確package與native版本/matrix未核准 |
| N-02 | Major | **待決／Analysis與Full阻擋**：VISUAL候選pHash未齊時的讓出Worker/持久等待/到期/調度未定义 |

D01～D12/D14与U01～U03在F01～F08核准範圍已同步；D13延後新Export（原規格保留）。N01/N02是最後檢查新揭露的必要前置，不拿舊待決清單重新詢問、不把所有MOD02核心一概阻擋。

### 剩餘必要決策一次列明（本輪停審核，不自行核准）

- N01來源：feature所有csproj無影像/ICC套件；MOD07只列候選，AGENTS§19規定「Agent 不得自行新增第三方 NuGet Package」。推薦單一跨平台managed引擎並凍結精確version/ICC/native版本/OS-runtime matrix；ImageSharp/OpenCvSharp不是已批准選擇。需Architecture明確選型/版本，不能latest或虛構pin。沒有在本輪加package/程式碼。
- N02來源：F03 complete candidate snapshot與F07 VISUAL在Quality之前，但F06 wait只限VERSION；其他圖片pHash尚未完成時缺調度契約。推薦同既有T2-W擴展VISUAL候選等待（释放Worker、持久起算/deadline、不消耗retry、ready後原子唯一Running），等待budget/到期政策須一次批准；不擅用Recommendation120秒、不偷偷重排Workflow/發布partial。這是靜態規格缺口，不是已測得deadlock。

## 5. 修改清單（全部13份，對固定develop）

| 文件 | 類型 | 最終內容 |
|---|---|---|
| docs/INDEX.md | 修改既有 | 最終報告/Schema定位與分範圍准入 |
| docs/System-Level-Specification.md | 修改既有 | 部署/Workflow/跨模組責任/Outcome/延後邊界 |
| docs/Database-Schema.md | 新增 | Job基線與Run/checkpoint/History/binding/new results/input/wait mapping |
| docs/modules/MOD-02-Processing-Spec.md | 修改既有 | 核心交易/recovery/claim/Workflow/T2-W/diagnostic准入 |
| docs/modules/MOD-03-Metadata-Spec.md | 修改既有 | Metadata provenance/有效性/Geo/time/獨立結果 |
| docs/modules/MOD-04-Naming-Spec.md | 修改既有 | artifact/input/OperationId/Create/Inspect/禁止cleanup |
| docs/modules/MOD-05-Duplicate-Spec.md | 修改既有 | 完整SHA256/版本結果/EXACT唯一與晚到candidate |
| docs/modules/MOD-06-Similarity-Spec.md | 修改既有 | snapshot/capacity/Query/數值profile/Golden/N01/N02 |
| docs/modules/MOD-07-Quality-Spec.md | 修改既有 | 完整component公式/Metadata/DQT/缺失/format/Grade |
| docs/modules/MOD-08-Recommendation-Spec.md | 修改既有 | 真pair均值/排序/snapshot/依賴wait/deadline/終態 |
| docs/modules/MOD-09-Monitoring-Spec.md | 修改既有 | 新只讀diagnostic/runtime與DBmetrics分離 |
| docs/modules/MOD-10-Export-Spec.md | 修改既有 | 原規格保留、延後新能力且非完整實作READY |
| docs/reviews/Specification-Revision-Review.md | 新增 | 本報告/最終Issue/准入/歷史決策稽核 |

## 6. 未驗證與延後限制

沒有執行dotnet/build/test/SQL/Storage/Geo/算法/校準/性能/Migration：本輪只有文件作業，不把預設/fixtures/門檻當已證实能力。後續需真SQL原子Claim/並行race/各crash-window、provider conditional create/Inspect、primary aggregate、Query deadline/limits與Dependency重啟互斥驗收。
pHash跨平台bit一致/holdout observed與Wilson門檻為待驗證要求；固定basis table/golden實際bytes與expected/checksum/engine pins尚未產生或批准。達不到必須回報重決策，不自行降門檻。300圖/各100pair不是充分準確率證據。
Naming input可能未由Upload交接、MOD01 feature未合併、TASK11/12最新验收仍待確認；不修改MOD01，不主張已交接/已PASS。
延後：新Export能力/進階Monitoring、artifact reapply/保留/cleanup、capacity Failed重執行/Completed重分析、多Instance/多使用者/租戶。限制：原Export不刪/非完整准入；不自動清理；不擴張transient人工Retry；無technical tenancy/fencing/必達/持久counter保證。

## 7. 文件核對紀錄與停止點

核對固定refs/HEAD/13檔與develop Git blobs、active文件引用、關鍵Status/Log/Schema/Workflow/timeout/錯誤/ownership/primary/延後契約；清理同題歷史追加待決摘要，保留下方完整歷史審閱證據。只有N01/N02仍作明確局部阻擋，不報全套規格READY。
Repo只有13份docs成果；source/MOD01/TASK/migration未物化/修改，tracked/staged diff空，HEAD/refs不變。/tmp輔助不納成果。最終檢查結果由本輪工具輸出核對，非程式測試。
最後唯讀工具核對：13檔集合完全保留；11份修改既有develop文件＋2份新增。HEAD/固定refs未變；tracked/staged diff空；untracked只有上述13份docs，無/tmp/helper/source/TASK/migration成果。Markdown相對引用皆可由本地草稿或固定develop/feature Git來源解析。SYS/MOD02 Workflow、Schema/MOD08 wait欄位、Quality完整公式、profile未驗收標示、N01/N02跨文件與Export保留檢查通過。此處PASS僅表示文件核對，不表示SQL/算法/效能或完整Workflow測試通過。

**停在文件審核關卡，本輪結束；沒有TASK/實作/commit/push/merge。**

---

<details>
<summary>歷史審閱與逐批提案（稽核保留；當時待決狀態不代表目前狀態）</summary>

# MOD-02～MOD-10 規格修訂與逐模組審閱紀錄

日期：2026-10-12（Asia/Taipei）。狀態：**SPECIFICATION DRAFT — DECISIONS PENDING / CONFLICT DETECTED**。
本文件不是 Implementation TASK、ADR 批准或驗收結果。

## 1. 固定版本與工作範圍

| 來源 | Commit | 用途 |
|---|---|---|
| origin/develop | 8ff1dd983af2ffa68dfc93893f9cca61fadb2c90 | 正式規格來源；遠端 ls-remote／Fetch／本地 ref 已核對 |
| origin/feature/mod-01-upload | a1bb29e1d7cf704a202adbe1c14741b6441575be | MOD-01 TASK、Mapping、Queue、API、驗收測試的補充證據；未合併 |
| working HEAD | 8e28b678085b9863da88c3ca77a346fc49a4a76e | Initial main；本次沒有 Checkout、切換或 reset |

兩來源的 AGENTS.md／Requirements.md／docs 無差異，merge-base = develop 的上述 SHA。舊兩次審閱仍有效，以本紀錄修正狀態與增加逐模組發現。
工作目錄原本沒有規格文件；僅將授權的 MOD-02～MOD-10、System-Level／INDEX 文件從固定 Git blob 產生為可審閱草稿，並新增 Schema 與本紀錄。Git 對 working HEAD 顯示新 untracked docs，不代表已提交或已合併 develop。
本次不修改 source／test／project／migration／lockfile／MOD-01 Spec／TASK／AGENTS.md／Requirements.md／README；不建立正式實作 TASK，不 Commit／Push／Merge、不改環境設定。

### 已讀來源

AGENTS.md、Requirements.md、docs/INDEX.md、docs/System-Level-Specification.md、MOD-01 Spec 與 MOD-02～MOD-10 Spec。
MOD-03～MOD-08／MOD-10 此輪逐份讀取完整規格／Schema／API／測試／驗收內容；MOD-09 以未變動的既有完整閱讀內容與此次補充核對。MOD-02 全文基準延用固定 SHA 的前次完整閱讀。
MOD-01 TASK-01～TASK-12 的相關閱讀、scope／Mapping／Queue／Transaction／API／觀測／驗收已核對；source／test 只唯讀，不作新的測試通過宣稱。
共用 Schema 是此輪新增、含待決事項的草稿；不假設原來缺失的 Database-Schema.md 已存在。

## 2. 本次使用者已核准的決策

| ID | 已核准內容 | 文件落點／限制 |
|---|---|---|
| D-01 | MVP 單 Instance、多 Worker，SQL 原子 Claim | SYS 與 MOD-02；多 Instance ownership、失效判定、舊執行者寫入防護後續，不宣稱已支援 |
| D-02 | MOD-02 統一協調 Step transient retry；初次 + 三次 retry，總四次，1／2／4 秒 | MOD-02、MOD-03～MOD-08；子模組無疊加 retry；Geo 耗盡 UnknownLoc／Warning／保留 Metadata／繼續；Shutdown 不算 retry |
| D-03 | 共用 Schema 管 physical mapping；MOD-02 Job／Log 語意、MOD-09 Query；Log Id 原則 BIGINT，先核對既有實作與 DB | feature 的 Entity／Migration／Snapshot 沒有 Log 表；實際 DB 未驗證；若已有 GUID 或資料遷移影響，先回報，不改 MOD-01／原始碼 |
| D-04 | 重複 CreateJob 四種既存狀態均 409；Retry 原 Job、保留 attempt history | PROCESSING_JOB_ALREADY_EXISTS 共用 envelope；人工 Retry只Failed/transient，其他INVALID_STATE_TRANSITION；重新分析待決 |
| D-05 | 新增Run/checkpoint；四個邏輯槽位、Shutdown同槽位Resume；人工最多三個新增Run；可設定MVP defaults | MOD-02§1.10/1.11/1.18～1.22、Schema§3/4；Queue5秒僅補排程，不改Upload，未效能驗證 |
| D-06 | 單一Historywriter、LogStatus、同交易保存後通知、獨立保存重試、best-effort事件與pagination | Schema§5、MOD-02§1.16/1.18/1.21、MOD-09；不保證memory跨重啟保留 |
| D-07 | 原Upload主Jobbinding、secondary不改aggregate、delta/Batchstate與CreateJoberrors | MOD-02§1.15/3.6、Schema§6、MOD-09；未知binding先阻擋，TotalCount與Query不改 |

新增錯誤 code 是本次核准的 MOD-02 duplicate API 契約，不變更 MOD-01 Upload 的 INTERNAL_ERROR mapping。
D-05～D-07已由後續使用者訊息核准具體方案與限制，見§3；U-01／U-03及D-14部分已核准；D-08／D-09／D-10本批部分核准見最新紀錄；隔離、容量、profile、詳細mapping與D-11～D-14／U-02仍待決。核准限規格，不是實作、遷移、效能驗證或TASK批准。

## 3. D-05～D-07 核准落點與其他待決策（不建立正式實作 TASK）

### D-05 — 已核准具體設計與限制

新增ProcessingRuns／ProcessingStepCheckpoints，保持MOD-01既有Mapping。Physical欄位、unique／FK／checks見Schema§3／§4。
四個邏輯槽位0～3，Shutdown沿用Run／slot、新增invocationHistory；JobRetryCount只累計首次開始重試槽位，不計resume／保存重試。人工只Failed/transient，最多三個新增ManualRetryRun，Completed重新分析不納入。
可設定預設：scan5秒／batch100、Geo15秒／其他Step120秒／shutdown30秒／MOD-02補排程Enqueue5秒；不改MOD-01Upload、不宣稱效能驗證。
補齊T1～T5原子順序、RunPending/Active/Completed/Failed與checkpointPending/Processing/RetryWaiting/Completed/Failed狀態機、欄位更新與crash窗口。
單Instance由部署控制確認舊程序真正停止，未知不回收；未知binding／版本／副作用不開始執行、不用新Run掩蓋。
細節見MOD-02§1.10、§1.11、§1.18～§1.22。模組級Context／fingerprint／result／安全重算與相同Image/Group保護仍待D-08～D-14。

### D-06 — 已核准writer、Schema、保存與事件

MOD-02單一writer，LogIdBIGINT identity；LogStatusRunning/Success/Warning/Failed/Interrupted/Skipped；StepCheckpointId+AttemptNo及slot+Resume唯一，同checkpoint最多一Running。
JobClaim/ActiveRun/RunningLog同T1commit後才開始；Outcome與checkpoint/History/必要Job/Run/Primarydelta同T3commit後才通知；保存結果獨立最多三次retry不耗businessbudget、不重跑模組。
memory在T3前中斷可能遺失；純只讀須可重現版本才可重算，已提交SQL須核對，Group與Storage副作用必須reconcile，未定義adapter則阻擋。不要把未保存memory當durable checkpoint。
事件schemaVersion1/eventId/occurredAt與correlation，Step/Job/Batch分開，best-effort、REST重新同步、不使用全域Sequence／Outbox保證。
page預設1/pageSize50（1～500），TimestampDESC/IdDESC、一次一致性讀取、server分页；invalid為400/INVALID_MONITORING_QUERY。
詳見Schema§5、MOD-02§1.16/1.18/1.21、MOD-09§1.12/1.15/1.21/3.4～3.5。QueueMetric與少數操作映射見U-01～U-03，不反稱全部Monitoring完整。

### D-07 — 已核准binding、aggregate與輸入驗證

原UploadJob為Primary，不強制Full；新ImageProcessingBindings不改MOD-01欄位。Secondary不改Uploadaggregate／Image全域Status。
PrimaryPending/Processing貢獻0，Completed貢獻Processed1/Success1，Failed貢獻Processed1/Failed1；同交易套用舊新delta，重複completion無增量。
FailedRetry減Processed/Failed各一；若還有其他終態Image則BatchProcessing，只有全部PrimaryPending才Pending。全終態有成功Completed、全失敗Failed；空Batch保留Pending/0counts/0progress。
輸入順序與錯誤已定：invalid400/INVALID_PROCESSING_JOB_REQUEST、Image404/IMAGE_NOT_FOUND、Batch404/BATCH_NOT_FOUND、mismatch409/IMAGE_BATCH_MISMATCH、duplicate409/PROCESSING_JOB_ALREADY_EXISTS。
unknownbinding不猜主Job、不開始相關執行／副作用，先回報handoff。第一批已核准API 503／PROCESSING_PREREQUISITE_UNAVAILABLE與完整Batch集合准入；診斷DTO／路由／保存細節仍待確認。
詳見Schema§6、MOD-02§1.15/3.6、MOD-09aggregate投影；TotalCount=圖片數、QueryDTO／Decimal／零分母契約不變。

---

### D-08 — Metadata time／Orientation／fallback provenance（Major）

來源：MOD-03 §1.6／§1.11／§1.12／§1.17／§1.19／§2.1／§3.6～§3.7；MOD-04 §1.8；MOD-07 §1.9。
FileCreatedAt 不在現有 DTO／Storage／Image Contract；EXIF 缺 offset、GPS ref、Orientation 保存位置、invalid GPS、永久 Geo error 的 Warning／Failed、persisted GET 與 Geo failure mapping 未定。
選項 A：新增受控來源資訊／Orientation／provenance mapping，要求來源時間與 offset 可核對。選項 B：明確採 Image.CreatedAt 作 fallback，Orientation 留結果 DTO 或另定保存方式。
建議先維持不猜來源，請選 fallback／timezone／保存方式；B 改 FileCreatedAt 的原規則，A 可能改 MOD-01 Image／Migration，兩者都需影響確認。GET 建議讀取已保存結果，Geo failure 應在 Step 分類，不在 Query 自動重試。
影響：MOD-01（如增欄位或改共同 fallback，先批准）、MOD-03／04／07／10。

### D-09 — Storage read／rename 與 Naming context（Major）

來源：MOD-01 IFileStorageService 僅 SaveAsync／DeleteAsync；MOD-03～MOD-07 需 Stream read；MOD-04 §1.12～§1.26／§3～§4；R-07／M04-01～06。
選項 A：新增獨立 Application read／rename abstraction，不改 MOD-01 Save／Delete；持久化 Naming Request（Template、Order、Sequence、Collision Policy），明定 preserve-original 與 no-overwrite 操作。選項 B：擴充既有 IFileStorageService 與實作，涉及 MOD-01 必須先批准。
建議 A，具體 signatures／Stream ownership／保存位置尚未核准。保留 StoredPath = original/{guid}，副檔名從 OriginalFileName 取得，不假設含 extension。
還須決定：OriginalExtension token 是否已提供 extension、Sequence overflow／limits、duplicate imageIds、Sanitization／reserved name／空結果、collision retry bound／Skip mapping、Apply 是否必須 Preview confirmation、跨 Batch Apply response、重新套新 Template 語意與 Rename compensation。
影響：MOD-01 potential shared Contract、MOD-02～MOD-07、MOD-09；不以新增接口提案冒充已核准原始碼變更。

### D-10 — EXACT／VISUAL Group、pair score 與 shared ImageAnalysis（Major）

來源：MOD-05 §1.5～§1.8／§2.3、MOD-06 §1.6～§1.14／§2.3～§2.5／§3.2、MOD-07 §1.16、MOD-08 §1.8。
EXACT 選項 A：以核准 FileSize+SHA256 key 建立 DB uniqueness；B：只用 member unique 不足以满足原規則，不建議。須定 hash 大小寫、候選範圍、晚到 candidate 與已完成無 duplicate 的證據。
VISUAL 選項 A：pairwise 相似關係＋明定 clustering；B：代表圖片 group score，需明确不代表每 pair。建議 A 保持每張 Query 的 pair score 正確；single-link 與 complete-link 有不同 A~B~C 語意，需要選擇，不以建議作需求。
另定 pHash preprocessing／bit length／serialization／version（64 為範例）、candidate filter、group merge／identity、低於 default threshold 的 read-only Query、pHash／Quality partial row 與獨立完成版本／atomic update。
影響：MOD-05～MOD-08、MOD-02、MOD-09、MOD-10；Schema 需要批准後才能規劃 Migration。

### D-11 — Quality normalization／grade／metadata completeness（Major）

來源：MOD-07 §1.6～§1.12；MOD-03 fallback。
固定 weighted formula 與算例已修正，仍缺 Resolution、Laplacian、Exposure、Compression 的 normalize 算法／校準值／numeric precision。
Grade 選項 A：連續區間 [0,50)、[50,70)、[70,85)、[85,100]；B：先按核准 rounding 再用整數區間。建議 A，避免 49.5 無 grade；尚未批准。
Metadata 選項 A：以有效原始 Metadata／provenance 計六項 completeness；B：fallback 也算存在，容易將 missing EXIF 誤算完整。建議 A；GPS pair／分母／小數 rounding 均需明列。
影響：MOD-03／07／08／10，provenance 若新增既有 Image 欄位涉及 MOD-01 先確認。

### D-12 — Recommendation group／version／ranking 與 API（Major）

來源：MOD-08 §1.5／§1.10～§1.17／§2／§3.4～§3.5；MOD-06 pair score。
選項 A：對明確 VISUAL Group snapshot 計算 Recommendation；B：另建 VERSION Group，須定派生／membership／identity。建議 A 減少平行 group，但改原文的 VersionGroup 管理須批准。
另定 per-candidate SimilarityScore 相對對象、State／AnalysisVersion／RuleVersion／invalidation persistence、group concurrency、single candidate Confidence、empty group／多 group Query、tie-break ImageId ASC／DESC、reasons 數值門檻、Quality permanent failure 的終止／waiting、ranking API 合併或分開、是否永久保存。
維持既有權重，MOD-06 score * 100 僅計算尺度轉換、不改來源資料。API 算例已修正，不代表 Recommendation 全部規則已完整。
影響：MOD-02／06／07／08／09／10。

### D-13 — Export streaming／CSV profile／filter／snapshot／formula safety（Major／Risk）

來源：MOD-10 §1.4～§1.12／§3.4～§3.9／§4.3。
選項 A：bounded streaming writer／async projection，核准 Stream ownership／cancel／response-start failure；B：bounded buffered export，必須先定上限，不能與原 unlimited export／streaming要求同時宣稱成立。建議 A。
還須定完整 header order／資料來源／Status 意義／date filter 欄位與邊界／offset／culture、Join row cardinality／排序、best-effort 或 consistent snapshot。
Excel formula 文本選項：neutralize（建議，但改文字表示）、拒絕（新增驗證語意）、保留原值（風險留存）。CSV 引號 escape 不能當作已防 formula。
影響：MOD-10 與 MOD-03～MOD-09 的查詢資料；Export 不修改來源分析、不建立 Job／Log／Audit 表。

### D-14 — 正式 Workflow Definition 與 Step mapping（Major）

來源：SYS §1.13、MOD-01 FR-05／WorkflowType、MOD-02 §1.3／§1.6／§1.17；R-07。
Naming／Analysis／Full 已有模組順序，但尚未有完整 Step mapping；DuplicateDetection已核准SHA256_CALCULATION → EXACT_DUPLICATE_CHECK。VERSION_ANALYSIS 是否是 MOD-08 Recommendation adapter、GEO_LOOKUP 的條件執行與 COMPLETED 是否僅 lifecycle marker 也須明列。
選項 A：DuplicateDetection 只做 SHA256_CALCULATION → EXACT_DUPLICATE_CHECK；選項 B：先執行 Metadata 再做這兩個 Step。建議 A，避免與 duplicate 無關的 Geo 成本；A已由第一批核准，不由resolver新增其他Step。
建議以已定模組顺序展開 Naming＝Metadata＋Naming、Analysis＝Metadata＋Duplicate＋Similarity＋Quality＋Recommendation、Full＝Analysis＋Naming；每個 Step 的正式識別、可選／跳過條件、Context、fatal／warning 對應先審查 D-08～D-12。這是提案，不新增已核准 Workflow、不改 MOD-01 enum。
影響：SYS、MOD-02～MOD-09；Naming Context 與 Recommendation dependency 尚未解決前，不能只因 Step list 完整就宣稱可實作。

### 尚需新的集中決策（不自行核准）

| ID | 缺口／推薦與選項 | 影響 |
|---|---|---|
| U-01 | 已核准503／PROCESSING_PREREQUISITE_UNAVAILABLE，安全共用格式、零新增／排程副作用；duplicate／mismatch仍409 | MOD-02API／MOD-09；不改MOD-01 |
| U-02 | Queue metrics still缺notification/active/retry/restartcounter精確definitions。推薦分開runtime通知量與DBJob數，取消/duplicate不計Job完成；不以目前Channel或History筆數作已核准公式 | MOD-09，R-09剩餘觀測部分 |
| U-03 | 已核准完整Batch Primary binding集合確認後才執行Primary／更新aggregate；只阻擋該Batch。原因唯讀可查，補證據後重驗解除；診斷DTO／route／保存仍待確認 | MOD-02／MOD-09；不新增MOD-01狀態或改Query |

U-01／U-03已核准、U-02待決，均不是D-05～D-07的否定；核准的拒絕副作用、計數與狀態規則已寫入，缺失處沒有假造code或狀態。D-09基本邊界與D-14部分已核准；其餘詳細選項／建議／影響保持待決。

---

## 4. 原兩次 MOD-02 Issue 狀態（全部保留）

| ID | 程度 | 修訂結果 | 未解除部分／決策 |
|---|---|---|---|
| C-01 | Major | 文件冲突已解除：一般/管理transition、Run/checkpoint狀態機與T1～T5一致 | Runtime未實作；模組結果/重算前提待D-08～D-14 |
| C-02 | Major | 文件Schema/ownership冲突已解除：BIGINT identity、checkpoint關聯、LogStatus/slot/Resume與Query映射一致 | 實際DB/舊GUID資料/遷移相容性未驗證；不是已遷移PASS |
| C-03 | Major | 已修正 Job／Image mapping，承接 MOD-01 datetimeoffset／Unicode／unique／NoAction；新增共用 Schema 草稿 | 其他結果表仍未決；不改 MOD-01 Migration |
| C-04 | Major | 已修正 MOD-02 Domain→Infrastructure 錯誤依賴圖 | 無此文件衝突；未驗證實作 |
| C-05 | Major | 已修正六個 Processing error envelope 與 Export error.traceId | 新 Input validation／API 細節仍待相關決策，不改 MOD-01 mapping |
| R-01 | Major | Pending補排程、可設定scan/batch/5秒、Startup兩類恢復与crash窗口規則已解除缺漏 | 個別binding/Context/副作用核對仍待D-08～D-14/U-03，非已證實delivery |
| R-05 | Major | 原子JobClaim/Run/RunningLog順序已明列、openRun/Running unique与獨立scope契約已解除Job層缺口 | 跨Job相同ImageStep/Group結果保護待D-10等；不宣稱只用MemoryLock |
| R-02 | Major | MVP單Instance+舊程序已確認停止才回收的部署前提已明列 | 不實作自動liveness，未知時阻擋；多Instance後續 |
| R-03 | Major | 槽位/Job累計、ShutdownResume、manual三個Run、MVPtimeout/defaults已解除共用缺漏 | 永久Geo/模組分類待D-08；defaults未效能校準 |
| R-04 | Major | 交易顺序、保存retry、memory可能遺失、安全重算vs副作用reconcile與crash-window已明列 | 未知Group/Rename receipt/Context仍待D-09～D-12，整體副作用一致性未解除 |
| R-06 | Major | 單一writer、Schema/Historyfailure、Step/Job/Batchevents與best-effort契約已解除共用歧義 | 模組Outcome/Result adapter尚未定，沒有runtime writer驗證 |
| R-07 | Major | 已修正 Hash Step 名稱、Workflow enum重用與跨模組分類邊界 | 正式 Workflow mapping／Naming Context／Group dependency待 D-09／D-10／D-12／D-14；DuplicateDetection未自訂順序 |
| R-08 | Major | Primarybinding、secondary隔離、delta/Batchstatus與retry/空Batch已核准寫入 | 完整Batch准入已核准；診斷細節待確認，Query不變、無實作證據 |
| R-09 | Risk | Worker-before-Producer、分批扫描向前遍歷、5秒限MOD02補排程已補齊 | metricsU-02、效能待驗證；不是已證實deadlock |
| R-10 | Major | 獨立scope/DbContext、queuedEntity非authority、T1/T2/T3原子契約已解除文件缺漏 | 實際Persistence/result方法適配仍待D-08～D-14/Task前review |
| R-11 | Major | duplicate四態409、manual原JobRunhistory、Image/Batchvalidation及secondary語意已核准 | Completed重新分析/D-09/D-12、handoffAPI 503已核准；診斷細節待確認 |
| A-01 | Minor | 已區分舊TASK-10結果與新TASK-11／12實作 | 最新驗收版本與結果仍待確認；不標PASS |

## 5. 逐模組審閱狀態與新增發現

每份 Module 修訂節保留 Issue ID／程度／原章節、問題與驗收缺口；本表記錄跨模組影響與修訂／決策落點。所有模組完成文件閱讀與審閱，不表示 READY FOR IMPLEMENTATION。

| MOD | 審閱／修訂結果 | 新 Issue 與具體處理 | 影響／仍待決策 |
|---|---|---|---|
| 02 Processing | D-05～07已落地於規格；仍BLOCKED | 原子順序/state/crash/Schema/aggregate已補齊 | D-08～D-14、U-01～03、MOD-01交接 |
| 03 Metadata | 已獨立審閱與修訂；仍 BLOCKED | M03-01來源時間、02 Orientation、03 timezone/GPS、04 Step/Stream、05 Query/Geo；Id/mapping/retry已對齊 | D-08／D-09；MOD-01／04／07／10 |
| 04 Naming | 已獨立審閱與修訂；仍 BLOCKED | M04-01 context、02 storage／extension、03 sanitization、04 collision／order／limits、05 extension／reapply、06 Skipped；時間／Unicode／Log引用已修正 | D-04／D-06／D-07／D-09；MOD-01／02／03／09 |
| 05 Duplicate | 已獨立審閱與修訂；仍 BLOCKED | M05-01 Group key、02候選/完成證據、03hex、04Stream；文件誤標MOD-03改MOD-05、Step名稱及FileNotFound retry矛盾已修正 | D-09／D-10；MOD-02／06／08 |
| 06 Similarity | 已獨立審閱與修訂；仍 BLOCKED | M06-01hash profile、02candidate、03non-transitive group、04pair/threshold、05shared partial row | D-10；MOD-02／07／08／10 |
| 07 Quality | 已獨立審閱與修訂；仍 BLOCKED | M07-01算例85.5／90.95已修正；02normalize、03decimalgrade、04fallbackcompleteness、05partialrow待決；不能row存在就Skip | D-08／D-10／D-11；MOD-03／06／08／10 |
| 08 Recommendation | 已獨立審閱與修訂；仍 BLOCKED | M08-01算例92.0／gap4.6=Low已修正；02score轉尺度已明列，其來源pair待定；03group、04version/state、05ranking/reasons/API、06persistence待決 | D-10／D-12；MOD-02／06／07／09／10 |
| 09 Monitoring | M09-01/02/03/04/06文件契約解除，未實作 | Historysource/完整events/pagination已同步；metrics05保留 | U-02與模組結果/D-08～D-14 |
| 10 Export | 已獨立審閱與修訂；仍 BLOCKED | M10-01byte[]vsstream、02CSV/filter、03formulaRisk、04snapshot/joins、05弱測試例；error.traceId與offsetDTO已修正 | D-13；MOD-03～09；不加入Pipeline |

新 Major Issue 的最小建議與選項均列 §3，不將既有範例中的值或未核准 Mapping 當作產品需求。Minor算例／label／格式錯誤直接修；Algorithm／Schema／API重大差異保留Gate。

## 6. MOD-01 TASK與驗收交接

### 保留的既有 TASK

- `tasks/MOD-01/TASK-01-Project-Foundation.md` — 原 blob `c5e51a5619dd5df5ae7078106fab9fd7e623a6e3`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-02-Upload-Domain.md` — 原 blob `e4534c927a586353123bc42984b6bb3153869c99`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-03-Upload-Application-Contracts.md` — 原 blob `e3fc429f342cee87d4ecea6b724bb62ed798d35c`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-04-File-Validation.md` — 原 blob `849cbdaa205fe8ac6b6aef18e8f186afc72d9553`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-05-Local-File-Storage.md` — 原 blob `d2506fbdd71a7a747c31829adc45ed6e26e5e9bb`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-06-EF-Core-Persistence.md` — 原 blob `767b43d3d0a1f8c17b290bd1a243ec8dcd500f46`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-07 Processing Queue.md` — 原 blob `2e555d4e5514c4b31feb7c2cb60efd9da5512ca8`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-08 -Upload-Application-Service.md` — 原 blob `5a3df9dc5d1946f9b5a4a33be38dbce2b202ad61`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-09-Transaction-Compensation.md` — 原 blob `04d8fbcf775328f476349e1f958aa76374c5a29a`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-10-Upload-API.md` — 原 blob `00897a89bfd4df8428fb85af9d52441bb1f0c0b9`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-11-Batch-Status-Observability.md` — 原 blob `a64926994a51a69827e049c61edb2b2c8041d7b8`，不修改、不複製到本 working tree。
- `tasks/MOD-01/TASK-12-Integration-E2E-Module-Acceptance.md` — 原 blob `011300818b1371b96787f302fc7af758f1121129`，不修改、不複製到本 working tree。


兩個基準均沒有 MOD-02～MOD-10 TASK；沒有可直接更新的既有 TASK，也未新建正式 Implementation TASK。
以下為對 MOD-01 TASK 的影響提案，不是已修訂內容：

| TASK | 核對結果／是否需要修改 |
|---|---|
| TASK-01／02／03 | Solution／Domain／Application基線保留；完整StateMachine屬MOD-02。若新增Domain behavior／Storage abstraction，後續TASK需明確對接，不能將舊TASK當授權 |
| TASK-04／05 | 基本Signature不是完整Decode；Save/Delete與original/{guid}保留。若新read／rename／FileCreatedAt需求要改既有Contract，先取得MOD-01批准 |
| TASK-06 | mapping已定；RowVersion／新table／額外Index原本不允許。新Schema或attempt若影響既有Job，先回報並批准，不回寫舊驗收 |
| TASK-07 | 文件Status仍Planned，但Queue實作存在。不能依label判未實作，也不能依實作判最新測試PASS；建議以新受驗SHA补狀態，需MOD-01批准 |
| TASK-08／09 | Commit前後與Exception／cleanup規則保留。TASK-08有較早流程表達，TASK-09給出最終Storage→Begin→兩階段Save→Commit→Enqueue；若整理文案，先批准MOD-01文件修改 |
| TASK-10 | §23／24歷史不覆蓋§25；f55a279版本使用者回報Build PASS、51/51 integration、261/261 regression。保留NU1900／EF diagnostics／upload limits／adapter cleanup未決，不延用為新版本PASS |
| TASK-11 | code／tests commit 1c55045存在；持久化Batch只讀、ILogger與failure ownership保留。最新完整驗收待確認 |
| TASK-12 | acceptance code／tests commit cd512dc存在；最新完整結果／matrix待確認。Backend不要求後續Worker/Recovery/UI完成，不因模組依賴deferred判BackendBLOCKED |

對MOD-01可能修改的理由／影響：Orientation／source time／provenance可能影響Image，attempt／TraceId可能影響Job，read／rename可能影響StorageContract，Batch計數方案可能影響Query、TASK狀態補記影响驗收證據。所有這些修改都必須先由使用者確認，沒有直接修改。
目前Feature尚未合併。TASK-11／12最新驗收=待確認，不標PASS；本次沒有Build／restore／test／SQL operation。D-05～07採新增表、不改MOD-01Mapping；原計數/Query保留，只批准MOD-02未來狀態更新。檢查有測試實作不等於執行結果。

## 7. 修訂後進入條件

D-01～D-07保留核准；本輪補足原子Claim/Run/Running順序、Run/checkpoint終止與ManualRetry封存、停止前提、memory與side-effect重啟差異。
仍需D-08～D-14的Context／版本／結果／Group／Naming/Export算法契約與U-01～U-03。MOD-01Feature未合併，TASK-11／12最新驗收仍待確認，實際DB與遷移亦未驗證。
MOD-02 **尚不具備可直接實作的正式TASK拆分條件**；共用規則已補齊不表示模組級adapter/Schema/Workflow全部核准。
本次只有已授權規格文件修改，無source/MOD-01/TASK/Migration/checkout/commit/push/merge。

---

## 8. 本次文件檢查結果

- 13 份文件的 fenced JSON、error.traceId、code fence 配對與本地 Markdown 連結檢查通過。
- Quality／Recommendation 算例與 Candidate Confidence 核算通過，固定權重不變。
- 共用 Schema 的 MOD-01 已確認表格與原 blob 逐字一致（僅調整 heading 導航）。
- 以固定 develop 原文對比的 whitespace 檢查沒有錯誤；no-index 因存在差異回傳 1，不是格式檢查失敗。
- 原 working HEAD／兩個 remote-tracking SHA 保持不變，tracked files 無差異；新增路徑僅授權 docs。
- 未建立正式 TASK、未執行 restore／build／test／SQL；本節不代表 runtime、資料庫或整合驗收 PASS。

## 9. 本輪補充核准與驗收範圍

使用者核准D-05～D-07具體方案，明確要求Queue5秒不改Upload、defaults可設定且未效能驗證、結果保存前crash不承諾memory保留、未知binding零副作用與partial-terminal Retry仍BatchProcessing。
已加入MOD-02§1.18～§1.22的交易/狀態機/crash矩陣：這是規格自洽檢查，不是程式/SQL/效能測試PASS。檢查與新待決事項如§3/§4，未重新核准D-08～D-14。

## 第一批核准與後續方案狀態

U-01／U-03已寫入MOD-02／MOD-09／SYS；D-09基本邊界寫入MOD-04／MOD-02／SYS；D-14的DuplicateDetection寫入MOD-05／MOD-02／SYS。原有已核准D-01～D-07維持。D-09詳細契約、D-14完整mapping、D-08／D-10～D-13／U-02保持待決，不新建正式TASK。
診斷能力與解除規則已明列，但不自行核准新增API route／DTO／physical table。AGENTS原Storage單一介面的文字與新read／rename分離方向有差異；依使用者明確核准本次邊界處理，未修改AGENTS或MOD-01。

## 第二批原始推薦方案（歷史提案；以後續本批核准紀錄為準）

以下位置與名稱均為提案，不能據此建立正式TASK或Migration。既有Run／checkpoint／Log狀態機不變；新結果與checkpoint在T3同交易提交，SQL commit確認後才通知。

### D-08：Metadata result 與 provenance

建議新增MOD-03擁有的 `image_metadata_results`，以 `(ImageId, InputFingerprint, MetadataRuleVersion)` 唯一；保存SourceSHA256、raw EXIF local time／offset、TimeSource、ResolvedTakenAt、OffsetKnown、Orientation(1～8 nullable)、GPS原始reference與有效座標、各欄位Valid/Missing/Invalid/Fallback provenance、GeoOutcome與結果版本。完整型別／長度另待Schema確認，不修改MOD-01 Image mapping。
來源檔案真正FileCreatedAt目前無可信交接來源，不拿storage建立時間代替。建議EXIF明確offset才投影為TakenAt；無offset保留raw local time、OffsetKnown=false，不自行套server timezone；無可靠instant時使用已持久化Image.CreatedAt作顯式UploadCreatedAt fallback，不算真實拍攝時間／原始Metadata completeness。此fallback已由本批核准，不改MOD-01 mapping。
Orientation缺失／無效保留provenance，分析用identity transform；合法1～8僅在解碼分析視圖套用，不改原檔。GPS latitude/longitude pair、N/S/E/W、範圍完整有效才Geo；無效GPS採Warning且不呼叫Geo。Geo transient依已核准四槽位；耗盡UnknownLoc/Warning繼續。provider auth/config失敗建議Failed、不Retry，須確認是否採此政策。
結果首次持久化前crash可重讀同一原始內容重新parse；Geo可能再呼叫、結果不保證跨時間一致。T3後重用版本／fingerprint匹配的結果，GET不parse／Geo。fingerprint不同不冒充Shutdown Resume，阻擋等待處理，不自動重分析。
驗收：EXIF有offset跨UTC日期正確；無offset不猜UTC；missing EXIF fallback可辨識；非法Orientation不改原檔；GPS缺半邊不呼叫Geo；結果commit前crash可重算、後crash不重做；多Worker同鍵僅一持久結果。
MOD-01影響：不改interface／mapping；可透過既有欄位投影MOD-03結果但更新責任需在正式契約確認。若要取得真正FileCreatedAt／新增Upload來源欄位，另行申請MOD-01變更。

### D-09：Storage read／Naming request／operation receipt

建議Application新增 `IImageContentReader.OpenOriginalAsync(ImageId, CancellationToken)`，回傳可釋放ContentHandle(Stream、length、contentVersion)，caller負責dispose；不承諾seek，adapter校驗DB reference與storage範圍，client不傳path。原始內容immutable；如需暫存可seek，須由Infrastructure受控處理並清理。
建議 `INamingArtifactStorage.ApplyAsync(OperationId, ImageId, ExpectedContentVersion, TargetName, CancellationToken)` + `InspectAsync(OperationId, CancellationToken)`。相同operation/input重入回同receipt，參數不一致拒絕。Local/GCS實作conditional no-overwrite，不用memory Exists→Rename保證安全。
優先推薦preserve-original：不搬走 `original/{guid}`，新增受控命名artifact；NewFileName只是顯示名稱，不改StoredPath。此為詳細語意提案，若必須實體移動原檔，需另確認對MOD-01 Delete／補償的影響。
建議新增MOD-04擁有的NamingRequests／NamingRequestItems／NamingOperations。Request保存JobId、SchemaVersion、Template／RuleVersion、CollisionPolicy、InputFingerprint；Item保存唯一(RequestId,ImageId)、OrderOrdinal、已凍結Sequence、時間／location snapshot、OriginalExtension、預計TargetName。同Run建立／綁定input reference後才執行，Shutdown與Failed Retry沿用輸入；改Template不是Retry。
Operation保存唯一OperationId與唯一(RequestId,ImageId)、ExpectedContentVersion、TargetReference、ReceiptVersion、狀態Prepared/ArtifactCreated/Committed/Compensated/NeedsReconciliation。先保存Prepared及目標reservation，再外部操作，再T3提交receipt／result／checkpoint；通知在commit後。
建議collision採既有policy：Skip對應Skipped，但Naming完成需每Item可核對的結果；自動suffix按ordinal穩定分配，建議每Item最多100個候選（可設定、未效能驗證）；不overwrite。副檔名只附加一次，若Template已產生相同副檔名不重附；拒絕duplicate ImageId、跨Batch、空／reserved／path traversal名稱。序號以凍結Order分配、失敗不重編；Start>=0、Increment>0、checked BIGINT、不允許overflow。Preview建議非強制，但Apply必須驗證完整凍結input；reapply不同Template待獨立產品決策。
crash恢復先Inspect：receipt匹配且artifact完整則補T3、不再copy；不存在才安全重做；未知／內容不符則NeedsReconciliation阻擋。自動刪除補償提案已撤回；artifact保留／清理未核准前禁止自動清理，不承諾DB+Storage同交易。
驗收：兩Worker同目標僅一成功；copy成功DB保存前crash不重複artifact；client取消後仍核對receipt；template含extension不重複；序號overflow拒絕；外部未知目標不刪除；原StoredPath與原始bytes不变。
MOD-01影響：Save/Delete及mapping不變；新artifact lifecycle不依賴MOD-01 Delete。需額外storage與MOD-04新表，清理策略仍需確認，不承諾不存在孤立artifact。

### D-10：Exact／Visual 與 shared result

推薦MOD-05以ImageHashResults保存唯一(ImageId,ContentVersion,HashRuleVersion)的FileSize／SHA256；SHA256為64字元小寫hex、ordinal比較，Images.SHA256只作既有兼容投影，不新增其mapping。EXACT group保存唯一(FileSize,SHA256)，members唯一(GroupId,ImageId)。本批已限同授權集合、可跨Batch，不能跨owner/tenant；全repository提案已撤回。
建議SHA256_CALCULATION對本Job圖片完整hash，即使目前無同size候選；這改MOD-05避免無候選hash的優化，換取晚到candidate可比對的持久證據。EXACT_DUPLICATE_CHECK成功時保存Matched／NoMatch＋candidate snapshot版本；NoMatch不是永遠無duplicate。新hash完成用相同唯一key交易合併既有成員／建立group；不重跑舊Completed Job或改其History。
MOD-06保存ImagePerceptualHashes，唯一(ImageId,ContentVersion,HashProfileVersion)；建議profile含Orientation normalization、white alpha compositing、sRGB grayscale、32x32 resize、DCT、zigzag取首64個非DC低頻係數、median threshold、64-bit固定big-endian hex。全部是待校準提案；算法／resize實作版本必須凍結，不稱已驗證準確率。保留既有SimilarityScore=1-HammingDistance/bitLength及default threshold=0.85。
PairResults保存canonical ImageLo<ImageHi、兩端content/profile版本與score，唯一canonical pair+版本。保存所有pair提案未核准；新的bounded計算/query方案見後續下一批提案，不以預設門檻以下缺row當0。
推薦VISUAL採single-link connected components：A~B、B~C可同group，A~C仍回真實pair分數，不宣稱每pair都達threshold；若產品要求全pair達標則改complete-link，需不同重建規則。群組版本／membership snapshot持久化；merge建立新snapshot並保留舊版本，舊Recommendation不得默默套新members。singleton以完成且no-match表示，不造有兩張的group。
ImageAnalysis採PHash與Quality獨立狀態／版本／ResultReference，不因shared row存在而Skip。推薦以新結果side tables作authority，shared projection按column+rowversion更新；SQL唯一key／條件更新保護相同Image-step-version，同group merge用DB transaction，不靠memory lock。Pending/Processing/Completed/Failed在各結果域分開，Job層完成不等於所有分析域完成。
恢復：hash／pair純計算可按同snapshot重算；已commit結果按版本重用；group更新與完成checkpoint同T3 transaction，未知commit先讀DB；candidate membership改動產生新snapshot，不篡改原Run輸入。候選晚到如何觸發新增snapshot及上限／清理仍需確認，不新增已核准Job workflow。
驗收：同hash並行建group只一筆；晚到同內容能加入；NoMatch結果可區分未分析；A~B~C群組不偽造A~C分數；低threshold query只讀；pHash／Quality並行不lost-update；groupmerge crash不孤立member；舊Recommendation可辨識過期snapshot。
MOD-01影響：不改Save/Delete、Images／Jobs mapping或TASK；新表／indexes只是提案。既有SHA256欄位可投影但不能在無內容／版本證據時作唯一完成判斷。新表Migration與既有資料回填均未授權。

## 本批 D-08～D-10 核准紀錄（優先於前節未核准提案）

D-08核准獨立Metadata表／可信offset／UploadCreatedAt fallback且不計completeness／分析視圖Orientation／Geo transient Warning與auth/config不Retry。auth/config最終Failed或Warning未核准，先前Failed只是提案。
D-09核准保留原檔＋命名artifact／StoredPath不改／獨立read/artifact／持久input／固定序號／no-overwrite／OperationId/Inspect／可設定collision100；reapply與保留/清理未決，禁止自動清理，取代前節自動刪除補償提案。詳細介面名稱/physical mapping與錯誤相容待決。
D-10核准版本hash、EXACT uniqueness、真實pair、versioned membership、各分析獨立完成；scope限同授權集合、可跨Batch不跨owner/tenant。single-link同群不保證每pair達標、不代表可刪。全pair持久化與pHash profile未核准。
隔離證據：Requirements§28明列Account/Authentication/Authorization/MultiTenant MVP out-of-scope；feature Image只有BatchId、無owner/tenant/scope。先前全repository候選建議撤回，不能默認整DB單scope。需使用者核准部署隔離或新的集合模型；不擅自新增MOD-01/Auth需求。

### 下一批具體提案（全部待決）

1. 隔離：優先建議MVP部署/DB/storage只承載一個明確授權資料集合，由operator確認，禁止混合使用者/租戶資料；不藉MVP無帳號就推定單集合。若要共存多集合，需新scope資料/授權/查詢/queue邊界設計與對MOD-01影響評估。未確認先阻擋跨圖片比較。
2. 容量：Similarity每集合每snapshot最多1000張具有同Content/Profile版本的可比較圖片（可設定、未效能驗證）；超限不悄悄截前1000，不宣稱Completed/NoMatch，回報需要縮小合法scope或後续容量方案。不得利用篩選偷偷更改授權集合。
3. 候選：在已核准scope與1000上限內做完整hash距離比較，每新增圖片最多999個比較，single-link snapshot最多499500個pair計算；只持久化達現有0.85群組threshold的edge/result，不保存全部pair。建議每snapshot edge最多50000筆、hash比較同時最多2個Worker；都是待確認預設，超限阻擋snapshot發布、不部分發布完整group。
4. 低threshold Query：可在scope<=1000且版本一致時，唯讀計算指定圖片對其餘最多999個已保存pHash，回傳真實score/coverage/profile/candidateSnapshot；不寫Job/History/Pair/Group、不啟動解碼或補hash。threshold0～1、min可至0；缺hash/版本不同/超限明列NotReady或Unsupported，不回空陣列當NoMatch。新增HTTP/DTO字段與錯誤碼仍需確認；現有API只支持stored Query，不能假稱此能力已實作。
5. pHash重現性：先凍結test corpus與reference outputs，再選profile；ProfileManifest須列decoder/版本、Orientation、ICC/colorspace、alpha、resize interpolation、DCT/係數/DC/median規則、bit length/order/encoding與implementation版本。相同bytes/profile在支援OS與重啟下bit-for-bit一致；跨profile不比較。32x32/64bit/DCT仍只候選，不先核准。
6. 校準：建議至少300張有權使用的圖片、至少100個人工標記positive與100個hard-negative pair，另保留未參與調參holdout；涵蓋JPEG/PNG、EXIF orientation、alpha、resize/compression/亮度、相似但不同內容。輸出0.85附近precision/recall/FPR/FNR及不同threshold曲線；目前不設未核准PASS門檻，不宣稱準確率已驗證。性能測1000張、499500比較、50000edge、Query999、同Worker併發，記憶體/時間P50/P95/DB量測，再由產品核准SLO及profile。
7. 其他待決：Geo auth/config終態、MissingGPS/InvalidGPS Log與LocationName呈現；Metadata physical mapping及舊欄位投影；Naming簽章／operation/request表與reservation/receipt狀態機、舊RENAME_FAILED/FILE_RENAME相容、artifact保留/清理/reapply；D-11 Quality、D-12 Recommendation、D-13 Export、D-14完整Step mapping、U-02 metrics與交接診斷DTO/route。

驗收狀態：以上為規格案例與驗收提案，未執行算法/容量/SQL/storage測試。MOD-01 feature未合併、TASK-11/12最新驗收待確認；不建立正式TASK。

## 最終集中決策紀錄（F-01～F-08 已核准，補充限制適用）

最新核准：管理者確認單部署/DB/Storage單集合（非技術隔離）、1000圖片/50000edges/比較併發2暫定可設定限制、只存threshold edges、唯讀既有同版本pHash低threshold Query、版本/重現/holdout方向。300張/各100pair非充分準確率證據。舊隔離待決／全pair存儲／所有MOD02均被Export阻擋說法由本節取代。
F-01～F-08已由使用者核准推薦方向與MVP延後限制；最後補充亦適用。數值與契約以各模組最終同步章及本報告結論為準；歷史提案保留供稽核，不代表未核准事項已自動通過。原始碼／MOD-01／TASK／Migration不變。

### F-01：D-08 Metadata完整結果／失敗分類（MVP必要）

推薦MissingGPS＝GeoSkipped、LocationName=UnknownLoc、Metadata正常完成；InvalidGPS＝Warning/UnknownLoc、不Geo、不計GPS completeness；有效座標但transient耗盡＝Warning/UnknownLoc；auth/config＝不可重試Failed（不是MissingGPS、不是Warning），不保存半成品為Completed。缺可信時間fallback UploadCreatedAt已核准；原始local EXIF仍保存。Orientation缺少/非法以identity view與provenance表示，非法Warning，不改原檔。
推薦表image_metadata_results：BIGINT identity、ImageId BIGINT FK、SourceSHA256 CHAR(64)、MetadataRuleVersion VARCHAR(50)、InputFingerprint CHAR(64)、Status VARCHAR(20) Pending/Processing/Completed/Failed、Outcome Success/Warning、RawTakenAt NVARCHAR(64) nullable、ExifOffsetMinutes SMALLINT nullable、ResolvedTakenAt datetimeoffset(7)、TimeSource VARCHAR(30)、OffsetKnown bit、Orientation tinyint nullable（合法1～8）、Latitude/Longitude decimal(10,7) nullable、GpsValidity VARCHAR(20)、GeoOutcome VARCHAR(30)、LocationName NVARCHAR(150)、FieldProvenance NVARCHAR(MAX) versioned JSON、ErrorCode VARCHAR(100) nullable、CreatedAt/CompletedAt datetimeoffset(7)、rowversion。JSON只允許規格列明的欄位，不作任意client input／PII dump；camera/ISO/shutter/aperture與raw值保存在versioned結果JSON，按既有Unicode/precision約束驗證。
唯一(ImageId,InputFingerprint,MetadataRuleVersion)，日期/完成/Outcome constraints依Run狀態不變量。MOD03管理自身結果；MOD02只協調T3以同transaction提交結果/History/checkpoint；MOD09讀。版本化result authoritative，既有Image欄位僅在已核對PrimaryJob T3投影，secondary不覆寫全域Image Metadata；不改MOD01 mapping/Entity。本提案只批准規格形狀，物理SQLDDL/Migration/原始碼均未授權。
驗收：缺/非法GPS/服務錯誤三者可區分；auth/config不Retry且Job Failed；無offset不猜；fallback不計completeness；同result-key並行唯一、T3失敗整體回滾、純parse crash可重算。

### F-02：D-09 Naming request／artifact／恢复（MVP必要）

推薦Application簽章：IImageContentReader.OpenOriginalAsync(ImageId, token)→ContentHandle(Stream,Length,ContentVersion)；caller dispose、不承諾seek。INamingArtifactStorage.CreateAsync(OperationId,ImageId,ExpectedContentVersion,TargetName,token)→ArtifactReceipt；InspectAsync(OperationId,token)→Missing/Complete/Unknown＋receipt。client不提供storage path。Create同ID同payload回同receipt，不同payload拒絕；Local/GCS atomic conditional no-overwrite，Application不直接SDK/FileSystem。
推薦MOD04表naming_requests（GUID PK、BatchId FK、Template NVARCHAR(1000)、RuleVersion VARCHAR(50)、CollisionPolicy、InputFingerprint CHAR64、CreatedAt）、naming_request_items（BIGINT PK、RequestId FK、ImageId FK、JobId FK UNIQUE、Ordinal INT、Sequence BIGINT、OriginalExtension NVARCHAR(20)、ResolvedInput NVARCHAR(MAX) versionedJSON、TargetName NVARCHAR255；唯一Request+Image和Request+Ordinal）、naming_operations（OperationId GUID PK、ItemId FK UNIQUE、ExpectedContentVersion CHAR64、TargetReference VARCHAR500、Receipt NVARCHAR(MAX) nullable、Status Prepared/ArtifactCreated/Committed/NeedsReconciliation、rowversion、CreatedAt/CompletedAt）；目标reservation在MOD04由独立naming_artifact_reservations持久化（TargetKey VARCHAR500 normalized UNIQUE、OperationId FK UNIQUE）。具体provider storage key与display名分离，不把名字直接当任意path。
一批Apply共享Request，Item绑定对应Image的Job，Run引用Item而不是将一个图片Job伪装成BatchJob；不新增MOD01Job类型。冻结request/order/sequence/input引用在Run准入前，不能靠默认模板补Upload Naming/Full。MVP Increment=1沿用既有契约，Start默认1、允许0、checked BIGINT，sequence位数仅padding、不截断；重复imageId/跨Batch/overflow/empty/reserved/path traversal拒绝。Template已包含同extension不再补，冲突extension拒绝；原始extension来自OriginalFileName，不能从StoredPath猜。
collision沿用既有policy：Skip记录Skipped并满足该Item的明确终态，计入Naming已处理但不是artifact创建成功；Workflow必要Step最终是否完成按F07定义。suffix最多100候选（原名为第一候选，后99为稳定suffix），找不到以FILENAME_COLLISION永久失败；请求内部同名也走reservation。Preview非强制、只读；Apply重新验证、固定原输入。改Template/reapply未授权。
Prepared/reservation先commit→Create→Inspect匹配→T3提交artifact result/checkpoint/Log与Primary投影，commit后notify。原StoredPath始终不改；NewFileName仅PrimaryJob可投影。Storage成功DB失败不删除artifact；未知结果NeedsReconciliation、不盲目重建、不自动清理。本轮不新增修复/cleanup endpoint。RENAME_FAILED旧错误码保留兼容，message明确artifact失败；新adapter/文档不再用Rename。历史FILE_RENAME Step字符串保留版本兼容但语义明确Create Artifact。
验收：同target并发仅一个reservation/创建、shutdown同OperationId恢复、crash afterCreate补T3、失败无删除、固定sequence/extension正确、Skip与Success可区分。MOD01接口/StoredPath/mapping不变。

### F-03：D-10 capacity／Query／hash结果（MVP必要）

推荐已核准单集合候选snapshot包含完整集合内可比较图片，跨Batch；1000为整个目标集合容量上限，不用hash缺失/先筛选少数图片绕过容量。缺pHash/混version明确NotReady，不宣称完整NoMatch。存canonical pair(ImageLo<ImageHi、ContentVersions、ProfileVersion、SnapshotId、Score)，只存>=0.85 edge；每snapshot最多50000。membership/edges发布在T3一次原子切换snapshot reference，超限不切换、不发布部分结果，旧snapshot标明版本不是本次结果。已完成hash可保留。snapshot staging不当正式结果，不自动清理。
容量错误建议Processing API 409/SIMILARITY_CAPACITY_EXCEEDED；Error为安全limit分类，不泄漏storage/SQL。Step/Run/Job Failed，ErrorCode同上，不Retry、不标Completed，不影响其他Batch；Failed primary按已核准delta更新aggregate。管理员核准配置后只能处理符合现有Retry资格的Failed；capacity不符合transient人工Retry条件，本轮只报告阻挡，重新分析/容量失败重新执行的管理命令可延后，不偷扩大Retry。
低threshold Query推荐参数threshold(0..1)、page默认1、pageSize默认50/1..100；稳定score DESC/ImageId ASC，先完整评估同snapshot最多999比较再分page，不以top100称全结果。timeout2秒、shared比较gate=2、无无界等待队列，资源满503/SIMILARITY_QUERY_BUSY，timeout503/SIMILARITY_QUERY_TIMEOUT，缺hash或混version409/SIMILARITY_NOT_READY，capacity409，同request count/items一致，跨page不承诺snapshot稳定但返回snapshotId。totalMatched与coverage=Complete仅完整比较成功时提供；取消只结束read，不写数据。空集合单图可Completed NoMatch；无有效hash则NotReady，不能空回成功。
ImageHashResults与ImagePerceptualHashes独立表唯一(ImageId,ContentVersion,Rule/ProfileVersion)，结果Pending/Processing/Completed/Failed，source/hash值/checksum/completedAt/error/rowversion；scope不新增MOD01栏位，归deployment前提。EXACT Group UNIQUE(FileSize,SHA256)，成员UNIQUE(GroupId,ImageId)。SHA256默认所有本Workflow图片完整计算，消除MOD05无候选优化与晚到candidate冲突；无candidate NoMatch证据与match区分、晚到通过新snapshot更新，不更改旧CompletedJob History。新内容不是Shutdown Resume。
验收：1001张、50001edge均不发布部分正式结果；允许旧snapshot只读但标版本；未计算pair不能假回score；threshold0<=1000可真实只读计算；资源满有界拒绝；单图/缺hash区分。

### F-04：D-10具体pHash profile与验收门槛（MVP Analysis必要，核心Claim无依赖）

推荐ProfileId=phash-dct64-v1。原文件decode→合法EXIF Orientation一次→embedded ICC转换sRGB（缺ICC假定sRGB，invalidICC永久失败）→8bit RGBA、alpha在白底以整数舍入混合→Y=round((299R+587G+114B)/1000)→32x32 area resize（按pixel-area覆盖、定点/有理数权重、round half away）→32x32 DCT-II。DCT basis为随profile保存的Q16固定系数矩阵及checksum，不在每机器动态用cos生成；64bit整数累加、固定顺序，溢出拒绝，不依硬件float/FMA。按固定zigzag次序取64个非DC低频系数，median为排序第32/33的均值；coefficient>median为1，相等为0；bit0为首项MSB，8bytes big-endian/lowercase16hex。bitLength64，score=1-popcount(xor)/64；既有threshold0.85不变，不改变为学习模型。
ProfileManifest记录解码/ICC引擎及版本、数值/resize/DCT表/zigzag表checksum、Orientation/alpha约定、编码与实现版本。不在本轮核准新第三方package；实现前依AGENTS另查已有依赖，新增包需另批准，不能偷偷换解码器。
验收门槛建议：golden fixtures与supported OS/runtime矩阵全部bit一致；重复/并发/重启hash100%一致、同原始byte距离0，跨版本拒绝，异常输入不产生Completed。独立holdout以图片来源分组隔离（同原图所有变体不跨train/holdout），初步>=300张与positive/hard-negative各100仅起点；0.85处observed precision>=95%、recall>=90%、hard-negative FPR<=5%，报告95% Wilson区间与bootstrap结果，不凭初步样本声称总体达标。再要求precision的95% Wilson下界>=90%、recall下界>=85%、hard-negative FPR上界<=10%；任一未达或类别不足就不能PASS并补样，调参数必须新增profile并换未使用holdout，不在holdout反复调参。
性能后续测1000/499500pair/50000edge/Query999/併发2；Query验收2秒内成功或有界timeout，无OOM/无漏写/无部分发布；批分析不新增未量测的时间承诺，记录walltime/peakmemory/DBsize/P50/P95并审查部署容量。不要求文件阶段运行，也不在本轮建立TASK。

### F-05：D-11 Quality normalization（MVP Analysis必要）

推荐quality-rules-v1，固定25/30/15/10/20权重不变。原Orientation正常化尺寸；Resolution=100*min(pixelCount/12000000,1)。分析视图长边缩至<=1024（不放大），同profile灰阶Y；Sharpness=100*min(populationVariance(4-neighbour Laplacian)/1000,1)，忽略边界。Exposure=100*(1-(countY<=5+countY>=250)/N)。Metadata=100*validOriginalFields/6：可信TakenAt/有效CameraModel/ISO>0/Aperture>0/ExposureTime>0/合法GPSpair各一项，fallback/UnknownCamera/UnknownLoc不充数。Compression：PNG无损=100；JPEG已解析quantization table的luma AC平均q，100*clamp((100-q)/99,0,1)，缺/非法表永久错误而非猜quality；声明此为启发式指标非真实压缩质量保证，其他格式不擅自加支持。
Component与总分Decimal、计算保留6位最后输出2位round half away，Grade用输出前总分：[0,50)Poor/[50,70)Fair/[70,85)Good/[85,100]Excellent。保存rawmetrics/components/Grade/ContentVersion/RuleVersion/MetadataResultReference，独立Completed，不因shared row或pHash完成就Skip；原权重与旧算例不变。
验收0/100、小数49.5/69.99/84.99、全fallback metadata=0、JPEG/PNG差异、并行无lost update、重算同版本一致。参数是产品启发式默认，需本轮核准，准确性/校准留实作验收；不扩充新的AI质量模型。

### F-06：D-12 Recommendation（MVP Analysis必要）

推荐直接对VISUAL membership snapshot评分，不建立平行VERSION Group；保留VERSION_ANALYSIS旧Step adapter到MOD08。所有member Quality同rule且Completed才评分；Pending/Processing回DependencyNotReady，不占Worker等待：VERSION_ANALYSIS invocation前把checkpoint保留Pending并持久化DependencyWaitStartedAt（datetimeoffset nullable，checkpoint的新技术字段提案）、无RunningLog，释放Worker；既有5秒scanner检查本Run依赖就绪，在SQL锁定Job/Run/checkpoint并条件更新后按T2创建唯一invocation，不用Recovery Processing→Pending或新Run。等待最久120秒，到期不可重试DEPENDENCY_NOT_READY失败，shutdown按原持久时刻核对；等待不消耗StepRetry预算。只调度明确等待依赖且无Running invocation的Processing Run，不回收未知正在运行Job；永久Failed输入使Recommendation不可重试Failed，不无限Waiting，不把缺分当0。
每候选SimilarityScore为对其余members实际pHash距离score的均值（不从edges缺失推0），同profile最多1000；这些临时pair只计算不全存，但持久输入snapshot/score摘要以可复算。固定50/20/10/10/10权重；singleton推荐自己、SimilarityScore=100、confidence=null(reason=NoRunnerUp)，空group Empty/无推荐（Step Skipped条件须F07）；排序Score/Quality/Resolution/Sharpness/Metadata DESC、ImageId ASC。既有gap>=15 High、>=5 Medium、其余Low不变。
Reasons用固定code：Quality>=groupmean+5；Sharpness/Resolution/Exposure>groupmean；Metadata=100才CompleteMetadata；不虚构文字。保存唯一(SnapshotId,QualityRuleVersion,RecommendationRuleVersion)的ranked结果/inputs/resultVersion/状态，group merge或输入版本变更明确Stale，不隐式改旧结果。API保留既有GET推荐入口，ranking作为同结果内容，不新增独立ranking endpoint；响应明确snapshot/version/singleton/empty。不是可删保证，single-link不代表全pair近似。
验收A~B~C真分、Quality永久失败不等待、single/empty/tie deterministic、snapshot变后标Stale、不覆盖旧History。后续自动重新推荐／多group响应只取当前snapshot，历史查询扩展延后。

### F-07：D-14 Workflow冻结（完整集成必要，核心基础可独立规划）

推荐VALIDATION作为执行准入校验，不另列业务checkpoint；COMPLETED仅Job/Run lifecycle，不伪造独立执行Step。
Metadata=EXIF_PARSING→GEO_LOOKUP；合法GPS才调用Geo，缺/无效保存显式Skipped/Warning结果；auth/config为Failed。Naming=Metadata→NAMING（冻结解析/目标结果）→FILE_RENAME（兼容identifier、实际Create Artifact）。DuplicateDetection=SHA256_CALCULATION→EXACT_DUPLICATE_CHECK（已核准）。Analysis=Metadata→SHA256_CALCULATION→EXACT_DUPLICATE_CHECK→PHASH_GENERATION→VISUAL_SIMILARITY→QUALITY_ANALYSIS→VERSION_ANALYSIS。Full=Analysis→NAMING→FILE_RENAME。
每Run在创建时冻结WorkflowVersion、stepOrder、source/fingerprint和inputReferences；Shutdown同Run恢复同版本，manualRetry新Run沿用原业务input；不能在版本未知时猜adapter。所有required Step Completed Success/Warning或明确允许Skipped才Job Completed；Geo无GPS、NamingCollision Skip、Recommendation空/单图按上述明确规则处理，不把任意Skip视作完成。永久错误／容量/未知副作用阻挡按各Step policy；无Naming input不Claim，不默认Template。
相同image-step-version新结果通过SQL唯一键/条件更新互斥；同group snapshot发布加DB transaction/rowversion。Outcome/ResultReference/fingerprint由各模块负责，MOD02协调用T1/T2/T3原子提交与恢复，不跨模块猜meaning。新表result生命周期不等于Job生命周期。
验收四workflow列表/版本、缺input不Claim、可跳与不可跳、warning继续、fatal闭Run、shutdown不换版本、artifact无move。

### F-08：U-02＋诊断 API（最小诊断MVP必要，进阶指标延后）

推荐新增只读GET /api/v1/processing/batches/{batchId}/prerequisites，既有MOD01Query不改。200包含batchId/checkedAt/ready/reasons[{code,imageId?,jobId?}]/resolutionHint；404 BATCH_NOT_FOUND；provider无法核对503 PROCESSING_PREREQUISITE_UNAVAILABLE，不返回假ready=false。reasons仅BINDING_MISSING/BINDING_AMBIGUOUS/RELATIONSHIP_MISMATCH/COLLECTION_UNCONFIRMED/CONTEXT_MISSING/SIDE_EFFECT_UNCONFIRMED/AGGREGATE_INCONSISTENT（安全分类，不含内部证据/SQL/path）。resolutionHint是补交接证据后自动重验，不提供unblock写API。Query不修复、选主、排程、清理；检查完整集合，不取部分即可Ready；分页详细image/job诊断延后，返回前100条与truncated/count说明，ready仍按全体核对。
诊断不设新增持久状态表，不新增MOD01Status；实时查数据库＋受控runtime回收前提。SQL不可访问才503；正常blocked可200 ready=false。CreateJob/管理写API保持503交接拒绝与409 duplicate/mismatch顺序/无副作用契约。
最小runtime metrics定义：queuedNotifications=当前已接受但未取出通知（重复也计通知、不是Job）；activeInvocations=T1/T2已提交至T3或shutdown完成的实际执行数；stepRetryInvocationsTotal=开始首次逻辑retry槽位累计，不含resume/保存retry；shutdownResumesTotal=恢复invocation累计；droppedDuplicateNotificationsTotal=DB检查无执行资格的重复通知。metrics按processInstanceId标记、重启归零、不当Job终态/历史，Job counts用DB状态另查。并发counter在queue操作边界更新，enqueue失败不增、dequeue失败不减；sampling必须标runtime best-effort不可当persistent exact state。不增加exporter/dashboard或持久metric表。
验收正常blocked200与写API503区分、Query零写入、其他Batch仍可处理、provider故障不假报Ready、重启runtime归零但DBcounts不变。

### 可延后及限制（不阻挡MOD02核心基础规划）

- D-13 Export：本轮推荐先冻结边界不实现/开启新Export功能。若后续要实现，采用UTF8 BOM/RFC4180 bounded stream，既有DTO固定列顺序（ImageId,BatchId,OriginalFileName,NewFileName,TakenAt,CameraModel,Iso,ShutterSpeed,Aperture,Latitude,Longitude,LocationName,Sha256,QualityScore,SharpnessScore,ExposureScore,ResolutionScore,MetadataScore,Status,CreatedAt）；追加TimeSource列需届时核准。一Image一row、current primary结果、不join成多row；CreatedAt filter [from,to)，必须offset，UTC O格式、InvariantCulture、null空值，ImageId ASC；best-effort一致性，不承诺跨整次stream snapshot；危险文本首非空白=+-@/tab/CR/LF加单引号，先neutralize再CSVescape；响应开始后失败终止流而非JSON夹CSV，cancel释放资源。未来完整Export验收另行授权，不影响本轮Claim/Recovery。
- artifact reapply/保留期/cleanup：不自动清理、不支持改Template重Apply/Completed重分析；持久artifact/storage增长风险明确，管理员部署容量需保证，不能以压力为由删数据。
- 多用户/租户/多Instance：不支持；单集合与旧process确认停止仍是部署前提，无technical tenancy或lease/fencing保障。
- advanced Monitoring：不做持久counter/outbox/事件必达/跨重启指标连续性/详细dashboard；保留best-effort事件和REST authority。
- 算法/性能测量：不在文档阶段执行、不本轮建TASK；将校准、holdout、SQL并发/crash和容量量测列为未来实施验收要求，未通过前不称功能/性能PASS。
- capacity Failed重执行/Completed重分析：不扩张既有仅Failed/transient人工Retry；需后续独立产品决策，当前capacity失败只能报告、配置变更不能自行恢复运行。

### MOD02可行规划范围与必要前置（本轮仍不建TASK）

可独立规划核心：Run/checkpoint/History状态机、T1～T5原子边界/SQLClaim、singleInstanceRecovery、Pending补排程、retry/shutdown、perBatch binding准入/aggregate、best-effort通知、module adapter契约与diagnostic。依赖已核准D01～07及最新部署边界；F08诊断外部契约、F07的通用Outcome/必需Step/版本input约束需冻结，但不需等待所有Quality参数或Export。
完整Naming集成依赖F01/F02/F07；完整Analysis/Full依赖F01/F03/F04/F05/F06/F07；Export与进阶Monitoring不是上述核心的阻挡。真实集成验收/交接发布仍需MOD01 feature可用、TASK11/12最新状态核对，不能把未合并说已合并或自行标PASS。
用户集中核准F01～F08及上述延后边界后：只同步现有13份文档、执行最后文件一致性/引用/状态检查、提交文档审查报告并结束本轮；不建TASK、不实现、不checkout/commit/push/merge、不动MOD01/migration。

</details>
