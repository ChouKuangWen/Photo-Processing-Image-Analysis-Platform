# MOD-02 Processing TASK Index

**Planning Baseline:** `1460fb1b4ae9b76e492adde262988f03c19934ba`  
**Branch:** `feature/mod-02-processing`  
**Scope:** TASK 文件規劃；尚未開始 Code／Build／Tests／Migration。

沿用 `tasks/MOD-01/TASK-XX-*.md` 組織，使用獨立 MOD-02 目錄。本索引不修改正式規格或宣稱 runtime PASS。

## TASK 與依賴

| TASK | 名稱 | 狀態 | 直接前置 |
|---|---|---|---|
| [TASK-01](TASK-01-Processing-State-Model.md) | Processing 狀態模型與不變量 | PLANNED | 無 |
| [TASK-02](TASK-02-Execution-Persistence-Schema.md) | 執行資料持久化與 Migration | PLANNED | TASK-01 |
| [TASK-03](TASK-03-Primary-Binding-Admission.md) | Primary Binding 與 Batch 准入 | PLANNED | TASK-02 |
| [TASK-04](TASK-04-Workflow-Context-Contracts.md) | Workflow、Input Context 與 Adapter 契約 | PLANNED | TASK-01、TASK-02 |
| [TASK-05](TASK-05-Atomic-Claim-Invocation-Start.md) | T1／T2 SQL Atomic Claim 與 Invocation Start | PLANNED | TASK-02、TASK-03、TASK-04 |
| [TASK-06](TASK-06-Outcome-History-Aggregates-Events.md) | T3 Outcome、History、Aggregate 與事件 | PLANNED | TASK-05 |
| [TASK-07](TASK-07-Retry-Timeout-Cancellation.md) | Retry、Timeout、Cancellation 與人工 Retry | PLANNED | TASK-06 |
| [TASK-08](TASK-08-Startup-Recovery-Reconciliation.md) | Startup Recovery 與 Crash Reconciliation | PLANNED | TASK-07 |
| [TASK-09](TASK-09-Worker-Pending-Scanner-DI.md) | BackgroundService、Pending Scanner 與 DI | PLANNED | TASK-08 |
| [TASK-10](TASK-10-Recommendation-Dependency-Wait.md) | VERSION_ANALYSIS 持久依賴等待 | PLANNED | TASK-06、TASK-09 |
| [TASK-11](TASK-11-Processing-API-Prerequisite-Diagnostics.md) | Processing API 與最小 Prerequisite 診斷 | PLANNED | TASK-03、TASK-07、TASK-09 |
| [TASK-12](TASK-12-Module-Adapters-Naming-Handoff.md) | 真實模組 Adapter 與 Naming Input Handoff | DEPENDENCY_PENDING | TASK-04、TASK-05、TASK-06、TASK-07、TASK-08、TASK-09、TASK-10、TASK-11；另需 MOD-03/04/05 與 reader/artifact 交付 |
| [TASK-13](TASK-13-Core-Workflow-Acceptance.md) | Processing 核心與交接 Acceptance | PLANNED_WITH_DEPENDENCIES | TASK-01、TASK-02、TASK-03、TASK-04、TASK-05、TASK-06、TASK-07、TASK-08、TASK-09、TASK-10、TASK-11；真實 Workflow 另依 TASK-12 |
| [TASK-14](TASK-14-Analysis-Full-Workflow-Integration.md) | Analysis／Full 完整 Workflow 整合 | BLOCKED | TASK-10、TASK-12、TASK-13；另需 N-01/N-02 核准及 MOD-03～08 交付 |

## 實作順序與範圍准入

1. TASK-01 → 02 → 03；TASK-04 依 01/02，可在 03 之外獨立推進契約。
2. TASK-05 依 02/03/04；之後 06 → 07 → 08 → 09。
3. TASK-10 依 06/09；TASK-11 依 03/07/09，兩者互不反向依賴。
4. TASK-12 等 04～11 及各模組真實能力；可分 DuplicateDetection/Naming 交付，不以假 adapter 完成。
5. TASK-13 核心階段依 01～11；真實 Workflow 階段另依 12。TASK-12 不依 TASK-13，沒有循環。
6. TASK-14 依 10/12/13 與決策/模組交付，當前 BLOCKED。TASK-13 不依 TASK-14；完整 Analysis/Full 驗收在 14。

建議先進行 TASK-01 實作前 Review，通過並取得人工批准才寫 Code。此索引不授權自動執行任一 TASK。

## 交易與責任分配

| 能力 | 單一責任 TASK | 整合消費者 |
|---|---|---|
| Domain state / attempt identity | 01 | 02～14 |
| 四張執行表及核准等待欄位 | 02 | SQL TASK |
| Primary binding / 完整 Batch 准入 / aggregate policy | 03 | 05～11 |
| Workflow / canonical input / adapter contracts | 04 | 05～14 |
| T1 Claim、T2 invocation start | 05 | 07～10 |
| T3 result/History/terminal/aggregate、提交後事件 | 06 | 07～14 |
| StepRetry/Timeout/Shutdown分類、T5人工Retry | 07 | 08～14 |
| 停止證據、T4安全恢復、crash reconciliation | 08 | 09/10/12～14 |
| Host/Consumer/只掃Pending的補排程 | 09 | API/acceptance |
| VERSION_ANALYSIS T2-W與T2原子wake | 10 | 13/14 |
| Create/Get/Retry與最小唯讀診斷 | 11 | acceptance |
| 真實模組Result/Storage/Input適配 | 12 | 13/14 |
| 核心／真實Workflow分階段驗收 | 13 | 14 |
| 完整Analysis/Full與其驗收 | 14 BLOCKED | 未來核准後 |

T3 接受受控 Outcome 決策，實際 Retry 分類/executor 在 07；06 不反向依賴 07。02 只物化核准等待欄位，等待執行在 10；08/09不得在10未交付時誤啟用該路徑。
所有 Claim/執行權由 SQL writer 授予，Queue/Scanner不另行read-check-write；事件writer不新增第二套History。
Concrete結果 writer 未交付前核心可用測試替身，但結果+T3原子性必須在12補真實SQL驗證。

## 共通契約保全

- Job：Pending/Processing/Completed/Failed；Run：Pending/Active/Completed/Failed。
- Checkpoint：Pending/Processing/RetryWaiting/Completed/Failed；ResultStatus：NULL/Success/Warning/Skipped。
- Log：Running→Success/Warning/Failed/Interrupted/Skipped，terminal不可重寫。
- Initial RunNo=1；ManualRetry=2～4；Recovery同Run。同Step/Run RetryOrdinal=0～3、1/2/4秒；Resume不加新Retry。
- T1不可先Commit Claim再補Run/Log；T3結果/History/checkpoint/必要aggregate同交易；T4不猜程序死亡；T5不改input/版本。
- Primary為可核對原Upload Job；secondary不改Upload aggregate。每次相關交易重核完整Batch集合，不用memory ready flag。
- 原Queue signature、Upload Commit→Enqueue及commit後不補償、原三表Mapping不變。
- 四個新核心表之外，各結果/Input表由所屬模組交付；不新增通用Context欄位、Outbox、診斷表、Job lease。
- N-01/N-02仍阻擋完整Analysis/Full；VERSION_ANALYSIS 120秒wait不自動適用VISUAL。
- 新Task文件不是新業務契約；正式來源優先。具體服務signatures在每Task Review確認。

## 規格與實作前核對事項

正式來源：`AGENTS.md`、`Requirements.md`、`docs/System-Level-Specification.md`、`docs/modules/MOD-02-Processing-Spec.md`、相關MOD最終章、`docs/Database-Schema.md`。
`docs/INDEX.md` 為導航，集中Review保留歷史；同題最終核准章優先於歷史待決文字。先前「MOD-01未合併」觀察不是當前Git事實。

仍需在對應實作前具體確認：
- TASK-04：凍結input/source/reference如何由核准mapping重建；fingerprint不是input備份。若缺mapping停止受影響功能並回報，不自行加欄位。
- TASK-08/09：部署控制提供舊程序停止證據的實際接點；不新增未核准liveness政策。
- TASK-12：各模組真實adapter/input/result/Storage receipt及provider契約交付。
- TASK-14：N-01/N-02需Owner決策與正式規格同步，現在不得補政策。
- TASK-02：實際測試DB mapping/資料影響；既有UploadPersistenceTests的全DB表數/Migration數/FK數斷言須精準調整，保留原三表契約。
- Pkcs技術債維持正式部署/公開服務前修補限制；此批文件不展開安全審查。

## 驗收與授權

每TASK自帶Expected Files、層級責任、交易/例外/取消/恢復、Unit/Integration Tests、AC、DoD、Commit邊界與Out of Scope。
真實SQL驗證不可用EF InMemory替代；SQL環境/清理需先確認隔離且取得執行授權。測試未執行標Not Run，不能以MOD01既有226 PASS替代新功能證據。
文件完成後等待人工Review；不得自動Code、Commit或Push。

