# 專案文件索引

## 目的與使用方式

本文件協助 Agent 找到目前 TASK 對應的規格、契約、程式碼與測試，並判斷何時需要擴大查閱範圍。

本文件只負責導航，不是規格或例外規則。階段銜接、必讀來源、文件優先權與衝突處理仍以 `AGENTS.md` 為準；Pre-Implementation Review 與 Task Implementation 的執行條件仍以各自的 `SKILL.md` 為準。

使用方式：

```text
依 AGENTS.md 閱讀必要文件
→ 用本索引定位目前 TASK 的相關章節與檔案
→ 檢查直接契約、相依程式碼與測試
→ 發現跨模組影響、衝突或資訊不足時擴大查閱
```

不要以本索引的「可能相關」清單排除其他實際受影響的文件。

---

## 全域文件

| 文件 | 定位重點 |
|---|---|
| `AGENTS.md` | Agent 行為、閱讀順序、Scope、Approval、Conflict 與 Git 規則。 |
| `Requirements.md` | 產品需求、技術基準、依賴與測試原則；涉及 NuGet、Framework、ProjectReference 或 Infrastructure 技術時，特別檢查相關章節。 |
| `docs/System-Level-Specification.md` | 系統架構、模組邊界與全域 Workflow、API、Persistence 原則；跨模組或架構疑義時，特別檢查相關章節。 |
| 目前 TASK | 本次實作範圍、允許變更、預期檔案、驗收條件與範圍外事項。 |

依 `AGENTS.md` 與適用 skill 閱讀上述文件；表格中的定位重點不代表可省略其他必要內容。

---

## Module 索引

| Module | 文件 | 主要內容 | 可能相關的文件或程式碼 |
|---|---|---|---|
| MOD-01 Upload | `docs/modules/MOD-01-Upload-Spec.md` | Upload、Batch、Image、ProcessingJob 建立、檔案驗證、Storage handoff | MOD-02 Processing、Entity、EF Core Configuration |
| MOD-02 Processing | `docs/modules/MOD-02-Processing-Spec.md` | ProcessingJob、Queue、Worker、Retry、Recovery、Idempotency | MOD-01 Upload、MOD-09 Monitoring、Entity、EF Core Configuration |
| MOD-03 Metadata | `docs/modules/MOD-03-Metadata-Spec.md` | EXIF、GPS、TakenAt、Reverse Geocoding、Metadata fallback | MOD-04 Naming、MOD-07 Quality |
| MOD-04 Naming | `docs/modules/MOD-04-Naming-Spec.md` | Template、Token、Sequence、Sanitization、Collision、Preview、Apply | MOD-03 Metadata、Storage、File handling |
| MOD-05 Duplicate | `docs/modules/MOD-05-Duplicate-Spec.md` | SHA-256、Exact Duplicate、Duplicate Group | MOD-06 Similarity、Entity、EF Core Configuration |
| MOD-06 Similarity | `docs/modules/MOD-06-Similarity-Spec.md` | pHash、Hamming Distance、Visual Similarity | MOD-05 Duplicate、MOD-07 Quality、MOD-08 Recommendation |
| MOD-07 Quality | `docs/modules/MOD-07-Quality-Spec.md` | Resolution、Sharpness、Exposure、Metadata、Compression、QualityScore | MOD-03 Metadata、MOD-06 Similarity、MOD-08 Recommendation |
| MOD-08 Recommendation | `docs/modules/MOD-08-Recommendation-Spec.md` | Recommendation、Best Image、Quality Ranking、Recommendation Explanation | MOD-05 Duplicate、MOD-06 Similarity、MOD-07 Quality |
| MOD-09 Monitoring | `docs/modules/MOD-09-Monitoring-Spec.md` | ProcessingLog、TraceId、SignalR、Realtime Status、Observability | MOD-02 Processing |
| MOD-10 Export | `docs/modules/MOD-10-Export-Spec.md` | CSV、Export Format、Filtering、Analysis Result Export | 實際匯出資料所屬的 Module |

「可能相關」只提供查找起點；是否需要閱讀其他 Module，應依目前 TASK 的跨模組契約與實際影響判斷。

---

## Source Code 載入原則

Review 完成必要文件閱讀，或 Implementation 確認 Review 結果仍有效後，從目前 TASK 的預期檔案開始定位程式碼：

```text
Expected Files
→ Direct Contracts
→ Direct Dependencies
→ Related Implementation
→ Related Tests
```

先查直接相關的檔案，再依發現的依賴擴大範圍；不預設掃描整個 Module 或 Repository。若 TASK 未列出預期檔案，從模組規格與既有契約定位。

---

## Persistence / EF Core

目前 Repository 未見獨立的 Database Specification。涉及 EF Core、Table、Column、Foreign Key、Index、Constraint 或 Migration 時，依 `AGENTS.md` 的閱讀規則，特別查閱：

```text
相關 Module Specification 的資料表章節
→ 目前 TASK 的 Database Scope
→ Target Entity
→ EF Core Configuration
→ DbContext 與 Migration（涉及時）
→ 相關 Integration Tests
```

若日後新增獨立 Database Specification，應納入 `AGENTS.md` 規定的閱讀與衝突檢查。

---

## Pre-Implementation Review

`pre-implementation-review` 依其 `SKILL.md` 進行唯讀審查。本索引協助定位要比對的規格、直接契約、ProjectReference、依賴及測試；不能縮減 skill 與 `AGENTS.md` 的必讀來源。

Review 通過後，`pre-implementation-review` skill 產生的 Implementation Context 包含下列資訊，供後續定位：

- TASK / Module
- 已確認的 Scope 與範圍外事項
- 使用的 Contracts
- 預期變更檔案
- 重要 Constraints 與依賴
- 已讀 Sources
- Review Base Commit 與相關未提交變更的可核對版本基準
- Conflict Status

---

## Task Implementation

`task-implementation` 依其 `SKILL.md` 的進入條件執行。Approved Review Result 與 Implementation Context 協助定位，並供實作前核對 Review 是否仍有效；在來源可確認未變動且 Context 足夠時，無須重新完整閱讀已審查的規格。

若實作時發現新的衝突、依賴、契約缺口或資訊不足，依 `AGENTS.md` 與 skill 的衝突規則處理，並回查受影響的文件與程式碼。

---

## 核心原則

```text
INDEX = 去哪裡找
Specification = 什麼是正確的
Pre-Implementation Review = 能不能開始做
Task Implementation = 實際完成 TASK
```
