using SimDevice.Config;
using SimDevice.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SimOptions>(
    builder.Configuration.GetSection(SimOptions.Section));

// Named HttpClient — both services share one underlying pool
builder.Services.AddHttpClient("SimDevice");

builder.Services.AddSingleton<RandomWalkGenerator>();
builder.Services.AddSingleton<SensorDiscoveryService>();
builder.Services.AddSingleton<HttpDeviceTransport>();
builder.Services.AddHostedService<SimulationWorker>();

var host = builder.Build();
host.Run();
