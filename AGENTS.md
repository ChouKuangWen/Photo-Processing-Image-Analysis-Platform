# AGENTS.md

# Photo Processing & Intelligent Image Analysis Platform

本文件定義 AI Agent 在本 Repository 中的開發行為、限制、工作流程與衝突處理規則。

本文件的目的不是描述產品功能，而是回答：

> **AI Agent 在這個專案裡應該怎麼工作？**

---

# 1. Agent Role

AI Agent 在本專案中的角色是：

> **Development Collaborator**

Agent 可以：

- 閱讀規格
- 分析現有程式碼
- 建立程式碼
- 修改程式碼
- 建立測試
- 執行測試
- 修復明確的 Implementation Error
- 重構不改變行為的程式碼
- 提出 Architecture / API / Database 改善建議

Agent 不是：

> **Architecture Owner**

因此 Agent 不得自行重新設計系統。

---

# 2. Required Reading Order

開始任何 Development Task 前，Agent 必須依序閱讀：

```text
AGENTS.md
 ↓
Requirements.md
 ↓
System-Level Specification
 ↓
Target Module Specification
 ↓
Related Module Specifications
 ↓
Database / API Specification
 ↓
Current Task
 ↓
Existing Code
```

不得只閱讀 Task 就直接開始 Implementation。

---

# 3. Source of Truth

文件優先順序如下：

```text
1. AGENTS.md
   Agent 行為與開發規則

2. System-Level Specification
   系統架構、Module Boundary、跨模組契約

3. Approved ADR
   已核准的 Architecture Decision

4. Module-Level Specification
   模組功能與 Business Rules

5. Database / API Specification
   Physical Schema / External Contract

6. Development Task
   本次 Implementation Scope

7. Existing Implementation
   現有程式碼
```

Existing Code 不代表一定正確。

如果 Existing Code 與 Specification 衝突：

> Specification 不應被程式碼反向覆寫。

但 Agent 也不得自行修改程式碼來「猜測」正確答案。

必須依照 Conflict Handling Rule 處理。

---

# 4. Conflict Handling Rule

如果發現以下任何衝突：

- System-Level vs Module-Level
- Module-Level vs Database Schema
- Module-Level vs API Contract
- Specification vs Existing Code
- Task vs Specification
- Cross-Module Contract Conflict
- 缺少必要 Business Rule

Agent 必須：

```text
STOP
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Approval
```

Agent 不得：

- 自行選擇其中一份規格
- 自行修改 Specification
- 自行修改 Database Schema
- 自行修改 API Contract
- 自行補出未定義的 Business Rule
- 為了讓 Build 通過而偷偷改 Architecture

---

# 5. Architecture Rules

本專案使用：

> **Clean Architecture**

主要 Dependency：

```text
API
 ↓
Application
 ↓
Domain
```

Infrastructure 負責實作 Application / Domain Contract。

Domain 不得直接依賴：

- ASP.NET Core
- Entity Framework Core
- Microsoft SQL Server Provider
- Google Cloud SDK
- File System Implementation
- SignalR
- External API SDK
- Concrete Image Processing Library

Agent 不得因為實作方便破壞 Dependency Rule。

---

# 6. Project Structure

預期 Solution 結構：

```text
src/
├── PhotoPlatform.Api
├── PhotoPlatform.Application
├── PhotoPlatform.Domain
└── PhotoPlatform.Infrastructure

tests/
├── PhotoPlatform.UnitTests
├── PhotoPlatform.IntegrationTests
└── PhotoPlatform.E2ETests

docs/
├── System-Level-Specification.md
└── modules/

tasks/
```

各 Layer 主要責任：

## PhotoPlatform.Api

負責：

- Controllers
- API Contract Mapping
- HTTP
- Middleware
- Dependency Injection Setup
- SignalR Endpoint

不得包含核心 Business Logic。

---

## PhotoPlatform.Application

負責：

- Use Cases
- Application Services
- DTOs
- Interfaces
- Workflow Coordination
- Validation Coordination

不得直接依賴 Infrastructure Implementation。

---

## PhotoPlatform.Domain

負責：

- Entities
- Value Objects
- Domain Rules
- Domain Policies
- Domain Services where required

Domain 必須保持 Infrastructure Independent。

---

## PhotoPlatform.Infrastructure

負責：

- EF Core
- Microsoft SQL Server
- Repository Implementation
- File Storage
- Google Cloud Storage
- Image Processing Implementation
- External Geo Service
- Queue Infrastructure
- Logging Infrastructure

---

# 7. Module Boundary Rules

系統正式 Module：

```text
MOD-01 Upload
MOD-02 Processing
MOD-03 Metadata
MOD-04 Naming
MOD-05 Duplicate
MOD-06 Similarity
MOD-07 Quality
MOD-08 Recommendation
MOD-09 Monitoring
MOD-10 Export
```

Module Number：

> 代表功能與文件識別編號。

不代表：

> Processing Execution Order。

Agent 不得跨 Module 實作 Business Logic。

例如：

```text
MOD-05 Duplicate
```

不得實作：

```text
Visual Similarity
```

因為 Visual Similarity 屬於：

```text
MOD-06 Similarity
```

同理：

```text
MOD-06 Similarity
```

不得自行實作：

- Exact Duplicate Rule
- Quality Score
- Recommendation Ranking
- Naming Rule

Cross-Module Interaction 應透過：

- Interface
- Application Contract
- Domain Contract
- Persisted Result

完成。

---

# 8. Database Rules

本專案 Database Baseline：

```text
Microsoft SQL Server
```

ORM：

```text
Entity Framework Core
```

Provider：

```text
Microsoft.EntityFrameworkCore.SqlServer
```

Agent 不得自行：

- 更換 Database Provider
- 改用 MySQL
- 改用 PostgreSQL
- 改用 SQLite 作為 Production Design
- 新增 Table
- 刪除 Table
- 修改 Column
- 修改 Nullability
- 修改 FK
- 修改 Unique Constraint
- 修改 Index
- 修改 Check Constraint

除非：

> Specification 已明確要求。

Schema Change 必須遵循：

```text
Approved Requirement
 ↓
Specification Change
 ↓
EF Core Migration
 ↓
Integration Test
```

---

# 9. Database Access Rules

Application / Domain 不得直接使用：

```text
DbContext
SqlConnection
Raw SQL Server Infrastructure
```

Infrastructure 負責 Persistence Implementation。

如果現有架構已定義 Repository / Interface：

> Agent 必須優先使用既有 Abstraction。

不得因為寫起來比較快而繞過 Architecture。

---

# 10. API Rules

API Base：

```text
/api/v1
```

Agent 不得自行：

- 修改 Endpoint
- 修改 HTTP Method
- 修改 Request Contract
- 修改 Response Contract
- 修改 Error Code
- 修改既有 HTTP Status
- 修改 API Version

除非 Specification 或 Task 明確授權。

API 必須：

- Validate Input
- 使用安全 Error Message
- 提供 TraceId where required
- 不暴露內部 Exception Detail

不得回傳：

- StackTrace
- SQL Error
- Internal Path
- Connection String
- Secret
- Credential

---

# 11. Asynchronous Processing Rules

Long-running Image Processing：

> 不得阻塞 HTTP Request。

MVP Queue：

```text
System.Threading.Channels
```

Persistent Job State：

```text
Microsoft SQL Server
```

Channel 是：

> Runtime Queue

ProcessingJob 是：

> Persistent State

Agent 不得把 Channel 當成永久 Job Storage。

---

# 12. Processing Responsibility

MOD-02 Processing 負責：

- Queue
- Worker
- Workflow
- State
- Retry
- Recovery
- Idempotency Coordination
- Concurrency Coordination

Processing Module：

> 協調其他 Module。

不得把 Metadata、Duplicate、Similarity、Quality、Naming、Recommendation 的所有 Business Logic 塞進 Worker。

---

# 13. File Storage Rules

所有 Image Storage Access 必須透過：

```text
IFileStorageService
```

Development：

```text
Local File Storage
```

Production：

```text
Google Cloud Storage
```

Application 不得直接使用：

```text
Google Cloud Storage SDK
```

Client 不得指定：

- 任意 File System Path
- 任意 Cloud Storage Path

Database 不保存 Raw Image Binary。

---

# 14. Reliability Rules

系統使用：

```text
Retry
Fallback
Recovery
Compensation
```

Retry 僅適合：

- Timeout
- Temporary I/O Failure
- HTTP 408
- HTTP 429
- HTTP 5xx
- Temporary Storage Failure

MVP Retry：

```text
1 second
 ↓
2 seconds
 ↓
4 seconds
```

不得無限 Retry。

例如：

- Corrupted Image
- Invalid File
- Unsupported Format
- Invalid Configuration

通常不應 Retry。

---

# 15. Idempotency Rules

重要 Processing Step 必須考慮：

```text
ImageId
+
ProcessingStep
```

必要時加入：

```text
AnalysisVersion / RuleVersion
```

Agent 必須避免：

- Duplicate Processing
- Duplicate Analysis
- Duplicate Group Member
- Duplicate Rename
- Duplicate Persistent Result

---

# 16. Concurrency Rules

Agent 不得假設：

> 同一 Image 永遠只會被一個 Worker 操作。

必須考慮：

- Concurrent Workers
- Same Image Processing
- Database Race
- Group Creation Race
- Filename Collision
- Lost Update

重要資料一致性優先使用：

- Database Constraint
- Unique Index
- Transaction
- Concurrency Control
- Idempotent Logic

不得只依靠 In-Memory Lock。

---

# 17. Logging Rules

MVP Logging 分成：

```text
System Log
+
Processing Log
```

System Log：

- Runtime
- Exception
- Infrastructure Failure
- Debugging
- Trace Correlation

Processing Log：

- Image Processing Lifecycle
- Step
- Status
- Duration
- Retry
- Error

目前 MVP：

> 不建立 Audit Log。

---

# 18. Security Rules

Agent 必須考慮：

- File Validation
- File Size Limit
- MIME Validation
- Filename Sanitization
- Path Traversal
- Input Validation
- Storage Isolation
- Exception Sanitization
- CORS
- HTTPS
- Rate Limiting
- Security Headers where applicable

不得 Commit：

- Password
- API Key
- Connection String
- GCP Credential
- Secret

---

# 19. Package Rules

Agent 不得自行新增第三方 NuGet Package。

如果 Implementation 需要新的 Package：

```text
Report Need
 ↓
Explain Purpose
 ↓
Explain Alternatives
 ↓
Wait for Approval
```

.NET / ASP.NET Core / EF Core 已有能力可以完成時，優先使用既有 Framework。

---

# 20. Scope Control

Agent 必須：

> 只完成 Task 明確要求的內容。

不得因為：

- 「順便」
- 「未來可能會需要」
- 「這樣比較完整」
- 「最佳實務通常會」

而加入未核准功能。

例如 MVP 不得自行加入：

- Authentication
- RBAC
- Redis
- RabbitMQ
- Kafka
- CLIP
- Vector Database
- OCR
- Face Recognition
- Semantic Search

---

# 21. Refactoring Rules

Agent 可以進行：

- Small Refactor
- Rename for clarity
- Remove obvious duplication
- Extract private helper
- Improve testability

前提：

> 不改變 Public Contract、Business Rule、Architecture Boundary。

如果 Refactor 會影響：

- Public Interface
- API
- Database
- Cross-Module Contract

必須先提出 Change Proposal。

---

# 22. Testing Rules

每個 Task 應依需求建立：

```text
Unit Test
Integration Test
E2E Test
```

不得以：

> Build Success

視為功能完成。

至少必須執行：

```bash
dotnet restore
dotnet build
dotnet test
```

如果其中一項失敗：

> 不得宣稱 Task Completed。

---

# 23. Unit Test

Unit Test 應：

- 測 Business Rule
- 測 Edge Case
- 測 Error Behavior
- 不依賴真正 SQL Server
- 不依賴真正 GCS
- 不依賴外部 Network

---

# 24. Integration Test

Integration Test 應驗證實際整合。

Database Integration 應優先使用：

> Real Microsoft SQL Server Test Environment

不得只使用：

```text
EF Core InMemory Provider
```

來宣稱 SQL Server Integration 已驗證。

---

# 25. E2E Test

需要 UI / 完整 User Flow 的功能，可使用：

```text
Playwright
```

E2E Test 負責驗證：

> 使用者操作到系統結果的完整流程。

不是取代 Unit / Integration Test。

---

# 26. Code Quality

Agent 實作應優先：

- Readability
- Explicit Behavior
- Small Methods
- Clear Naming
- Dependency Injection
- Async where appropriate
- CancellationToken where appropriate
- Proper resource disposal
- Controlled Memory Usage

避免：

- God Service
- Huge Controller
- Static Global State
- Hidden Side Effects
- Duplicate Business Rules
- Over-engineering

---

# 27. Comment Rules

不要對明顯程式碼加入大量無意義 Comment。

Comment 應用於：

- Business Reason
- Non-obvious Constraint
- Important Tradeoff
- Compatibility Requirement

不要只描述程式碼字面行為。

---

# 28. Task Workflow

每個 Task 建議依序執行：

```text
1. Read Specification
2. Inspect Existing Code
3. Identify Affected Layers
4. Identify Affected Tests
5. Implement Minimum Required Change
6. Build
7. Run Tests
8. Review Diff
9. Report Result
```

---

# 29. Before Implementation

Agent 開始修改前，應確認：

```text
What Module?
What Requirement?
What Layer?
What Existing Contract?
What Tests?
```

如果答案無法從文件或程式碼確定：

> 不要猜。

使用 Conflict / Missing Rule Process。

---

# 30. After Implementation

Agent 完成後應回報：

```text
Implemented
Changed Files
Tests Added
Tests Executed
Build Result
Known Limitations
Specification Conflicts
```

不要只回覆：

> Done.

---

# 31. Git Change Scope

單一 Task 應盡量維持單一目的。

避免在同一 Task：

- 大量格式化無關檔案
- 修改無關 Module
- 更新無關 Package
- 重構整個 Solution
- 修改不相關 API

Diff 應保持：

> Small, Reviewable, Traceable.

---

# 32. Definition of Completion

Task 可以標記 Completed 前：

- [ ] Requirement implemented
- [ ] Module Boundary preserved
- [ ] Architecture preserved
- [ ] API Contract preserved
- [ ] Database Contract preserved
- [ ] Build passes
- [ ] Required tests pass
- [ ] Error handling implemented
- [ ] No unrelated scope added
- [ ] No secret committed
- [ ] No unresolved specification conflict

如果存在 Unresolved Conflict：

> Task 不得宣稱 Completed。

---

# 33. Core Agent Principle

本專案最重要的 Agent 原則：

> **Specification before Implementation.**

> **Boundary before Convenience.**

> **Minimum Required Change before Over-engineering.**

> **Tests before Completion.**

當不確定時：

```text
STOP
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Approval
```

不得自行修改 Architecture、Database、API 或 Business Rule。