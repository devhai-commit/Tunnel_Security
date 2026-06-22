# Hệ Thống Giám Sát Xâm Nhập Cống Ngầm — Tunnel Security

## Mục Lục
1. [Tổng Quan Hệ Thống](#1-tổng-quan-hệ-thống)
2. [Cơ Sở Dữ Liệu (CSDL)](#2-cơ-sở-dữ-liệu-csdl)
3. [ERD — Mô Hình Thực Thể Quan Hệ](#3-erd--mô-hình-thực-thể-quan-hệ)
4. [Kiến Trúc Hệ Thống](#4-kiến-trúc-hệ-thống)
5. [Workflow — Luồng Dữ Liệu](#5-workflow--luồng-dữ-liệu)
6. [Pipeline — Xử Lý Dữ Liệu](#6-pipeline--xử-lý-dữ-liệu)
7. [Phân Tích Chi Tiết Station & Backend](#7-phân-tích-chi-tiết-station--backend)
8. [Giao Thức Kết Nối Thiết Bị](#8-giao-thức-kết-nối-thiết-bị)

---

## 1. Tổng Quan Hệ Thống

Hệ thống giám sát xâm nhập cống ngầm (Tunnel Security) là giải pháp IoT giám sát môi trường và phát hiện xâm nhập trái phép trong hệ thống cống ngầm tại Hà Nội. Hệ thống gồm 4 thành phần chính:

| Thành Phần | Công Nghệ | Vai Trò |
|-----------|-----------|---------|
| **Backend** | ASP.NET Core 8 Web API | Xử lý dữ liệu, SignalR real-time, REST API |
| **Station (Trạm)** | WinUI 3 Desktop App | Giao diện giám sát tại trạm địa phương |
| **Center (Trung Tâm)** | WinUI 3 Desktop App | Giám sát tập trung nhiều trạm |
| **SimDevice** | .NET 8 Console App | Mô phỏng thiết bị cảm biến |

**Công nghệ chính:**
- **Ngôn ngữ:** C# .NET 8
- **ORM:** Entity Framework Core (SQLite + Npgsql)
- **CSDL quan hệ:** SQLite (topology, cấu hình, alerts, users)
- **CSDL time-series:** PostgreSQL + TimescaleDB (sensor readings, heartbeats, camera events)
- **Real-time:** SignalR (`/hubs/sensors`), WebSocket (`/ws/device`, `/ws/join`)
- **Message Queue:** `System.Threading.Channels` (Channel trong quá trình)
- **Bản đồ:** Mapbox (WebView2 tích hợp)
- **Xử lý ảnh:** SixLabors.ImageSharp
- **Báo cáo:** QuestPDF, ClosedXML

---

## 2. Cơ Sở Dữ Liệu (CSDL)

Hệ thống sử dụng chiến lược **lưu trữ kép (Dual Database)**:

- **SQLite** → dữ liệu quan hệ truyền thống (cấu hình, topology, alert metadata)
- **TimescaleDB/PostgreSQL** → dữ liệu time-series hiệu năng cao

### 2.1. Cấu Trúc Database

#### 2.1.1. Relational Database (SQLite — `TunnelDbContext`)

| Table | Mục Đích | Key Fields |
|-------|---------|-----------|
| `Stations` | Trạm giám sát | Id, Name, District, CenterLng/Lat, bounding box |
| `Lines` | Tuyến cống | Id, StationId, Code, Name, StartLng/Lat, EndLng/Lat, Length |
| `Nodes` | Nút giám sát (máy tính nhúng) | Id, LineId, Code, Lng/Lat, NodeByteId, Status, BatteryLevel, RSSI, CameraId |
| `Sensors` | Cảm biến gắn trên nút | Id, NodeId, SensorByteId, Type, Unit, WarningThreshold, CurrentValue |
| `Cameras` | Camera IP | Id, NodeId, StreamUrl, Resolution, Fps, Codec, IrEnabled |
| `CameraSnapshots` | Ảnh chụp từ camera | Id, CameraId, Timestamp, FilePath, DetectionType, Confidence |
| `VideoClips` | Đoạn video ghi lại | Id, CameraId, StartTime, EndTime, FilePath, TriggerReason |
| `Alerts` | Cảnh báo | Id, Title, Severity, Status, NodeId, SensorId, SensorValue, Threshold |
| `AlertNotes` | Nhật ký xử lý cảnh báo | Id, AlertId, Content, AuthorId |
| `Users` | Người dùng hệ thống | Id, Username, PasswordHash, Role |
| `DevicePendingJoins` | Yêu cầu gia nhập thiết bị | Id, MacAddress, HardwareId, FirmwareVersion, Status |

#### 2.1.2. TimescaleDB Hypertables (PostgreSQL — `TimeSeriesDbContext`)

| Hypertable | Chunk Interval | Chính Sách | Mục Đích |
|-----------|---------------|------------|---------|
| `sensor_readings` | 1 ngày | Nén sau 7 ngày, giữ 1 năm | Giá trị cảm biến theo thời gian |
| `sensor_frames_raw` | 1 giờ | Giữ 7 ngày | Raw binary frame log (debug) |
| `node_heartbeats` | 6 giờ | Giữ 30 ngày | Lịch sử heartbeat thiết bị |
| `camera_events` | 1 ngày | Nén sau 7 ngày, giữ 6 tháng | Sự kiện phát hiện AI camera |

#### 2.1.3. Continuous Aggregates (Materialized Views)

| View | Bucket | Cập Nhật | Mục Đích |
|-----|--------|---------|---------|
| `sensor_stats_hourly` | 1 giờ | Mỗi giờ | avg/min/max, warning/critical count theo giờ |
| `sensor_stats_daily` | 1 ngày | Mỗi ngày | Tổng hợp theo ngày (rollup) |

#### 2.1.4. Views Tiện Ích

| View | Mục Đích |
|-----|---------|
| `v_sensor_status` | Trạng thái hiện tại tất cả cảm biến (dashboard real-time) |
| `v_active_alerts` | Cảnh báo đang hoạt động (chưa resolved/closed) |

### 2.2. Quan Hệ Dữ Liệu

```
Station ──1:N──> Line ──1:N──> Node ──1:N──> Sensor
                                     │
                                     └──0:1──> CameraDevice ──1:N──> CameraSnapshot
                                                                ──1:N──> VideoClip

Node ──1:N──> Alert
Sensor ──1:N──> Alert

Alert ──1:N──> AlertNote
Alert ──N:1──> User (acknowledged_by / resolved_by)
User ──1:N──> AlertNote (author_id)
```

### 2.3. File Schema SQL

| File | Nội Dung |
|------|---------|
| `docs/database_schema.sql` | Full schema PostgreSQL + TimescaleDB (enums, tables, hypertables) |
| `docs/view_schema.sql` | Continuous aggregates + views |
| `docs/seed.sql` | Dữ liệu mẫu (station_config, lines, nodes, sensors) |
| `sql/tunnel_security_relational.sql` | Schema SQL Server + seed tương thích |
| `docker/timescaledb-init.sql` | Init script cho Docker TimescaleDB |

---

## 3. ERD — Mô Hình Thực Thể Quan Hệ

### 3.1. Mô Hình Domain Chính

```
┌─────────────┐       ┌─────────────┐       ┌─────────────┐       ┌─────────────┐
│   Station   │1────N→│    Line     │1────N→│    Node     │1────N→│   Sensor    │
├─────────────┤       ├─────────────┤       ├─────────────┤       ├─────────────┤
│ Id (PK)     │       │ Id (PK)     │       │ Id (PK)     │       │ Id (PK)     │
│ Name        │       │ StationId   │       │ LineId      │       │ NodeId      │
│ District    │       │ Code        │       │ Code        │       │ SensorByteId│
│ CenterLng   │       │ Name        │       │ Name        │       │ Type (enum) │
│ CenterLat   │       │ StartLng    │       │ Lng         │       │ Name        │
│ MinLng      │       │ StartLat    │       │ Lat         │       │ Unit        │
│ MinLat      │       │ EndLng      │       │ MapX        │       │ WarnThresh  │
│ MaxLng      │       │ EndLat      │       │ MapY        │       │ CritThresh  │
│ MaxLat      │       │ Length      │       │ NodeByteId  │       │ CurrentValue│
│ CreatedAt   │       │ Status      │       │ DistanceM   │       │ CurrentLevel│
│ UpdatedAt   │       │ CreatedAt   │       │ HardwareId  │       │ IsEnabled   │
└─────────────┘       │ UpdatedAt   │       │ Mac         │       │ CreatedAt   │
                      └─────────────┘       │ IpAddress   │       └─────────────┘
                                            │ FirmwareVer │
                                            │ IsHub       │
                                            │ BatteryLevel│
                                            │ RSSI        │
                                            │ Status      │
                                            │ CameraId    │
                                            │ CreatedAt   │
                                            └──────┬──────┘
                                                   │
                                                   │ 0..1
                                                   ▼
                                      ┌─────────────────────┐
                                      │   CameraDevice      │
                                      ├─────────────────────┤
                                      │ Id (PK)             │
                                      │ NodeId              │
                                      │ Name                │
                                      │ StreamUrl           │
                                      │ Protocol (enum)     │
                                      │ Status              │
                                      │ Resolution          │
                                      │ Fps                 │
                                      │ Codec               │
                                      │ IrEnabled           │
                                      │ HdrEnabled          │
                                      │ CreatedAt           │
                                      └─────────┬───────────┘
                                                │ 1
                                                │
                          ┌─────────────────────┼─────────────────────┐
                          │ 1:N                │ 1:N                 │ 1:N
                          ▼                     ▼                     ▼
                ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
                │ CameraSnapshot   │  │    VideoClip     │  │  CameraEventTs   │
                ├──────────────────┤  ├──────────────────┤  ├──────────────────┤
                │ Id (PK)          │  │ Id (PK)          │  │ Time (PK)        │
                │ CameraId         │  │ CameraId         │  │ CameraId         │
                │ Timestamp        │  │ StartTime        │  │ NodeId           │
                │ FilePath         │  │ EndTime          │  │ EventType (enum) │
                │ ThumbnailPath    │  │ FilePath         │  │ Confidence       │
                │ DetectionType    │  │ SizeBytes        │  │ ObjectClass      │
                │ Confidence       │  │ TriggerReason    │  │ IsIntrusion      │
                │ Metadata (JSON)  │  │ CreatedAt        │  │ BboxX/Y/W/H      │
                └──────────────────┘  └──────────────────┘  │ ImagePath        │
                                                            │ GeneratedAlert   │
                                                            │ AlertId          │
                                                            └──────────────────┘
```

### 3.2. Mô Hình Alert & User

```
┌─────────────┐       ┌─────────────┐       ┌─────────────┐
│    Alert    │1────N→│  AlertNote  │       │    User     │
├─────────────┤       ├─────────────┤       ├─────────────┤
│ Id (PK)     │       │ Id (PK)     │       │ Id (PK)     │
│ Title       │       │ AlertId     │       │ Username    │
│ Description │       │ Content     │       │ PasswordHash│
│ Category    │       │ AuthorId    │←─N:1──│ FullName    │
│ Severity    │       │ CreatedAt   │       │ Email       │
│ Status      │       └─────────────┘       │ Role (enum) │
│ NodeId      │                             │ IsActive    │
│ SensorId    │                             │ CreatedAt   │
│ CameraId    │                             └─────────────┘
│ StationId   │
│ SensorValue │
│ Threshold   │
│ CreatedAt   │
│ AckBy     ←─┼────N:1── User
│ ResolvedBy←─┼────N:1── User
└─────────────┘
```

### 3.3. Mô Hình Time-Series (TimescaleDB Hypertables)

```
┌─────────────────────────────┐
│     sensor_readings         │  ← partition key: time (1-day chunks)
├─────────────────────────────┤
│ Time (PK, TIMESTAMPTZ)      │
│ Id (PK, BIGSERIAL)          │
│ NodeId (TEXT)               │
│ SensorId (TEXT)             │
│ NodeByteId (SMALLINT)       │
│ SensorByteId (SMALLINT)     │
│ Value (DOUBLE PRECISION)    │
│ Seq (SMALLINT)              │
│ Level (reading_level enum)  │
│ Crc8Ok (BOOLEAN)            │
├─────────────────────────────┤
│ Compression: segmentby=(node_id,sensor_id) orderby=(time DESC) 7d
│ Retention: 1 year
└─────────────────────────────┘

┌─────────────────────────────┐
│     node_heartbeats         │  ← partition key: time (6-hour chunks)
├─────────────────────────────┤
│ Time (PK, TIMESTAMPTZ)      │
│ NodeId (TEXT)               │
│ NodeByteId (SMALLINT)       │
│ Status (node_status enum)   │
│ BatteryLevel (REAL)         │
│ Rssi (SMALLINT)             │
│ IpAddress (TEXT)            │
│ FirmwareVersion (TEXT)      │
│ UptimeSec (INTEGER)         │
├─────────────────────────────┤
│ Retention: 30 days
└─────────────────────────────┘

┌─────────────────────────────┐
│     camera_events           │  ← partition key: time (1-day chunks)
├─────────────────────────────┤
│ Time (PK, TIMESTAMPTZ)      │
│ CameraId (TEXT)             │
│ NodeId (TEXT)               │
│ EventType (cam_event_type)  │
│ Confidence (REAL)           │
│ ObjectClass (TEXT)          │
│ IsIntrusion (BOOLEAN)       │
│ BboxX/Y/W/H (SMALLINT)      │
│ ImagePath (TEXT)            │
│ GeneratedAlert (BOOLEAN)    │
│ AlertId (TEXT)              │
├─────────────────────────────┤
│ Compression: segmentby=(camera_id,node_id) 7d
│ Retention: 6 months
└─────────────────────────────┘
```

---

## 4. Kiến Trúc Hệ Thống

### 4.1. Tổng Thể (Overall Architecture)

```
                                ┌─────────────┐
                                │   Center    │
                                │  (WinUI 3)  │
                                ├─────────────┤
                                │ SignalR     │
                                │ Client +    │
                                │ REST Client │
                                └──────┬──────┘
                                       │ HTTP / SignalR
                                       ▼
┌─────────────────────────────────────────────────────────────────┐
│                    BACKEND (ASP.NET Core 8)                      │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                    API LAYER                              │   │
│  │  ┌─────────────────┐ ┌────────────────┐ ┌─────────────┐ │   │
│  │  │ StationsCtrl    │ │ ReadingsCtrl   │ │ AlertsCtrl  │ │   │
│  │  │ SensorsCtrl     │ │ CamerasCtrl    │ │ DeviceJoin  │ │   │
│  │  └─────────────────┘ └────────────────┘ └─────────────┘ │   │
│  │  ┌──────────────────────────────────────────────────┐   │   │
│  │  │           SignalR Hub: /hubs/sensors             │   │   │
│  │  │   (JoinStation, LeaveStation, SensorUpdated)     │   │   │
│  │  └──────────────────────────────────────────────────┘   │   │
│  │  ┌──────────────────────────────────────────────────┐   │   │
│  │  │           WebSocket Endpoints                     │   │   │
│  │  │   /ws/device — binary frame ingestion            │   │   │
│  │  │   /ws/join   — device join request               │   │   │
│  │  └──────────────────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                  SERVICES LAYER                           │   │
│  │                                                           │   │
│  │  ┌────────────────┐  ┌────────────────┐  ┌─────────────┐ │   │
│  │  │ SensorBroadcast│  │ SensorBroadcast│  │ DeviceHealth│ │   │
│  │  │ er (Scoped)    │  │ Queue (Single) │  │ Service     │ │   │
│  │  └───────┬────────┘  └───────┬────────┘  └─────────────┘ │   │
│  │          │                   │                            │   │
│  │  ┌───────┴───────────────────┴────────────────────────┐   │   │
│  │  │         Channel<T> (Bounded Capacity=5000)          │   │   │
│  │  │         DropOldest policy                           │   │   │
│  │  └────────────────────────────────────────────────────┘   │   │
│  │                                                           │   │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────┐            │   │
│  │  │ MqttIngest │ │ HttpPoll   │ │ WsIngest   │            │   │
│  │  │ (Disabled) │ │ (Disabled) │ │ (Disabled) │            │   │
│  │  └────────────┘ └────────────┘ └────────────┘            │   │
│  │                                                           │   │
│  │  ┌────────────────┐ ┌────────────────┐                   │   │
│  │  │ VideoCapture   │ │ PeriodicClip   │                   │   │
│  │  │ Service        │ │ Service        │                   │   │
│  │  └────────────────┘ └────────────────┘                   │   │
│  │  ┌────────────────┐ ┌────────────────┐                   │   │
│  │  │ DeviceJoinReg  │ │ DataRetention  │                   │   │
│  │  │ istry (Single) │ │ Service        │                   │   │
│  │  └────────────────┘ └────────────────┘                   │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                  CACHING LAYER                            │   │
│  │  IMemoryCache: SensorRepository (10K entries, 5-min TTL)  │   │
│  │  CameraFrameBuffer (Singleton — latest frame per camera)  │   │
│  │  VideoSourceRegistry (Singleton — camera→video file map)  │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                  DATA LAYER                               │   │
│  │                                                           │   │
│  │  ┌──────────────────┐    ┌──────────────────────────┐    │   │
│  │  │  TunnelDbContext │    │  TimeSeriesDbContext      │    │   │
│  │  │  (SQLite)        │    │  (PostgreSQL+TimescaleDB) │    │   │
│  │  │                 │    │                           │    │   │
│  │  │  Stations       │    │  sensor_readings          │    │   │
│  │  │  Lines          │    │  sensor_frames_raw        │    │   │
│  │  │  Nodes          │    │  node_heartbeats          │    │   │
│  │  │  Sensors        │    │  camera_events            │    │   │
│  │  │  Alerts         │    │                           │    │   │
│  │  │  Cameras        │    │  Continuous Aggregates:   │    │   │
│  │  │  Users          │    │  sensor_stats_hourly      │    │   │
│  │  │  DevicePending  │    │  sensor_stats_daily       │    │   │
│  │  └──────────────────┘    └──────────────────────────┘    │   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
                │                          │
                │ HTTP / SignalR            │ TCP/IP / RTSP
                ▼                          ▼
       ┌──────────────────┐    ┌──────────────────────┐
       │ Station (WinUI 3) │    │  Thiết bị thực tế    │
       ├──────────────────┤    │  (Sensor Nodes,       │
       │ Monitoring Dash  │    │   Radar Nodes,        │
       │ Data Page        │    │   Camera Nodes)       │
       │ Live Video       │    │                       │
       │ Alerts           │    │  Protocol: Binary     │
       │ Configuration    │    │  Frame 32-byte        │
       └──────────────────┘    │  over WebSocket       │
                               └──────────────────────┘
```

### 4.2. Kiến Trúc Backend Chi Tiết

#### 4.2.1. Controllers (6 Controllers, RESTful API)

| Controller | Route Prefix | Endpoints | Chức Năng |
|-----------|-------------|-----------|----------|
| **StationsController** | `/api/stations` | 8 endpoints | Quản lý trạm, tuyến, nút, sensor (có GeoJSON) |
| **SensorsController** | `/api/sensors` | 3 endpoints | CRUD sensor, push measurement |
| **ReadingsController** | `/api/readings` | 6 endpoints | Truy vấn time-series readings, stats, aggregates, heartbeats |
| **CamerasController** | `/api/cameras` | 5+ endpoints | CRUD camera, snapshot, video clip, MJPEG stream |
| **AlertsController** | `/api/alerts` | 5 endpoints | CRUD alert, acknowledge, resolve |
| **DeviceJoinController** | `/api/device-joins` | 3 endpoints | Phê duyệt/từ chối yêu cầu gia nhập thiết bị |

#### 4.2.2. SignalR Hub (`/hubs/sensors`)

| Method | Tham Số | Mục Đích |
|--------|---------|---------|
| `JoinStation(stationId)` | string | Subscribe vào nhóm station |
| `LeaveStation(stationId)` | string | Rời nhóm station |
| `JoinNode(nodeId)` | string | Subscribe vào node cụ thể |
| `LeaveNode(nodeId)` | string | Rời node |

**Server → Client Events:**
- `SensorUpdated` — `SensorBroadcastMessage` (khi có reading mới)
- `NewJoinRequest` — Thông báo thiết bị yêu cầu gia nhập
- `DeviceStatusChanged` — Thay đổi trạng thái thiết bị

#### 4.2.3. Services Layer

| Service | Loại | Chức Năng |
|--------|------|----------|
| **SensorBroadcaster** | Scoped | Xử lý reading: cache-first lookup → persist → enqueue broadcast |
| **SensorBroadcastQueue** | Singleton + HostedService | Channel-based fan-out: dequeues → SignalR `SendAsync("SensorUpdated")` |
| **DeviceSimulatorWsHandler** | Static | Parse 32-byte binary frame từ WebSocket simulator |
| **DeviceJoinWsHandler** | Static | Xử lý JOIN_REQUEST frame (20 bytes) qua WebSocket |
| **DeviceJoinRegistry** | Singleton | Map requestId → TaskCompletionSource cho operator decision |
| **VideoCaptureService** | HostedService | Chụp frame từ camera |
| **VideoClipService** | Singleton | Ghi video clip |
| **PeriodicClipService** | HostedService | Ghi clip định kỳ (VD: 5s clip mỗi 1 phút) |
| **DeviceHealthService** | HostedService | PeriodicTimer: kiểm tra offline nodes, ghi heartbeat |
| **DataRetentionService** | HostedService | Chạy 2AM UTC: xóa dữ liệu cũ (drop_chunks + SQL) |
| **MqttIngestionService** | HostedService (Disabled) | Ingestion MQTT |
| **HttpPollingService** | HostedService (Disabled) | Polling HTTP |
| **WebSocketIngestionService** | HostedService (Disabled) | Ingestion WebSocket |

#### 4.2.4. Caching Layer

| Cache | Kích Thước | TTL | Mục Đích |
|-------|-----------|-----|---------|
| `IMemoryCache` (SensorRepository) | 10,000 entries | 5 phút | Cache sensor/node metadata — tránh 2 DB queries mỗi reading |
| `CameraFrameBuffer` | Singleton | — | Buffer frame mới nhất mỗi camera |
| `VideoSourceRegistry` | Singleton | — | Map cameraId → file path video |

### 4.3. Kiến Trúc Station App Chi Tiết

```
┌─────────────────────────────────────────────────────────────────┐
│                    STATION (WinUI 3 App)                        │
│                                                                  │
│  ┌──────────── ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ┐  │
│  │                     VIEW LAYER                             │  │
│  │                                                            │  │
│  │  MonitoringDashboard  DataPage  DevicesPage  LiveVideo    │  │
│  │  SensorChartsPage    AlertsPage  AnalyticsReportPage       │  │
│  │  ConfigurationPage   SnapshotGallery  UserManagement      │  │
│  │                                                            │  │
│  │  📦 Dialogs (10): AddNode, AlertVideo, DeviceControl,     │  │
│  │  DeviceData, EditDevice, EditNode, NodeDetail, Playback,  │  │
│  │  SensorConfig, SensorDetail                                │  │
│  │                                                            │  │
│  │  🎮 Controls: CameraVideoControl, RadarChartControl       │  │
│  └──────────────────────────┬─────────────────────────────────┘  │
│                             │ Data Binding (MVVM)               │
│  ┌──────────────────────────┴─────────────────────────────────┐  │
│  │                  VIEWMODEL LAYER                           │  │
│  │  MonitoringDashboardVM  AlertsVM  DevicesVM               │  │
│  │  LiveVideoVM  SensorChartsVM  ConfigurationVM              │  │
│  │  AnalyticsReportVM  DataVM  UserManagementVM              │  │
│  └──────────────────────────┬─────────────────────────────────┘  │
│                             │                                    │
│  ┌──────────────────────────┴─────────────────────────────────┐  │
│  │                  SERVICES LAYER                            │  │
│  │                                                            │  │
│  │  ┌────────────────┐  ┌────────────────┐                   │  │
│  │  │   RealData     │  │   MockData     │  DataService      │  │
│  │  │   Service      │  │   Service      │  Locator          │  │
│  │  │ (SignalR+REST) │  │  (built-in sim)│  (env var switch) │  │
│  │  └───────┬────────┘  └────────┬───────┘                   │  │
│  │          │                    │                            │  │
│  │  ┌───────┴────────────────────┴────────────────────────┐  │  │
│  │  │  ApiSensorClient (SignalR Client — real-time updates) │  │  │
│  │  │  SimulationApiServer (HTTP+WS — cho web simulator)   │  │  │
│  │  │  StationConfigService (cấu hình trạm)                │  │  │
│  │  │  ThemeService (UI theme)                             │  │  │
│  │  │  ReportExporter (QuestPDF + ClosedXML)               │  │  │
│  │  └──────────────────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────────────────┘  │  │
│                                                                  │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │                  CONFIG LAYER                             │   │
│  │  .env → EnvironmentConfig: STATION_ID, BACKEND_URL, MAPBOX│   │
│  └──────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────┘
```

#### 4.3.1. Station Views (10 Pages)

| Page | Mục Đích |
|------|---------|
| `MonitoringDashboardPage` | Dashboard chính: charts, node status, alerts |
| `DataPage` | Dữ liệu cảm biến real-time |
| `DevicesPage` | Quản lý thiết bị |
| `LiveVideoPage` | Xem camera trực tiếp |
| `SensorChartsPage` | Biểu đồ lịch sử sensor |
| `AlertsPage` | Danh sách và quản lý cảnh báo |
| `AnalyticsReportPage` | Báo cáo và phân tích (PDF/Excel) |
| `ConfigurationPage` | Cấu hình trạm |
| `SnapshotGalleryPage` | Thư viện ảnh chụp camera |
| `UserManagementPage` | Quản lý người dùng |

#### 4.3.2. Station Models

| Model | Mục Đích |
|-------|---------|
| `StationConfig` | Cấu hình trạm (area, route, zone, GPS, Center URL) |
| `TunnelLine` | Tuyến cống (LineId, LineName, Nodes) |
| `TunnelNode` | Nút giám sát (NodeId, NodeName, LineId) |
| `Device` | Thiết bị (Camera, Sensor, Radar) |
| `SecurityMapNode` | Node trên bản đồ (Secure/Warning/Critical/Offline) |
| `CameraDetection` | Phát hiện từ camera |
| `CameraStream` | Luồng camera |

---

## 5. Workflow — Luồng Dữ Liệu

### 5.1. Luồng Dữ Liệu Từ Sensor → Hiển Thị

```
┌──────────┐    ┌──────────┐    ┌───────────┐    ┌───────────┐    ┌──────────┐
│ Hardware  │───→│ WebSocket│───→│ Sensor    │───→│ Broadcast │───→│ Station  │
│ Simulator │    │ Endpoint │    │ Broadcaster│   │ Queue     │    │ (WinUI)  │
│ / SimDev  │    │ /ws/device│   │ (Scoped)   │   │ (Channel)  │    │ SignalR  │
└──────────┘    └──────────┘    └─────┬──────┘   └─────┬─────┘    └──────────┘
                                      │                │
                                      │                │ SensorUpdated event
                                      ▼                ▼
                              ┌───────────┐    ┌──────────────┐
                              │ Repository │    │  IHubContext  │
                              │ (Cache)    │    │  SendAsync   │
                              └─────┬─────┘    └──────────────┘
                                    │
                                    ▼
                            ┌───────────────┐
                            │ TimescaleDB   │
                            │ hypertable    │
                            │ sensor_readings│
                            └───────────────┘
```

**Mô tả chi tiết từng bước:**

1. **Thiết bị gửi dữ liệu** — 32-byte binary frame qua WebSocket `/ws/device`
2. **DeviceSimulatorWsHandler** — parse frame:
   - Tìm START byte `0xAA`
   - Đọc 6 giá trị float32 (temp, hum, light, radar_dist, radar_speed, radar_energy) + uint16 (vl53_height)
   - Kiểm tra CRC-16/CCITT-FALSE
   - Gọi `SensorBroadcaster.ProcessReadingAsync()` cho mỗi sensor
3. **SensorBroadcaster**:
   - **Cache-first**: lấy metadata sensor từ IMemoryCache (SensorRepository)
   - Nếu cache miss → query SQLite → cache kết quả (5 phút)
   - Tính toán level (Normal/Warning/Critical) dựa trên threshold
   - **Enqueue broadcast message** → `SensorBroadcastQueue` (Channel)
   - **Persist** (best-effort):
     - Update `CurrentValue` trong SQLite (ExecuteUpdateAsync)
     - Insert vào TimescaleDB `sensor_readings` hypertable
4. **SensorBroadcastQueue** (Singleton BackgroundService):
   - Drained từ Channel (capacity 5000, DropOldest)
   - Gọi `IHubContext<SensorHub>.Clients.All.SendAsync("SensorUpdated", msg)`
5. **Station App** (RealDataService):
   - `ApiSensorClient` (SignalR Client) nhận event `SensorUpdated`
   - Cập nhật `SimulatedSensor.CurrentValue`
   - Trigger `SensorTick` event → UI thread update via DispatcherQueue
   - Nếu vượt ngưỡng → tạo Alert local

### 5.2. Luồng JOIN Thiết Bị Mới

```
┌──────────┐    ┌───────────┐    ┌──────────────┐    ┌───────────┐    ┌──────────┐
│ Hardware  │───→│ /ws/join  │───→│ DeviceJoin   │───→│ DeviceJoin│───→│ Station  │
│ (20 bytes)│    │ WebSocket │    │ Handler      │    │ Registry  │    │ (SignalR)│
└──────────┘    └───────────┘    └──────┬───────┘    └─────┬─────┘    └──────────┘
                                        │                  │
                                        ▼                  │
                                ┌──────────────┐          │
                                │ Lưu vào DB   │          │
                                │ DevicePending│          │
                                │ Join         │          │
                                └──────────────┘          │
                                        │                  │
                                        ▼                  ▼
                                ┌──────────────────────────────┐
                                │  Operator quyết định         │
                                │  POST /api/device-joins/{id}/│
                                │  approve  hoặc  reject       │
                                └──────────────┬───────────────┘
                                               │
                                               ▼
                                ┌──────────────────────────────┐
                                │  DeviceJoinRegistry          │
                                │  TryDecide → TCS.SetResult   │
                                └──────────────┬───────────────┘
                                               │
                                               ▼
                                ┌──────────────────────────────┐
                                │  JOIN_RESPONSE (8 bytes)     │
                                │  gửi lại thiết bị qua WS     │
                                │  0x01 = Accept + NodeByteId  │
                                │  0x00 = Reject               │
                                └──────────────────────────────┘
```

**Frame JOIN_REQUEST (20 bytes, Device → Server):**

```
[0xAA][0x20][MAC×6][HW_ID×4][FW_MAJ][FW_MIN][FW_PAT][0x00][CRC×2][0x00][0xBB]
```

**Frame JOIN_RESPONSE (8 bytes, Server → Device):**

```
[0xAA][0x21][STATUS][NODE_ID][CRC×2][0x00][0xBB]
```

### 5.3. Luồng Xử Lý Cảnh Báo

```
Sensor vượt ngưỡng (Warning/Critical)
       │
       ▼
┌──────────────────┐
│ SensorBroadcaster│
│ ComputeLevel()   │
│ level = "Warning"│  hoặc "Critical"
└────────┬─────────┘
         │
         ▼
┌─────────────────────────────────────┐
│ Frontend (Station/Center) tạo Alert│
│ local khi nhận SensorUpdated event │
└──────────────────┬──────────────────┘
                   │
                   ▼
┌─────────────────────────────────────┐
│ POST /api/alerts (tạo alert trên DB)│
└──────────────────┬──────────────────┘
                   │
         ┌─────────┴─────────┐
         ▼                   ▼
┌──────────────────┐  ┌──────────────────┐
│ Operator Ack     │  │ Auto-resolve     │
│ POST .../acknowl-│  │ (khi sensor về   │
│ edge             │  │ mức Normal)      │
└────────┬─────────┘  └──────────────────┘
         │
         ▼
┌──────────────────┐
│ POST .../resolve  │
│ + AlertNote       │
└──────────────────┘
```

### 5.4. Luồng Device Health Check

```
DeviceHealthService (HostedService)
       │
       ▼
PeriodicTimer (configurable interval, mặc định 30s)
       │
       ▼
┌──────────────────────────────────────────────┐
│ CheckAllNodesAsync():                         │
│                                               │
│  foreach node in DB:                          │
│    query last reading time từ TimescaleDB     │
│      + fallback SQLite.LastReading            │
│                                               │
│    if lastReading < offlineTimeout → Offline  │
│    else → Online (giữ nguyên nếu đang Online) │
│                                               │
│    if status changed → SignalR broadcast      │
│    ghi heartbeat → node_heartbeats hypertable │
└──────────────────────────────────────────────┘
```

---

## 6. Pipeline — Xử Lý Dữ Liệu

### 6.1. Data Ingestion Pipeline

```
                        ┌──────────────────────┐
                        │  Multiple Sources     │
                        │                       │
  ┌────────────┐   ┌───│ WebSocket /ws/device   │
  │  SimDevice │   │   │ MQTT (disabled)       │
  │  (.NET 8)  │───┤   │ HTTP Poll (disabled)  │
  └────────────┘   │   │ WebSocket (disabled)  │
                    │   └──────────────────────┘
  ┌────────────┐   │
  │  Browser   │───┤
  │ Simulators  │   │
  └────────────┘   │
                    │   ┌──────────────────────┐
  ┌────────────┐   │   │ Device Join /ws/join  │
  │  Hardware  │───┘   └──────────────────────┘
  │  Devices   │
  └────────────┘
        │
        ▼
┌────────────────────────────────────────────────────┐
│                BACKEND PIPELINE                     │
│                                                      │
│  Step 1: Parse & Validate                            │
│  ┌────────────────────────────────────────────────┐ │
│  │ • Binary frame: START 0xAA, STOP 0xBB          │ │
│  │ • CRC-16/CCITT-FALSE verification              │ │
│  │ • Extract sensors: float32 LE, uint32, uint16  │ │
│  │ • Sequence check (0–255, detect frame loss)    │ │
│  └────────────────────────────────────────────────┘ │
│                                                      │
│  Step 2: Cache Resolution                            │
│  ┌────────────────────────────────────────────────┐ │
│  │ • IMemoryCache lookup (5-min TTL)              │ │
│  │ • Cache miss → SQLite query → populate cache   │ │
│  │ • Fallback: infer type từ sensor ID suffix     │ │
│  │   (-TEMP, -HUM, -LIGHT, -RADAR, -VIB, -WATER) │ │
│  └────────────────────────────────────────────────┘ │
│                                                      │
│  Step 3: Level Classification                        │
│  ┌────────────────────────────────────────────────┐ │
│  │ • value < WarningThreshold → Normal            │ │
│  │ • value ≥ WarningThreshold → Warning           │ │
│  │ • value ≥ CriticalThreshold → Critical         │ │
│  └────────────────────────────────────────────────┘ │
│                                                      │
│  Step 4: Broadcast (Non-blocking Enqueue)            │
│  ┌────────────────────────────────────────────────┐ │
│  │ • Channel<SensorBroadcastMessage>.TryWrite()   │ │
│  │ • Capacity 5000, DropOldest policy             │ │
│  │ • Không chờ persist → giảm latency            │ │
│  └────────────────────────────────────────────────┘ │
│                                                      │
│  Step 5: Persist (Best-effort, parallel)             │
│  ┌────────────────────────────────────────────────┐ │
│  │ • SQLite: UPDATE CurrentValue (ExecuteUpdate)  │ │
│  │ • TimescaleDB: INSERT sensor_readings          │ │
│  │ • Nếu TimescaleDB lỗi → skip (không block)    │ │
│  │ • Raw frame log: INSERT sensor_frames_raw      │ │
│  └────────────────────────────────────────────────┘ │
│                                                      │
│  Step 6: SignalR Fan-out (Background Queue Worker)   │
│  ┌────────────────────────────────────────────────┐ │
│  │ • Dequeue từ Channel (SingleReader)            │ │
│  │ • await Hub.Clients.All.SendAsync              │ │
│  │ • "SensorUpdated" event → tất cả Station App   │ │
│  └────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────┘
```

### 6.2. Broadcast Queue Design

```
┌─────────────────────────────────────────────────────────┐
│              SensorBroadcastQueue                        │
│                                                          │
│  ┌──────────────────────────────────────────────────┐   │
│  │  Channel<SensorBroadcastMessage>                  │   │
│  │  BoundedChannelOptions:                           │   │
│  │    Capacity        = 5000                          │   │
│  │    FullMode        = DropOldest                    │   │
│  │    SingleReader    = true                          │   │
│  │    SingleWriter    = false                         │   │
│  │                                                    │   │
│  │  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐   │   │
│  │  │ Msg1 │→│ Msg2 │→│ Msg3 │→│ ...  │→│ MsgN │   │   │
│  │  └──────┘ └──────┘ └──────┘ └──────┘ └──────┘   │   │
│  └──────────────────────────┬───────────────────────┘   │
│                             │                            │
│                     ┌───────▼────────┐                  │
│                     │  SingleReader   │                  │
│                     │  ReadAllAsync   │                  │
│                     └───────┬────────┘                  │
│                             │                            │
│                     ┌───────▼────────┐                  │
│                     │  foreach msg    │                  │
│                     │  Hub.Clients    │                  │
│                     │  .All.SendAsync │                  │
│                     │  ("SensorUpdat- │                  │
│                     │   ed", msg)     │                  │
│                     └────────────────┘                  │
└─────────────────────────────────────────────────────────┘
```

### 6.3. Data Retention Pipeline

```
DataRetentionService (HostedService)
       │
       ▼
Chạy lúc 2:00 AM UTC mỗi ngày
       │
       ▼
┌──────────────────────────────────────────────────────┐
│  TimescaleDB: drop_chunks()                          │
│  • Xóa chunk files (O(1), không scan row)           │
│  • sensor_readings: giữ cấu hình DataRetention:Days  │
│  • sensor_frames_raw: giữ 7 ngày                     │
│  • node_heartbeats: giữ 30 ngày                      │
│  • camera_events: giữ 180 ngày (6 tháng)             │
├──────────────────────────────────────────────────────┤
│  SQLite: ExecuteDeleteAsync()                        │
│  • CameraSnapshots: xóa bản ghi cũ hơn retention     │
│  • VideoClips: xóa bản ghi cũ hơn retention          │
└──────────────────────────────────────────────────────┘
```

### 6.4. Compression Pipeline (TimescaleDB)

```
sensor_readings hypertable
       │
       ▼
┌─────────────────────────────────────┐
│  Chunk interval: 1 ngày             │
│                                     │
│  ┌────┐ ┌────┐ ┌────┐ ┌────┐      │
│  │ D1  │ │ D2  │ │ D3  │ │ D4  │  │
│  └────┘ └────┘ └────┘ └────┘      │
│                                     │
│  Sau 7 ngày → Compression Policy    │
│  ┌────────────────────────────┐    │
│  │ Columnar compression       │    │
│  │ segmentby: node_id,sen_id  │    │
│  │ orderby: time DESC         │    │
│  │ Tiết kiệm ~90% dung lượng │    │
│  └────────────────────────────┘    │
└─────────────────────────────────────┘
```

### 6.5. Video Pipeline

```
┌──────────────┐
│ Camera       │
│ (RTSP/HTTP/  │
│  WebSocket)  │
└──────┬───────┘
       │
       ▼
┌──────────────────────────────────────────────────────┐
│              VIDEO PIPELINE                           │
│                                                        │
│  ┌────────────────────┐  ┌────────────────────────┐   │
│  │ VideoCaptureService│  │ PeriodicClipService     │   │
│  │ (HostedService)     │  │ (HostedService)         │   │
│  │                     │  │                         │   │
│  │ Chụp frame định kỳ  │  │ Ghi clip định kỳ       │   │
│  │ → CameraFrameBuffer │  │ VD: 5s mỗi 1 phút     │   │
│  │ → Lưu snapshot      │  │ → VideoClipService     │   │
│  └────────────────────┘  └────────────────────────┘   │
│                                                        │
│  ┌────────────────────────────────────────────────┐   │
│  │ CameraFrameBuffer (Singleton)                   │   │
│  │ • Map cameraId → latest frame (byte[])          │   │
│  │ • Dùng cho MJPEG stream push mode               │   │
│  └────────────────────────────────────────────────┘   │
│                                                        │
│  ┌────────────────────────────────────────────────┐   │
│  │ CameraFrameGenerator                             │   │
│  • Tạo synthetic JPEG frame cho simulation         │   │
│  └────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────┘
```

---

## 7. Phân Tích Chi Tiết Station & Backend

### 7.1. Station — Kiến Trúc Xử Lý Dữ Liệu

Station App sử dụng **2 chế độ dữ liệu** được chọn qua biến môi trường `DATA_SOURCE`:

| Chế Độ | Service | Mô Tả |
|--------|---------|-------|
| **Real** (mặc định) | `RealDataService` | Kết nối Backend thật: REST fetch topology + SignalR real-time updates |
| **Mock** | `MockDataService` | Tự sinh dữ liệu local: random-walk sensors, synthetic camera frames |

**Cơ chế fallback:** `RealDataService` tự động fallback về `MockDataService` khi Backend không available.

**RealDataService flow:**

```
Start()
  │
  ▼
InitializeAsync()
  │
  ├── LoadTopologyWithRetryAsync()
  │     └── GET /api/stations/{ST01}
  │           └── Parse JSON → List<SimulatedSensor>, List<SimulatedCamera>
  │
  ├── TopologyLoaded event → UI update
  │
  └── ConnectSignalRAsync()
        └── ApiSensorClient (SignalR client)
              │
              ├── On "SensorUpdated" → OnSensorUpdated()
              │     ├── Update sensor value
              │     ├── Trigger SensorTick event (→ charts, dashboard)
              │     └── If anomaly → TryGenerateAlert()
              │
              └── On "NewJoinRequest" → notify DevicesPage
```

### 7.2. Backend — Startup Flow

```
Program.cs
  │
  ▼
ConfigureServices:
  ├── AddControllers() + AddSignalR()
  ├── AddDbContext<TunnelDbContext> (SQLite)
  ├── AddDbContext<TimeSeriesDbContext> (PostgreSQL+TimescaleDB, flag-gated)
  ├── AddMemoryCache(10K) + SensorRepository
  ├── AddScoped<SensorBroadcaster>
  ├── AddSingleton<SensorBroadcastQueue>
  ├── AddSingleton<DeviceJoinRegistry>
  ├── AddSingleton<CameraFrameBuffer>
  ├── AddSingleton<VideoSourceRegistry>
  ├── AddHostedService:
  │     VideoCapture, PeriodicClip, MqttIngestion, HttpPolling,
  │     WebSocketIngestion, DeviceHealth, DataRetention,
  │     SensorBroadcastQueue (singleton)
  └── AddCors + Swagger
       │
       ▼
Configure:
  ├── Apply EF Migrations (SQLite)
  ├── SeedAsync (MockData)
  ├── TimescaleDB Initialize (hypertables)
  ├── UseWebSockets
  ├── Map /ws/device → DeviceSimulatorWsHandler
  ├── Map /ws/join → DeviceJoinWsHandler
  └── MapControllers + MapHub<SensorHub>("/hubs/sensors")
```

### 7.3. Backend API Endpoints — Danh Sách Đầy Đủ

#### Stations API (`/api/stations`)

| Method | Route | Mô Tả |
|--------|-------|-------|
| GET | `/api/stations` | Danh sách tất cả trạm (bao gồm lines, nodes, sensors) |
| GET | `/api/stations/{id}` | Một trạm cụ thể |
| GET | `/api/stations/{id}/lines` | Danh sách tuyến cống của trạm |
| GET | `/api/stations/{id}/nodes` | Nodes dạng GeoJSON FeatureCollection |
| GET | `/api/stations/{id}/lines-geojson` | Lines dạng GeoJSON LineString |
| GET | `/api/stations/{stationId}/lines/{lineId}/nodes` | Nodes của một tuyến |
| GET | `/api/stations/{stationId}/nodes/{nodeId}` | Chi tiết node |
| GET | `/api/stations/{stationId}/nodes/{nodeId}/sensors` | Sensors của node |

#### Sensors API (`/api/sensors`)

| Method | Route | Mô Tả |
|--------|-------|-------|
| GET | `/api/sensors` | Danh sách sensor, filter theo nodeId |
| GET | `/api/sensors/{id}` | Một sensor cụ thể |
| POST | `/api/sensors/{id}/measurements` | Push giá trị mới cho sensor |

#### Readings API (`/api/readings`)

| Method | Route | Mô Tả |
|--------|-------|-------|
| GET | `/api/readings/{sensorId}?from=&to=&limit=` | Lịch sử readings (TimescaleDB) |
| GET | `/api/readings/{sensorId}/latest?count=` | N giá trị gần nhất |
| GET | `/api/readings/{sensorId}/stats?from=&to=` | Thống kê (min, max, avg, warnings, criticals) |
| GET | `/api/readings/{sensorId}/hourly?from=&to=` | Tổng hợp theo giờ |
| GET | `/api/readings/{sensorId}/heartbeats?from=&to=` | Lịch sử heartbeat |
| GET | `/api/readings/node/{nodeId}?from=&to=` | Readings của một node |

#### Alerts API (`/api/alerts`)

| Method | Route | Mô Tả |
|--------|-------|-------|
| GET | `/api/alerts?status=&severity=&from=&to=&page=` | Danh sách alerts (phân trang) |
| GET | `/api/alerts/{id}` | Chi tiết alert |
| POST | `/api/alerts` | Tạo alert mới |
| POST | `/api/alerts/{id}/acknowledge` | Xác nhận alert |
| POST | `/api/alerts/{id}/resolve` | Giải quyết alert |

#### Device Join API (`/api/device-joins`)

| Method | Route | Mô Tả |
|--------|-------|-------|
| GET | `/api/device-joins/pending` | Danh sách yêu cầu pending |
| POST | `/api/device-joins/{id}/approve` | Phê duyệt, gán NodeByteId |
| POST | `/api/device-joins/{id}/reject` | Từ chối |

### 7.4. Station API Endpoints (từ Center)

Station App cũng chạy một HTTP server nội bộ (`SimulationApiServer`) để phục vụ web-based simulators:

| Method | Route | Mô Tả |
|--------|-------|-------|
| GET | `/api/station-info` | Thông tin trạm |
| GET | `/api/nodes` | Danh sách node |
| GET | `/api/sensors` | Danh sách sensor |
| GET | `/api/sensors/{id}/data` | Dữ liệu sensor |
| WebSocket | `/ws/simulator` | Kết nối simulator |

---

## 8. Giao Thức Kết Nối Thiết Bị

### 8.1. Binary Frame Protocol (32 bytes — Sensor Data)

```
Byte    0 : START       = 0xAA
Byte    1 : HEADER      = 0x01
Bytes  2–5 : float32 LE – Nhiệt độ    (°C)
Bytes  6–9 : float32 LE – Độ ẩm       (%)
Bytes 10–13: float32 LE – Ánh sáng    (lux)
Bytes 14–17: float32 LE – Radar Dist  (m)
Bytes 18–21: float32 LE – Radar Speed (m/s)
Bytes 22–25: uint32  LE – Radar Energy
Bytes 26–27: uint16  LE – VL53 Height (mm)
Bytes 28–29: uint16  LE – CRC-16/CCITT-FALSE (over bytes 0–27)
Byte   30  : Reserved   = 0x00
Byte   31  : STOP       = 0xBB
```

### 8.2. Wire Protocol Byte Mapping (10 bytes / sensor)

```
[AA][NODE_ID][SENSOR_ID][SEQ][VALUE×4][CRC8][BB]

SENSOR_ID: 01=Temp  02=Hum  03=Light
           04=RDist  05=RSpd  06=REnrg  07=VL53H
```

### 8.3. JOIN_REQUEST Frame (20 bytes)

```
[0xAA][0x20][MAC×6][HW_ID×4][FW_MAJ][FW_MIN][FW_PAT][0x00][CRC×2][0x00][0xBB]
```

### 8.4. JOIN_RESPONSE Frame (8 bytes)

```
[0xAA][0x21][STATUS][NODE_ID][CRC×2][0x00][0xBB]

STATUS: 0x01 = Accept  0x00 = Reject
NODE_ID: Assigned NodeByteId (1–10) hoặc 0x00
```

---

## Phụ Lục

### A. Cấu Hình Môi Trường

| Biến | Mô Tả | Mặc Định |
|------|-------|---------|
| `MAPBOX_ACCESS_TOKEN` | Mapbox API token | (required) |
| `BACKEND_BASE_URL` | Backend API URL | `http://localhost:5280` |
| `STATION_ID` | Station ID | `ST01` |
| `DATA_SOURCE` | Station data source | `real` (hoặc `mock`) |

### B. Docker

```yaml
services:
  timescaledb:
    image: timescale/timescaledb:latest-pg16
    ports: ["5433:5432"]
    volumes:
      - timescale_data:/var/lib/postgresql/data
      - ./docker/timescaledb-init.sql:/docker-entrypoint-initdb.d/01-init.sql
```

### C. File Quan Trọng

| File | Mô Tả |
|------|-------|
| `Backend/Program.cs` | Entry point, DI, middleware |
| `Backend/Controllers/StationsController.cs` | Station API (8 endpoints) |
| `Backend/Services/SensorBroadcaster.cs` | Core: xử lý & broadcast reading |
| `Backend/Services/SensorBroadcastQueue.cs` | Channel-based SignalR fan-out |
| `Backend/Services/DeviceSimulatorWsHandler.cs` | Parse 32-byte binary frame |
| `Backend/Services/DeviceJoinWsHandler.cs` | Xử lý JOIN_REQUEST |
| `Backend/Services/DeviceHealthService.cs` | Periodic health check |
| `Backend/Data/TunnelDbContext.cs` | SQLite DbContext |
| `Backend/Data/TimeSeriesDbContext.cs` | TimescaleDB DbContext |
| `Backend/Mock/MockData.cs` | Seed data station ST01 |
| `docs/database_schema.sql` | Full PostgreSQL schema |
| `docs/view_schema.sql` | Materialized views + views |
| `docker-compose.yml` | TimescaleDB Docker |
| `erd.drawio` | ER Diagram |
| `architecture.drawio` | Architecture diagram |
| `data_stream.drawio` | Data stream diagram |

---

*Tài liệu được tạo từ mã nguồn dự án Tunnel Security — cập nhật lần cuối: 13/06/2026*
