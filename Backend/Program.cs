using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend.Hubs;
using Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TunnelSecurity.Auth;
using TunnelSecurity.Backend.Extensions;
using TunnelSecurity.Data.Auth;
using TunnelSecurity.Data.Auth.Models;
using TunnelSecurity.Data.Auth.Services;

var builder = WebApplication.CreateBuilder(args);

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p =>
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddAuth(builder.Configuration);

builder.Services.AddSingleton<BackgroundSensorSimulation>();

var app = builder.Build();

await AuthDbInitializer.InitializeAsync(app.Services);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => new
{
    message = "Tunnel Security Backend API",
    version = "1.0.0",
    endpoints = new
    {
        swagger = "/swagger",
        sensors = "/api/sensors",
        stations = "/api/stations",
        signalR = "/hubs/sensors"
    }
});

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SensorHub>("/hubs/sensors");

var sensorSimulation = app.Services.GetRequiredService<BackgroundSensorSimulation>();
sensorSimulation.Start();

app.Run();
