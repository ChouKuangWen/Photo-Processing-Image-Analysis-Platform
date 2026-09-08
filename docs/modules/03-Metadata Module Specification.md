# Metadata Module Specification

**文件名稱：** Metadata Module Specification  
**模組編號：** MOD-03  
**模組名稱：** Metadata Module  
**對應系統：** Photo Processing & Intelligent Image Analysis Platform  
**架構：** Clean Architecture  
**技術：** ASP.NET Core / C# / EF Core  
**文件用途：** AI Agent Development Specification  
**文件版本：** V2.0  
**文件狀態：** Development  

---

# 1. 規格

## 1.1 模組目的

Metadata Module 負責從圖片檔案中取得、解析、標準化與儲存圖片 Metadata。

主要處理：

- EXIF Metadata
- TakenAt
- CameraModel
- ISO
- ShutterSpeed
- Aperture
- GPS
- Orientation
- Reverse Geocoding
- Metadata Fallback

本模組的目標是：

> 將圖片原始 Metadata 轉換成系統可以統一使用與查詢的資料。

---

## 1.2 模組邊界

### 本模組負責

- EXIF Parsing
- Metadata Normalization
- GPS Extraction
- GPS Validation
- GPS DMS → Decimal Degrees
- Reverse Geocoding
- Metadata Fallback
- Metadata Persistence
- Metadata 查詢

### 本模組不負責

- 圖片 Upload
- Background Queue
- Processing Job 管理
- SHA-256
- Exact Duplicate Detection
- pHash
- Visual Similarity
- Quality Analysis
- Naming
- Recommendation
- 實際圖片旋轉

Metadata Module 只提供 Metadata，不負責其他影像分析功能。

---

## 1.3 系統層級 Workflow 邊界

本模組不定義整個系統的 Workflow。

例如：

```text
Upload
→ Processing
→ Metadata
→ Duplicate Detection
→ Similarity
→ Quality
→ Naming
```

這類 Module 之間的執行順序由 **System-Level Specification** 定義。

Metadata Module 只定義自身收到 Processing Step 後如何完成 Metadata 處理。

本模組內部處理概念：

```text
Read Image
    ↓
EXIF Parsing
    ↓
Normalize Metadata
    ↓
Process GPS
    ↓
Reverse Geocoding
    ↓
Apply Fallback
    ↓
Persist
```

---

## 1.4 Processing Steps

Metadata Module 提供以下 Processing Steps：

```text
EXIF_PARSING
GEO_LOOKUP
```

Processing Module 負責 Job、Queue、Worker 與 Step Execution。

Metadata Module 負責實際執行：

```text
EXIF_PARSING
GEO_LOOKUP
```

Metadata Module 不應自行修改 Processing Module 定義的 Job State 或 Step Order。

---

## 1.5 EXIF Metadata

系統至少支援以下 EXIF 欄位：

```text
DateTimeOriginal
Model
ISOSpeedRatings
ExposureTime
FNumber
GPSLatitude
GPSLongitude
Orientation
```

解析後轉換成系統統一格式。

---

## 1.6 TakenAt

優先使用：

```text
EXIF DateTimeOriginal
```

如果 EXIF 沒有拍攝時間：

```text
TakenAt = FileCreatedAt
```

Fallback 不應造成整個 Processing Failed。

---

## 1.7 CameraModel

優先使用：

```text
EXIF Model
```

如果不存在：

```text
CameraModel = UnknownCamera
```

---

## 1.8 ISO

如果 EXIF 存在 ISO：

```text
ISO = EXIF ISO
```

如果不存在：

```text
ISO = null
```

ISO 缺失不視為 Processing Failure。

---

## 1.9 ShutterSpeed

來源：

```text
EXIF ExposureTime
```

系統應轉換成統一表示方式。

例如：

```text
0.004
```

可表示為：

```text
1/250
```

如果無法取得：

```text
ShutterSpeed = null
```

---

## 1.10 Aperture

來源：

```text
EXIF FNumber
```

例如：

```text
2.8
```

統一表示為：

```text
f/2.8
```

如果無法取得：

```text
Aperture = null
```

---

## 1.11 Orientation

來源：

```text
EXIF Orientation
```

Metadata Module 只負責儲存 Orientation。

**不負責實際旋轉圖片。**

---

## 1.12 GPS

### GPS Extraction

如果圖片包含：

```text
GPSLatitude
GPSLongitude
```

則解析為：

```text
decimal Latitude
decimal Longitude
```

---

### GPS Normalization

EXIF GPS 可能使用：

```text
Degrees
Minutes
Seconds
```

系統必須轉換成 Decimal Degrees。

例如：

```text
25° 02' 00"
```

轉換為：

```text
25.033333
```

---

### GPS Validation

Latitude：

```text
-90 <= Latitude <= 90
```

Longitude：

```text
-180 <= Longitude <= 180
```

超出範圍的 GPS 視為 Invalid GPS。

無效座標不得寫入 Database。

---

## 1.13 Reverse Geocoding

如果圖片具有有效 GPS，Metadata Module 可以透過 Geo Service 將：

```text
Latitude + Longitude
```

轉換為：

```text
LocationName
```

例如：

```text
25.0330
121.5654
```

可能得到：

```text
Taipei
```

---

## 1.14 Geo Service Abstraction

Application Layer 不得直接依賴特定 Geo API。

使用：

```csharp
public interface IGeoService
{
    Task<string?> ReverseGeocodeAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken);
}
```

Infrastructure Layer 負責：

- HTTP Client
- External Geo API
- Response Mapping

Application Layer 不直接依賴 Geo API SDK。

---

## 1.15 Geo Service Failure

以下情況屬於可 Retry 的暫時性錯誤：

```text
Timeout
429
5xx
Network Error
```

處理方式：

```text
Geo Service Failure
    ↓
Retry
    ↓
仍然失敗
    ↓
LocationName = UnknownLoc
    ↓
Warning Log
    ↓
Continue
```

Geo Service Failure 不應直接造成整個圖片 Processing Failed。

---

## 1.16 Metadata Fallback

Metadata 缺失時使用以下規則：

| Metadata | Fallback |
|---|---|
| TakenAt | FileCreatedAt |
| CameraModel | UnknownCamera |
| LocationName | UnknownLoc |
| ISO | null |
| ShutterSpeed | null |
| Aperture | null |
| GPS | null |

Fallback 的基本原則：

```text
資料不存在
    ↓
使用 Fallback
    ↓
記錄 Warning
    ↓
繼續 Processing
```

---

## 1.17 Metadata Normalization

Metadata 必須轉換成系統統一格式。

主要轉換：

```text
EXIF DateTime
    ↓
DateTime

EXIF GPS DMS
    ↓
Decimal Latitude / Longitude

EXIF FNumber
    ↓
Aperture

EXIF ExposureTime
    ↓
ShutterSpeed
```

---

## 1.18 Domain Model

Metadata 可以作為 Image Entity 的 Value Object。

概念：

```text
Image
 └── Metadata
      ├── TakenAt
      ├── CameraModel
      ├── ISO
      ├── ShutterSpeed
      ├── Aperture
      ├── Latitude
      ├── Longitude
      ├── LocationName
      └── Orientation
```

實際 Domain Model 依專案既有 Domain Design 實作。

---

## 1.19 Application Interface

Metadata Module 提供：

```csharp
public interface IMetadataService
{
    Task<ImageMetadata> ExtractAsync(
        long imageId,
        CancellationToken cancellationToken);
}
```

EXIF Parsing：

```csharp
public interface IExifService
{
    Task<ImageMetadata> ExtractAsync(
        Stream imageStream,
        CancellationToken cancellationToken);
}
```

Geo Service：

```csharp
public interface IGeoService
{
    Task<string?> ReverseGeocodeAsync(
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken);
}
```

---

## 1.20 Infrastructure

Infrastructure Layer 負責：

```text
EXIF Library
HTTP Client
Geo API
EF Core
Database
```

Application Layer 不得直接依賴：

```text
EXIF Library
Geo API SDK
EF Core Provider
```

必須透過 Interface 解耦。

---

## 1.21 Error Handling

Metadata Processing 分為：

```text
Fatal Error
Non-Fatal Error
```

### Non-Fatal Error

例如：

- EXIF 不存在
- GPS 不存在
- CameraModel 不存在
- ISO 不存在
- Geo API 暫時失敗

處理：

```text
Fallback
+
Warning Log
+
Continue
```

### Fatal Error

例如：

- Image File 不存在
- Storage 無法讀取
- 圖片完全無法解析

處理：

```text
Processing Failed
+
Error Log
+
ProcessingLog
```

---

## 1.22 Retry

Geo Service 使用 Exponential Backoff：

```text
Retry 1 → 1 sec
Retry 2 → 2 sec
Retry 3 → 4 sec
```

適用：

```text
408
429
5xx
Timeout
```

不適用：

```text
400
401
403
404
```

最大 Retry 次數由 Configuration 控制。

---

## 1.23 Idempotency

Metadata Processing 必須具備 Idempotent Design。

Logical Identity：

```text
ImageId + ProcessingStep
```

例如：

```text
ImageId = 1024
ProcessingStep = EXIF_PARSING
```

如果該 Step 已成功完成：

```text
Skip
```

避免：

- 重複解析
- 重複寫入
- Retry 造成資料異常

---

## 1.24 Logging

Metadata Processing 必須記錄：

```text
TraceId
ImageId
ProcessingStep
Status
DurationMs
ErrorCode
RetryCount
Timestamp
```

例如：

```text
ImageId=1024
Step=EXIF_PARSING
Status=Success
DurationMs=85
```

Fallback：

```text
ImageId=1024
Step=EXIF_PARSING
Status=Warning
ErrorCode=EXIF_MISSING
```

---

## 1.25 Agent 開發規則

AI Agent 實作本模組時必須：

1. 遵守 System-Level Specification。
2. 遵守 Processing Module Contract。
3. 遵守 Database Schema。
4. 維持 Clean Architecture。
5. 透過 Interface 隔離 EXIF Library 與 Geo API。
6. 不得直接修改其他 Module 的資料。
7. 不得自行修改 Processing Workflow。
8. 不得自行修改 API Contract。
9. 不得為了通過測試而修改規格。
10. 新增功能時必須同步建立測試。

如果實作需求與既有規格衝突：

```text
Identify Conflict
    ↓
Explain Impact
    ↓
Propose Change
    ↓
Wait for Approval
```

---

# 2. 資料表

## 2.1 Images

Metadata 主要儲存在：

```text
Images
```

Metadata Module 使用的欄位：

| 欄位 | 型別 | Nullable | 說明 |
|---|---|---|---|
| ImageId | bigint / GUID | No | Image Identifier |
| TakenAt | datetime | Yes | 拍攝時間 |
| CameraModel | nvarchar(100) | Yes | 相機型號 |
| ISO | int | Yes | ISO |
| ShutterSpeed | nvarchar(50) | Yes | 快門速度 |
| Aperture | nvarchar(50) | Yes | 光圈 |
| Latitude | decimal | Yes | 緯度 |
| Longitude | decimal | Yes | 經度 |
| LocationName | nvarchar(150) | Yes | 地點名稱 |
| Orientation | int | Yes | 圖片方向 |
| UpdatedAt | datetime | No | 更新時間 |

實際欄位型別應以 `Database-Schema.md` 為準。

---

## 2.2 Database Rules

### GPS

```text
Latitude  : -90 ~ 90
Longitude : -180 ~ 180
```

無效 GPS 不應寫入。

### Nullable

以下 Metadata 可以為 NULL：

```text
ISO
ShutterSpeed
Aperture
Latitude
Longitude
```

Metadata 缺失不代表資料庫錯誤。

---

## 2.3 Original Image

Metadata Processing 不得修改原始圖片內容。

```text
Original Image
      ↓
Read Only
```

Metadata Module 只讀取圖片並更新 Metadata 資料。

---

# 3. API

## 3.1 API Base

```text
/api/v1
```

---

## 3.2 Get Image Metadata

```http
GET /api/v1/images/{imageId}
```

用途：

> 取得指定圖片及其 Metadata。

---

## 3.3 Request

Path Parameter：

```text
imageId : bigint / GUID
```

Request Body：

```text
None
```

範例：

```http
GET /api/v1/images/1024
```

---

## 3.4 Success Response

```http
200 OK
```

```json
{
  "success": true,
  "data": {
    "imageId": 1024,
    "originalFileName": "IMG_0001.jpg",
    "takenAt": "2026-08-23T14:30:25",
    "cameraModel": "Canon EOS R6",
    "iso": 400,
    "shutterSpeed": "1/250",
    "aperture": "f/2.8",
    "latitude": 25.0330,
    "longitude": 121.5654,
    "locationName": "Taipei",
    "orientation": 1
  }
}
```

API Response 只回傳系統允許公開的 Image / Metadata 資訊。

---

## 3.5 Error Response

統一格式：

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

不得回傳：

```text
Exception
StackTrace
Internal Path
Connection String
Database Error
```

---

## 3.6 HTTP Status Code

| Status Code | 用途 |
|---:|---|
| 200 | Metadata 查詢成功 |
| 404 | Image 不存在 |
| 500 | Metadata / System Error |
| 503 | Geo Service 暫時不可用 |

---

## 3.7 Error Code

| Code | HTTP | 說明 |
|---|---:|---|
| IMAGE_NOT_FOUND | 404 | Image 不存在 |
| METADATA_PARSE_FAILED | 500 | Metadata 解析失敗 |
| INVALID_GPS | 500 | GPS 資料無效 |
| GEO_SERVICE_ERROR | 503 | Geo Service 錯誤 |
| INTERNAL_ERROR | 500 | 系統內部錯誤 |

---

# 4. 測試

## 4.1 Unit Test

測試框架：

```text
xUnit
```

---

## 4.2 EXIF Tests

必須測試：

```text
Valid EXIF
Missing EXIF
Partial EXIF
Invalid EXIF
```

驗證：

- TakenAt
- CameraModel
- ISO
- ShutterSpeed
- Aperture
- Orientation

---

## 4.3 TakenAt Tests

測試：

```text
EXIF TakenAt 存在
EXIF TakenAt 不存在
Fallback → FileCreatedAt
```

---

## 4.4 CameraModel Tests

測試：

```text
CameraModel 存在
CameraModel 不存在
Fallback → UnknownCamera
```

---

## 4.5 GPS Tests

必須測試：

```text
Valid GPS
Missing GPS
Invalid GPS
DMS → Decimal
Latitude Boundary
Longitude Boundary
```

---

## 4.6 Geo Service Tests

測試：

```text
Geo API Success
Geo API Timeout
Geo API 429
Geo API 500
Network Error
Geo API Unavailable
```

並驗證：

```text
Retry
Fallback
Warning
Continue
```

---

## 4.7 Fallback Tests

必須驗證：

```text
Metadata Missing
    ↓
Fallback
    ↓
Warning Log
    ↓
Processing Continue
```

---

## 4.8 Idempotency Tests

必須測試：

```text
Step 已完成
    ↓
再次執行
    ↓
Skip
```

並驗證不會：

- 重複建立 Metadata
- 重複寫入錯誤資料
- 因 Retry 產生不一致狀態

---

## 4.9 Integration Test

至少驗證：

```text
Metadata Service
      ↓
EXIF Service
      ↓
Geo Service
      ↓
EF Core
      ↓
Database
```

測試：

1. EXIF Parsing
2. Metadata Normalization
3. GPS Processing
4. Reverse Geocoding
5. Fallback
6. Persistence
7. Error Handling

---

## 4.10 API Integration Test

測試：

### Existing Image

```http
GET /api/v1/images/{imageId}
```

預期：

```text
200 OK
```

並包含 Metadata。

### Image Not Found

預期：

```text
404
IMAGE_NOT_FOUND
```

---

## 4.11 Playwright E2E Test

### Metadata Display

```text
Upload Image
    ↓
Processing Complete
    ↓
Open Image Detail
    ↓
Display Metadata
```

驗證：

```text
TakenAt
CameraModel
ISO
ShutterSpeed
Aperture
GPS
LocationName
Orientation
```

---

### Missing EXIF

```text
Upload Image Without EXIF
    ↓
Processing
    ↓
Image Detail
```

驗證：

```text
TakenAt = FileCreatedAt
CameraModel = UnknownCamera
```

且：

```text
Processing != Failed
```

---

### Missing GPS

```text
Upload Image Without GPS
    ↓
Processing
    ↓
Image Detail
```

驗證：

```text
LocationName = UnknownLoc
```

且 Processing 不應 Failed。

---

### Geo Service Failure

模擬 Geo API：

```text
Timeout / 429 / 5xx
```

驗證：

```text
Retry
    ↓
Fallback = UnknownLoc
    ↓
Warning Log
    ↓
Processing Continue
```

---

## 4.12 Acceptance Criteria

### AC-01 EXIF Parsing

Given：

> 圖片包含 EXIF。

When：

> Metadata Processing 執行。

Then：

```text
TakenAt
CameraModel
ISO
ShutterSpeed
Aperture
```

應正確解析與儲存。

---

### AC-02 Missing EXIF

Given：

> 圖片沒有 EXIF。

When：

> Metadata Processing 執行。

Then：

```text
TakenAt = FileCreatedAt
CameraModel = UnknownCamera
```

且 Processing 不應 Failed。

---

### AC-03 GPS

Given：

> 圖片包含有效 GPS。

When：

> Metadata Processing 執行。

Then：

```text
Latitude
Longitude
```

應正確解析與儲存。

---

### AC-04 Reverse Geocoding

Given：

> 圖片包含有效 GPS。

When：

> Geo Service 正常。

Then：

```text
LocationName
```

應成功取得並儲存。

---

### AC-05 Geo Failure

Given：

> Geo Service 發生 Timeout / 5xx。

When：

> Retry 仍然失敗。

Then：

```text
LocationName = UnknownLoc
```

並：

```text
ProcessingLog = Warning
Processing Continue
```

---

### AC-06 Metadata API

Given：

> Image 存在。

When：

```http
GET /api/v1/images/{imageId}
```

Then：

```text
HTTP 200
```

並回傳 Metadata。

---

### AC-07 Image Not Found

Given：

> Image 不存在。

When：

```http
GET /api/v1/images/{imageId}
```

Then：

```text
HTTP 404
IMAGE_NOT_FOUND
```

---

# Definition of Done

Metadata Module 完成前必須確認：

### 規格

- [ ] EXIF Parsing
- [ ] Metadata Normalization
- [ ] TakenAt
- [ ] CameraModel
- [ ] ISO
- [ ] ShutterSpeed
- [ ] Aperture
- [ ] Orientation
- [ ] GPS Parsing
- [ ] GPS Validation
- [ ] Reverse Geocoding
- [ ] Fallback
- [ ] Retry
- [ ] Idempotency
- [ ] Error Handling
- [ ] Logging

### 資料表

- [ ] Metadata 欄位完成
- [ ] Nullable 規則完成
- [ ] GPS Constraint 完成
- [ ] EF Core Mapping 完成

### API

- [ ] Metadata Query API
- [ ] Response Contract
- [ ] Error Response
- [ ] HTTP Status Code
- [ ] Error Code

### 測試

- [ ] EXIF Unit Tests
- [ ] GPS Unit Tests
- [ ] Fallback Unit Tests
- [ ] Geo Service Tests
- [ ] Idempotency Tests
- [ ] Integration Tests
- [ ] API Tests
- [ ] Playwright E2E Tests
- [ ] Acceptance Criteria 全部通過