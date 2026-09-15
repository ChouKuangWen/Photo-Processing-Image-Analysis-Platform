---
name: pre-implementation-review
description: Use before implementing any project TASK specification. Perform a read-only consistency review across AGENTS.md, Requirements.md, System-Level specification, the relevant Module specification, the current TASK, existing contracts, project dependencies, and related code. Detect conflicts, ambiguities, scope violations, or missing contracts before implementation. Do not modify files or implement code during this review.
---

# Pre-Implementation Review

## Purpose

在任何 TASK 開始實作前，先進行唯讀檢查，確認規格、既有 Contract、Architecture、Project Reference 與現有程式碼彼此一致。

此 Skill 只負責 **Pre-Implementation Review**。

**不得在同一個 Review 階段實作、修改或建立任何檔案。**

Review 通過後，回報 `READY FOR IMPLEMENTATION`，並等待使用者明確指示開始實作。

---

## Authoritative Sources

依目前 TASK 的範圍，讀取並比對下列資料：

1. `AGENTS.md`
   - Agent 行為規則
   - Scope 與 approval rules
   - Conflict handling rules

2. `Requirements.md`
   - Project baseline
   - Technology / dependency constraints
   - Specification locations

3. `docs/System-Level-Specification.md`
   - System architecture
   - Cross-module rules
   - Global API / DB / infrastructure principles

4. Relevant Module Specification
   - 例如：`docs/modules/MOD-01-Upload-Spec.md`
   - Module responsibilities
   - Business rules
   - Contracts
   - Error handling
   - API / DB / test requirements

5. Current TASK Specification
   - 例如：`tasks/MOD-01/TASK-05-Local-File-Storage.md`
   - Current implementation scope
   - Expected files
   - Definition of Done
   - Out-of-scope items

6. Existing source code related to the TASK
   - Interfaces
   - DTOs
   - Domain Entities / Enums
   - Existing Services
   - Infrastructure implementations
   - Test doubles / tests
   - Project references

---

## Specification Priority

若設計內容彼此衝突，優先順序為：

```text
System-Level Specification
→ Module Specification
→ TASK Specification
→ Existing implementation
```

`AGENTS.md` 負責 Agent 行為與工作流程，不用來取代系統或模組業務規格。

若 `Requirements.md` 與其他 authoritative specification 發生矛盾，也必須回報，不得自行選擇其中一個版本實作。

---

## Review Procedure

### 1. Identify TASK Scope

確認：

- TASK 要解決的問題
- Expected files
- Allowed modifications
- Out-of-scope items
- Definition of Done

不得擴大 TASK Scope。

### 2. Inspect Existing Contracts

確認 TASK 使用到的既有 Contract 是否足夠，包括：

- Interface method signatures
- DTO fields
- Domain Entity / Enum definitions
- Return types
- Error codes / messages
- CancellationToken behavior
- Stream ownership rules
- Persistence / Storage / Queue abstractions

不得自行修改既有 Contract。

### 3. Check Architecture Boundaries

確認沒有造成不合法 dependency，例如：

```text
Domain → Infrastructure
Application → Infrastructure
```

確認新實作應位於正確 Layer：

```text
API
Application
Domain
Infrastructure
```

### 4. Check Project Dependencies

檢查：

- `.csproj` ProjectReference
- Target framework
- Existing NuGet packages
- 是否真的需要新增 dependency

不得自行新增未核准 NuGet Package。

### 5. Check API / Database Impact

若 TASK 不包含 API 或 Database 變更，確認實作不需要：

- 新增或修改 Endpoint
- 修改 Request / Response contract
- 修改 Database Schema
- 新增 Migration
- 修改 EF Core mapping

若發現實作必須修改上述內容，視為 conflict。

### 6. Check Missing Decisions

特別檢查 TASK 是否缺少會影響實作的決策，例如：

- Naming strategy
- Collision behavior
- Missing-file behavior
- Stream starting position
- Error mapping
- Retry behavior
- Transaction boundary
- Cleanup behavior
- Return value semantics

不得自行補完重大設計決策。

### 7. Inspect Related Tests

確認：

- 現有 tests 是否與新 TASK 規格一致
- 是否已有 reusable test helper / test double
- TASK 要求的測試是否可在現有 architecture 下完成

---

## Prohibited Actions During Review

Review 階段不得：

- 修改任何 source file
- 修改 specification
- 新增檔案
- 刪除檔案
- 安裝 NuGet Package
- 建立 Migration
- Commit
- Push
- 實作 TASK
- 自行修正發現的 conflict

可以唯讀檢查：

- source code
- specification
- `.csproj`
- git status / git diff
- repository structure

若任何命令可能修改 tracked files，先不要執行。

---

## Result: READY FOR IMPLEMENTATION

只有在沒有 blocking conflict 或 ambiguity 時，回報：

```text
READY FOR IMPLEMENTATION
```

並包含：

### Scope Confirmation
- TASK purpose
- In-scope work
- Out-of-scope work

### Contracts Reused
- Interfaces
- DTOs
- Entities / Enums
- Existing abstractions

### Expected Changes
- Files expected to be created
- Files expected to be modified

### Dependency Check
- Required project references
- NuGet changes, if any
- Architecture dependency direction

### Conflict Check
- Confirm no blocking conflict found

### Risks / Notes
- Non-blocking implementation considerations only

最後明確寫：

```text
No files were modified.
Implementation has not started.
Waiting for explicit implementation approval.
```

---

## Result: CONFLICT DETECTED

只要存在會影響正確實作的 conflict、missing contract 或 ambiguity：

**立即停止，不得實作。**

依以下格式回報：

```text
CONFLICT DETECTED
```

### Conflict
指出衝突或缺口是什麼。

### Sources
指出涉及的文件、Contract 或程式碼。

### Impact
說明如果直接實作，會造成什麼問題。

### Minimal Proposed Change
提出最小必要修改方案。

### Approval Required
明確說明需要使用者決定或批准什麼。

然後停止。

---

## Critical Rule

永遠遵守：

```text
Stop
→ Report Conflict
→ Explain Impact
→ Propose Minimal Change
→ Wait for Approval
```

不得因為「看起來合理」就自行決定 Architecture、Schema、API、Contract、Workflow 或 Error behavior。

---

## Completion Rule

此 Skill 的完成條件只有兩種：

```text
READY FOR IMPLEMENTATION
```

或：

```text
CONFLICT DETECTED
```

此 Skill 本身不執行 implementation。
