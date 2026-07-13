using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendV2.Migrations
{
    /// <inheritdoc />
    public partial class BackendParityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Readings");

            // Existing Sensors.Type rows hold enum names as strings (e.g. "temperature").
            // Convert them to their numeric ordinal before narrowing the column to int,
            // otherwise SQL Server's implicit CAST in ALTER COLUMN fails.
            migrationBuilder.Sql(@"
                UPDATE Sensors SET Type = CASE LOWER(Type)
                    WHEN 'radar' THEN '0'
                    WHEN 'vibration' THEN '1'
                    WHEN 'smokefire' THEN '2'
                    WHEN 'temperature' THEN '3'
                    WHEN 'humidity' THEN '4'
                    WHEN 'gas' THEN '5'
                    WHEN 'pressure' THEN '6'
                    WHEN 'waterlevel' THEN '7'
                    WHEN 'motion' THEN '8'
                    ELSE Type
                END;
            ");

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "Sensors",
                type: "int",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Sensors",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<double>(
                name: "CriticalThreshold",
                table: "Sensors",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentLevel",
                table: "Sensors",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CurrentValue",
                table: "Sensors",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsEnabled",
                table: "Sensors",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReading",
                table: "Sensors",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SamplingRate",
                table: "Sensors",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "SamplingRateHz",
                table: "Sensors",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "SensorByteId",
                table: "Sensors",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "Sensors",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Sensors",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WarningThreshold",
                table: "Sensors",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BatteryLevel",
                table: "Nodes",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CameraId",
                table: "Nodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Nodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Nodes",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "FirmwareVersion",
                table: "Nodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HardwareId",
                table: "Nodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "Nodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHub",
                table: "Nodes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOnline",
                table: "Nodes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Mac",
                table: "Nodes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "NodeByteId",
                table: "Nodes",
                type: "tinyint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RSSI",
                table: "Nodes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Nodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Nodes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Codec",
                table: "Cameras",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Cameras",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "Fps",
                table: "Cameras",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HdrEnabled",
                table: "Cameras",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IrEnabled",
                table: "Cameras",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRecording",
                table: "Cameras",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFrameTime",
                table: "Cameras",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Protocol",
                table: "Cameras",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                table: "Cameras",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Cameras",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StreamUrl",
                table: "Cameras",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "CriticalThreshold",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "CurrentLevel",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "CurrentValue",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "IsEnabled",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "LastReading",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "SamplingRate",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "SamplingRateHz",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "SensorByteId",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "WarningThreshold",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "BatteryLevel",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "CameraId",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "FirmwareVersion",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "HardwareId",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "IsHub",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "LastOnline",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Mac",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "NodeByteId",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "RSSI",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "Codec",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "Fps",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "HdrEnabled",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "IrEnabled",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "IsRecording",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "LastFrameTime",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "Protocol",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "Resolution",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Cameras");

            migrationBuilder.DropColumn(
                name: "StreamUrl",
                table: "Cameras");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Sensors",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "Readings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SensorId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Readings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Readings_Sensors_SensorId",
                        column: x => x.SensorId,
                        principalTable: "Sensors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Readings_SensorId_Timestamp",
                table: "Readings",
                columns: new[] { "SensorId", "Timestamp" });
        }
    }
}
