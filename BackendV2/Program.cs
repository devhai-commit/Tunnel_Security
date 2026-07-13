using BackendV2.Data;
using Microsoft.EntityFrameworkCore;
using BackendV2.Hubs;
using BackendV2.Services;
using BackendV2.Middlewares;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

// ── TimescaleDB / PostgreSQL (time-series Readings) ───────────────────────────
// Enabled by TimeSeries:Enabled flag. App starts fine without TimescaleDB running;
// Reading writes gracefully degrade.
if (builder.Configuration.GetValue<bool>("TimeSeries:Enabled"))
{
    var tsConnStr = builder.Configuration.GetConnectionString("TimeSeries")!;
    builder.Services.AddDbContext<TimeSeriesDbContext>(options => options.UseNpgsql(tsConnStr));
}

builder.Services.AddHostedService<MqttSubscriberService>();

builder.Services.AddSingleton<CameraRelayRegistry>();

var app = builder.Build();

// TimescaleDB — EnsureCreated + hypertable init (best-effort; app still starts if Postgres is down)
using (var scope = app.Services.CreateScope())
{
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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.UseStaticFiles();
app.UseWebSockets();
app.UseMiddleware<CameraIngestMiddleware>();
app.UseMiddleware<CameraViewMiddleware>();

app.MapControllers();

app.MapHub<SensorHub>("/hubs/sensor");

app.Run();
