using Backend.Data;
using Backend.Hubs;
using Backend.Services;
using Backend.Services.Caching;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();

// ── SQL Server (relational topology) ──────────────────────────────────────────
builder.Services.AddDbContext<TunnelDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ── TimescaleDB / PostgreSQL (time-series hypertables) ────────────────────────
// Enabled by TimeSeries:Enabled flag. App starts fine without TimescaleDB running;
// writes gracefully degrade and SignalR still works.
if (builder.Configuration.GetValue<bool>("TimeSeries:Enabled"))
{
    builder.Services.AddDbContext<TimeSeriesDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("TimeSeries")));
}

// ── Application services ──────────────────────────────────────────────────────
builder.Services.AddScoped<DataSeeder>();

// IMemoryCache for sensor/node metadata (avoids 2 DB queries per reading on hot path).
// SizeLimit = 10_000 → each entry uses Size=1, supports up to 10k cached sensor+node pairs.
builder.Services.AddMemoryCache(o => o.SizeLimit = 10_000);
builder.Services.AddScoped<ISensorRepository, SensorRepository>();
builder.Services.AddScoped<SensorBroadcaster>();

builder.Services.AddHttpClient();

// Video capture services
builder.Services.AddHostedService<VideoCaptureService>();
builder.Services.AddSingleton<VideoClipService>();

// SensorBroadcastQueue: registered as singleton so SensorBroadcaster (scoped) can inject it.
// AddHostedService uses the same instance — avoids the double-instantiation trap.
builder.Services.AddSingleton<SensorBroadcastQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SensorBroadcastQueue>());

// DeviceJoinRegistry: singleton — shared between WS handler and REST controller
builder.Services.AddSingleton<DeviceJoinRegistry>();

// VideoSourceRegistry: maps cameraId → video file path for MJPEG simulation
builder.Services.AddSingleton<VideoSourceRegistry>();

// CameraFrameBuffer: holds latest pushed frame per camera (used by simulator push mode)
builder.Services.AddSingleton<CameraFrameBuffer>();

// Periodic clip recorder: saves 10-second clips every 5 minutes for active cameras
builder.Services.AddHostedService<PeriodicClipService>();

// Device ingestion (disabled by default; enable via appsettings)
builder.Services.AddHostedService<MqttIngestionService>();
builder.Services.AddHostedService<HttpPollingService>();
builder.Services.AddHostedService<WebSocketIngestionService>();
builder.Services.AddHostedService<DeviceHealthService>();
builder.Services.AddHostedService<DataRetentionService>();


// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

// ── Startup: apply migrations / seed data ─────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    // SQLite — apply pending migrations (creates DB on first run)
    var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();
    await db.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();

    // TimescaleDB — EnsureCreated + hypertable init
    var ts = scope.ServiceProvider.GetService<TimeSeriesDbContext>();
    if (ts != null)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        try
        {
            await TimescaleInitializer.InitializeAsync(ts, logger);
        }
        catch (Exception ex)
        {
            logger.LogWarning("[Startup] TimescaleDB initialization failed: {Msg} — " +
                              "time-series writes will be skipped", ex.Message);
        }
    }
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// ── Device simulator WebSocket endpoint ──────────────────────────────────────
app.Map("/ws/device", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
    var nodeId = context.Request.Query["nodeId"].FirstOrDefault() ?? "NODE-SIM-01";
    var socket = await context.WebSockets.AcceptWebSocketAsync();
    using var scope = context.RequestServices.CreateScope();
    var broadcaster = scope.ServiceProvider.GetRequiredService<SensorBroadcaster>();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
    await DeviceSimulatorWsHandler.HandleAsync(socket, nodeId, broadcaster, logger, context.RequestAborted);
});

// ── Device join request WebSocket endpoint ────────────────────────────────────
app.Map("/ws/join", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
    var socket   = await context.WebSockets.AcceptWebSocketAsync();
    var registry = context.RequestServices.GetRequiredService<DeviceJoinRegistry>();
    var hub      = context.RequestServices.GetRequiredService<IHubContext<SensorHub>>();
    var logger   = context.RequestServices.GetRequiredService<ILogger<Program>>();
    using var scope = context.RequestServices.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TunnelDbContext>();
    await DeviceJoinWsHandler.HandleAsync(socket, registry, db, hub, logger, context.RequestAborted);
});

// ── Routes ────────────────────────────────────────────────────────────────────
app.MapGet("/", () => new
{
    message = "Tunnel Security Backend API",
    version = "2.0.0",
    databases = new { relational = "SQL Server", timeSeries = "TimescaleDB/PostgreSQL" },
    endpoints = new { swagger = "/swagger", sensors = "/api/sensors",
                      readings = "/api/readings", stations = "/api/stations",
                      signalR = "/hubs/sensors",
                      joinRequests = "/api/device-joins",
                      joinWs = "/ws/join" }
});

app.MapControllers();
app.MapHub<SensorHub>("/hubs/sensors");

app.Run();
