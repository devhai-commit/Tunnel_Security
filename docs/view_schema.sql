--  PHẦN 3 – CONTINUOUS AGGREGATES
--  Materialized view tự động cập nhật – truy vấn thống kê không cần scan
--  toàn bộ hypertable (dùng cho chart, báo cáo, dashboard)
-- ═══════════════════════════════════════════════════════════════════════════

-- ─── 3.1  Theo giờ ──────────────────────────────────────────────────────────
-- NOTE: Dùng SUM(CASE WHEN ...) thay cho COUNT(*) FILTER và BOOL_OR(NOT ...)
--       để tương thích TimescaleDB 2.x mọi phiên bản (FILTER yêu cầu >= 2.7)
CREATE MATERIALIZED VIEW sensor_stats_hourly
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 hour', time)                          AS bucket,
    node_id,
    sensor_id,
    sensor_byte_id,
    AVG(value)                                           AS avg_value,
    MIN(value)                                           AS min_value,
    MAX(value)                                           AS max_value,
    COUNT(*)                                             AS sample_count,
    SUM(CASE WHEN level = 'warning'  THEN 1 ELSE 0 END) AS warning_count,
    SUM(CASE WHEN level = 'critical' THEN 1 ELSE 0 END) AS critical_count,
    SUM(CASE WHEN crc8_ok THEN 0 ELSE 1 END)            AS crc_error_count
FROM sensor_readings
GROUP BY time_bucket('1 hour', time), node_id, sensor_id, sensor_byte_id
WITH NO DATA;

SELECT add_continuous_aggregate_policy('sensor_stats_hourly',
    start_offset      => INTERVAL '3 hours',
    end_offset        => INTERVAL '1 hour',
    schedule_interval => INTERVAL '1 hour'
);

COMMENT ON MATERIALIZED VIEW sensor_stats_hourly IS
    'Tổng hợp giá trị sensor theo giờ – tự động làm mới mỗi giờ';


-- ─── 3.2  Theo ngày ──────────────────────────────────────────────────────────
-- NOTE: Query trực tiếp từ sensor_readings thay vì sensor_stats_hourly
--       để tương thích với TimescaleDB < 2.9 (hierarchical CAgg yêu cầu 2.9+)
--       Nếu dùng TimescaleDB >= 2.9, có thể đổi nguồn thành sensor_stats_hourly
CREATE MATERIALIZED VIEW sensor_stats_daily
WITH (timescaledb.continuous) AS
SELECT
    time_bucket('1 day', time)                               AS bucket,
    node_id,
    sensor_id,
    sensor_byte_id,
    AVG(value)                                               AS avg_value,
    MIN(value)                                               AS min_value,
    MAX(value)                                               AS max_value,
    COUNT(*)                                                 AS sample_count,
    SUM(CASE WHEN level = 'warning'  THEN 1 ELSE 0 END)     AS warning_count,
    SUM(CASE WHEN level = 'critical' THEN 1 ELSE 0 END)     AS critical_count,
    SUM(CASE WHEN crc8_ok THEN 0 ELSE 1 END)                AS crc_error_count
FROM sensor_readings
GROUP BY time_bucket('1 day', time), node_id, sensor_id, sensor_byte_id
WITH NO DATA;

SELECT add_continuous_aggregate_policy('sensor_stats_daily',
    start_offset      => INTERVAL '2 days',
    end_offset        => INTERVAL '1 day',
    schedule_interval => INTERVAL '1 day'
);

COMMENT ON MATERIALIZED VIEW sensor_stats_daily IS
    'Tổng hợp giá trị sensor theo ngày – rollup từ sensor_stats_hourly';


-- ═══════════════════════════════════════════════════════════════════════════
--  PHẦN 4 – VIEWS TIỆN ÍCH
-- ═══════════════════════════════════════════════════════════════════════════

-- Trạng thái hiện tại tất cả cảm biến (cho dashboard)
CREATE VIEW v_sensor_status AS
SELECT
    s.id               AS sensor_id,
    s.name             AS sensor_name,
    s.type,
    s.unit,
    s.current_value,
    s.current_level,
    s.last_reading_at,
    s.warning_threshold,
    s.critical_threshold,
    n.id               AS node_id,
    n.name             AS node_name,
    n.code             AS node_code,
    n.node_byte_id,
    n.status           AS node_status,
    n.distance_m,
    l.id               AS line_id,
    l.name             AS line_name
FROM sensors s
JOIN embedded_nodes n ON s.node_id  = n.id
JOIN lines          l ON n.line_id  = l.id
WHERE s.is_enabled = TRUE;

COMMENT ON VIEW v_sensor_status IS 'Trạng thái hiện tại của tất cả cảm biến – dùng cho dashboard realtime';


-- Cảnh báo đang hoạt động (chưa đóng)
CREATE VIEW v_active_alerts AS
SELECT
    a.*,
    n.name             AS node_display_name,
    n.node_byte_id,
    l.name             AS line_display_name,
    u.full_name        AS acknowledged_by_name
FROM alerts a
LEFT JOIN embedded_nodes n ON a.node_id         = n.id
LEFT JOIN lines          l ON n.line_id         = l.id
LEFT JOIN users          u ON a.acknowledged_by = u.id
WHERE a.state NOT IN ('resolved', 'closed')
ORDER BY
    CASE a.severity
        WHEN 'critical' THEN 1 WHEN 'high' THEN 2
        WHEN 'medium'   THEN 3 ELSE 4
    END,
    a.created_at DESC;

COMMENT ON VIEW v_active_alerts IS 'Cảnh báo đang hoạt động, sắp xếp theo mức độ nghiêm trọng';
