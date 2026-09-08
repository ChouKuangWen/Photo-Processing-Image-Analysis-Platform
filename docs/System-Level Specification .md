# System-Level Specification

# 智慧影像處理與分析平台

**文件名稱：** System-Level Specification  
**文件版本：** V2.0  
**文件類型：** System-Level Specification  
**產品名稱：** Photo Processing & Intelligent Image Analysis Platform  
**中文名稱：** 智慧影像處理與分析平台  
**後端技術：** ASP.NET Core / C#  
**架構：** Clean Architecture  
**ORM：** Entity Framework Core  
**Database：** Microsoft SQL Server  
**API：** REST API + SignalR  
**Processing：** Background Worker + System.Threading.Channels  
**Storage：** Local File Storage / Google Cloud Storage  
**Deployment：** Docker / Google Cloud Run  
**Production Database：** Google Cloud SQL for SQL Server  
**文件狀態：** Development Baseline  

---

# 1. 規格

## 1.1 文件目的

本文件定義 Photo Processing & Intelligent Image Analysis Platform 的系統層級需求，包括：

- 系統目標
- MVP Scope
- 系統架構
- 模組劃分
- 模組責任與邊界
- 模組依賴
- Processing Architecture
- Reliability
- Security
- Observability
- Infrastructure
- Database 原則
- API 原則
- Testing Strategy
- AI Agent Development Governance

本文件為：

> **System-Level Source of Truth**

開發文件依序為：

```text
System-Level Specification
        ↓
Module-Level Specification
        ↓
Development Task
        ↓
Implementation
        ↓
Testing
        ↓
Deployment
```

System-Level 負責決定整個系統如何分工。

Module-Level 負責決定單一模組如何完成自己的責任。

---

## 1.2 文件邊界

System-Level Specification 定義：

> **What the system must provide.**

Module-Level Specification 定義：

> **How each module fulfills that requirement.**

因此本文件不詳細定義：

- EXIF Parser Implementation
- SHA-256 Calculation Implementation
- pHash Algorithm Detail
- Similarity Threshold
- Quality Score Formula
- Quality Normalization Algorithm
- Naming Token Detail
- Collision Algorithm Detail
- Recommendation Ranking Formula
- EF Core Repository Implementation

以上內容由各 Module-Level Specification 定義。

---

# 1.3 Product Overview

本系統是一套以 ASP.NET Core / C# 開發的：

> **Asynchronous Image Processing & Intelligent Analysis Platform**

主要能力：

- Batch Photo Upload
- Metadata / EXIF Parsing
- GPS / Reverse Geocoding
- Background Processing
- Template-based Naming
- Exact Duplicate Detection
- Visual Similarity Detection
- Photo Quality Analysis
- Photo Recommendation
- Processing Monitoring
- CSV Export

系統核心定位：

> **File Processing + Image Analysis + Workflow Automation**

---

# 1.4 Product Goal

系統目標是將：

```text
Raw Images
```

轉換成：

```text
Structured
Analyzable
Traceable
Organized
Exportable
Image Data
```

使用者可以：

- 批次匯入照片
- 取得照片 Metadata
- 使用規則重新命名
- 找出完全相同圖片
- 找出視覺相似圖片
- 評估照片品質
- 比較同組照片
- 取得照片推薦
- 追蹤處理進度
- 匯出處理與分析結果

---

# 1.5 MVP Scope

MVP 包含：

### File Processing

- Batch Upload
- File Validation
- Batch Management
- Image Record
- File Storage

### Background Processing

- Processing Job
- Processing Queue
- Background Worker
- Workflow Orchestration
- Processing State
- Retry
- Startup Recovery
- Idempotency
- Controlled Concurrency

### Metadata

- EXIF Parsing
- TakenAt
- Camera Model
- ISO
- Aperture
- Shutter Speed
- Orientation
- GPS
- Reverse Geocoding
- Metadata Fallback

### Naming

- Custom Text
- Template
- Metadata Token
- Sequence
- Filename Sanitization
- Collision Handling
- Rename Preview
- Safe Rename

### Duplicate Detection

- File Size
- SHA-256
- Exact Duplicate Detection
- Duplicate Group

### Visual Similarity

- pHash
- Hamming Distance
- Visual Similarity
- Similarity Group

### Quality Analysis

- Resolution
- Sharpness
- Exposure
- Metadata Completeness
- Compression
- Quality Score
- Quality Grade

### Recommendation

- Version Comparison
- Recommendation Score
- Recommended Image
- Recommendation Reasons
- Recommendation Confidence

### Monitoring

- Processing Status
- Processing Logs
- Batch Progress
- TraceId
- SignalR
- Health Checks

### Export

- CSV Export
- UTF-8 BOM
- Metadata Export
- Analysis Result Export
- Processing Status Export

---

# 1.6 Out of Scope

目前 MVP 不包含：

- User Account
- Authentication
- Authorization
- RBAC
- Audit Log
- Multi-Tenant SaaS
- Payment
- AI Image Generation
- Face Recognition
- OCR
- Object Detection
- Semantic Image Search
- CLIP
- Vector Database
- Social Features
- Online Image Editor

系統不得因未來可能需要而提前增加 MVP Scope。

---

# 1.7 System Modules

系統正式劃分為 **10 個 Module**。

| 編號 | Module | 主要責任 |
|---|---|---|
| MOD-01 | Upload Module | 接收、驗證、保存圖片並建立 Batch / Image / Job |
| MOD-02 | Processing Module | Queue、Worker、Workflow、State、Retry、Recovery |
| MOD-03 | Metadata Module | EXIF、GPS、Reverse Geocoding、Metadata |
| MOD-04 | Naming Module | Template、Token、Sequence、Preview、Collision、Rename |
| MOD-05 | Duplicate Module | File Size + SHA-256 Exact Duplicate Detection |
| MOD-06 | Similarity Module | pHash、Hamming Distance、Visual Similarity |
| MOD-07 | Quality Module | Resolution、Sharpness、Exposure、Metadata、Compression |
| MOD-08 | Recommendation Module | Version Comparison、Ranking、Recommendation |
| MOD-09 | Monitoring Module | Status、Progress、Processing Logs、SignalR、Health |
| MOD-10 | Export Module | CSV Export |

以下不建立為獨立 Business Module：

- File Storage
- Database
- SignalR
- Logging
- Cloud Infrastructure

以上屬於：

> **Infrastructure / Cross-Cutting Capability**

---

# 1.8 Module Boundary

## 1.8.1 MOD-01 Upload Module

負責：

```text
Receive Files
     ↓
Validate
     ↓
Persist Original Files
     ↓
Create Batch
     ↓
Create Image Records
     ↓
Create Processing Jobs
     ↓
Enqueue
```

不負責：

- EXIF Parsing
- Naming
- SHA-256
- Duplicate Detection
- pHash
- Similarity
- Quality
- Recommendation

---

## 1.8.2 MOD-02 Processing Module

負責：

- Processing Job
- Queue
- Background Worker
- Workflow Selection
- Workflow Orchestration
- Processing State
- Retry
- Startup Recovery
- Idempotency
- Controlled Concurrency
- Processing Step Coordination

Processing Module 負責：

> **協調處理**

而不是：

> **實作所有圖片分析演算法**

因此不直接負責：

- EXIF Algorithm
- Naming Algorithm
- Duplicate Algorithm
- Similarity Algorithm
- Quality Algorithm
- Recommendation Algorithm

---

## 1.8.3 MOD-03 Metadata Module

負責：

- EXIF Parsing
- Metadata Normalization
- GPS Extraction
- GPS Validation
- Reverse Geocoding
- Metadata Fallback
- Metadata Persistence

不負責：

- Naming
- Duplicate
- Similarity
- Quality
- Recommendation

---

## 1.8.4 MOD-04 Naming Module

負責：

- Custom Text Naming
- Template Naming
- Token Resolution
- Sequence
- Metadata Fallback Usage
- Filename Sanitization
- Collision Detection
- Collision Strategy
- Rename Preview
- Safe Rename

Naming Module 可以使用 Metadata Module 已產生的 Metadata。

Naming Module 不負責：

- EXIF Parsing
- Duplicate Detection
- pHash
- Quality Analysis
- Recommendation

---

## 1.8.5 MOD-05 Duplicate Module

負責：

> **Binary Content Completely Identical**

MVP 主要依據：

```text
File Size
+
SHA-256
```

Duplicate Module 負責：

- Candidate Filtering
- SHA-256
- Exact Duplicate Detection
- Exact Duplicate Group

不負責：

- Visual Similarity
- Quality
- Recommendation

---

## 1.8.6 MOD-06 Similarity Module

負責：

> **Visual Similarity**

MVP 使用：

```text
pHash
+
Hamming Distance
```

Similarity Module 負責：

- pHash Generation
- Hamming Distance
- Visual Similarity
- Similarity Group

不負責：

- Exact Duplicate 判定
- Quality Score
- Recommendation Ranking

---

## 1.8.7 MOD-07 Quality Module

負責：

- Resolution Analysis
- Sharpness Analysis
- Exposure Analysis
- Metadata Completeness
- Compression Analysis
- Quality Score
- Quality Grade

Quality Module 不負責：

- Similarity Group
- Recommendation Ranking
- Naming

---

## 1.8.8 MOD-08 Recommendation Module

Recommendation 使用：

```text
Similarity Result
+
Quality Result
+
Metadata
```

產生：

- Recommendation Score
- Ranking
- Recommended Image
- Reasons
- Confidence

Recommendation 不重新計算：

- pHash
- Hamming Distance
- Quality Score
- EXIF

Recommendation 屬於：

> **Decision Support**

不是：

> **Automatic Final Decision**

---

## 1.8.9 MOD-09 Monitoring Module

負責：

- Batch Progress
- Job Status
- Processing Logs
- SignalR Events
- System Health

Monitoring 不負責：

- 執行 Processing Job
- 分析圖片
- Naming
- Recommendation

---

## 1.8.10 MOD-10 Export Module

負責：

```text
Existing Data
     ↓
Query
     ↓
Projection
     ↓
CSV Generation
     ↓
HTTP Response
```

Export 為：

> **Read-Only Module**

不得修改：

- Image
- Metadata
- Analysis Result
- Processing State
- Duplicate Result
- Similarity Result
- Recommendation Result
- Naming Result

---

# 1.9 Clean Architecture

系統採：

> **Clean Architecture**

```text
┌─────────────────────────────┐
│          Vue Client         │
└──────────────┬──────────────┘
               │
        REST API / SignalR
               │
               ▼
┌─────────────────────────────┐
│       ASP.NET Core API      │
│          API Layer          │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│      Application Layer      │
│ Use Cases / DTO / Contracts │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│         Domain Layer        │
│ Entities / Rules / Policies │
└─────────────────────────────┘
               ▲
               │
┌──────────────┴──────────────┐
│     Infrastructure Layer    │
│ SQL / Storage / Image / Geo │
└─────────────────────────────┘
```

主要 Dependency Rule：

```text
API
 ↓
Application
 ↓
Domain
```

Infrastructure 實作 Application / Domain 所定義的 Contract。

Domain 不得直接依賴：

- ASP.NET Core
- EF Core
- SQL Server
- Google Cloud SDK
- File System Implementation
- External Geo API
- Image Processing Library

---

# 1.10 Processing Architecture

系統採：

> **Configurable Asynchronous Processing Workflow**

長時間影像分析不得阻塞 HTTP Request。

```text
HTTP Request
      ↓
Application Use Case
      ↓
Persist Processing Job
      ↓
Bounded Channel
      ↓
Background Worker
      ↓
Workflow Orchestrator
      ↓
Target Module
      ↓
Persist Result
      ↓
Monitoring Event
```

---

# 1.11 Queue Architecture

MVP 使用：

```text
System.Threading.Channels
```

Channel 為：

> **Runtime In-Memory Queue**

SQL Server 中的 ProcessingJob 為：

> **Persistent Job State**

架構：

```text
ProcessingJob
      ↓
Bounded Channel
      ↓
Background Worker
```

Queue 必須：

- Bounded
- Asynchronous
- Support Backpressure
- Controlled Concurrency
- Prevent Unlimited Memory Growth

Channel 不作為永久 Job Storage。

---

# 1.12 Processing State

基本狀態：

```text
Pending
   ↓
Processing
   ↓
Completed
```

失敗：

```text
Processing
   ↓
Failed
```

Retry：

```text
Failed
   ↓
Pending
```

詳細 State Transition 由 MOD-02 Processing Module 定義。

---

# 1.13 System Processing Flow

完整圖片分析流程可以表示為：

```text
Upload
  ↓
Processing
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

注意：

> **Module 編號不代表 Processing 執行順序。**

因此 MOD-04 為 Naming Module，不代表 Naming 必須在 Duplicate、Similarity、Quality 之前執行。

Processing Module 可以依 Workflow 選擇不同 Steps。

例如：

### Naming Workflow

```text
Upload
 ↓
Metadata
 ↓
Naming
```

### Analysis Workflow

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

### Full Workflow

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

Monitoring 橫跨整個 Processing Lifecycle。

Export 不屬於 Processing Pipeline，而是讀取既有資料。

---

# 1.14 Module Dependency

主要依賴概念：

```text
Upload
  ↓
Processing
```

Processing 協調：

```text
Metadata
Naming
Duplicate
Similarity
Quality
Recommendation
```

Metadata Result 可提供：

```text
Naming
Quality
Recommendation
Export
```

Duplicate 依賴：

```text
Image
+
File Storage
```

Similarity 依賴：

```text
Image
+
File Storage
```

Quality 依賴：

```text
Image
+
Metadata
```

Recommendation 主要依賴：

```text
Similarity
+
Quality
```

Monitoring 依賴：

```text
Processing State
+
Processing Log
+
Application Event
```

Export 只讀取既有：

```text
Image
Metadata
Analysis
Naming
Duplicate
Similarity
Quality
Recommendation
Processing Status
```

---

# 1.15 Metadata System Rule

Metadata 缺失在非必要情況下，不應造成整張 Image Processing Failed。

基本 Fallback：

```text
TakenAt Missing
      ↓
File CreatedAt
```

```text
Location Missing
      ↓
UnknownLoc
```

```text
Camera Model Missing
      ↓
UnknownCamera
```

詳細規則由 MOD-03 Metadata Module 定義。

---

# 1.16 Naming System Rule

Naming 必須提供：

- Custom Text
- Template
- Metadata Tokens
- Sequence
- Metadata Fallback
- Filename Sanitization
- Collision Handling
- Rename Preview
- Safe Rename

系統不得：

- 覆蓋既有檔案
- 允許 Path Traversal
- 產生非法檔名
- 操作 Storage Boundary 外的檔案

具體：

- Token Definition
- Token Syntax
- Sequence Format
- Collision Strategy

由 MOD-04 Naming Module 定義。

---

# 1.17 Exact Duplicate System Rule

Exact Duplicate 代表：

> **Binary Content Completely Identical**

MVP 採：

```text
File Size
+
SHA-256
```

File Size 可以作為 Candidate Filtering。

最終判定必須使用 Content Hash。

具體 Hash Calculation、Grouping 與 Persistence 由 MOD-05 Duplicate Module 定義。

---

# 1.18 Visual Similarity System Rule

Visual Similarity 與 Exact Duplicate 為不同概念。

MVP 採：

```text
pHash
+
Hamming Distance
```

具體：

- Similarity Threshold
- Score Normalization
- Grouping Strategy

由 MOD-06 Similarity Module 定義。

---

# 1.19 Quality System Rule

Quality Analysis 至少評估：

- Resolution
- Sharpness
- Exposure
- Metadata Completeness
- Compression

產生：

```text
Quality Score
+
Quality Grade
```

Quality Score 是：

> **Product Scoring Model**

不是：

> **AI Confidence Probability**

具體 Formula 與 Normalization Algorithm 由 MOD-07 Quality Module 定義。

---

# 1.20 Recommendation System Rule

Recommendation 使用既有分析結果進行照片比較。

輸出可包含：

- RecommendedImageId
- RecommendationScore
- Reasons
- Confidence

Recommendation 不重新計算：

- Metadata
- pHash
- Similarity
- Quality Score

具體 Ranking Formula、Tie-Break Rule、Confidence Rule 由 MOD-08 Recommendation Module 定義。

---

# 1.21 File Storage

Application 必須透過：

```text
IFileStorageService
```

存取圖片。

Development：

```text
Local File Storage
```

Production：

```text
Google Cloud Storage
```

Application 不得直接依賴 Google Cloud Storage SDK。

SQL Server 不保存：

> **Raw Image Binary Content**

Database 只保存 Storage Reference。

---

# 1.22 Reliability

系統可靠性主要由：

```text
Retry
Fallback
Recovery
Compensation
```

組成。

Retry 適用：

- Timeout
- HTTP 408
- HTTP 429
- HTTP 5xx
- Temporary Network Failure
- Temporary Storage Failure

MVP Retry：

```text
1 second
 ↓
2 seconds
 ↓
4 seconds
```

Fallback 適用：

- Missing EXIF
- Missing GPS
- Reverse Geocoding Failure

Recovery：

```text
Application Startup
       ↓
Read Persisted Jobs
       ↓
Find Recoverable Jobs
       ↓
Restore Required State
       ↓
Requeue
```

File Rename / Move 無法依賴 Database Transaction 完整 Rollback，因此需要 Compensation Strategy。

---

# 1.23 Idempotency

重要 Processing Step 必須具有 Idempotent Characteristics。

系統必須避免：

- Duplicate Processing
- Duplicate Analysis
- Duplicate Group Creation
- Duplicate Rename

至少應能識別：

```text
ImageId
+
ProcessingStep
+
Rule / Analysis Version when required
```

具體 Persistence Strategy 由相關 Module 定義。

---

# 1.24 Concurrency

系統必須考慮：

- Concurrent Workers
- Same Image Concurrent Processing
- Database Lost Update
- Filename Collision
- Duplicate Group Collision
- Storage Race Condition

系統不得假設：

> 同一 Image 永遠只會由一個 Worker 操作。

重要唯一性應使用：

- SQL Server Unique Constraint
- Unique Index
- Transaction
- Concurrency Control
- Idempotency Rule

不得只依賴 Application Memory Lock。

---

# 1.25 Error Classification

### Validation Error

例如：

- Invalid File
- Unsupported Format
- Invalid Request

通常不 Retry。

### Transient Error

例如：

- Timeout
- HTTP 429
- Temporary Storage Failure
- External API 5xx

可以 Retry。

### Recoverable Business Condition

例如：

- Missing EXIF
- Missing GPS

使用 Fallback。

### Fatal Processing Error

例如：

- Corrupted Image
- Unreadable Image

Image 可以進入 Failed。

---

# 1.26 Batch Isolation

核心原則：

> **Single Image Failure Must Not Unnecessarily Fail the Entire Batch.**

例如：

```text
100 Images

98 Completed
1 Failed
1 Warning
```

Batch 仍可以完成，並提供：

- Total
- Success
- Failed
- Warning

等資訊。

---

# 1.27 Logging & Observability

MVP Logging 分為：

### System Log

負責：

- Runtime
- Exception
- Infrastructure Failure
- Debugging
- Trace Correlation

### Processing Log

負責記錄 Image Processing Lifecycle。

至少可以關聯：

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

> **不建立 Audit Log。**

Audit Log 待未來 User / Authentication / Authorization 導入後再評估。

---

# 1.28 Monitoring & SignalR

Realtime Monitoring 使用：

```text
SignalR
```

主要事件：

- ProcessingStarted
- ProcessingCompleted
- ProcessingFailed
- BatchProgressUpdated
- BatchCompleted

架構：

```text
Background Worker
       ↓
Application Event
       ↓
Monitoring Module
       ↓
SignalR Hub
       ↓
Vue Client
```

SignalR 不得成為唯一 State Source。

Client 必須能透過 REST API 重新同步狀態。

---

# 1.29 Security

MVP 至少提供：

- File Extension Validation
- MIME Validation
- File Size Limit
- Image Decode Validation where necessary
- Filename Sanitization
- Path Traversal Prevention
- Storage Isolation
- Input Validation
- Exception Sanitization
- Rate Limiting
- CORS
- HTTPS
- Security Headers

不得在 Log / API Response 洩漏：

- Password
- API Key
- Secret
- Connection String
- SQL Detail
- Stack Trace
- Internal File Path
- Cloud Credential

---

# 1.30 Authentication Boundary

目前 MVP：

> **不實作 User Account System。**

因此目前不建立：

- User
- Role
- Permission
- Resource Ownership
- Audit Log

未來若導入 Authentication / Authorization，必須另行更新 System-Level Specification。

---

# 1.31 Privacy

Photo Metadata 可能包含：

- GPS
- Timestamp
- Camera Information
- Device Metadata

UI 應提醒使用者：

> Photo metadata may contain location and device information.

EXIF Removal、GPS Removal 等 Privacy Feature 不屬於目前 MVP。

---

# 1.32 Performance

Upload API 流程：

```text
Receive
 ↓
Validate
 ↓
Persist Required Data
 ↓
Create Processing Job
 ↓
Enqueue
 ↓
202 Accepted
```

不得等待完整 Image Analysis。

Worker 必須：

- Async
- Controlled Concurrency
- Bounded Queue
- Avoid Large Batch Full-Memory Loading
- Stream Large Data where appropriate

---

# 1.33 Scalability

MVP：

```text
1 API
1 Worker
1 SQL Server
1 Storage Provider
```

未來可以擴充：

```text
Load Balancer
      ↓
Multiple APIs
      ↓
External Message Broker
      ↓
Multiple Workers
```

MVP Queue：

```text
System.Threading.Channels
```

Future 可評估：

- Google Cloud Pub/Sub
- RabbitMQ
- Kafka

不納入目前 MVP。

---

# 1.34 Development Infrastructure

Development Baseline：

```text
Windows / VS Code
       ↓
.NET SDK
       ↓
ASP.NET Core
       ↓
Entity Framework Core
       ↓
Microsoft SQL Server
       ↓
Local File Storage
```

SQL Server 開發環境可透過 Docker 執行。

---

# 1.35 Production Infrastructure

Production：

```text
Internet
   ↓
Google Cloud Run
   ↓
ASP.NET Core
   ├── Google Cloud SQL for SQL Server
   ├── Google Cloud Storage
   └── External Geo Service
```

Application 以 Docker Container 部署。

---

# 1.36 Database Technology Decision

本專案正式採：

> **Microsoft SQL Server**

ORM：

> **Entity Framework Core**

Provider：

```text
Microsoft.EntityFrameworkCore.SqlServer
```

Development：

```text
Microsoft SQL Server
```

Production：

```text
Google Cloud SQL for SQL Server
```

所有：

- Module Database Specification
- EF Core Entity Mapping
- Migration
- Index
- Constraint
- Integration Test

皆以 Microsoft SQL Server 為 Baseline。

不得自行替換：

- MySQL
- PostgreSQL
- SQLite

如需更換 Database Provider，必須提出 Architecture Change。

---

# 1.37 Configuration Management

不得 Commit：

- SQL Server Password
- Connection String
- API Key
- GCP Credential
- Secret

Development 可以使用：

- appsettings.json
- appsettings.Development.json
- Environment Variables
- .NET User Secrets

Production 使用安全的 Secret Management Mechanism。

---

# 1.38 CI/CD

最低 CI：

```text
GitHub Pull Request
        ↓
dotnet restore
        ↓
dotnet build
        ↓
dotnet test
```

Production：

```text
GitHub
 ↓
CI
 ↓
Test
 ↓
Docker Build
 ↓
Container Registry
 ↓
Google Cloud Run
```

---

# 1.39 Architectural Constraints

### SYS-ARCH-001

系統必須遵循 Clean Architecture。

### SYS-ARCH-002

Domain 不得依賴 Infrastructure。

### SYS-ARCH-003

Long-running Image Processing 不得阻塞 HTTP Request。

### SYS-ARCH-004

MVP Queue 使用 System.Threading.Channels。

### SYS-ARCH-005

File Storage 必須透過 Storage Abstraction。

### SYS-ARCH-006

Database Access 使用 EF Core / Infrastructure Layer。

### SYS-ARCH-007

Database Baseline 為 Microsoft SQL Server。

### SYS-ARCH-008

非致命錯誤不得不必要地造成整個 Batch Failed。

### SYS-ARCH-009

File Operation 不得假設可以透過 SQL Transaction Rollback。

### SYS-ARCH-010

Module 不得跨越既定 Responsibility。

### SYS-ARCH-011

Module Number 不代表 Processing Execution Order。

### SYS-ARCH-012

AI Agent 不得自行修改 System-Level Architecture Contract。

---

# 1.40 AI Agent Development Governance

AI Agent 是：

> **Development Collaborator**

不是：

> **Architecture Owner**

Agent 開始實作前依序閱讀：

```text
AGENTS.md
      ↓
System-Level Specification
      ↓
Target Module Specification
      ↓
Related Module Contract
      ↓
Database / API Specification
      ↓
Development Task
      ↓
Existing Implementation
```

Agent 必須：

1. 不得自行修改 System Architecture。
2. 不得自行修改 Module Boundary。
3. 不得自行修改 Database Schema。
4. 不得自行修改 API Contract。
5. 不得自行修改 Domain Rule。
6. 不得跨 Module Responsibility。
7. 不得自行增加 MVP Scope。
8. 不得自行更換 SQL Server。
9. 不得自行新增第三方 Package。
10. 修改 Cross-Module Contract 前必須進行 Impact Analysis。

如果 Requirement、Specification、Database、API 或 Implementation 發生衝突：

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

Agent 不得自行選擇或猜測。

---

# 1.41 Source of Truth Hierarchy

```text
1. AGENTS.md
   Agent 行為規則

2. System-Level Specification
   系統架構與跨模組契約

3. Approved ADR
   Architecture Decision

4. Module-Level Specification
   Module Rules

5. Database / API Specification
   Physical / External Contract

6. Development Task
   Implementation Scope

7. Existing Implementation
```

若上下層文件發生衝突：

> **不得自行修改或選擇。**

必須依 Conflict Handling Rule 處理。

---

# 2. 資料表

## 2.1 Database Overview

Database：

> **Microsoft SQL Server**

ORM：

> **Entity Framework Core**

Provider：

```text
Microsoft.EntityFrameworkCore.SqlServer
```

System-Level 只定義：

- Core Entity
- Entity Relationship
- Database Responsibility
- Constraint Principle

具體：

- Column
- SQL Type
- Length
- Nullability
- Default
- PK
- FK
- Index
- Unique Constraint
- EF Core Mapping

由 Database Schema / Module-Level Specification 定義。

---

# 2.2 Core Data Entities

MVP 核心資料概念包含：

- Batch
- Image
- ProcessingJob
- ProcessingLog
- ImageAnalysis
- DuplicateGroup
- DuplicateGroupMember

其他 Module-specific Persistence 必須以已核准的 Database Schema 為準。

Agent 不得自行新增 Table。

---

# 2.3 Entity Relationship

概念關係：

```text
Batch
 │
 └── Images
       │
       ├── Metadata
       ├── ImageAnalysis
       ├── ProcessingJobs
       ├── ProcessingLogs
       └── Group Membership
```

Group 可支援：

- Exact Duplicate
- Visual Similarity
- Recommendation / Version Comparison

實際 Physical Schema 以 Database Schema Specification 為準。

---

# 2.4 Database Responsibilities

SQL Server 保存：

- Batch State
- Image Record
- Storage Reference
- Metadata
- Analysis Result
- Processing Job State
- Processing History
- Duplicate / Similarity Relationship
- Naming Result where required
- Recommendation Result where required
- Operational State

SQL Server 不保存：

> **Raw Image Binary Content**

圖片 Binary Content 由 Storage Layer 管理。

---

# 2.5 Database Constraint Principles

重要資料唯一性不得只由 Application Code 判斷。

應依需求使用：

- Primary Key
- Foreign Key
- Unique Constraint
- Unique Index
- Check Constraint
- Transaction
- Concurrency Mechanism

Database Schema Section 只描述 Physical Data Structure。

不得把：

- Workflow
- State Transition
- Retry Logic
- Business Processing Flow

塞入 Database Schema。

---

# 2.6 Database Boundary

Domain / Application 不得直接依賴：

```text
SqlConnection
Raw SQL Server Infrastructure
SQL Server SDK
```

Infrastructure 負責：

```text
EF Core
DbContext
SQL Server Provider
Persistence Implementation
```

---

# 2.7 Migration

Schema Change 流程：

```text
Requirement
 ↓
Approved Specification Change
 ↓
EF Core Migration
 ↓
Integration Test
```

Agent 不得因 Implementation Error 直接修改 Database Schema。

---

# 3. API

## 3.1 API Architecture

API Base：

```text
/api/v1
```

系統使用：

```text
REST API
+
SignalR
```

REST API：

- Command
- Query
- Status
- Result
- Export

SignalR：

- Progress
- Processing Event
- Completion Notification
- Failure Notification

SignalR 不取代 REST API。

---

# 3.2 Module API Responsibility

| Module | API Responsibility |
|---|---|
| MOD-01 Upload | Upload Image / Batch |
| MOD-02 Processing | Processing Job / Status / Retry |
| MOD-03 Metadata | Metadata Query |
| MOD-04 Naming | Rename Preview / Apply |
| MOD-05 Duplicate | Exact Duplicate Query |
| MOD-06 Similarity | Visual Similarity Query |
| MOD-07 Quality | Quality Result Query |
| MOD-08 Recommendation | Recommendation Query |
| MOD-09 Monitoring | Batch / Job / Log / SignalR / Health |
| MOD-10 Export | CSV Export |

具體 Endpoint、Request、Response、Error Code 由 Module Specification 定義。

---

# 3.3 Asynchronous API Semantics

Upload：

```http
POST /api/v1/images/upload
```

成功接受：

```http
202 Accepted
```

代表：

> Request 已接受，必要 Processing Job 已建立或排程。

不代表：

> 所有 Image Processing 已完成。

後續狀態透過：

- REST Status API
- Monitoring API
- SignalR

取得。

---

# 3.4 Common Error Contract

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

Error Response 應包含：

- Stable Error Code
- Safe Message
- TraceId

不得包含：

- Stack Trace
- SQL Detail
- Connection String
- Internal File Path
- Secret

---

# 3.5 HTTP Status

| HTTP | 意義 |
|---|---|
| 200 | Success |
| 202 | Accepted for asynchronous processing |
| 400 | Invalid request |
| 404 | Resource not found |
| 409 | Business / resource state conflict |
| 500 | Unexpected internal error |
| 503 | Temporary dependency unavailable |

---

# 3.6 API Versioning

目前使用：

```text
/api/v1
```

Backward-compatible Change 原則上維持 v1。

Breaking Change 必須評估新的 API Version。

Agent 不得自行修改 API Version。

---

# 3.7 Upload Security

Upload 至少執行：

```text
Extension Validation
+
MIME Validation
+
File Size Validation
+
Image Validation where required
```

不得只依賴 Filename Extension。

---

# 3.8 Monitoring API Principle

SignalR Event 可能：

- 遺失
- 延遲
- Out-of-order
- Client Disconnect

因此 Client 必須可以透過 REST API 重新取得目前狀態。

> **SignalR 是 Realtime Notification。**

> **Persistent State 才是可重新查詢的 State Source。**

---

# 4. 測試

## 4.1 Testing Strategy

系統測試分為：

```text
Unit Test
Integration Test
E2E Test
```

---

# 4.2 Unit Test

各 Module 至少涵蓋：

### MOD-01 Upload

- File Validation
- Upload Service

### MOD-02 Processing

- State Transition
- Retry
- Workflow
- Queue
- Idempotency
- Recovery

### MOD-03 Metadata

- EXIF
- GPS
- Fallback
- Normalization

### MOD-04 Naming

- Template
- Token
- Sequence
- Sanitization
- Collision
- Preview

### MOD-05 Duplicate

- SHA-256
- Exact Duplicate
- Grouping
- Idempotency

### MOD-06 Similarity

- pHash
- Hamming Distance
- Similarity Rule
- Grouping

### MOD-07 Quality

- Resolution
- Sharpness
- Exposure
- Metadata Completeness
- Compression
- Quality Score

### MOD-08 Recommendation

- Ranking
- Tie-Break
- Reasons
- Confidence
- Idempotency

### MOD-09 Monitoring

- Progress Calculation
- Status Query
- Event Publishing

### MOD-10 Export

- Projection
- CSV
- UTF-8 BOM
- Empty Result

---

# 4.3 Integration Test

至少驗證：

```text
API
 ↓
Application
 ↓
EF Core
 ↓
SQL Server
```

以及：

```text
ProcessingJob
 ↓
Channel
 ↓
Worker
 ↓
Target Module
 ↓
SQL Server / Storage
```

Integration Test 應使用：

> **Real SQL Server Test Environment**

避免只使用 EF Core InMemory Provider 取代真正的 Database Integration Test。

---

# 4.4 Database Integration Test

至少驗證：

- Primary Key
- Foreign Key
- Unique Constraint
- Transaction
- Concurrency
- Idempotency
- EF Core Migration
- SQL Server Provider Behavior

---

# 4.5 E2E Test

### E2E-01 — MOD-01 Upload

```text
Upload Multiple Images
        ↓
Create Batch
        ↓
Create Images
        ↓
Create Jobs
        ↓
202 Accepted
```

### E2E-02 — MOD-02 Processing

```text
Pending
 ↓
Worker
 ↓
Processing
 ↓
Completed / Failed
```

### E2E-03 — MOD-03 Metadata

```text
Image
 ↓
EXIF / GPS
 ↓
Persist
 ↓
Query
```

### E2E-04 — MOD-04 Naming

```text
Images
 ↓
Preview
 ↓
Apply
 ↓
Safe Rename
```

### E2E-05 — MOD-05 Duplicate

```text
Exact Duplicate Images
        ↓
SHA-256
        ↓
Duplicate Detection
        ↓
Duplicate Result
```

### E2E-06 — MOD-06 Similarity

```text
Similar Images
      ↓
pHash
      ↓
Hamming Distance
      ↓
Similarity Result
```

### E2E-07 — MOD-07 Quality

```text
Image
 ↓
Quality Analysis
 ↓
Quality Result
```

### E2E-08 — MOD-08 Recommendation

```text
Similarity Result
        +
Quality Result
        ↓
Recommendation
```

### E2E-09 — MOD-09 Monitoring

```text
Processing
 ↓
Progress
 ↓
Processing Log
 ↓
REST / SignalR
```

### E2E-10 — MOD-10 Export

```text
Existing Data
 ↓
Export Request
 ↓
CSV
```

---

# 4.6 Reliability Test

至少包含：

- Worker Restart
- Startup Recovery
- Retryable Failure
- Non-Retryable Failure
- Geo Timeout
- HTTP 429
- HTTP 5xx
- SQL Server Failure
- Storage Failure
- Same Image Concurrent Processing
- Duplicate Group Collision
- Filename Collision

---

# 4.7 Edge Cases

至少包含：

- Empty File
- Invalid File
- Unsupported Format
- Corrupted Image
- Missing EXIF
- Missing GPS
- Exact Duplicate
- Similar Image
- Low Quality Image
- Large Image
- Large Batch
- Filename Collision
- External Service Timeout
- Worker Restart
- SQL Server Failure
- Storage Failure
- Empty Export Result

---

# 4.8 System Acceptance Criteria

### SYS-AC-001
系統可以接受多張圖片 Batch Upload。

### SYS-AC-002
Upload API 不等待完整 Image Processing。

### SYS-AC-003
系統可以建立並排程 Processing Job。

### SYS-AC-004
Worker 可以依 Workflow 執行 Processing Step。

### SYS-AC-005
Worker Restart 後可以恢復符合條件的 Processing Job。

### SYS-AC-006
系統可以解析並保存 Metadata。

### SYS-AC-007
系統可以依 Template 產生 Naming Preview。

### SYS-AC-008
系統可以安全執行 Rename。

### SYS-AC-009
系統可以偵測 Exact Duplicate。

### SYS-AC-010
系統可以執行 Visual Similarity Analysis。

### SYS-AC-011
系統可以產生 Quality Result。

### SYS-AC-012
系統可以提供 Recommendation。

### SYS-AC-013
單一 Image Failure 不應不必要地造成整個 Batch Failed。

### SYS-AC-014
系統可以查詢 Batch / Job Status。

### SYS-AC-015
系統可以保存與查詢 Processing Logs。

### SYS-AC-016
系統可以透過 SignalR 回報 Processing Progress。

### SYS-AC-017
系統可以輸出 CSV。

### SYS-AC-018
系統使用 Microsoft SQL Server 保存系統資料。

### SYS-AC-019
系統可以透過 Docker 執行。

### SYS-AC-020
Production Architecture 支援：

```text
Google Cloud Run
+
Google Cloud SQL for SQL Server
+
Google Cloud Storage
```

### SYS-AC-021
Implementation 不得破壞 Module Boundary。

### SYS-AC-022
AI Agent 不得自行修改 System / Database / API Contract。

---

# 4.9 System-Level Definition of Done

System-Level Specification 完成條件：

- [x] Product Goal defined
- [x] MVP Scope defined
- [x] Out-of-Scope defined
- [x] 10 Modules defined
- [x] MOD-01 Upload defined
- [x] MOD-02 Processing defined
- [x] MOD-03 Metadata defined
- [x] MOD-04 Naming defined
- [x] MOD-05 Duplicate defined
- [x] MOD-06 Similarity defined
- [x] MOD-07 Quality defined
- [x] MOD-08 Recommendation defined
- [x] MOD-09 Monitoring defined
- [x] MOD-10 Export defined
- [x] Module Boundary defined
- [x] Clean Architecture defined
- [x] Processing Architecture defined
- [x] Queue Architecture defined
- [x] Reliability defined
- [x] Idempotency defined
- [x] Concurrency defined
- [x] Storage Architecture defined
- [x] Microsoft SQL Server selected
- [x] Entity Framework Core selected
- [x] API Architecture defined
- [x] Logging defined
- [x] Monitoring defined
- [x] Security defined
- [x] Infrastructure defined
- [x] CI/CD defined
- [x] Testing Strategy defined
- [x] Acceptance Criteria defined
- [x] AI Agent Governance defined

---

# System-Level Conclusion

本系統由十個正式 Module 組成：

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

其中：

> **Module Number 代表文件與系統功能的正式識別編號，不代表 Processing Execution Order。**

系統主要技術基線：

```text
ASP.NET Core
+
C#
+
Clean Architecture
+
Entity Framework Core
+
Microsoft SQL Server
+
BackgroundService
+
System.Threading.Channels
+
SignalR
+
Docker
+
Google Cloud Run
+
Google Cloud SQL for SQL Server
+
Google Cloud Storage
```

System-Level Specification 負責：

```text
System Goal
Architecture
Module Boundary
Cross-Module Contract
Database Principle
API Principle
Reliability
Security
Infrastructure
Agent Governance
```

Module-Level Specification 負責：

```text
Module Responsibility
Internal Processing Flow
Business Rules
Interfaces
Database Schema Detail
API Contract
Error Handling
Tests
Acceptance Criteria
```

Development Task 負責：

> **定義這一次 Agent 要完成的具體工作。**

AI Agent 必須在已核准的 System-Level 與 Module-Level Boundary 內實作。

發生規格衝突時：

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

不得自行修改 Architecture Contract。