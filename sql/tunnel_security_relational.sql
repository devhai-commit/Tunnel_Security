-- =============================================================================
-- Tunnel Security — Relational Database Schema (SQL Server / T-SQL)
-- Generated from: erd.drawio
-- Database: TunnelSecurity
-- =============================================================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'TunnelSecurity')
    CREATE DATABASE TunnelSecurity
        COLLATE Vietnamese_CI_AS;
GO

USE TunnelSecurity;
GO

-- =============================================================================
-- 1. BẢNG: users
--    Tài khoản người dùng hệ thống (operator, admin, viewer)
-- =============================================================================
CREATE TABLE users (
    id            VARCHAR(50)      NOT NULL,
    username      VARCHAR(100)     NOT NULL,
    password_hash NVARCHAR(MAX)    NOT NULL,
    full_name     NVARCHAR(200)    NULL,
    role          VARCHAR(20)      NOT NULL  DEFAULT 'viewer',
    is_active     BIT              NOT NULL  DEFAULT 1,
    created_at    DATETIMEOFFSET   NOT NULL  DEFAULT SYSDATETIMEOFFSET(),

    CONSTRAINT PK_users          PRIMARY KEY (id),
    CONSTRAINT UQ_users_username UNIQUE      (username),
    CONSTRAINT CK_users_role     CHECK       (role IN ('admin', 'operator', 'viewer'))
);
GO

-- =============================================================================
-- 2. BẢNG: station_config
--    Cấu hình của một trạm giám sát (mỗi Station app tương ứng 1 bản ghi)
-- =============================================================================
CREATE TABLE station_config (
    id                 UNIQUEIDENTIFIER NOT NULL  DEFAULT NEWSEQUENTIALID(),
    station_code       VARCHAR(50)      NOT NULL,
    station_name       NVARCHAR(200)    NOT NULL,
    area               NVARCHAR(500)    NULL,
    address            NVARCHAR(500)    NULL,
    latitude           FLOAT            NULL,
    longitude          FLOAT            NULL,
    center_url         VARCHAR(500)     NULL,
    api_key            VARCHAR(200)     NULL,
    heartbeat_interval INT              NOT NULL  DEFAULT 30,   -- giây
    sync_interval      INT              NOT NULL  DEFAULT 60,   -- giây
    configured_at      DATETIMEOFFSET   NOT NULL  DEFAULT SYSDATETIMEOFFSET(),

    CONSTRAINT PK_station_config          PRIMARY KEY (id),
    CONSTRAINT UQ_station_config_code     UNIQUE      (station_code)
);
GO

-- =============================================================================
-- 3. BẢNG: lines
--    Tuyến cống / đường hầm (mỗi tuyến chứa nhiều node)
-- =============================================================================
CREATE TABLE lines (
    id              VARCHAR(50)     NOT NULL,
    code            VARCHAR(20)     NULL,
    name            NVARCHAR(200)   NOT NULL,
    start_longitude FLOAT           NULL,
    start_latitude  FLOAT           NULL,
    end_longitude   FLOAT           NULL,
    end_latitude    FLOAT           NULL,
    length_m        FLOAT           NULL,
    status          VARCHAR(20)     NOT NULL  DEFAULT 'active',
    created_at      DATETIMEOFFSET  NOT NULL  DEFAULT SYSDATETIMEOFFSET(),

    CONSTRAINT PK_lines        PRIMARY KEY (id),
    CONSTRAINT CK_lines_status CHECK       (status IN ('active', 'inactive', 'maintenance'))
);
GO

-- =============================================================================
-- 4. BẢNG: embedded_nodes
--    Thiết bị nhúng IoT gắn tại mỗi điểm trên tuyến cống
-- =============================================================================
CREATE TABLE embedded_nodes (
    id              VARCHAR(50)     NOT NULL,
    line_id         VARCHAR(50)     NOT NULL,
    code            VARCHAR(50)     NULL,
    name            NVARCHAR(200)   NOT NULL,
    node_byte_id    SMALLINT        NOT NULL,   -- byte ID trong wire protocol (1–10)
    longitude       FLOAT           NULL,
    latitude        FLOAT           NULL,
    distance_m      FLOAT           NULL,       -- khoảng cách từ đầu tuyến (m)
    hardware_id     VARCHAR(100)    NULL,
    mac             VARCHAR(17)     NULL,       -- định dạng XX:XX:XX:XX:XX:XX
    ip              VARCHAR(45)     NULL,
    firmware        VARCHAR(50)     NULL,
    status          VARCHAR(20)     NOT NULL  DEFAULT 'offline',
    battery_level   FLOAT           NULL,       -- % còn lại
    rssi            INT             NULL,       -- tín hiệu dBm
    is_hub          BIT             NOT NULL  DEFAULT 0,
    last_heartbeat  DATETIMEOFFSET  NULL,
    created_at      DATETIMEOFFSET  NOT NULL  DEFAULT SYSDATETIMEOFFSET(),
    updated_at      DATETIMEOFFSET  NOT NULL  DEFAULT SYSDATETIMEOFFSET(),

    CONSTRAINT PK_embedded_nodes            PRIMARY KEY (id),
    CONSTRAINT UQ_embedded_nodes_byte_id    UNIQUE      (node_byte_id),
    CONSTRAINT FK_embedded_nodes_line       FOREIGN KEY (line_id)
                                                REFERENCES lines (id)
                                                ON DELETE CASCADE
                                                ON UPDATE CASCADE,
    CONSTRAINT CK_embedded_nodes_status     CHECK (status IN ('online', 'offline', 'warning', 'maintenance')),
    CONSTRAINT CK_embedded_nodes_byte_id    CHECK (node_byte_id BETWEEN 1 AND 10),
    CONSTRAINT CK_embedded_nodes_battery    CHECK (battery_level IS NULL OR battery_level BETWEEN 0 AND 100)
);
GO

-- =============================================================================
-- 5. BẢNG: sensors
--    Cảm biến gắn trên mỗi node (nhiệt độ, độ ẩm, radar, v.v.)
-- =============================================================================
CREATE TABLE sensors (
    id                 VARCHAR(50)     NOT NULL,
    node_id            VARCHAR(50)     NOT NULL,
    sensor_byte_id     SMALLINT        NOT NULL,   -- byte ID trong wire protocol (1–7)
    type               VARCHAR(30)     NOT NULL,
    name               NVARCHAR(200)   NOT NULL,
    unit               VARCHAR(20)     NULL,
    warning_threshold  FLOAT           NULL,
    critical_threshold FLOAT           NULL,
    current_value      FLOAT           NULL,       -- cache: giá trị đo gần nhất
    current_level      VARCHAR(20)     NULL        DEFAULT 'Normal',
    sampling_rate_hz   FLOAT           NOT NULL  DEFAULT 1.0,
    is_enabled         BIT             NOT NULL  DEFAULT 1,

    CONSTRAINT PK_sensors               PRIMARY KEY (id),
    CONSTRAINT FK_sensors_node          FOREIGN KEY (node_id)
                                            REFERENCES embedded_nodes (id)
                                            ON DELETE CASCADE
                                            ON UPDATE CASCADE,
    CONSTRAINT CK_sensors_type          CHECK (type IN (
                                            'temperature', 'humidity', 'light',
                                            'radar', 'vibration', 'waterlevel', 'energy')),
    CONSTRAINT CK_sensors_byte_id       CHECK (sensor_byte_id BETWEEN 1 AND 7),
    CONSTRAINT CK_sensors_level         CHECK (current_level IN ('Normal', 'Warning', 'Critical'))
);
GO

-- =============================================================================
-- 6. BẢNG: cameras
--    Camera IP gắn trên node, dùng cho phân tích video AI
-- =============================================================================
CREATE TABLE cameras (
    id           VARCHAR(50)     NOT NULL,
    node_id      VARCHAR(50)     NOT NULL,
    name         NVARCHAR(200)   NOT NULL,
    stream_url   VARCHAR(500)    NULL,
    resolution   VARCHAR(20)     NULL,       -- ví dụ: '1920x1080'
    fps          INT             NULL,
    codec        VARCHAR(20)     NULL,       -- ví dụ: 'H264', 'H265'
    ir_enabled   BIT             NOT NULL  DEFAULT 0,
    hdr_enabled  BIT             NOT NULL  DEFAULT 0,
    is_online    BIT             NOT NULL  DEFAULT 0,
    is_recording BIT             NOT NULL  DEFAULT 0,

    CONSTRAINT PK_cameras          PRIMARY KEY (id),
    CONSTRAINT FK_cameras_node     FOREIGN KEY (node_id)
                                       REFERENCES embedded_nodes (id)
                                       ON DELETE CASCADE
                                       ON UPDATE CASCADE
);
GO

-- =============================================================================
-- 7. BẢNG: alerts
--    Cảnh báo được tạo ra từ ngưỡng cảm biến hoặc sự kiện camera
-- =============================================================================
CREATE TABLE alerts (
    id                VARCHAR(50)     NOT NULL,
    title             NVARCHAR(500)   NOT NULL,
    description       NVARCHAR(MAX)   NULL,
    category          VARCHAR(30)     NOT NULL,    -- 'sensor' | 'camera' | 'system'
    severity          VARCHAR(20)     NOT NULL,    -- 'warning' | 'critical'
    state             VARCHAR(20)     NOT NULL  DEFAULT 'open',
    node_id           VARCHAR(50)     NULL,
    sensor_id         VARCHAR(50)     NULL,
    camera_id         VARCHAR(50)     NULL,
    sensor_value      FLOAT           NULL,
    threshold         FLOAT           NULL,
    unit              VARCHAR(20)     NULL,
    created_at        DATETIMEOFFSET  NOT NULL  DEFAULT SYSDATETIMEOFFSET(),
    acknowledged_at   DATETIMEOFFSET  NULL,
    acknowledged_by   VARCHAR(50)     NULL,
    resolved_by       VARCHAR(50)     NULL,
    closed_by         VARCHAR(50)     NULL,

    CONSTRAINT PK_alerts                 PRIMARY KEY (id),
    CONSTRAINT FK_alerts_node            FOREIGN KEY (node_id)
                                             REFERENCES embedded_nodes (id)
                                             ON DELETE SET NULL
                                             ON UPDATE CASCADE,
    CONSTRAINT FK_alerts_sensor          FOREIGN KEY (sensor_id)
                                             REFERENCES sensors (id)
                                             ON DELETE NO ACTION
                                             ON UPDATE NO ACTION,
    CONSTRAINT FK_alerts_camera          FOREIGN KEY (camera_id)
                                             REFERENCES cameras (id)
                                             ON DELETE NO ACTION
                                             ON UPDATE NO ACTION,
    CONSTRAINT FK_alerts_acknowledged_by FOREIGN KEY (acknowledged_by)
                                             REFERENCES users (id),
    CONSTRAINT FK_alerts_resolved_by     FOREIGN KEY (resolved_by)
                                             REFERENCES users (id),
    CONSTRAINT FK_alerts_closed_by       FOREIGN KEY (closed_by)
                                             REFERENCES users (id),
    CONSTRAINT CK_alerts_severity        CHECK (severity IN ('warning', 'critical')),
    CONSTRAINT CK_alerts_state           CHECK (state   IN ('open', 'acknowledged', 'resolved', 'closed')),
    CONSTRAINT CK_alerts_category        CHECK (category IN ('sensor', 'camera', 'system'))
);
GO

-- =============================================================================
-- 8. BẢNG: alert_notes
--    Ghi chú / nhật ký xử lý của operator trên từng cảnh báo
-- =============================================================================
CREATE TABLE alert_notes (
    id         VARCHAR(50)     NOT NULL,
    alert_id   VARCHAR(50)     NOT NULL,
    content    NVARCHAR(MAX)   NOT NULL,
    author_id  VARCHAR(50)     NOT NULL,
    created_at DATETIMEOFFSET  NOT NULL  DEFAULT SYSDATETIMEOFFSET(),

    CONSTRAINT PK_alert_notes          PRIMARY KEY (id),
    CONSTRAINT FK_alert_notes_alert    FOREIGN KEY (alert_id)
                                           REFERENCES alerts (id)
                                           ON DELETE CASCADE
                                           ON UPDATE CASCADE,
    CONSTRAINT FK_alert_notes_author   FOREIGN KEY (author_id)
                                           REFERENCES users (id)
);
GO

-- =============================================================================
-- INDEXES — tối ưu các câu query phổ biến
-- =============================================================================

-- embedded_nodes: tìm theo tuyến và trạng thái
CREATE INDEX IX_embedded_nodes_line_id ON embedded_nodes (line_id);
CREATE INDEX IX_embedded_nodes_status  ON embedded_nodes (status);

-- sensors: tìm theo node
CREATE INDEX IX_sensors_node_id ON sensors (node_id);
CREATE INDEX IX_sensors_type    ON sensors (type);

-- cameras: tìm theo node
CREATE INDEX IX_cameras_node_id ON cameras (node_id);

-- alerts: các filter thường dùng trên UI
CREATE INDEX IX_alerts_state      ON alerts (state);
CREATE INDEX IX_alerts_severity   ON alerts (severity);
CREATE INDEX IX_alerts_node_id    ON alerts (node_id);
CREATE INDEX IX_alerts_created_at ON alerts (created_at DESC);

-- alert_notes: lấy note theo alert
CREATE INDEX IX_alert_notes_alert_id ON alert_notes (alert_id);
GO

-- =============================================================================
-- DỮ LIỆU MẪU (Seed Data) — khớp với MockData.cs hiện tại
-- =============================================================================

-- Admin user mặc định (password: Admin@123 — hash minh họa, thay bằng bcrypt thực)
INSERT INTO users (id, username, password_hash, full_name, role, is_active)
VALUES ('USR-ADMIN-01', 'admin', '$2a$12$placeholder_hash_admin', N'Quản trị viên', 'admin', 1);

-- Tuyến cống
INSERT INTO lines (id, code, name, length_m, status)
VALUES
    ('LINE-XuanThuy',       'XT',   N'Tuyến Xuân Thủy',       1200.0, 'active'),
    ('LINE-CauGiay',        'CG',   N'Tuyến Cầu Giấy',        800.0,  'active'),
    ('LINE-TranThaiTong',   'TTT',  N'Tuyến Trần Thái Tông',  950.0,  'active');

-- Node trung tâm (hub)
INSERT INTO embedded_nodes (id, line_id, code, name, node_byte_id, latitude, longitude, status, is_hub)
VALUES ('NODE-L0-01', 'LINE-XuanThuy', 'L0-01', N'Hub – Ngã tư Cầu Giấy', 1, 21.0285, 105.7844, 'online', 1);

-- Nodes tuyến Xuân Thủy
INSERT INTO embedded_nodes (id, line_id, code, name, node_byte_id, status)
VALUES
    ('NODE-L1-01', 'LINE-XuanThuy', 'L1-01', N'Cống Xuân Thủy 1', 2, 'online'),
    ('NODE-L1-02', 'LINE-XuanThuy', 'L1-02', N'Cống Xuân Thủy 2', 3, 'online'),
    ('NODE-L1-03', 'LINE-XuanThuy', 'L1-03', N'Cống Xuân Thủy 3', 4, 'offline');

-- Nodes tuyến Cầu Giấy
INSERT INTO embedded_nodes (id, line_id, code, name, node_byte_id, status)
VALUES
    ('NODE-L2-01', 'LINE-CauGiay', 'L2-01', N'Cống Cầu Giấy 1',  5, 'online'),
    ('NODE-L2-02', 'LINE-CauGiay', 'L2-02', N'Cống Cầu Giấy 2',  6, 'warning'),
    ('NODE-L2-03', 'LINE-CauGiay', 'L2-03', N'Cống Cầu Giấy 3',  7, 'online');

-- Nodes tuyến Trần Thái Tông
INSERT INTO embedded_nodes (id, line_id, code, name, node_byte_id, status)
VALUES
    ('NODE-L3-01', 'LINE-TranThaiTong', 'L3-01', N'Cống Trần Thái Tông 1', 8,  'online'),
    ('NODE-L3-02', 'LINE-TranThaiTong', 'L3-02', N'Cống Trần Thái Tông 2', 9,  'online'),
    ('NODE-L3-03', 'LINE-TranThaiTong', 'L3-03', N'Cống Trần Thái Tông 3', 10, 'offline');

-- Sensors cho node L1-01 (mẫu đại diện)
INSERT INTO sensors (id, node_id, sensor_byte_id, type, name, unit, warning_threshold, critical_threshold)
VALUES
    ('NODE-L1-01-TEMP',  'NODE-L1-01', 1, 'temperature', N'Nhiệt độ',          '°C',  35.0,  45.0),
    ('NODE-L1-01-HUM',   'NODE-L1-01', 2, 'humidity',    N'Độ ẩm',             '%',   85.0,  95.0),
    ('NODE-L1-01-RADAR', 'NODE-L1-01', 3, 'radar',       N'Khoảng cách Radar', 'm',   5.0,   8.0),
    ('NODE-L1-01-VIB',   'NODE-L1-01', 4, 'vibration',   N'Tốc độ Radar',      'm/s', 15.0,  25.0),
    ('NODE-L1-01-WATER', 'NODE-L1-01', 5, 'waterlevel',  N'Độ cao VL53',       'mm',  2000,  3500);
GO

-- =============================================================================
-- XÁC NHẬN
-- =============================================================================
SELECT
    t.name      AS [Bảng],
    p.rows      AS [Số bản ghi]
FROM sys.tables t
JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
ORDER BY t.name;
GO
