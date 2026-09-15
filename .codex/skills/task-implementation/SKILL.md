---
name: task-implementation
description: Implement an approved project TASK only after the pre-implementation review has passed and the user has explicitly approved implementation. Follow AGENTS.md, Requirements.md, System-Level specification, the relevant Module specification, and the current TASK exactly. Do not expand scope, modify authoritative contracts/specifications without approval, commit, or push. Stop and report any newly discovered conflict or ambiguity.
---

# Task Implementation

## Purpose

在 TASK 已完成 Pre-Implementation Review，且使用者已明確批准開始實作後，依照核准規格執行程式碼實作。

此 Skill 只負責：

- 實作目前已核准的 TASK
- 建立或修改 TASK 明確允許的檔案
- 執行必要的 build / test
- 檢查 git diff
- 回報實作結果

此 Skill **不得自行改規格、擴大 Scope、Commit 或 Push**。

---

## Entry Conditions

只有同時滿足以下條件才能開始：

1. Current TASK Specification 已存在。
2. Pre-Implementation Review 已完成。
3. Review 結果為：

```text
READY FOR IMPLEMENTATION
```

4. 使用者已明確要求開始實作。

若任一條件不成立：

```text
DO NOT IMPLEMENT
```

並回報缺少的條件。

---

## Authoritative Sources

實作前必須重新確認：

1. `AGENTS.md`
2. `Requirements.md`
3. `docs/System-Level-Specification.md`
4. Relevant Module Specification
5. Current TASK Specification
6. Existing contracts and related source code
7. Approved Pre-Implementation Review result

---

## Specification Priority

若實作過程發現內容不一致，依下列優先順序判斷：

```text
System-Level Specification
→ Module Specification
→ TASK Specification
→ Existing implementation
```

`AGENTS.md` 控制 Agent 行為與工作流程。

若任何 authoritative source 發生衝突：

```text
Stop
→ Report Conflict
→ Explain Impact
→ Propose Minimal Change
→ Wait for Approval
```

不得自行選擇其中一個版本繼續實作。

---

# Implementation Procedure

## 1. Confirm Scope

開始前先列出：

- TASK purpose
- In-scope items
- Out-of-scope items
- Expected files
- Allowed modified files

不得修改 TASK 未授權的檔案，除非使用者先批准。

---

## 2. Inspect Current Working Tree

執行唯讀檢查：

```bash
git status
git diff
```

若存在與目前 TASK 無關的未提交變更：

- 不得覆蓋
- 不得刪除
- 不得自動 stash
- 不得 reset

應先回報並等待指示。

---

## 3. Implement Minimal Required Changes

只實作完成 TASK 所需要的最小變更。

遵守：

- Existing Interfaces
- Existing DTOs
- Existing Domain Entities / Enums
- Existing Architecture boundaries
- Existing error contracts
- Existing CancellationToken rules
- Existing ownership / lifetime rules

不得為了「順便整理」而重構未涉及的程式碼。

---

## 4. Architecture Boundaries

必須維持 Clean Architecture dependency direction。

禁止：

```text
Domain → Infrastructure
Application → Infrastructure
```

應維持：

```text
API
↓
Application
↓
Domain

Infrastructure
→ implements Application abstractions
```

若 TASK 為 Infrastructure implementation，只能實作既有 abstraction，除非 TASK 明確批准新增 Contract。

---

## 5. Contract Protection

不得自行修改：

- Interface method signatures
- DTO fields
- Domain Entity public contract
- Enum definitions
- API contract
- Database schema
- Error codes
- Error messages
- Workflow behavior

若現有 Contract 無法支援 TASK：

```text
Stop
→ Report Contract Gap
→ Explain Impact
→ Propose Minimal Change
→ Wait for Approval
```

---

## 6. Dependency Rules

不得自行新增 NuGet Package。

若實作確實需要新 package：

```text
Stop
→ Explain why built-in / existing dependencies are insufficient
→ Propose package
→ Wait for Approval
```

不得直接修改 package references。

ProjectReference 只有在 TASK 明確允許時才可新增。

---

## 7. Tests

依 TASK Specification 建立或修改測試。

測試至少應覆蓋 TASK 明確要求的：

- Success path
- Failure path
- Boundary conditions
- Cancellation
- Error mapping
- Resource ownership
- Scope-specific edge cases

不得為通過測試而降低既有驗證標準。

不得刪除既有 failing test 來取得綠燈。

---

## 8. Build and Test

實作完成後執行：

```bash
dotnet build
dotnet test
```

如果 TASK 只允許特定 project，也可先執行 targeted build / test，但完成前仍應依專案規則執行必要的整體測試。

若 build 或 test 失敗：

1. 判斷是否由目前 TASK 造成。
2. 若屬 TASK 內可修正問題，修正後重新執行。
3. 若發現新的規格衝突或超出 TASK Scope：

```text
Stop
→ Report
→ Wait for Approval
```

不得跨 Scope 修復。

---

## 9. Diff Review

完成後執行：

```bash
git status
git diff --stat
git diff
```

確認：

- 只有預期檔案被修改
- 沒有 accidental formatting
- 沒有 unrelated refactor
- 沒有 package / schema / API drift
- 沒有 debug code
- 沒有 temporary files
- 沒有 secrets
- 沒有自動修改 authoritative specifications

若發現不在 TASK Scope 的變更，必須移除自己造成的變更；若來源不明或是使用者原有變更，不得擅自處理，應回報。

---

# Newly Discovered Conflict During Implementation

即使 Pre-Implementation Review 已通過，實作過程仍可能發現新的問題。

例如：

- Contract 缺少必要資料
- Existing code 與文件實際不一致
- Test requirement 無法在既有 Contract 下完成
- 必須修改 API / DB / Domain 才能繼續
- Existing dependency 不足
- File ownership / lifetime 未定義
- Error behavior 未定義

此時必須立即停止。

回報格式：

```text
IMPLEMENTATION BLOCKED
```

並包含：

## Conflict
發現什麼問題。

## Current Progress
目前已完成哪些修改。

## Impact
為何無法在目前規格下安全繼續。

## Minimal Proposed Change
最小必要修改方案。

## Approval Required
需要使用者批准什麼。

不得自行解決重大設計問題。

---

# Prohibited Actions

此 Skill 不得：

- 修改 System-Level Specification
- 修改 Module Specification
- 修改 TASK Specification
- 自行新增 Architecture Pattern
- 自行新增 API Endpoint
- 自行修改 Database Schema
- 自行建立 Migration
- 自行新增 NuGet Package
- 自行擴充 Domain Model
- 自行新增 Error Code
- 跨 Module 修改
- Commit
- Push
- Merge branch
- Rebase
- Reset user changes
- Force checkout
- Auto-fix unrelated warnings

除非目前 TASK 明確授權，且使用者已批准。

---

# Completion Report

完成實作後，回報：

```text
IMPLEMENTATION COMPLETE
```

並包含：

## Implemented
- 完成的功能

## Files Created
- 新增檔案

## Files Modified
- 修改檔案

## Contracts
- 是否修改 Contract
- 正常情況應為 `No contract changes`

## Build
- `dotnet build` 結果

## Tests
- `dotnet test` 結果
- Passed / Failed 數量（若可得）

## Warnings
- Non-blocking warnings

## Scope Check
- 是否存在 Scope deviation
- 正常情況應為 `None`

## Git Status
- Working tree 中與本 TASK 相關的未提交變更

最後明確寫：

```text
No commit was created.
Waiting for manual code review and commit approval.
```

---

# Critical Rule

實作過程永遠遵守：

```text
Approved TASK only
→ Minimal implementation
→ Build
→ Test
→ Diff review
→ Report
→ Wait for manual review
```

若發現衝突：

```text
Stop
→ Report Conflict
→ Explain Impact
→ Propose Minimal Change
→ Wait for Approval
```

---

# Recommended Workflow With Pre-Implementation Review

完整工作流程：

```text
Write TASK Specification
        ↓
$pre-implementation-review
        ↓
READY FOR IMPLEMENTATION
        ↓
User approval
        ↓
$task-implementation
        ↓
Build + Test + Diff Review
        ↓
IMPLEMENTATION COMPLETE
        ↓
Manual Code Review
        ↓
User approval
        ↓
Commit
```

---

# End of Task Implementation Skill
