namespace Backend.Config;

public class MqttConfig
{
    public string BrokerHost { get; set; } = "localhost";
    public int BrokerPort { get; set; } = 1883;
    public string ClientId { get; set; } = "tunnel-backend";
    public string TopicPrefix { get; set; } = "tunnel";
    public bool Enabled { get; set; }
}

public class HttpEndpointConfig
{
    public string DeviceId { get; set; } = default!;
    public string Url { get; set; } = default!;
    public string SensorId { get; set; } = default!;
}

public class HttpPollingConfig
{
    public int PollIntervalSeconds { get; set; } = 30;
    public bool Enabled { get; set; }
    public List<HttpEndpointConfig> Endpoints { get; set; } = new();
}

public class WebSocketEndpointConfig
{
    public string DeviceId { get; set; } = default!;
    public string Url { get; set; } = default!;
}

public class WebSocketConfig
{
    public bool Enabled { get; set; }
    public List<WebSocketEndpointConfig> Endpoints { get; set; } = new();
}

public class HealthCheckConfig
{
    public int IntervalSeconds { get; set; } = 30;
    public int OfflineTimeoutSeconds { get; set; } = 60;
}
