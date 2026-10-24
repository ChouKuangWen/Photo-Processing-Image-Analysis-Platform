# TASK-07 Processing Queue

## 1. Task Context

- Module: MOD-01 Upload
- Related Module: MOD-02 Processing
- Layer: Application / Infrastructure
- Type: Infrastructure
- Status: Planned

---

## 2. Purpose

沿用既有 Processing Queue Contract，建立圖片處理流程所需要的 In-Memory Implementation。

Upload 流程完成必要資料建立後，應能將待處理工作加入 Queue，供後續 Background Processing Worker 取得。

本 TASK 只負責 Queue 機制，不負責實際執行圖片處理工作。

---

## 3. In Scope

本 TASK 包含：

- 重用既有 `IProcessingQueue` abstraction
- 沿用既有 `ProcessingJob` 作為 Queue Item
- In-memory queue implementation
- 非同步 Enqueue
- 非同步 Dequeue
- `CancellationToken` 支援
- FIFO 行為
- Queue 為空時的非同步等待
- 必要的 Dependency Injection registration
- Queue Unit Tests

Queue implementation 依 MOD-02 Processing Specification §1.7 使用 .NET 內建：

```text
System.Threading.Channels
```

若既有 Contract 已存在，必須優先重用。

---

## 4. Out of Scope

本 TASK 不包含：

- Background Worker / `BackgroundService`
- ProcessingJob 實際處理流程
- Retry / Backoff
- Startup Recovery
- Metadata / Duplicate / Similarity / Quality Processing
- Upload Service
- Upload API
- Compensation
- SignalR / Monitoring
- Database Schema Change
- Migration
- Distributed Queue
- RabbitMQ / Kafka / Redis Queue
- 新增第三方 Queue Package

不得因本 TASK 順便實作上述功能。

---

## 5. Expected Files

實際檔名與位置應以目前 Repository 結構及既有 Contract 為準。

可能新增：

```text
Infrastructure
- In-memory queue implementation

Tests
- Queue unit tests
```

可能修改：

```text
Dependency Injection registration
```

Pre-Implementation Review 必須先確認：

- Queue Contract 是否已存在
- Queue Item 是否已有可重用型別
- 正確的 Application / Infrastructure 放置位置

不得在 Implementation 階段自行建立重複 Contract。

沿用既有 `IProcessingQueue` 與 `ProcessingJob`，不新增 Application Queue Contract 或 `ProcessingQueueItem` DTO，不修改既有 Interface signature。

---

## 6. Requirements

Queue 必須符合：

- Enqueue / Dequeue 為 async
- 支援 `CancellationToken`
- FIFO
- Queue 為空時應非同步等待
- 不得使用 Busy Loop
- Queue Implementation 不執行 Business Processing
- Queue Implementation 不修改 ProcessingJob 狀態、RetryCount 或 Error 資訊
- 不得吞掉 Cancellation
- 不得自行新增第三方 NuGet Package

Queue 僅負責傳遞既有 `ProcessingJob`；Queue Item Contract 依 MOD-02 Processing Specification §1.7。

Queue Capacity、設定驗證與滿載 Backpressure 行為依 MOD-02 Processing Specification §1.7 實作，不另行定義。

---

## 7. Tests

至少覆蓋：

### FIFO

```text
Enqueue A
Enqueue B
→ Dequeue A
→ Dequeue B
```

### Empty Queue

Queue 為空時：

- Dequeue 應等待
- 不應 Busy Loop
- 新 Item 加入後可正常取得

### Cancellation

等待 Dequeue 時取消 `CancellationToken`：

- Operation 應正常取消
- Cancellation 應向上傳遞

### Multiple Items

多個 Queue Item：

- 不遺失
- 順序正確

### Item Integrity

Enqueue / Dequeue 後的重要識別資料保持一致。

Queue 不得修改 `ProcessingJob` 狀態、RetryCount 或 Error 資訊。

### Capacity / Backpressure

驗證 MOD-02 Processing Specification §1.7 的容量預設值、設定驗證、滿載等待、等待取消及不丟棄工作規則。

---

## 8. Definition of Done

TASK-07 完成需符合：

- Queue abstraction 可供 Producer / Consumer 使用
- In-memory queue implementation 完成
- Async Enqueue / Dequeue 正常
- `CancellationToken` 正常
- FIFO 正常
- Empty Queue 不使用 Busy Loop
- 必要 DI registration 完成
- Unit Tests 通過
- `dotnet build` 通過
- `dotnet test` 通過
- 沒有修改未授權 Contract
- 沒有 Database Schema / Migration 變更
- 沒有新增第三方 Queue Package
- 沒有 Scope 外修改
