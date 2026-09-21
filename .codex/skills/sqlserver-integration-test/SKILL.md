# SQL Server Integration Test Skill

## 1. Purpose

本 Skill 用於準備、檢查並執行 Photo Processing & Intelligent Image Analysis Platform 的真實 SQL Server Integration Tests。

主要責任：

- 檢查 Docker 是否可用
- 尋找並優先重用既有 SQL Server Container
- 必要時啟動既有 Container
- 檢查 SQL Server 是否 Ready
- 檢查 Integration Test Connection String
- 執行指定 Integration Tests
- 執行完整 Integration Tests
- 必要時執行完整 Test Suite
- 區分 Environment Failure 與 Implementation Failure
- 回報驗證結果

本 Skill 屬於「測試環境與驗證」職責。

不得藉由修改業務程式碼、測試語意或資料庫架構來讓測試通過。

---

## 2. Scope

本 Skill 可以：

- 執行 Docker CLI 查詢
- 啟動既有 SQL Server Container
- 檢查 Container 狀態
- 檢查 Port Mapping
- 查看 SQL Server 啟動 Logs
- 使用既有 Environment Variable
- 執行 `dotnet build`
- 執行指定 Integration Tests
- 執行完整 Integration Tests
- 執行完整 `dotnet test`
- 執行 `git diff --check`
- 回報環境或測試錯誤

本 Skill 不負責：

- 修改 Domain
- 修改 Application 業務邏輯
- 修改 Infrastructure 業務邏輯
- 修改 Controller / API
- 修改 Schema
- 修改 Migration
- 修改既有測試語意
- 使用 EF InMemory 取代 SQL Server
- 建立 Retry / Outbox / Compensation
- 修改 Production Connection String
- 修改 appsettings
- 修改 TASK / Requirements / Specification
- 自動 commit
- 自動 push

若測試發現 Implementation 問題：

```text
STOP
→ Report Failure
→ Return control to task-implementation
```

不得在本 Skill 中自行擴張修正範圍。

---

## 3. Project SQL Server Test Baseline

目前專案 Integration Tests 使用真實 SQL Server。

核准的 SQL Server Image：

```text
mcr.microsoft.com/mssql/server:2022-latest
```

SQL Server Container Port：

```text
1433
```

預設 SQL Login：

```text
sa
```

Integration Test Environment Variable：

```text
PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING
```

Integration Tests 應先連線至：

```text
master
```

測試本身負責建立隔離 Database，例如：

```text
PhotoPlatform_Task06_{Guid:N}
```

測試完成後由測試程式自行刪除 Database。

因此不得要求使用者預先建立固定的 Integration Test Database。

---

## 4. Important Rule — Prefer Existing Environment

開始任何操作前，先檢查目前 Docker 環境。

優先順序：

```text
Existing Running SQL Server Container
        ↓
Existing Stopped SQL Server Container
        ↓
Existing SQL Server Image
        ↓
Create New Container（需符合本 Skill 規則）
```

不得因為不知道 Container Name 就直接建立新的 Container。

不得因為不知道 Host Port 就假設一定是：

```text
1433:1433
```

必須實際檢查目前 Port Mapping。

---

## 5. Context Reuse

如果本 Skill 是由：

```text
task-implementation
```

呼叫或銜接進來，而且上一階段已提供：

- READY Context
- Task Scope
- Test Requirements
- Target Integration Tests

則直接沿用。

不要重新完整閱讀所有 Specification。

只有在：

- Context 不一致
- Repository 發生變更
- Test Target 不清楚
- SQL Server 要求與目前 Context 衝突

時，才重新載入必要文件。

---

## 6. Phase 1 — Inspect Docker Environment

首先確認 Docker 是否可用。

例如：

```powershell
docker version
```

接著檢查目前 Container：

```powershell
docker ps -a
```

尋找 SQL Server Container。

優先辨識使用：

```text
mcr.microsoft.com/mssql/server
```

的 Container。

如果找到既有 SQL Server Container，記錄：

- Container Name
- Image
- Running / Stopped
- Host Port
- Container Port

可以使用：

```powershell
docker port <container-name>
```

確認 Port Mapping。

---

## 7. Docker Secret Safety

禁止執行可能直接把 Secret 輸出的診斷方式。

特別避免直接將完整：

```powershell
docker inspect <container>
```

輸出到對話或 Report。

因為 Container Environment 可能包含：

```text
MSSQL_SA_PASSWORD
```

如果需要使用 `docker inspect`，只能透過明確的 format 取得非敏感欄位，例如：

- Image
- Container State
- Network
- Port

不得輸出 Container Environment Variables。

---

## 8. Existing Running Container

如果找到符合專案需求且正在執行的 SQL Server Container：

不要重新建立 Container。

確認：

```text
Image compatible
Port available
SQL Server ready
```

然後直接進入 Connection String 檢查。

---

## 9. Existing Stopped Container

如果符合專案需求的 SQL Server Container 已存在但目前停止：

允許執行：

```powershell
docker start <container-name>
```

啟動既有 Container 不需要重新提供 SA Password。

啟動後必須等待 SQL Server Ready。

不得因 Container 處於 Stopped 狀態就重新建立另一個 Container。

---

## 10. No Existing Container

如果完全沒有可用 SQL Server Container：

先確認本機是否已有：

```text
mcr.microsoft.com/mssql/server:2022-latest
```

如果沒有，可以 Pull：

```powershell
docker pull mcr.microsoft.com/mssql/server:2022-latest
```

但建立新的 SQL Server Container 需要：

```text
MSSQL_SA_PASSWORD
```

此時必須進入 Secret Handling 流程。

---

## 11. Secret / Password Handling

### Core Rule

實際 Password / Secret 不得：

- 出現在 Chat / Agent 對話
- 出現在 TASK
- 出現在 Specification
- 出現在 docs
- 出現在 Source Code
- 出現在 Test Code
- 出現在 appsettings
- 出現在 launchSettings
- 出現在 Git tracked files
- 出現在 Commit Message
- 出現在最終 Report

Agent 不得要求使用者：

```text
「請把你的 SQL Password 貼給我」
```

### Missing Secret

如果建立新 Container 或 Connection String 需要 Secret，
但目前執行環境沒有 Secret：

```text
STOP — SECRET REQUIRED
```

告知使用者：

> SQL Server 測試環境需要本機 Secret。  
> 請在本機執行環境設定 Secret，不需要將實際密碼提供給 Agent。  
> 設定完成後回覆「已設定，可以繼續」。

不得要求使用者提供實際 Password。

---

## 12. User-side Secret Setup

如果需要新的 SQL Server SA Password，
可以提示使用者在自己的 PowerShell 設定：

```powershell
$env:MSSQL_SA_PASSWORD="<your-local-secret>"
```

此指令由使用者自己執行。

Agent 不得填入或顯示真實 Secret。

注意：

如果使用者設定 `$env:` 的 PowerShell Session
與 Agent 執行命令的 Process Environment 不同，
Agent 可能無法取得該 Environment Variable。

若發生此情況：

```text
STOP
```

並告知使用者 Secret 必須設定在 Agent 實際執行命令可存取的環境中，
必要時重新開啟 Terminal / VS Code / Agent Session。

不得要求使用者把 Secret 貼進對話。

---

## 13. Creating New SQL Server Container

只有在：

- 沒有可重用 Container
- 使用者已準備好本機 Secret
- 專案已核准使用 SQL Server 2022

的情況下，才允許建立 Container。

概念命令：

```powershell
docker run `
  --name <approved-container-name> `
  -e "ACCEPT_EULA=Y" `
  -e "MSSQL_SA_PASSWORD=$env:MSSQL_SA_PASSWORD" `
  -p <host-port>:1433 `
  -d mcr.microsoft.com/mssql/server:2022-latest
```

Host Port 不得在有衝突時硬指定。

建立前應先確認：

```text
1433
14333
或其他預定 Port
```

是否已被使用。

如果既有專案已使用固定 Port，優先沿用。

如果沒有既有定義且需要新 Port：

```text
STOP
```

請使用者核准 Port。

不得自行建立新的環境規格。

---

## 14. SQL Server Readiness

Container 顯示：

```text
Running
```

不代表 SQL Server 已經可以接受 Connection。

啟動後必須等待 SQL Server Ready。

可以檢查：

```powershell
docker logs <container-name>
```

確認沒有：

- Password policy failure
- SQL Server startup failure
- Database engine fatal error
- Port / resource failure

並確認 SQL Server 已進入可以接受連線的狀態。

不要因 Container 剛啟動就立即執行 Integration Tests。

---

## 15. Connection String

Integration Tests 必須透過：

```text
PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING
```

取得 Connection String。

概念格式：

```text
Server=localhost,<host-port>;
Database=master;
User Id=sa;
Password=<local-secret>;
Encrypt=True;
TrustServerCertificate=True;
```

正式 Connection String 只能存在於本機測試環境。

不得寫入 Repository。

---

## 16. Connection String Secret Rule

不得在 Report 中輸出：

```text
Password=actual-password
```

如果需要描述 Connection String，只能顯示：

```text
Server=localhost,<port>;
Database=master;
User Id=sa;
Password=<redacted>;
Encrypt=True;
TrustServerCertificate=True;
```

或直接回報：

```text
PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING = configured
```

不得 Echo 完整 Environment Variable。

避免執行：

```powershell
echo $env:PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING
```

或任何會把 Password 寫進 Terminal Output 的命令。

---

## 17. Missing Connection String

如果：

```text
PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING
```

不存在：

```text
STOP — TEST CONNECTION STRING REQUIRED
```

要求使用者在本機設定。

例如由使用者自行執行：

```powershell
$env:PHOTO_PLATFORM_TEST_DB_CONNECTION_STRING="Server=localhost,<port>;Database=master;User Id=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
```

使用者設定完成後只需要告知：

```text
已設定，可以繼續
```

不需要提供 Connection String 內容。

---

## 18. Phase 2 — Build Verification

SQL Server 環境準備完成後，先執行：

```powershell
dotnet build
```

如果 Build Failure：

```text
STOP
```

分類：

### Existing unrelated failure

如果錯誤來自目前 TASK 之前就存在的程式碼：

```text
STOP
→ Report existing issue
→ Do not modify automatically
```

### Current implementation failure

如果錯誤來自目前 TASK 的修改：

```text
STOP
→ Report implementation issue
→ Return to task-implementation
```

本 Skill 不直接修正業務程式碼。

---

## 19. Phase 3 — Targeted Integration Tests

先執行目前 TASK 新增或直接相關的 Integration Tests。

例如 TASK-08：

```text
UploadPersistenceTests
```

可以使用 test filter 執行相關測試。

目的是先確認：

- SQL Server Connection 正常
- Test Database 可以建立
- Migration 可以執行
- Identity 可以正確回填
- Foreign Key 正常
- Transaction 正常
- Commit 正常
- Rollback 正常

Target Tests 尚未通過前，不要直接宣稱整個 TASK 完成。

---

## 20. TASK-08 SQL Server Verification Focus

TASK-08 Integration Tests 特別需要驗證：

```text
Begin Transaction
→ Add Batch / Images
→ SaveChanges
→ Image Identity 回填
→ 建立 ProcessingJobs
→ SaveChanges
→ Commit
```

以及 Failure 情境：

```text
第二階段失敗
→ Rollback
→ Batch / Image / ProcessingJob 不得留下部分提交資料
```

並確認：

```text
Commit 成功
→ 其他 DbContext 可以讀到 Batch / Image / ProcessingJob
→ 才代表 Queue 可以安全開始處理
```

這些測試不得使用 EF InMemory 取代真實 SQL Server。

---

## 21. Phase 4 — Full Integration Tests

Target Integration Tests 全部通過後：

執行完整 Integration Test Project。

例如：

```powershell
dotnet test tests/PhotoPlatform.IntegrationTests/PhotoPlatform.IntegrationTests.csproj
```

如果測試專案實際路徑不同，以 Repository 現況為準。

不要為了符合本 Skill 而重新命名或移動 Test Project。

---

## 22. Phase 5 — Full Test Suite

如果目前 Task 要求完整 Regression Verification：

執行：

```powershell
dotnet test
```

確認：

```text
Unit Tests
Integration Tests
Existing Regression Tests
```

全部結果。

如果 Integration Test Project 需要 Environment Variable，
必須確保完整 test run 也處於相同測試環境。

---

## 23. Phase 6 — Git Diff Verification

完成測試後執行：

```powershell
git diff --check
```

確認沒有：

- Whitespace errors
- Conflict markers
- 非預期格式問題

LF / CRLF warning 可以回報，
但不得把單純 Line Ending Warning 誤判成測試失敗。

---

## 24. Failure Classification

測試失敗必須先分類。

### A. Environment Failure

包含：

```text
Docker not running
Container not running
Container startup failure
Port conflict
SQL Server not ready
Missing secret
Missing connection string
Authentication failure
Network failure
Cannot create test database due to permission
Docker resource failure
```

處理：

```text
STOP
→ Report Environment Failure
```

不得修改程式碼來繞過。

### B. Test Infrastructure Failure

包含：

```text
Test fixture setup failure
Migration setup failure
Database cleanup failure
Test database creation failure
Environment variable handling failure
```

處理：

```text
STOP
→ Report exact failure
```

不得直接改 Test Infrastructure，
除非目前 Task 明確包含這個 Scope。

### C. Implementation Failure

例如：

```text
Identity 沒有回填
Foreign Key 錯誤
Transaction 沒有 rollback
Commit 順序錯誤
Queue 在 Commit 前執行
Persistence contract 行為錯誤
```

處理：

```text
STOP
→ Report failed test
→ Report expected behavior
→ Report actual behavior
→ Return to task-implementation
```

不得在本 Skill 中自行擴張修正。

### D. Existing Unrelated Failure

如果測試失敗來自目前 Task 之前就存在的問題：

```text
STOP
```

回報：

- File
- Test
- Error
- 是否存在於 Base Commit
- 是否與目前 Task 無關

等待使用者決定是否核准額外修正。

---

## 25. Cancellation / Interrupted Test

如果 Test Run 被：

- User cancellation
- Terminal interruption
- Docker shutdown
- Agent interruption

中止：

不得把 Partial Result 宣稱成 Passed。

回報：

```text
Verification Incomplete
```

並標示已完成與尚未完成的階段。

---

## 26. Test Database Cleanup

Integration Tests 應自行：

```text
Create isolated database
→ Run test
→ Delete isolated database
```

本 Skill 不得手動刪除非本次 Integration Tests 建立的 Database。

不得：

```text
DROP production database
DROP development database
DROP unrelated test database
```

如果測試因異常留下：

```text
PhotoPlatform_Task06_*
```

Database：

先列出可能的殘留 Database。

未經確認不得大量 DROP。

---

## 27. Container Lifecycle

預設不要：

```text
docker rm
docker rm -f
docker stop
```

已存在的 SQL Server Test Container。

Integration Tests 完成後可以保留 Container，
方便後續 TASK 重複使用。

只有使用者明確要求時才：

- Stop
- Remove
- Recreate

Container。

---

## 28. Do Not Modify Repository for Local Environment

不得為了解決本機 Integration Test Environment 問題自行新增：

```text
Dockerfile
docker-compose.yml
docker-compose.override.yml
.env
.env.local
PowerShell secret script
Connection String file
appsettings.Test.json
```

除非：

- 當前 TASK 明確要求
- 或使用者另外核准

本 Skill 預設使用本機 Docker + Environment Variable。

---

## 29. Do Not Commit Secrets

執行測試後確認：

沒有 Secret 被寫入 Git tracked files。

如果發現：

```text
Password
SA password
Connection string with password
```

出現在 Git diff：

```text
STOP — SECRET EXPOSURE RISK
```

不得 Commit。

應先回報使用者。

---

## 30. Interaction with task-implementation Skill

推薦流程：

```text
pre-implementation-review
        ↓
READY
        ↓
task-implementation
        ↓
Build
        ↓
Unit Tests
        ↓
sqlserver-integration-test
        ↓
Target Integration Tests
        ↓
Full Integration Tests
        ↓
Full Test Suite
        ↓
git diff --check
        ↓
Human Review
        ↓
Commit
```

`task-implementation` 負責：

```text
Implementation
Unit Tests
Task Scope
Code Changes
```

本 Skill 負責：

```text
SQL Server Environment
Integration Verification
Failure Classification
```

不得重複定義 Business Logic。

---

## 31. STOP Conditions

遇到以下任何一項立即 STOP：

```text
Docker unavailable
Required SQL Server image incompatible
No approved environment configuration
Secret required but unavailable
Connection String unavailable
Port conflict without approved alternative
SQL Server cannot become ready
Authentication failure
Database permission failure
Build failure
Target Integration Test failure
Unexpected Schema / Migration requirement
Existing unrelated failure blocks verification
Secret exposure risk
Repository state differs materially from Implementation Context
```

STOP 後：

- 不自行擴張 Scope
- 不修改規格
- 不修改業務程式碼
- 不修改測試語意
- 不 Commit
- 不 Push

---

## 32. Success Criteria

只有以下條件全部滿足時，
才可以宣稱 SQL Server Integration Verification 完成：

```text
Docker / SQL Server Ready
Connection String available
dotnet build passed
Target Integration Tests passed
Required Full Integration Tests passed
Required Regression Tests passed
git diff --check passed
No secret leaked
No unresolved blocking issue
```

如果部分測試因 Environment 原因沒有執行：

不得宣稱：

```text
TASK completed
All tests passed
Integration verified
```

只能回報：

```text
Implementation completed,
Integration verification incomplete.
```

---

## 33. Required Final Report

完成後回報以下內容：

### Environment

- Docker：Available / Unavailable
- SQL Server Container：Running / Stopped / Created
- Image
- Host Port → Container Port
- SQL Server Ready：Yes / No
- Connection String：Configured / Missing
- Secret：Configured / Missing

Secret 一律顯示：

```text
<redacted>
```

不得顯示實際值。

### Verification

回報：

```text
dotnet build:
Target Integration Tests:
Full Integration Tests:
Full dotnet test:
git diff --check:
```

需包含：

- Passed count
- Failed count
- Skipped count（如果有）

### Blocking Issues

如果沒有：

```text
Blocking Issues: None
```

如果有：

列出：

- Environment / Infrastructure / Implementation / Existing issue
- Error location
- Expected behavior
- Actual behavior
- Recommended next action

---

## 34. Git Rules

本 Skill 永遠不得自行：

```text
git commit
git push
git reset --hard
git clean -fd
```

除非使用者另外明確授權相應操作。

驗證完成後停在：

```text
Human Review
```

等待使用者決定是否 Commit。

---

## 35. Core Principle

本 Skill 的核心原則：

```text
真實 Database 問題
要用真實 Database 驗證。

環境問題
不要用修改程式碼來掩蓋。

Secret
永遠留在本機環境。

Integration Test Failure
先分類，再決定由誰處理。

Verification 沒完成
就不能宣稱完成。
```
