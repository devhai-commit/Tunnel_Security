using System;

namespace Station.Services;

/// <summary>
/// Static accessor trả về IDataService phù hợp dựa vào env var DATA_SOURCE.
/// DATA_SOURCE=mock  → MockDataService (mặc định, backward compat)
/// DATA_SOURCE=api   → RealDataService (kết nối Backend thực)
/// </summary>
public static class DataServiceLocator
{
    private static IDataService? _instance;

    public static IDataService Current => _instance
        ?? throw new InvalidOperationException("DataServiceLocator chưa được khởi tạo. Gọi Initialize() trong App.OnLaunched.");

    public static IDataService Initialize()
    {
        if (_instance != null) return _instance;

        // Default to the real API data service for development so the Station
        // connects to the running backend by default. Override with
        // DATA_SOURCE=mock if you explicitly want the mock service.
        var source = Environment.GetEnvironmentVariable("DATA_SOURCE")?.ToLower() ?? "api";

        _instance = source == "api"
            ? new RealDataService()
            : MockDataService.Instance;

        return _instance;
    }
}
