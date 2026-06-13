-- ═══════════════════════════════════════════════════════════════════════════
--  TUNNEL SECURITY – DATABASE SCHEMA
--  Engine  : PostgreSQL 16 + TimescaleDB 2.x
--  Strategy: Dữ liệu truyền thống  → PostgreSQL (relational)
--            Dữ liệu time-series   → TimescaleDB hypertable
-- ═══════════════════════════════════════════════════════════════════════════
--
--  KIẾN TRÚC HỆ THỐNG
--  ┌─────────────────────────────────────────────────────────────────┐
--  │  Máy tính nhúng (Embedded Node)                                 │
--  │  ├── Cảm biến: Temp/Hum/Light/RadarDist/RadarSpeed/Energy/VL53  │
--  │  └── Camera : RTSP stream, AI detection                         │
--  │       │  binary frame 10-byte / RTSP                            │
--  │       ▼                                                          │
--  │  Máy trạm (Station Machine)                                     │
--  │  ├── PostgreSQL : users, config, lines, nodes, sensors, alerts  │
--  │  └── TimescaleDB: sensor_readings, node_heartbeats, cam_events  │
--  └─────────────────────────────────────────────────────────────────┘
--
--  FRAME PROTOCOL (10 bytes / sensor):
--  [AA][NODE_ID][SENSOR_ID][SEQ][VALUE×4][CRC8][BB]
--  SENSOR_ID: 01=Temp 02=Hum 03=Light 04=RDist 05=RSpd 06=REnrg 07=VL53H
-- ═══════════════════════════════════════════════════════════════════════════

CREATE EXTENSION IF NOT EXISTS timescaledb;
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

-- ───────────────────────────────────────────────────────────────────────────
--  ENUM TYPES
-- ───────────────────────────────────────────────────────────────────────────

CREATE TYPE line_status    AS ENUM ('active', 'inactive', 'maintenance');
CREATE TYPE node_status    AS ENUM ('online', 'warning', 'critical', 'offline', 'maintenance');
CREATE TYPE sensor_type    AS ENUM (
    'temperature',   -- 0x01 · °C
    'humidity',      -- 0x02 · %RH
    'light',         -- 0x03 · lux
    'radar_dist',    -- 0x04 · m
    'radar_speed',   -- 0x05 · m/s
    'radar_energy',  -- 0x06 · raw uint32
    'vl53_height'    -- 0x07 · mm
);
CREATE TYPE reading_level  AS ENUM ('normal', 'warning', 'critical');
CREATE TYPE user_role      AS ENUM ('admin', 'operator', 'viewer');
CREATE TYPE alert_severity AS ENUM ('low', 'medium', 'high', 'critical');
CREATE TYPE alert_state    AS ENUM ('unprocessed', 'acknowledged', 'in_progress', 'resolved', 'closed');
CREATE TYPE alert_category AS ENUM (
    'temperature', 'humidity', 'radar', 'light',
    'intrusion', 'equipment', 'connection', 'other'
);
CREATE TYPE cam_event_type AS ENUM (
    'motion', 'object', 'face', 'license_plate',
    'intrusion', 'loitering', 'crossing', 'abandoned'
);


-- ═══════════════════════════════════════════════════════════════════════════
--  PHẦN 1 – DỮ LIỆU TRUYỀN THỐNG (RELATIONAL)
-- ═══════════════════════════════════════════════════════════════════════════

-- ─── 1.1  Người dùng hệ thống ───────────────────────────────────────────────
CREATE TABLE users (
    id            VARCHAR(50)  PRIMARY KEY DEFAULT gen_random_uuid()::text,
    username      VARCHAR(100) NOT NULL UNIQUE,
    password_hash TEXT         NOT NULL,
    full_name     VARCHAR(200) NOT NULL,
    email         VARCHAR(200) UNIQUE,
    role          user_role    NOT NULL DEFAULT 'viewer',
    is_active     BOOLEAN      NOT NULL DEFAULT TRUE,
    last_login    TIMESTAMPTZ,
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE  users IS 'Tài khoản người dùng hệ thống';
COMMENT ON COLUMN users.role IS 'admin=toàn quyền · operator=vận hành · viewer=chỉ xem';


-- ─── 1.2  Cấu hình máy trạm (1 bản ghi) ────────────────────────────────────
CREATE TABLE station_config (
    id                     UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    station_code           VARCHAR(50) NOT NULL UNIQUE,
    station_name           VARCHAR(200) NOT NULL,
    area                   VARCHAR(100),
    address                TEXT,
    latitude               DOUBLE PRECISION,
    longitude              DOUBLE PRECISION,

    center_url             VARCHAR(500),
    center_api_key         TEXT,

    contact_person         VARCHAR(200),
    phone                  VARCHAR(50),
    email                  VARCHAR(200),

    configured_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at             TIMESTAMPTZ
);
COMMENT ON TABLE  station_config IS 'Cấu hình máy trạm – 1 bản ghi duy nhất trên mỗi trạm';
COMMENT ON COLUMN station_config.center_url IS 'Endpoint API của trung tâm giám sát';


-- ─── 1.3  Tuyến cống ────────────────────────────────────────────────────────
CREATE TABLE lines (
    id          VARCHAR(50)  PRIMARY KEY,
    code        VARCHAR(20)  NOT NULL,
    name        VARCHAR(200) NOT NULL,
    description TEXT,
    start_lng   DOUBLE PRECISION,
    start_lat   DOUBLE PRECISION,
    end_lng     DOUBLE PRECISION,
    end_lat     DOUBLE PRECISION,
    length_m    DOUBLE PRECISION,
    status      line_status  NOT NULL DEFAULT 'active',
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ
);
COMMENT ON TABLE  lines IS 'Tuyến cống được giám sát bởi máy trạm này';
COMMENT ON COLUMN lines.length_m IS 'Chiều dài toàn tuyến (mét)';


-- ─── 1.4  Máy tính nhúng (nút giám sát trên tuyến cống) ────────────────────
CREATE TABLE embedded_nodes (
    id               VARCHAR(50)  PRIMARY KEY,
    line_id          VARCHAR(50)  NOT NULL REFERENCES lines(id) ON DELETE RESTRICT,
    code             VARCHAR(20)  NOT NULL,
    name             VARCHAR(200) NOT NULL,
    description      TEXT,

    longitude        DOUBLE PRECISION,
    latitude         DOUBLE PRECISION,
    distance_m       DOUBLE PRECISION,          -- khoảng cách từ đầu tuyến (m)
    map_x            DOUBLE PRECISION,          -- tọa độ pixel trên canvas
    map_y            DOUBLE PRECISION,
    node_byte_id     SMALLINT      NOT NULL,

    -- Phần cứng
    hardware_id      VARCHAR(100),
    mac_address      VARCHAR(17),
    ip_address       VARCHAR(50),
    firmware_version VARCHAR(50),
    is_hub           BOOLEAN      NOT NULL DEFAULT FALSE,

    -- Trạng thái
    status           node_status  NOT NULL DEFAULT 'offline',
    battery_level    REAL,
    rssi             SMALLINT,
    last_heartbeat   TIMESTAMPTZ,

    created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ
);
COMMENT ON TABLE  embedded_nodes IS 'Máy tính nhúng lắp dọc tuyến cống – mỗi node đo đạc và gửi dữ liệu';
COMMENT ON COLUMN embedded_nodes.distance_m   IS 'Khoảng cách từ điểm đầu tuyến (m) – dùng để hiển thị vị trí trên bản đồ tuyến';
COMMENT ON COLUMN embedded_nodes.is_hub       IS 'TRUE = node đóng vai trò gateway tập trung dữ liệu';

CREATE INDEX idx_nodes_line    ON embedded_nodes(line_id);
CREATE INDEX idx_nodes_byte_id ON embedded_nodes(node_byte_id);


-- ─── 1.5  Cảm biến ──────────────────────────────────────────────────────────
CREATE TABLE sensors (
    id                 VARCHAR(50)   PRIMARY KEY,
    node_id            VARCHAR(50)   NOT NULL REFERENCES embedded_nodes(id) ON DELETE CASCADE,
    sensor_byte_id     SMALLINT      NOT NULL,
    type               sensor_type   NOT NULL,
    name               VARCHAR(200)  NOT NULL,
    unit               VARCHAR(20)   NOT NULL,

    warning_threshold  DOUBLE PRECISION,
    critical_threshold DOUBLE PRECISION,
    is_enabled         BOOLEAN       NOT NULL DEFAULT TRUE,

    -- Cache giá trị mới nhất (không dùng cho phân tích lịch sử)
    current_value      DOUBLE PRECISION,
    current_level      reading_level NOT NULL DEFAULT 'normal',
    last_reading_at    TIMESTAMPTZ,

    created_at         TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ,

    UNIQUE (node_id, sensor_byte_id)
);
COMMENT ON TABLE  sensors IS 'Cảm biến vật lý gắn trên máy tính nhúng';
COMMENT ON COLUMN sensors.sensor_byte_id IS 'Byte[2] trong frame: 01=Temp 02=Hum 03=Light 04=RDist 05=RSpd 06=REnrg 07=VL53H';
COMMENT ON COLUMN sensors.current_value  IS 'Giá trị đọc gần nhất – chỉ là cache, dùng sensor_readings cho lịch sử';

CREATE INDEX idx_sensors_node ON sensors(node_id);


-- ─── 1.6  Camera ────────────────────────────────────────────────────────────
CREATE TABLE cameras (
    id            VARCHAR(50)  PRIMARY KEY,
    node_id       VARCHAR(50)  NOT NULL REFERENCES embedded_nodes(id) ON DELETE CASCADE,
    name          VARCHAR(200) NOT NULL,
    stream_url    VARCHAR(500),
    resolution    VARCHAR(20)  NOT NULL DEFAULT '1280x720',
    fps           SMALLINT     NOT NULL DEFAULT 30,
    codec         VARCHAR(20)  NOT NULL DEFAULT 'H.264',
    ir_enabled    BOOLEAN      NOT NULL DEFAULT FALSE,
    hdr_enabled   BOOLEAN      NOT NULL DEFAULT FALSE,
    is_online     BOOLEAN      NOT NULL DEFAULT FALSE,
    is_recording  BOOLEAN      NOT NULL DEFAULT FALSE,
    snapshot_dir  VARCHAR(500),
    created_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ
);
COMMENT ON TABLE cameras IS 'Camera IP gắn trên máy tính nhúng';

CREATE INDEX idx_cameras_node ON cameras(node_id);


-- ─── 1.7  Cảnh báo ──────────────────────────────────────────────────────────
CREATE TABLE alerts (
    id              VARCHAR(50)    PRIMARY KEY DEFAULT gen_random_uuid()::text,
    title           VARCHAR(500)   NOT NULL,
    description     TEXT,
    category        alert_category NOT NULL,
    severity        alert_severity NOT NULL,
    state           alert_state    NOT NULL DEFAULT 'unprocessed',

    node_id         VARCHAR(50)    REFERENCES embedded_nodes(id) ON DELETE SET NULL,
    node_name       VARCHAR(200),
    sensor_id       VARCHAR(50)    REFERENCES sensors(id) ON DELETE SET NULL,
    sensor_value    DOUBLE PRECISION,
    sensor_unit     VARCHAR(20),
    threshold       DOUBLE PRECISION,
    camera_id       VARCHAR(50)    REFERENCES cameras(id) ON DELETE SET NULL,
    snapshot_path   VARCHAR(500),
    video_clip_path VARCHAR(500),

    longitude       DOUBLE PRECISION,
    latitude        DOUBLE PRECISION,

    created_at      TIMESTAMPTZ    NOT NULL DEFAULT NOW(),
    acknowledged_at TIMESTAMPTZ,
    resolved_at     TIMESTAMPTZ,
    closed_at       TIMESTAMPTZ,
    acknowledged_by VARCHAR(50)    REFERENCES users(id) ON DELETE SET NULL,
    resolved_by     VARCHAR(50)    REFERENCES users(id) ON DELETE SET NULL,
    note            TEXT
);
COMMENT ON TABLE  alerts IS 'Cảnh báo phát sinh từ sensor hoặc camera';
COMMENT ON COLUMN alerts.threshold IS 'Giá trị ngưỡng đã bị vượt tại thời điểm tạo cảnh báo (snapshot)';

CREATE INDEX idx_alerts_state   ON alerts(state, severity, created_at DESC);
CREATE INDEX idx_alerts_node    ON alerts(node_id);
CREATE INDEX idx_alerts_sensor  ON alerts(sensor_id);
CREATE INDEX idx_alerts_created ON alerts(created_at DESC);


-- ─── 1.8  Ghi chú cảnh báo ──────────────────────────────────────────────────
CREATE TABLE alert_notes (
    id         VARCHAR(50) PRIMARY KEY DEFAULT gen_random_uuid()::text,
    alert_id   VARCHAR(50) NOT NULL REFERENCES alerts(id) ON DELETE CASCADE,
    content    TEXT        NOT NULL,
    author_id  VARCHAR(50) REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
COMMENT ON TABLE alert_notes IS 'Nhật ký xử lý cảnh báo – mỗi hành động ghi 1 bản ghi';

CREATE INDEX idx_alert_notes_alert ON alert_notes(alert_id);


-- ═══════════════════════════════════════════════════════════════════════════
--  PHẦN 2 – DỮ LIỆU TIME-SERIES (TimescaleDB Hypertables)
--
--  Nguyên tắc thiết kế:
--    · Cột TIME luôn là cột đầu tiên và là partition key
--    · KHÔNG dùng FOREIGN KEY constraint (tránh lock toàn bảng khi insert)
--    · Dữ liệu chỉ INSERT – không UPDATE / DELETE
--    · node_id / sensor_id lưu dưới dạng TEXT (không VARCHAR) theo best practice TimescaleDB
--    · Nén tự động (columnar) sau 7 ngày → tiết kiệm ~90% dung lượng
-- ═══════════════════════════════════════════════════════════════════════════

-- ─── 2.1  Giá trị cảm biến ──────────────────────────────────────────────────
CREATE TABLE sensor_readings (
    time           TIMESTAMPTZ      NOT NULL,   -- ← partition key (UTC)
    node_id        TEXT             NOT NULL,
    sensor_id      TEXT             NOT NULL,
    node_byte_id   SMALLINT         NOT NULL,   -- byte[1] frame giao thức
    sensor_byte_id SMALLINT         NOT NULL,   -- byte[2] frame giao thức
    value          DOUBLE PRECISION NOT NULL,
    seq            SMALLINT         NOT NULL,   -- số thứ tự frame (0–255) để phát hiện mất gói
    level          reading_level    NOT NULL DEFAULT 'normal',
    crc8_ok        BOOLEAN          NOT NULL DEFAULT TRUE
);

SELECT create_hypertable(
    'sensor_readings', 'time',
    chunk_time_interval => INTERVAL '1 day'
);

-- Indexes (TimescaleDB tự thêm index trên cột time)
CREATE INDEX idx_sr_node_sensor ON sensor_readings(node_id, sensor_id, time DESC);
CREATE INDEX idx_sr_warning     ON sensor_readings(level, time DESC)
    WHERE level <> 'normal';   -- partial index – chỉ giữ bản ghi bất thường

-- Nén columnar sau 7 ngày
ALTER TABLE sensor_readings SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'node_id, sensor_id',
    timescaledb.compress_orderby   = 'time DESC'
);
SELECT add_compression_policy('sensor_readings', INTERVAL '7 days');
SELECT add_retention_policy  ('sensor_readings', INTERVAL '1 year');

COMMENT ON TABLE  sensor_readings IS 'Giá trị cảm biến theo thời gian – TimescaleDB hypertable, chunk 1 ngày';
COMMENT ON COLUMN sensor_readings.time  IS 'Thời điểm đọc (UTC) – partition key';
COMMENT ON COLUMN sensor_readings.seq   IS 'Byte[3] frame: tăng dần 0→255, phát hiện mất frame khi nhảy số';
COMMENT ON COLUMN sensor_readings.crc8_ok IS 'FALSE nếu CRC8 sai – vẫn ghi để audit, loại khỏi phân tích';


-- ─── 2.2  Raw frame log (chỉ lưu frame lỗi để debug) ───────────────────────
CREATE TABLE sensor_frames_raw (
    time           TIMESTAMPTZ NOT NULL,        -- ← partition key
    node_byte_id   SMALLINT    NOT NULL,
    sensor_byte_id SMALLINT    NOT NULL,
    seq            SMALLINT    NOT NULL,
    raw_hex        CHAR(29)    NOT NULL,         -- "AA NN SS QQ VV VV VV VV CC BB"
    value_bytes    BYTEA,
    crc8_valid     BOOLEAN     NOT NULL,
    error_msg      TEXT
);

SELECT create_hypertable(
    'sensor_frames_raw', 'time',
    chunk_time_interval => INTERVAL '1 hour'    -- chunk nhỏ vì retention ngắn
);

SELECT add_retention_policy('sensor_frames_raw', INTERVAL '7 days');

COMMENT ON TABLE sensor_frames_raw IS 'Raw binary frame log – chủ yếu lưu frame lỗi CRC8 để debug';


-- ─── 2.3  Heartbeat máy tính nhúng ──────────────────────────────────────────
CREATE TABLE node_heartbeats (
    time             TIMESTAMPTZ NOT NULL,       -- ← partition key
    node_id          TEXT        NOT NULL,
    node_byte_id     SMALLINT    NOT NULL,
    status           node_status NOT NULL,
    battery_level    REAL,                       -- % (NULL nếu nguồn điện lưới)
    rssi             SMALLINT,                   -- dBm
    ip_address       TEXT,
    firmware_version TEXT,
    uptime_sec       INTEGER                     -- giây kể từ lần khởi động cuối
);

SELECT create_hypertable(
    'node_heartbeats', 'time',
    chunk_time_interval => INTERVAL '6 hours'
);

CREATE INDEX idx_nh_node_time ON node_heartbeats(node_id, time DESC);
SELECT add_retention_policy('node_heartbeats', INTERVAL '30 days');

COMMENT ON TABLE node_heartbeats IS 'Lịch sử trạng thái kết nối của từng máy tính nhúng, chunk 6 giờ';


-- ─── 2.4  Sự kiện phát hiện camera ─────────────────────────────────────────
CREATE TABLE camera_events (
    time            TIMESTAMPTZ    NOT NULL,     -- ← partition key
    camera_id       TEXT           NOT NULL,
    node_id         TEXT           NOT NULL,
    event_type      cam_event_type NOT NULL,
    confidence      REAL,                        -- 0.0 – 1.0
    object_class    TEXT,                        -- person, vehicle, animal...
    is_intrusion    BOOLEAN        NOT NULL DEFAULT FALSE,
    bbox_x          SMALLINT,
    bbox_y          SMALLINT,
    bbox_w          SMALLINT,
    bbox_h          SMALLINT,
    image_path      TEXT,
    generated_alert BOOLEAN        NOT NULL DEFAULT FALSE,
    alert_id        TEXT                         -- tham chiếu alerts.id (không FK)
);

SELECT create_hypertable(
    'camera_events', 'time',
    chunk_time_interval => INTERVAL '1 day'
);

CREATE INDEX idx_ce_camera_time    ON camera_events(camera_id, time DESC);
CREATE INDEX idx_ce_intrusion_time ON camera_events(time DESC)
    WHERE is_intrusion = TRUE;

ALTER TABLE camera_events SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'camera_id, node_id',
    timescaledb.compress_orderby   = 'time DESC'
);
SELECT add_compression_policy('camera_events', INTERVAL '7 days');
SELECT add_retention_policy  ('camera_events', INTERVAL '6 months');

COMMENT ON TABLE  camera_events IS 'Sự kiện phát hiện AI từ camera – TimescaleDB hypertable, chunk 1 ngày';
COMMENT ON COLUMN camera_events.alert_id IS 'Không dùng FK để tránh lock khi insert hàng loạt';




