-- ============================================================
-- TunnelSecurity – Auth & Admin Setup Script
-- Database: TunnelSecurity (SQL Server)
-- Generated from: sql/schema.sql + sql/data.sql
-- Run on: SQL Server Management Studio or sqlcmd
-- ============================================================

USE [TunnelSecurity]
GO

-- ============================================================
-- 1. SCHEMA – Tạo bảng (bỏ qua nếu đã tồn tại)
-- ============================================================

-- 1.1 Roles
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Roles')
BEGIN
    CREATE TABLE [dbo].[Roles] (
        [Id]   uniqueidentifier NOT NULL,
        [Code] nvarchar(450)    NOT NULL,
        [Name] nvarchar(450)    NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY CLUSTERED ([Id] ASC)
    )
    PRINT 'Created table: Roles'
END
GO

-- 1.2 FunctionGroups
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'FunctionGroups')
BEGIN
    CREATE TABLE [dbo].[FunctionGroups] (
        [Id]          uniqueidentifier NOT NULL,
        [Code]        nvarchar(450)    NOT NULL,
        [Name]        nvarchar(450)    NOT NULL,
        [Description] nvarchar(max)    NULL,
        CONSTRAINT [PK_FunctionGroups] PRIMARY KEY CLUSTERED ([Id] ASC)
    )
    PRINT 'Created table: FunctionGroups'
END
GO

-- 1.3 RoleFunctionGroups (junction table)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'RoleFunctionGroups')
BEGIN
    CREATE TABLE [dbo].[RoleFunctionGroups] (
        [RoleId]          uniqueidentifier NOT NULL,
        [FunctionGroupId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_RoleFunctionGroups] PRIMARY KEY CLUSTERED ([RoleId] ASC, [FunctionGroupId] ASC),
        CONSTRAINT [FK_RoleFunctionGroups_Roles_RoleId]
            FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RoleFunctionGroups_FunctionGroups_FunctionGroupId]
            FOREIGN KEY ([FunctionGroupId]) REFERENCES [dbo].[FunctionGroups] ([Id]) ON DELETE CASCADE
    )
    PRINT 'Created table: RoleFunctionGroups'
END
GO

-- 1.4 Users
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users')
BEGIN
    CREATE TABLE [dbo].[Users] (
        [Id]                   uniqueidentifier NOT NULL,
        [Username]             nvarchar(450)    NULL,
        [PasswordHash]         nvarchar(max)    NULL,
        [FullName]             nvarchar(max)    NULL,
        [RoleId]               uniqueidentifier NULL,
        [IsActive]             bit              NOT NULL,
        [LastLoginAt]          datetimeoffset(7) NULL,
        [CreatedAt]            datetimeoffset(7) NOT NULL,
        [UpdatedAt]            datetimeoffset(7) NOT NULL,
        [FailedLoginAttempts]  int              NOT NULL CONSTRAINT [DF_Users_FailedLoginAttempts] DEFAULT (0),
        [LastFailedLoginAt]    datetimeoffset(7) NULL,
        [LockoutEndAt]         datetimeoffset(7) NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_Users_Roles_RoleId]
            FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Roles] ([Id]) ON DELETE SET NULL
    )
    PRINT 'Created table: Users'
END
GO

-- 1.5 RefreshTokens
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'RefreshTokens')
BEGIN
    CREATE TABLE [dbo].[RefreshTokens] (
        [Id]               uniqueidentifier NOT NULL,
        [UserId]           uniqueidentifier NOT NULL,
        [TokenHash]        nvarchar(450)    NOT NULL,
        [CreatedAt]        datetimeoffset(7) NOT NULL,
        [ExpiresAt]        datetimeoffset(7) NOT NULL,
        [Revoked]          bit              NOT NULL,
        [ReplacedByToken]  uniqueidentifier NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_RefreshTokens_Users_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE CASCADE
    )
    PRINT 'Created table: RefreshTokens'
END
GO

-- 1.6 AuditLogs
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AuditLogs')
BEGIN
    CREATE TABLE [dbo].[AuditLogs] (
        [Id]            uniqueidentifier NOT NULL,
        [ActorUserId]   uniqueidentifier NULL,
        [Action]        nvarchar(max)    NOT NULL,
        [TargetType]    nvarchar(max)    NOT NULL,
        [TargetId]      nvarchar(max)    NOT NULL,
        [OldValueJson]  nvarchar(max)    NULL,
        [NewValueJson]  nvarchar(max)    NULL,
        [CreatedAt]     datetimeoffset(7) NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_AuditLogs_Users_ActorUserId]
            FOREIGN KEY ([ActorUserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE SET NULL
    )
    PRINT 'Created table: AuditLogs'
END
GO

-- ============================================================
-- 2. SEED DATA – Chèn dữ liệu mặc định (bỏ qua nếu đã có)
-- ============================================================

-- 2.1 FunctionGroups (7 nhóm chức năng)
MERGE [dbo].[FunctionGroups] AS target
USING (VALUES
    ('11111111-1111-1111-1111-111111111111', 'DASHBOARD_MONITORING',    N'Giám sát tổng quan',            N'Màn hình trung tâm, tổng hợp trạng thái toàn trạm'),
    ('27400000-0000-0000-0000-000000000001', 'ALERT_EVENT_MANAGEMENT',  N'Quản lý cảnh báo',              N'Xem, lọc, xác nhận, xử lý, ẩn/mở lại cảnh báo và sự kiện'),
    ('22222222-2222-2222-2222-222222222222', 'MONITORING_DETAIL',       N'Giám sát chi tiết',             N'Giao diện chuyên dụng cho giám sát viên quan sát dữ liệu, camera, AI realtime'),
    ('33333333-3333-3333-3333-333333333333', 'OPERATION_CONTROL',       N'Vận hành và điều khiển',        N'Xác nhận cảnh báo, ẩn cảnh báo, bật/tắt thiết bị và gửi lệnh điều khiển'),
    ('44444444-4444-4444-4444-444444444444', 'DATA_HISTORY_REPORTING',  N'Báo cáo và phân tích xu hướng', N'Tra cứu dữ liệu, xem lịch sử, thống kê, báo cáo và phân tích xu hướng'),
    ('55555555-5555-5555-5555-555555555555', 'SYSTEM_ADMINISTRATION',   N'Quản trị hệ thống',             N'Quản lý user, vai trò, phân quyền, cấu hình hệ thống và audit log'),
    ('b8d83b9e-d243-46c3-8326-611c61f0a782', 'DEVICE_MANAGEMENT',      N'Quản lý thiết bị',              N'Quản lý tuyến, cụm, node, sensor, camera, thiết bị ngoại vi và điều khiển thiết bị')
) AS source ([Id], [Code], [Name], [Description])
ON target.[Id] = source.[Id]
WHEN NOT MATCHED THEN
    INSERT ([Id], [Code], [Name], [Description])
    VALUES (source.[Id], source.[Code], source.[Name], source.[Description]);
PRINT 'Merged FunctionGroups'
GO

-- 2.2 Roles (3 vai trò)
MERGE [dbo].[Roles] AS target
USING (VALUES
    ('edf8e6dd-bad4-42b2-9db6-0b105881d5ce', 'VIEWER',   N'Viewer'),
    ('11111111-1111-1111-1111-111111111111', 'ADMIN',    N'Admin'),
    ('fccc33c5-4017-4bf3-b873-a65ea3974f61', 'OPERATOR', N'Operator')
) AS source ([Id], [Code], [Name])
ON target.[Id] = source.[Id]
WHEN NOT MATCHED THEN
    INSERT ([Id], [Code], [Name])
    VALUES (source.[Id], source.[Code], source.[Name]);
PRINT 'Merged Roles'
GO

-- 2.3 RoleFunctionGroups – Ma trận phân quyền
--
--  VIEWER   : DASHBOARD_MONITORING
--  OPERATOR : DASHBOARD_MONITORING, ALERT_EVENT_MANAGEMENT, MONITORING_DETAIL,
--             DATA_HISTORY_REPORTING, DEVICE_MANAGEMENT
--  ADMIN    : Tất cả 7 nhóm chức năng
--
MERGE [dbo].[RoleFunctionGroups] AS target
USING (VALUES
    -- VIEWER (chỉ xem tổng quan)
    ('edf8e6dd-bad4-42b2-9db6-0b105881d5ce', '11111111-1111-1111-1111-111111111111'),
    -- ADMIN (tất cả)
    ('11111111-1111-1111-1111-111111111111', '11111111-1111-1111-1111-111111111111'),
    ('11111111-1111-1111-1111-111111111111', '27400000-0000-0000-0000-000000000001'),
    ('11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222'),
    ('11111111-1111-1111-1111-111111111111', '33333333-3333-3333-3333-333333333333'),
    ('11111111-1111-1111-1111-111111111111', '44444444-4444-4444-4444-444444444444'),
    ('11111111-1111-1111-1111-111111111111', '55555555-5555-5555-5555-555555555555'),
    ('11111111-1111-1111-1111-111111111111', 'b8d83b9e-d243-46c3-8326-611c61f0a782'),
    -- OPERATOR (không có SYSTEM_ADMINISTRATION)
    ('fccc33c5-4017-4bf3-b873-a65ea3974f61', '11111111-1111-1111-1111-111111111111'),
    ('fccc33c5-4017-4bf3-b873-a65ea3974f61', '27400000-0000-0000-0000-000000000001'),
    ('fccc33c5-4017-4bf3-b873-a65ea3974f61', '22222222-2222-2222-2222-222222222222'),
    ('fccc33c5-4017-4bf3-b873-a65ea3974f61', '44444444-4444-4444-4444-444444444444'),
    ('fccc33c5-4017-4bf3-b873-a65ea3974f61', 'b8d83b9e-d243-46c3-8326-611c61f0a782')
) AS source ([RoleId], [FunctionGroupId])
ON target.[RoleId] = source.[RoleId] AND target.[FunctionGroupId] = source.[FunctionGroupId]
WHEN NOT MATCHED THEN
    INSERT ([RoleId], [FunctionGroupId])
    VALUES (source.[RoleId], source.[FunctionGroupId]);
PRINT 'Merged RoleFunctionGroups'
GO

-- 2.4 Users mặc định (3 tài khoản)
-- Mật khẩu: Admin@123 / Operator@123 / Viewer@123
-- Hash được tạo bằng ASP.NET Identity PasswordHasher<T> (PBKDF2-SHA512)
-- ⚠️ Đổi mật khẩu trong môi trường production

-- Xóa tài khoản cũ (nếu có) để tránh xung đột hash
DELETE FROM [dbo].[Users]
WHERE [Username] IN (N'admin', N'operator', N'viewer')
GO

INSERT INTO [dbo].[Users]
    ([Id], [Username], [PasswordHash], [FullName], [RoleId], [IsActive],
     [LastLoginAt], [CreatedAt], [UpdatedAt], [FailedLoginAttempts], [LastFailedLoginAt], [LockoutEndAt])
VALUES
    (
        NEWID(),
        N'admin',
        N'AQAAAAIAAYagAAAAEK3/Wq/nVBSFA3AafQEd2fmAYJ2o+Ao0iWyzaWL5vU1yU++4hhFJFhZ2AAt++Y4Drg==',
        N'Admin',
        '11111111-1111-1111-1111-111111111111',  -- ADMIN role
        1, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0, NULL, NULL
    ),
    (
        NEWID(),
        N'operator',
        N'AQAAAAIAAYagAAAAEJmfB1soIkrMDfs5bJHtSkutTyZEbjVRqEOECoifJ1CdOiMkakM3UmE5kRMj47Is5A==',
        N'Giám sát viên',
        'fccc33c5-4017-4bf3-b873-a65ea3974f61', -- OPERATOR role
        1, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0, NULL, NULL
    ),
    (
        NEWID(),
        N'viewer',
        N'AQAAAAIAAYagAAAAEKUmhkYnVoQvZGulAfrtRpkYySdhShdRl389p6iG9tsZ50viOhIGRbEiMz0QsKnghQ==',
        N'Người xem',
        'edf8e6dd-bad4-42b2-9db6-0b105881d5ce', -- VIEWER role
        1, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0, NULL, NULL
    )
PRINT 'Seeded Users'
GO

-- ============================================================
-- 3. XÁC NHẬN KẾT QUẢ
-- ============================================================
SELECT 'Roles'             AS [Table], COUNT(*) AS [Rows] FROM [dbo].[Roles]
UNION ALL
SELECT 'FunctionGroups',                COUNT(*) FROM [dbo].[FunctionGroups]
UNION ALL
SELECT 'RoleFunctionGroups',            COUNT(*) FROM [dbo].[RoleFunctionGroups]
UNION ALL
SELECT 'Users',                         COUNT(*) FROM [dbo].[Users]
UNION ALL
SELECT 'RefreshTokens',                 COUNT(*) FROM [dbo].[RefreshTokens]
UNION ALL
SELECT 'AuditLogs',                     COUNT(*) FROM [dbo].[AuditLogs]
GO

-- Xem ma trận phân quyền
SELECT
    r.[Code]  AS [Role],
    fg.[Code] AS [FunctionGroup],
    fg.[Name] AS [FunctionName]
FROM [dbo].[RoleFunctionGroups] rfg
JOIN [dbo].[Roles]          r  ON r.[Id]  = rfg.[RoleId]
JOIN [dbo].[FunctionGroups] fg ON fg.[Id] = rfg.[FunctionGroupId]
ORDER BY r.[Code], fg.[Code]
GO
