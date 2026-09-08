# TASK-01 Project Foundation

## Target Module

MOD-01 Upload Module

## Goal

建立 Photo Processing & Image Analysis Platform 的
.NET Solution 與 Clean Architecture 專案基礎結構，
作為後續各 Module 開發基線。

## Required Reading

開始前必須依序閱讀：

1. AGENTS.md
2. Requirements.md
3. docs/System-Level-Specification.md
4. docs/modules/MOD-01-Upload-Spec.md
5. 本 Task

## Scope

建立：

PhotoPlatform.sln

src/
- PhotoPlatform.Api
- PhotoPlatform.Application
- PhotoPlatform.Domain
- PhotoPlatform.Infrastructure

tests/
- PhotoPlatform.UnitTests
- PhotoPlatform.IntegrationTests

建立必要的 Project References。

## Dependency Direction

PhotoPlatform.Api
    ↓
PhotoPlatform.Application
    ↓
PhotoPlatform.Domain

PhotoPlatform.Infrastructure
    ↓
PhotoPlatform.Application
    ↓
PhotoPlatform.Domain

Domain 不得依賴其他 Project。

## Out of Scope

本 Task 不實作：

- Upload API
- Upload Service
- Database Table
- EF Core Migration
- File Storage
- Processing Queue
- Background Worker
- Metadata
- Naming
- Duplicate
- Similarity
- Quality
- Recommendation
- Monitoring
- Export

## Package Rule

不得自行加入未經核准的第三方 NuGet Package。

## Acceptance Criteria

- Solution 建立成功
- Project Structure 符合 Clean Architecture
- Project References 正確
- Domain 保持 Infrastructure Independent
- dotnet restore 成功
- dotnet build 成功
- dotnet test 成功
- 沒有加入 Module Business Logic

## Conflict Rule

發現規格衝突或必要資訊不足時：

Stop
→ Report Conflict
→ Explain Impact
→ Propose Change
→ Wait for Approval