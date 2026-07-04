using NodeSim.Config;
using NodeSim.Services;
using SimDevice.Config;
using SimDevice.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<SimOptions>(
    builder.Configuration.GetSection(SimOptions.Section));
builder.Services.Configure<CameraOptions>(
    builder.Configuration.GetSection(CameraOptions.Section));

builder.Services.AddHttpClient("SimDevice");
builder.Services.AddHttpClient("NodeCamera");

// Sensor pipeline — reused as-is from SimDevice
builder.Services.AddSingleton<RandomWalkGenerator>();
builder.Services.AddSingleton<SensorDiscoveryService>();
builder.Services.AddSingleton<HttpDeviceTransport>();
builder.Services.AddHostedService<SimulationWorker>();

// Camera pipeline
builder.Services.AddSingleton<FfmpegCameraSource>();
builder.Services.AddSingleton<CameraPushTransport>();
builder.Services.AddHostedService<CameraPushWorker>();

var host = builder.Build();
host.Run();
