using Center.Config;
using Center.Models;
using Center.Services;
using Center.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace Center
{
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }

        public static SignalRService SignalR { get; } = new SignalRService();
        public static CenterApiService Api { get; private set; } = null!;
        public static DashboardViewModel Dashboard { get; } = new DashboardViewModel();
        public static DataStreamsViewModel DataStreams { get; } = new DataStreamsViewModel();
        public static AlertsViewModel Alerts { get; } = new AlertsViewModel();
        public static MainViewModel Main { get; } = new MainViewModel();

        private DispatcherQueue? _dispatcherQueue;

        public App()
        {
            InitializeComponent();
            EnvironmentConfig.Load();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            var baseUrl = EnvironmentConfig.BackendBaseUrl;
            Api = new CenterApiService(baseUrl);

            WireSignalREvents();

            MainWindow = new MainWindow();
            MainWindow.Activate();

            // Start SignalR connection asynchronously after UI is ready
            _ = Task.Run(() => StartSignalRAsync(baseUrl));

            // Load initial data
            _ = Task.Run(LoadInitialDataAsync);
        }

        private void WireSignalREvents()
        {
            SignalR.SensorUpdated += update =>
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    Dashboard.OnSensorUpdated(update);
                    DataStreams.AddSensorReading(update);
                });
            };

            SignalR.DeviceStatusChanged += update =>
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    Dashboard.OnDeviceStatusChanged(update);
                    DataStreams.AddDeviceStatusChange(update);

                    var level = update.CurrentStatus == "Offline" ? LogLevel.Warning : LogLevel.Info;
                    Dashboard.AddLog(new LogEntry
                    {
                        Timestamp = DateTimeOffset.Now,
                        Level = level,
                        Source = update.Name,
                        Message = $"Device {update.CurrentStatus}: {update.Id}"
                    });
                });
            };

            SignalR.AlertReceived += alert =>
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    Alerts.AddAlert(alert);
                    Main.UnreadAlertCount++;
                    (MainWindow as MainWindow)?.SetAlertBadge(Main.UnreadAlertCount);

                    Dashboard.AddLog(new LogEntry
                    {
                        Timestamp = alert.Timestamp,
                        Level = alert.Severity == AlertSeverity.Critical ? LogLevel.Error : LogLevel.Warning,
                        Source = alert.StationCode,
                        Message = alert.Message
                    });
                });
            };

            SignalR.StationUpdated += station =>
            {
                _dispatcherQueue?.TryEnqueue(() => Dashboard.UpdateStation(station));
            };

            SignalR.Reconnected += () =>
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    Dashboard.SetSignalRConnected(true);
                    DataStreams.SetConnected(true);
                    Dashboard.AddLog(new LogEntry
                    {
                        Timestamp = DateTimeOffset.Now,
                        Level = LogLevel.Info,
                        Source = "SignalR",
                        Message = "Reconnected to backend"
                    });
                });
            };

            SignalR.Disconnected += () =>
            {
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    Dashboard.SetSignalRConnected(false);
                    DataStreams.SetConnected(false);
                    Dashboard.AddLog(new LogEntry
                    {
                        Timestamp = DateTimeOffset.Now,
                        Level = LogLevel.Warning,
                        Source = "SignalR",
                        Message = "Disconnected from backend"
                    });
                });
            };
        }

        private async Task StartSignalRAsync(string baseUrl)
        {
            var hubUrl = $"{baseUrl}/hubs/sensors";
            await SignalR.StartAsync(hubUrl);

            _dispatcherQueue?.TryEnqueue(() =>
            {
                bool connected = SignalR.IsConnected;
                Dashboard.SetSignalRConnected(connected);
                DataStreams.SetConnected(connected);
                Dashboard.AddLog(new LogEntry
                {
                    Timestamp = DateTimeOffset.Now,
                    Level = connected ? LogLevel.Info : LogLevel.Warning,
                    Source = "SignalR",
                    Message = connected
                        ? $"Connected to {hubUrl}"
                        : $"Could not connect to {hubUrl} — will retry"
                });
            });
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                var stations = await Api.GetStationsAsync();
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    Dashboard.TotalStations = stations.Count;
                    foreach (var s in stations)
                        Dashboard.UpdateStation(s);
                });

                var alerts = await Api.GetAlertsAsync();
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var a in alerts)
                        Alerts.AddAlert(a);
                    int unread = alerts.FindAll(a => !a.IsAcknowledged).Count;
                    Main.UnreadAlertCount = unread;
                    (MainWindow as MainWindow)?.SetAlertBadge(unread);
                });
            }
            catch
            {
                // Backend not available yet — SignalR AutoReconnect will handle reconnection
            }
        }
    }
}
