using System.Text;
using System.Text.Json;
using System.Buffers;
using Microsoft.AspNetCore.SignalR;
using BackendV2.Data;
using BackendV2.Hubs;
using BackendV2.Models;
using MQTTnet;


namespace BackendV2.Services
{
    public class MqttSubscriberService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<SensorHub> _hubContext;

        public MqttSubscriberService(IServiceScopeFactory scopeFactory, IHubContext<SensorHub> hubContext)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var mqttFactory = new MqttClientFactory();
            using var mqttClient = mqttFactory.CreateMqttClient();

            var options = new MqttClientOptionsBuilder()
                .WithTcpServer("localhost", 1883)
                .Build();

            mqttClient.ApplicationMessageReceivedAsync += HandleMessageAsync;

            await mqttClient.ConnectAsync(options, stoppingToken);

            var subscribeOptions = mqttFactory.CreateSubscribeOptionsBuilder()
                .WithTopicFilter("sensors/+/reading")
                .Build();

            await mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
        {
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.Payload.ToArray());
            try
            {
                var reading = JsonSerializer.Deserialize<Reading>(payload);

                if (reading is null)
                {
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TimeSeriesDbContext>();

                db.Readings.Add(reading);
                await db.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("NewReading", reading);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving reading: {ex.Message}");
            }
        }
    }
}