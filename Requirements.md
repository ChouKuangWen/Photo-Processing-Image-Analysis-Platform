# Requirements.md

# Photo Processing & Intelligent Image Analysis Platform

**Project Type:** Backend Processing Platform  
**Backend:** ASP.NET Core / C#  
**Architecture:** Clean Architecture  
**ORM:** Entity Framework Core  
**Database:** Microsoft SQL Server  
**API:** REST API + SignalR  
**Background Processing:** BackgroundService + System.Threading.Channels  
**Storage:** Local File Storage / Google Cloud Storage  
**Deployment:** Docker / Google Cloud Run  
**Status:** Development Baseline  

---

# 1. Project Overview

本專案是一套：

> **智慧影像處理與分析平台**

英文名稱：

> **Photo Processing & Intelligent Image Analysis Platform**

系統主要處理大量照片的：

- Upload
- Metadata Extraction
- Naming
- Duplicate Detection
- Visual Similarity
- Quality Analysis
- Recommendation
- Monitoring
- Export

專案核心定位：

> **File Processing + Image Analysis + Workflow Automation**

不是單純 CRUD 系統，也不是單純照片重新命名工具。

---

# 2. Project Goal

系統目標是將：

```text
Raw Images
```

轉換為：

```text
Structured
Analyzable
Traceable
Organized
Exportable
Image Data
```

使用者應可以：

- 批次上傳圖片
- 取得照片 EXIF / Metadata
- 使用 Metadata 進行命名
- 找出完全相同照片
- 找出視覺相似照片
- 評估照片品質
- 取得照片推薦
- 查看 Processing Progress
- 匯出分析結果

---

# 3. Technical Baseline

Backend：

```text
ASP.NET Core
C#
```

Architecture：

```text
Clean Architecture
```

ORM：

```text
Entity Framework Core
```

Database：

```text
Microsoft SQL Server
```

EF Core Provider：

```text
Microsoft.EntityFrameworkCore.SqlServer
```

Background Processing：

```text
BackgroundService
System.Threading.Channels
```

Realtime：

```text
SignalR
```

Development Storage：

```text
Local File Storage
```

Production Storage：

```text
Google Cloud Storage
```

Container：

```text
Docker
```

Production Deployment：

```text
Google Cloud Run
```

Production Database Target：

```text
Google Cloud SQL for SQL Server
```

Frontend：

```text
Vue
```

E2E Testing：

```text
Playwright
```

---

# 4. Architecture

Solution 採用 Clean Architecture。

基本依賴：

```text
PhotoPlatform.Api
       ↓
PhotoPlatform.Application
       ↓
PhotoPlatform.Domain
```

Infrastructure：

```text
PhotoPlatform.Infrastructure
```

負責實作 Application / Domain 定義的 Infrastructure Contract。

---

# 5. Expected Solution Structure

```text
PhotoPlatform/
│
├── AGENTS.md
├── Requirements.md
│
├── docs/
│   ├── System-Level-Specification.md
│   │
│   └── modules/
│       ├── MOD-01-Upload-Spec.md
│       ├── MOD-02-Processing-Spec.md
│       ├── MOD-03-Metadata-Spec.md
│       ├── MOD-04-Naming-Spec.md
│       ├── MOD-05-Duplicate-Spec.md
│       ├── MOD-06-Similarity-Spec.md
│       ├── MOD-07-Quality-Spec.md
│       ├── MOD-08-Recommendation-Spec.md
│       ├── MOD-09-Monitoring-Spec.md
│       └── MOD-10-Export-Spec.md
│
├── tasks/
│
├── src/
│   ├── PhotoPlatform.Api/
│   ├── PhotoPlatform.Application/
│   ├── PhotoPlatform.Domain/
│   └── PhotoPlatform.Infrastructure/
│
└── tests/
    ├── PhotoPlatform.UnitTests/
    ├── PhotoPlatform.IntegrationTests/
    └── PhotoPlatform.E2ETests/
```

實際 Repository 若與此結構略有差異，Agent 應先檢查 Existing Structure。

不得只因本文件範例不同而自行大量搬移檔案。

---

# 6. System Modules

系統正式包含以下十個 Module：

| ID | Module | Responsibility |
|---|---|---|
| MOD-01 | Upload | 圖片接收、驗證、儲存、Batch / Image / Job 建立 |
| MOD-02 | Processing | Queue、Worker、Workflow、State、Retry、Recovery |
| MOD-03 | Metadata | EXIF、GPS、Reverse Geocoding、Metadata |
| MOD-04 | Naming | Template、Token、Sequence、Preview、Collision、Rename |
| MOD-05 | Duplicate | File Size + SHA-256 Exact Duplicate |
| MOD-06 | Similarity | pHash、Hamming Distance、Visual Similarity |
| MOD-07 | Quality | Resolution、Sharpness、Exposure、Metadata、Compression |
| MOD-08 | Recommendation | Version Comparison、Ranking、Recommendation |
| MOD-09 | Monitoring | Status、Progress、Processing Log、SignalR、Health |
| MOD-10 | Export | CSV Export |

Module 編號：

> 不代表 Processing Execution Order。

---

# 7. MOD-01 Upload

主要責任：

```text
Receive Files
 ↓
Validate
 ↓
Store Original File
 ↓
Create Batch
 ↓
Create Image
 ↓
Create Processing Job
 ↓
Enqueue
 ↓
202 Accepted
```

Upload 不負責：

- EXIF
- Naming
- SHA-256
- pHash
- Quality
- Recommendation

詳細規則：

> `docs/modules/MOD-01-Upload-Spec.md`

---

# 8. MOD-02 Processing

主要責任：

- ProcessingJob
- Queue
- Background Worker
- Workflow
- State
- Retry
- Startup Recovery
- Idempotency
- Controlled Concurrency

Processing Module 是：

> **Orchestrator**

而不是所有 Image Processing Business Logic 的集合。

詳細規則：

> `docs/modules/MOD-02-Processing-Spec.md`

---

# 9. MOD-03 Metadata

主要責任：

- EXIF Parsing
- GPS
- Reverse Geocoding
- Metadata Normalization
- Metadata Fallback

主要 Fallback 包含：

```text
TakenAt
→ FileCreatedAt

LocationName
→ UnknownLoc

CameraModel
→ UnknownCamera
```

詳細規則：

> `docs/modules/MOD-03-Metadata-Spec.md`

---

# 10. MOD-04 Naming

主要責任：

- Custom Text
- Template
- Token
- Sequence
- Metadata Fallback Usage
- Filename Sanitization
- Collision Handling
- Preview
- Apply / Safe Rename

Naming 不負責解析 EXIF。

它使用 Metadata Module 已產生的資料。

詳細規則：

> `docs/modules/MOD-04-Naming-Spec.md`

---

# 11. MOD-05 Duplicate

目標：

> 找出 Binary Content 完全相同的圖片。

MVP 使用：

```text
File Size
+
SHA-256
```

Duplicate Module 不負責：

> Visual Similarity。

詳細規則：

> `docs/modules/MOD-05-Duplicate-Spec.md`

---

# 12. MOD-06 Similarity

目標：

> 找出檔案內容不同，但視覺上高度相似的圖片。

MVP：

```text
pHash
+
Hamming Distance
```

主要結果：

- PerceptualHash
- Similarity Score
- VISUAL Group
- Similar Images

Similarity 不負責：

- Exact Duplicate
- Quality
- Recommendation
- Naming

詳細規則：

> `docs/modules/MOD-06-Similarity-Spec.md`

---

# 13. MOD-07 Quality

Quality Analysis 至少包含：

- Resolution
- Sharpness
- Exposure
- Metadata Completeness
- Compression

產生：

```text
QualityScore
QualityGrade
```

Quality Score 是：

> Product Scoring Model

不是 AI Confidence Probability。

詳細公式與計算：

> `docs/modules/MOD-07-Quality-Spec.md`

---

# 14. MOD-08 Recommendation

Recommendation 使用：

```text
Similarity
+
Quality
+
Metadata
```

提供：

- Ranking
- Recommendation Score
- Recommended Image
- Reasons
- Confidence

Recommendation 是：

> Decision Support

不是自動替使用者刪除照片的功能。

詳細規則：

> `docs/modules/MOD-08-Recommendation-Spec.md`

---

# 15. MOD-09 Monitoring

負責：

- Batch Progress
- Processing Status
- Processing Logs
- Job Status
- SignalR
- Health Checks

Monitoring：

> 觀察 Processing。

不負責：

> 執行 Processing。

詳細規則：

> `docs/modules/MOD-09-Monitoring-Spec.md`

---

# 16. MOD-10 Export

Export 為：

> **Read-Only Module**

主要支援：

```text
Existing Data
 ↓
Query
 ↓
Projection
 ↓
CSV
```

不得修改分析結果或 Processing State。

詳細規則：

> `docs/modules/MOD-10-Export-Spec.md`

---

# 17. Processing Architecture

Long-running Image Processing 不得阻塞 HTTP Request。

主要流程：

```text
HTTP Request
      ↓
Application
      ↓
Create ProcessingJob
      ↓
Persist to SQL Server
      ↓
System.Threading.Channels
      ↓
Background Worker
      ↓
Workflow Orchestrator
      ↓
Target Module
      ↓
Persist Result
      ↓
Monitoring
```

Channel：

> Runtime Queue

ProcessingJob：

> Persistent Job State

---

# 18. Example Workflows

Naming Workflow：

```text
Upload
 ↓
Metadata
 ↓
Naming
```

Analysis Workflow：

```text
Upload
 ↓
Metadata
 ↓
Duplicate
 ↓
Similarity
 ↓
Quality
 ↓
Recommendation
```

Full Workflow：

```text
Upload
 ↓
Metadata
 ↓
Duplicate
 ↓
Similarity
 ↓
Quality
 ↓
Recommendation
 ↓
Naming
```

Workflow 由 MOD-02 Processing 協調。

---

# 19. Database

Database Baseline：

```text
Microsoft SQL Server
```

ORM：

```text
Entity Framework Core
```

Database 保存：

- Batch
- Image
- Metadata
- ProcessingJob
- ProcessingLog
- Analysis Result
- Duplicate / Similarity Relationship
- Operational State

Database 不保存：

> Raw Image Binary Content。

Raw Image 儲存在 Storage Layer。

---

# 20. Core Data Entities

核心資料概念：

```text
Batch
Image
ProcessingJob
ProcessingLog
ImageAnalysis
DuplicateGroup
DuplicateGroupMember
```

實際：

- Column
- SQL Type
- Nullability
- FK
- Index
- Constraint

以正式 Database Schema Specification 為準。

Requirements.md 不作為 Physical Database Schema。

---

# 21. Storage

圖片存取必須透過：

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

Application 不直接依賴 GCS SDK。

---

# 22. API

API Base：

```text
/api/v1
```

主要使用：

```text
REST API
+
SignalR
```

Upload API 必須採非同步語意。

例如：

```http
POST /api/v1/images/upload
```

成功接受：

```http
202 Accepted
```

202 代表：

> Request 已接受並完成必要排程。

不代表：

> 所有圖片分析完成。

---

# 23. Error Contract

基本 Error Structure：

```json
{
  "success": false,
  "error": {
    "code": "IMAGE_NOT_FOUND",
    "message": "Image was not found.",
    "traceId": "00-abc123"
  }
}
```

不得暴露：

- StackTrace
- SQL Detail
- Internal File Path
- Connection String
- Secret
- Credential

---

# 24. Reliability

系統主要 Reliability Mechanism：

```text
Retry
Fallback
Recovery
Compensation
```

MVP Retry：

```text
1 second
 ↓
2 seconds
 ↓
4 seconds
```

Retry 適用 Temporary Failure。

Invalid / Corrupted Input 不應無限 Retry。

---

# 25. Batch Isolation

核心原則：

> **Single Image Failure Must Not Unnecessarily Fail the Entire Batch.**

例如：

```text
100 Images

98 Completed
1 Failed
1 Warning
```

Batch 仍應能回報整體結果。

---

# 26. Observability

MVP：

```text
System Log
+
Processing Log
```

Processing Operation 應能關聯：

- TraceId
- BatchId
- ImageId
- JobId
- ProcessingStep
- Duration
- Status
- ErrorCode
- RetryCount

目前 MVP：

> 不建立 Audit Log。

---

# 27. Security

MVP 至少考慮：

- Extension Validation
- MIME Validation
- File Size Validation
- Filename Sanitization
- Path Traversal Prevention
- Input Validation
- Storage Isolation
- Exception Sanitization
- Rate Limiting
- CORS
- HTTPS

不得將 Secret Commit 進 Git。

---

# 28. MVP Out of Scope

目前不實作：

- User Account
- Authentication
- Authorization
- RBAC
- Audit Log
- Multi-Tenant SaaS
- Payment
- Face Recognition
- OCR
- Object Detection
- AI Image Generation
- CLIP
- Vector Database
- Semantic Search
- Social Feature
- Online Image Editor

Agent 不得自行加入以上功能。

---

# 29. Testing Strategy

系統測試：

```text
Unit Test
Integration Test
E2E Test
```

Unit Test：

> Business Rule。

Integration Test：

> Application + Infrastructure + SQL Server / Storage。

E2E Test：

> User Flow。

Database Integration：

> 使用真正 Microsoft SQL Server Test Environment。

不得只依賴 EF Core InMemory Provider。

---

# 30. Development Workflow

整體 Development Flow：

```text
System-Level Specification
        ↓
Module-Level Specification
        ↓
Development Task
        ↓
Implementation
        ↓
Unit Test
        ↓
Integration Test
        ↓
E2E Test where required
        ↓
Review
        ↓
Commit
        ↓
Pull Request
```

---

# 31. Task Rule

每次開發應有明確 Task。

Task 應定義：

- Target Module
- Goal
- Scope
- Out of Scope
- Files / Layers Expected
- Acceptance Criteria
- Required Tests

Task：

> 不得取代 Module Specification。

Task 只描述：

> 這一次要完成什麼。

---

# 32. Specification Rule

如果 Requirements.md 與更詳細 Specification 發生差異：

> 不要自行修改。

依照 `AGENTS.md` Conflict Rule：

```text
Stop
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Approval
```

---

# 33. Development Baseline

目前正式 Baseline：

```text
ASP.NET Core
C#
Clean Architecture
Entity Framework Core
Microsoft SQL Server
BackgroundService
System.Threading.Channels
SignalR
Docker
Google Cloud Run
Google Cloud SQL for SQL Server
Google Cloud Storage
Vue
Playwright
```

---

# 34. Agent Entry Point

AI Agent 第一次進入 Repository 時：

```text
Read AGENTS.md
 ↓
Read Requirements.md
 ↓
Read System-Level Specification
 ↓
Identify Target Module
 ↓
Read Module Specification
 ↓
Read Current Task
 ↓
Inspect Existing Code
 ↓
Implement
```

Agent 不應只根據 Requirements.md 實作完整 Module。

真正 Business Detail 必須查看：

> **Target Module Specification。**

---

# 35. Project Development Principle

本專案開發原則：

> **System-Level 定義整體系統。**

> **Module-Level 定義單一模組。**

> **Task 定義本次工作。**

> **Implementation 必須服從 Specification。**

> **Agent 必須服從 AGENTS.md。**

當任何內容不確定時：

```text
Stop
 ↓
Report Conflict
 ↓
Explain Impact
 ↓
Propose Change
 ↓
Wait for Approval
```