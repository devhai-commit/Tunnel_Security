using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Station.ServicesV2
{
    /// <summary>
    /// REST client for BackendV2 (Auth/Node/Sensor/Camera/Reading endpoints).
    /// </summary>
    public class ApiClient
    {
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public string? AccessToken { get; private set; }
        public string? RefreshToken { get; private set; }

        public ApiClient(string baseUrl)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        public async Task<LoginResultDto> LoginAsync(string username, string password)
        {
            var response = await _http.PostAsJsonAsync("/api/Auth/login", new { username, password });
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<LoginResultDto>(_jsonOpts)
                ?? throw new InvalidOperationException("Login response body was empty.");

            ApplyToken(result.AccessToken, result.RefreshToken);
            return result;
        }

        public async Task<bool> TryRefreshTokenAsync()
        {
            if (string.IsNullOrEmpty(RefreshToken))
                return false;

            var response = await _http.PostAsJsonAsync("/api/Auth/refresh", new { refreshToken = RefreshToken });
            if (!response.IsSuccessStatusCode)
                return false;

            var result = await response.Content.ReadFromJsonAsync<LoginResultDto>(_jsonOpts);
            if (result == null)
                return false;

            ApplyToken(result.AccessToken, result.RefreshToken);
            return true;
        }

        public void SignOut()
        {
            AccessToken = null;
            RefreshToken = null;
            _http.DefaultRequestHeaders.Authorization = null;
        }

        private void ApplyToken(string accessToken, string refreshToken)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        public async Task<List<NodeDto>> GetNodesAsync()
        {
            var json = await _http.GetStringAsync("/api/Node");
            return JsonSerializer.Deserialize<List<NodeDto>>(json, _jsonOpts) ?? new List<NodeDto>();
        }

        public async Task<List<SensorDto>> GetSensorsAsync()
        {
            var json = await _http.GetStringAsync("/api/Sensor");
            return JsonSerializer.Deserialize<List<SensorDto>>(json, _jsonOpts) ?? new List<SensorDto>();
        }

        public async Task<List<CameraDto>> GetCamerasAsync()
        {
            var json = await _http.GetStringAsync("/api/Camera");
            return JsonSerializer.Deserialize<List<CameraDto>>(json, _jsonOpts) ?? new List<CameraDto>();
        }

        // TODO: GetSensorsAsync() -> GET /api/Sensor, GetCamerasAsync() -> GET /api/Camera
        // cùng pattern như GetNodesAsync ở trên.
    }

    /// <summary>
    /// Khớp field-for-field với BackendV2.Models.Node. Status là int vì BackendV2 không
    /// cấu hình JsonStringEnumConverter — enum serialize theo thứ tự khai báo:
    /// 0=Online, 1=Warning, 2=Critical, 3=Offline, 4=Maintenance.
    /// </summary>
    public class NodeDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string Name { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Description { get; set; } = string.Empty;

        public int Status { get; set; }
        public DateTime? LastOnline { get; set; }

        public byte? NodeByteId { get; set; }

        public string? HardwareId { get; set; }
        public string? Mac { get; set; }
        public string? IpAddress { get; set; }
        public string? FirmwareVersion { get; set; }
        public bool IsHub { get; set; }

        public double? BatteryLevel { get; set; }
        public int? RSSI { get; set; }

        public string? CameraId { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class SensorDto
    {
        public string Id { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Type { get; set; } = 0; // 0=Analog, 1=Digital, 2=Temperature, 3=Humidity, 4=Pressure, 5=Light, 6=Sound, 7=Gas, 8=Motion, 9=Other
        public string Unit { get; set; } = string.Empty;
        public double? WarningThreshold { get; set; }
        public double? CriticalThreshold { get; set; }
        public bool IsActive { get; set; }
        public string Description { get; set; } = string.Empty;
        public double? CurrentValue { get; set; }
        public double? CurrentLevel { get; set; }
        public DateTime? LastReadingTime { get; set; }
        public int? SamplingRate { get; set; } // in seconds
        public int? SampleingRateHz { get; set; } // in Hz

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public class CameraDto
    {
        public string Id { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string CameraName { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        // Stream properties
        public string Resolution { get; set; } = "1280x720";
        public int? Fps { get; set; } = 30;
        public string Codec { get; set; } = "H.264";

        // Camera settings
        public bool IrEnabled { get; set; }
        public string IrMode { get; set; } = "AUTO"; // AUTO, ON, OFF
        public bool HdrEnabled { get; set; }
        public string HdrMode { get; set; } = "AUTO";

        // Status
        public bool IsOnline { get; set; }
        public bool IsRecording { get; set; }
        public DateTime? LastFrameTime { get; set; }

        // Stats
        public double Bitrate { get; set; } // Mbps
        public int FrameCount { get; set; }
        public int DroppedFrames { get; set; }
    }

    /// <summary>Khớp field-for-field với BackendV2.DTOs.LoginResponse.</summary>
    public class LoginResultDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
