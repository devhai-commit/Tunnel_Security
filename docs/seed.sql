-- ═══════════════════════════════════════════════════════════════════════════
--  PHẦN 5 – DỮ LIỆU MẪU (SEED)
-- ═══════════════════════════════════════════════════════════════════════════

INSERT INTO users (id, username, password_hash, full_name, role)
VALUES ('USR-ADMIN', 'admin', '$2b$12$CHANGE_THIS_HASH', 'Quản trị viên', 'admin');

INSERT INTO station_config (station_code, station_name, area, center_url)
VALUES ('TRM-HN-01', 'Trạm giám sát Nghĩa Đô', 'Hà Nội', 'http://center.internal:5000');

INSERT INTO lines (id, code, name, length_m) VALUES
    ('LINE-L1', 'L1', 'Tuyến cống Nghĩa Đô – Đoạn A', 850.0),
    ('LINE-L2', 'L2', 'Tuyến cống Nghĩa Đô – Đoạn B', 620.0);

INSERT INTO embedded_nodes (id, line_id, code, name, node_byte_id, distance_m) VALUES
    ('NODE-L1-01', 'LINE-L1', 'N01', 'Cửa vào hầm (Bắc)',   1,   0.0),
    ('NODE-L1-02', 'LINE-L1', 'N02', 'Hành lang A',          2, 170.0),
    ('NODE-L1-03', 'LINE-L1', 'N03', 'Ngã ba phân nhánh',    3, 340.0),
    ('NODE-L1-04', 'LINE-L1', 'N04', 'Giữa hầm',             4, 510.0),
    ('NODE-L1-05', 'LINE-L1', 'N05', 'Cửa ra hầm (Nam)',     5, 850.0);

INSERT INTO sensors (id, node_id, sensor_byte_id, type, name, unit, warning_threshold, critical_threshold) VALUES
    ('SNS-L1-N01-01', 'NODE-L1-01', 1, 'temperature',  'Nhiệt độ N01',          '°C',   45.0,  60.0),
    ('SNS-L1-N01-02', 'NODE-L1-01', 2, 'humidity',     'Độ ẩm N01',             '%',    90.0,  98.0),
    ('SNS-L1-N01-03', 'NODE-L1-01', 3, 'light',        'Ánh sáng N01',          'lux', 3000.0,4500.0),
    ('SNS-L1-N01-04', 'NODE-L1-01', 4, 'radar_dist',   'Radar khoảng cách N01', 'm',    25.0,  40.0),
    ('SNS-L1-N01-05', 'NODE-L1-01', 5, 'radar_speed',  'Radar tốc độ N01',      'm/s',  10.0,  20.0),
    ('SNS-L1-N01-06', 'NODE-L1-01', 6, 'radar_energy', 'Radar năng lượng N01',  '',   50000, 60000),
    ('SNS-L1-N01-07', 'NODE-L1-01', 7, 'vl53_height',  'Độ cao VL53 N01',       'mm',  3000,   3800);

INSERT INTO cameras (id, node_id, name, stream_url, resolution, fps) VALUES
    ('CAM-L1-N01-01', 'NODE-L1-01', 'Camera cửa vào hầm', 'rtsp://192.168.1.101/stream1', '1920x1080', 30),
    ('CAM-L1-N03-01', 'NODE-L1-03', 'Camera ngã ba',       'rtsp://192.168.1.103/stream1', '1280x720',  25);
