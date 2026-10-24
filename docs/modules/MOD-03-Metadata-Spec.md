# Metadata Module Specification

> **規格修訂草稿（2026-10-12）**：以 develop `8ff1dd983af2ffa68dfc93893f9cca61fadb2c90` 為基準。
> MOD-01 feature `a1bb29e1d7cf704a202adbe1c14741b6441575be` 僅為交接證據，未合併進 develop。
> 已核准與待決策內容分開記錄於 [規格審閱與決策紀錄](../reviews/Specification-Revision-Review.md)。
> F-01～F-08已核准並同步至最終契約章；同題舊待決文字由最終章取代。引擎/精確版本N-01與實作驗收仍未完成；不是TASK、實作或PASS授權。


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
TakenAt = Image.CreatedAt
TimeSource = UploadCreatedAt
```

Fallback 不應造成整個 Processing Failed；只有有效 EXIF offset 可建立可信 instant，無 offset 不猜時區。UploadCreatedAt 不是實際拍攝時間，不計入原始 Metadata completeness。

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
| TakenAt | Image.CreatedAt；TimeSource=UploadCreatedAt，不計原始 completeness |
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
DateTimeOffset（來源時區與 offset 缺失政策待 D-08）

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
- Storage 永久不可讀（temporary I/O／timeout 應分類為 transient 並交 MOD-02）
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

遵守 MOD-02 §1.10 統一的 Step transient retry：四個邏輯槽位（初次＋最多三次重試），1／2／4秒；Shutdown Resume另記invocation不增加槽位。
IGeoService／MetadataService 不另行 retry；正確分類 408／429／5xx／timeout／temporary network error 與永久 400／401／403／404。
GEO_LOOKUP transient failure 耗盡後，LocationName = UnknownLoc、Warning Log、保留已完成 EXIF／GPS 與其他 Metadata，繼續下一個 Workflow Step。
Fallback 不是 Job Failed；Host shutdown cancellation 不計入 Retry、不觸發 Geo fallback，交由 MOD-02 安全恢復。
Step checkpoint已依D-05定義；永久Geo/invalidGPS與result reference依F-01最終章，不能把auth/config套transient fallback。

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

## 與 MOD-02 的已核准執行契約

- MVP 單 Instance、多 Worker，MOD-02 以 SQL Server 原子 Claim 取得 Job 執行權；本模組不建立 Queue、Worker 或平行重試排程器。
- 每Run的每個transient Step由MOD-02管理四個邏輯槽位0～3，重試等待1／2／4秒；Shutdown Resume沿用槽位而另記invocation，本模組不疊加retry loop。
- host shutdown cancellation 向上傳播、不算 Retry；中斷由 MOD-02 處理，不能偽裝成 module failure 或 fallback。已提交成功結果需核對可重用性，不重做副作用。
- Job Status、Step Result、Log Status 不混用；失敗由 MOD-02 協調持久化 Job transition，本模組不直接改其 enum。
- D-05／D-06已核准MOD-02單一History writer、Run／checkpoint／invocation、提交後best-effort事件；Step從checkpoint映射、Timestamp為Log建立時間，schema見共用文件§3～§6。子模組回報Outcome，不重複寫History。
- 保留每次 attempt History，重要 operation 安全保留 JobId／ImageId／TraceId；沿用 ILogger<T>，不輸出 raw exception／SQL／path／secrets，不重複 MOD-01 failure event。
- 本模組的新增契約缺口與決策選項見 `docs/reviews/Specification-Revision-Review.md`；未決事項不能以一般實作細節自行補足。


### MOD-03 獨立審閱缺口（未核准）

- M03-01／Major：§1.6 的 FileCreatedAt 沒有在 MOD-01 DTO／Image／Storage Contract 定義來源，雲端也不能假设可讀原始檔案 creation time。D-08本批已核准Image.CreatedAt並標UploadCreatedAt；不計原始completeness，獨立結果保存。
- M03-02／Major：§1.11／§2.1 的 Orientation 為需求，但 MOD-01 feature 沒有欄位；保存位置與遷移影響待 D-08。
- M03-03／Major：EXIF 缺 offset、GPS ref／負座標與 malformed partial Metadata 的確切規則未定義；不得默認 machine timezone。
- M03-04／Major：§1.19 ExtractAsync 同時概述 EXIF／Geo／Persist，兩個 Step 的切分與 Stream read／ownership 未定義；既有 IFileStorageService 僅 Save／Delete，見 D-09。
- M03-05／Major：GET 既有 Metadata 與 Geo／INVALID_GPS 的 503／500 mapping 關係不明；Query 不應擅自觸發 Geo retry／解析。要選擇 persisted-only 查詢或明確另定契約，見 D-08。

新增驗收要求待上述決策定案後明確化：來源時區缺失、signed GPS、Orientation persistence、missing GPS 不呼叫 Geo、四次 Geo retry／Warning continue、host cancellation 不 fallback、已完成 EXIF 在 Geo recovery 時不重做。

---

### D-05～D-07 同步限制

只有binding／版本／Context可確認且T1／T2已原子提交Running Log後才開始本模組。結果／History保存重試不重做模組操作，也不消耗business retry budget。
程序在T3前中斷時memory可能遺失；只讀計算需可重現Input／RuleVersion才能安全重算；已有SQL／Group／Storage副作用先核對，不因未保存checkpoint盲目重做。
Unknown副作用／Context先阻擋。PrimaryJob才更新Image／BatchStatus，secondary不改Uploadaggregate。F-01～F-08的結果/版本/receipt已同步最終章；Analysis/Full尚有N-01/N-02，實作與驗收未完成。

---

# 2. 資料表

## 共用 Schema 與交接基線

physical mapping 以 `docs/Database-Schema.md` 已確認部分為共同依據；尚未定案的欄位、索引與關係明列為待決策，不假設不存在的原始 Database-Schema.md 已提供答案。
沿用 MOD-01 Image.Id BIGINT、BatchId uniqueidentifier、DateTimeOffset／datetimeoffset(7) 與 Unicode mapping。
SQL Server timestamp／rowversion 不是日期時間；日期不得以 TIMESTAMP mapping 實作。
MOD-01 feature 尚未合併；TASK-11／12 最新驗收維持待確認，不能把後續模組未完成判作 MOD-01 Backend 缺陷。

---

## 2.1 Images

Metadata 主要儲存在：

```text
Images
```

Metadata Module 使用的欄位：

| 欄位 | 型別 | Nullable | 說明 |
|---|---|---|---|
| Id | BIGINT | No | Image Identifier（API／DTO 稱 imageId） |
| TakenAt | datetimeoffset(7) | Yes | 拍攝時間；offset 解析政策待 D-08 |
| CameraModel | nvarchar(100) | Yes | 相機型號 |
| ISO | int | Yes | ISO |
| ShutterSpeed | varchar(50) | Yes | 快門速度（沿用 MOD-01） |
| Aperture | varchar(50) | Yes | 光圈（沿用 MOD-01） |
| Latitude | decimal(10,7) | Yes | 緯度 |
| Longitude | decimal(10,7) | Yes | 經度 |
| LocationName | nvarchar(150) | Yes | 地點名稱 |
| Orientation | 獨立Metadata結果tinyint CHECK1..8 | Yes | 不新增MOD-01 Image欄位，非法raw值保存在provenance |
| UpdatedAt | datetimeoffset(7) | No | 更新時間 |

已確認欄位見 `docs/Database-Schema.md`；Orientation 與來源時間／offset 的缺口不得以本表提案自行新增 Schema。

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
imageId : long（SQL Server BIGINT）
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
Fallback → Image.CreatedAt (UploadCreatedAt)
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
TakenAt = Image.CreatedAt
TimeSource = UploadCreatedAt
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
TakenAt = Image.CreatedAt
TimeSource = UploadCreatedAt
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

# 最終核准契約 — F-01／F-05（文件審核版）

本章依使用者F-01～F-08核准及最後補充同步，取代前文同題的舊待決策描述；不是實作／測試PASS。不修改MOD-01 mapping。

## Metadata來源、有效性與結果

獨立image_metadata_results為版本化authority；唯一(ImageId,InputFingerprint,MetadataRuleVersion)。時間可信須DateTimeOriginal可解析且OffsetTimeOriginal是有效±HH:mm、可構成DateTimeOffset；無offset不猜時區、保留原始local time。缺可信instant用Image.CreatedAt並標TimeSource=UploadCreatedAt，不稱真實拍攝時間，不计原始completeness。RawTakenAt/offset/provenance必須保留。
Orientation合法1～8僅套用解碼分析視圖一次；缺少按identity，非法按identity並Warning，provenance可區分，不改原始檔案。
Metadata有效六項：可信原始TakenAt；trim後非空且不為fallback UnknownCamera的原始CameraModel（符合既有100字元限制）；原始ISO整數>0；原始Aperture有限正值；原始ExposureTime可解析有限正數／正有理數（展示ShutterSpeed不代替驗證）；GPS為原始有效成對緯經度，EXIF reference N/S/E/W可核對，範圍[-90,90]/[-180,180]。Missing/Invalid/Fallback不充數，GPS一對只算一項，Geo的地名不是六項之一。

| 情況 | Geo／Step結果 | LocationName |
|---|---|---|
| MissingGPS | 不呼叫Geo，GEO_LOOKUP Skipped；Metadata可正常完成 | UnknownLoc |
| InvalidGPS：缺半邊／reference或範圍不合法 | 不呼叫Geo，GEO_LOOKUP Warning | UnknownLoc |
| 有效GPS、Geo成功 | Success、GeoOutcome=Resolved | 已保存結果 |
| transient耗盡四邏輯槽位 | Warning、GeoOutcome=TransientExhausted，後續繼續 | UnknownLoc |
| auth/configuration failure | Permanent Failed，GeoOutcome分別AuthFailure/ConfigurationFailure，不Retry，Job/Run失敗 | 不能用fallback掩蓋服務配置故障 |

EXIF結果與Geo結果分別對應checkpoint。缺少可選Metadata不失敗；損壞圖像／不支援格式／必需輸入不可讀依永久錯誤處理，不偽造Completed。失敗row不可當Completed結果重用。
Metadata結果與History/checkpoint、必要Primary投影同T3 transaction；MOD-03 owns result、MOD-02單一History writer、MOD-09查詢。只有已核對PrimaryJob投影既有Image欄位；secondary保存獨立結果，不覆寫全域Metadata。
GET只讀已持久化結果。InputFingerprint/SourceSHA256核對原始內容與RuleVersion；MOD-01未保存ContentVersion，reader須由原始內容SHA256核對，不能捏造storage version。此內容完整性hash不等同EXACT group建立，不能跨模組做duplicate副作用。結果前crash可重parse，Geo可能再呼叫；結果後按版本重用，不保證memory跨重啟。
