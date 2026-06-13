using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Station.Dialogs;
using Station.Services;
using Microsoft.UI.Xaml;
using Station.ViewModels;
using CommunityToolkit.WinUI.Controls.SettingsControlsRns;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Station.Config;
using Windows.Storage.Streams;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Station.Views
{
    public sealed partial class MonitoringDashboardPage : Page
    {
        public MonitoringDashboardViewModel ViewModel { get; }

        private readonly ThemeService _themeService;

        // Config sẽ được load từ .env file
        private string BackendBaseUrl => EnvironmentConfig.BackendBaseUrl;
        private string StationId => EnvironmentConfig.StationId;
        private string MapboxToken => EnvironmentConfig.MapboxAccessToken;
        private bool _securityMapInitialized = false;

        public ObservableCollection<SystemLogItem> SystemLogs { get; } = new();

        // Camera rotation variables
        private DispatcherTimer _cameraRotationTimer;
        private DispatcherTimer _cameraTimeTimer;
        private int _currentCameraIndex = 0;
		// Máy phát âm thanh cảnh báo chạy ngầm
		private readonly MediaPlayer _alarmPlayer = new MediaPlayer();
		private int _rotationCountdown = 10;
        private bool _isPaused = false;
        private string _focusedCamera = null; // Camera to focus when alert detected
        // Cameras are loaded live from MockDataService
        private readonly MockDataService _mockForCameras = MockDataService.Instance;

        // Alert filter variables
        private enum AlertFilterPeriod { Day, Week, Month }

        // Alert notification badge
        private int _pendingAlertCount = 0;
		private List<Station.Models.Alert> _pendingAlerts = new();
		private bool _alertDialogOpen = false;

        public MonitoringDashboardPage()
        {
            InitializeComponent();

            ViewModel = (MonitoringDashboardViewModel)DataContext;

            _themeService = ThemeService.Instance;

            // Subscribe theme changes
            _themeService.ThemeChanged += OnThemeChanged;

            // Apply current theme to icons
            UpdateThemeIcons(_themeService.CurrentTheme);

            // Subscribe to alert events for flash + badge
            MockDataService.Instance.AlertGenerated += OnAlertGeneratedForUI;

            // Initialize WebView2 + Mapbox HTML
            InitializeSecurityMap();

            // Initialize system logs
            InitializeSystemLogs();

            // Initialize camera rotation
            InitializeCameraRotation();
        }

        private void InitializeSystemLogs()
        {
            // Note: SystemLogsItems removed from XAML, keeping logs in memory
            // SystemLogsItems.ItemsSource = SystemLogs;

            // Add mock data
            AddSystemLog("✅", "Hệ thống khởi động thành công", "SYSTEM", "INFO", DateTime.Now.AddMinutes(-5));
            AddSystemLog("🔌", "RELAY_A kết nối thành công", "RELAY_A", "SUCCESS", DateTime.Now.AddMinutes(-4));
            AddSystemLog("🔌", "RELAY_B kết nối thành công", "RELAY_B", "SUCCESS", DateTime.Now.AddMinutes(-4));
            AddSystemLog("🔌", "RELAY_C kết nối thành công", "RELAY_C", "SUCCESS", DateTime.Now.AddMinutes(-3));
            AddSystemLog("📡", "S01: Radar đang hoạt động", "SENSOR", "INFO", DateTime.Now.AddMinutes(-2));
            AddSystemLog("🌡️", "S04: Nhiệt độ: 28.5°C", "SENSOR", "INFO", DateTime.Now.AddMinutes(-1));
            AddSystemLog("💧", "S05: Độ ẩm: 65%", "SENSOR", "INFO", DateTime.Now.AddSeconds(-30));
            AddSystemLog("⚠️", "S12: Phát hiện chuyển động", "ALERT", "WARNING", DateTime.Now.AddSeconds(-10));

            // Auto update logs every 10 seconds
            StartLogUpdateTimer();
        }

        private void AddSystemLog(string icon, string message, string source, string level, DateTime time)
        {
            var log = new SystemLogItem
            {
                Icon = icon,
                Message = message,
                Source = source,
                Level = level,
                Time = time.ToString("HH:mm:ss"),
                Timestamp = time
            };

            SystemLogs.Insert(0, log);

            // Keep only last 20 logs
            while (SystemLogs.Count > 20)
            {
                SystemLogs.RemoveAt(SystemLogs.Count - 1);
            }
        }

        private async void StartLogUpdateTimer()
        {
            while (true)
            {
                await System.Threading.Tasks.Task.Delay(10000); // 10 seconds

                var random = new Random();
                var logTypes = new[]
                {
                    ("📡", "Dữ liệu cảm biến cập nhật", "SENSOR", "INFO"),
                    ("🌡️", $"Nhiệt độ: {25 + random.Next(10)}.{random.Next(10)}°C", "SENSOR", "INFO"),
                    ("💧", $"Độ ẩm: {60 + random.Next(20)}%", "SENSOR", "INFO"),
                    ("🔄", "Đồng bộ dữ liệu thành công", "SYSTEM", "SUCCESS"),
                    ("📶", $"Tín hiệu mạng: {85 + random.Next(15)}%", "NETWORK", "INFO")
                };

                var selected = logTypes[random.Next(logTypes.Length)];
                AddSystemLog(selected.Item1, selected.Item2, selected.Item3, selected.Item4, DateTime.Now);
            }
        }

        private void AdminButton_Click(object sender, RoutedEventArgs e)
        {
            OpenModuleWindow("Quản trị người dùng", typeof(UserManagementPage));
        }

		private void PlayAlarmSound(Station.Models.AlertSeverity severity)
		{
			try
			{
				string soundFileName = severity switch
				{
					Station.Models.AlertSeverity.Critical => "critical.mp3",
					Station.Models.AlertSeverity.High => "high.mp3",
					Station.Models.AlertSeverity.Medium => "medium.mp3",
					_ => "low.mp3"
				};
				var uri = new Uri($"ms-appx:///Assets/Sounds/{soundFileName}");
				_alarmPlayer.Source = MediaSource.CreateFromUri(uri);
				_alarmPlayer.Volume = severity == Station.Models.AlertSeverity.Critical ? 1.0 : 0.6;
				_alarmPlayer.Play();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"❌ Lỗi âm thanh: {ex.Message}");
			}
		}
		private void AlertTimeFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                // Get resources safely
                var transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                var whiteBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
                var secondaryBrush = Application.Current.Resources.TryGetValue("MonitoringTextSecondaryBrush", out var secBrush)
                    ? (SolidColorBrush)secBrush
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 139, 148, 158));
                var borderBrush = Application.Current.Resources.TryGetValue("MonitoringBorderBrush", out var brdBrush)
                    ? (SolidColorBrush)brdBrush
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 48, 54, 61));
                var accentBrush = Application.Current.Resources.TryGetValue("MonitoringAccentButtonBrush", out var accBrush)
                    ? (SolidColorBrush)accBrush
                    : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 33, 150, 243));

                // Reset all filter buttons
                AlertDayButton.Background = transparentBrush;
                AlertDayButton.Foreground = secondaryBrush;
                AlertDayButton.BorderBrush = borderBrush;
                AlertDayButton.BorderThickness = new Thickness(1);

                AlertWeekButton.Background = transparentBrush;
                AlertWeekButton.Foreground = secondaryBrush;
                AlertWeekButton.BorderBrush = borderBrush;
                AlertWeekButton.BorderThickness = new Thickness(1);

                AlertMonthButton.Background = transparentBrush;
                AlertMonthButton.Foreground = secondaryBrush;
                AlertMonthButton.BorderBrush = borderBrush;
                AlertMonthButton.BorderThickness = new Thickness(1);

                // Set clicked button as active
                button.Background = accentBrush;
                button.Foreground = whiteBrush;
                button.BorderThickness = new Thickness(0);

                // Get filter type
                string filterTag = button.Tag?.ToString() ?? "Day";
                Debug.WriteLine($"Alert time filter changed to: {filterTag}");

                // TODO: Update alert distribution chart based on time filter
            }
        }

		private async void InitializeSecurityMap()
		{
			try
			{
				await SecurityMapWebView.EnsureCoreWebView2Async();

				// CHỈ CẦN DUY NHẤT 2 DÒNG NÀY ĐỂ MỞ ĐƯỜNG CHO JS ĐỌC FILE
				var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
				SecurityMapWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
					"app.local", assetsPath, CoreWebView2HostResourceAccessKind.Allow);

				// Các dòng còn lại giữ nguyên
				SecurityMapWebView.CoreWebView2.Navigate("https://app.local/Map/map.html");

				SecurityMapWebView.NavigationCompleted += SecurityMapWebView_NavigationCompleted;
				SecurityMapWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Lỗi Init Map: {ex.Message}");
			}
		}

		private async void SecurityMapWebView_NavigationCompleted(
            WebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess)
            {
                Debug.WriteLine($"SecurityMap navigation failed: {args.WebErrorStatus}");
                return;
            }

            // Đảm bảo chỉ init 1 lần
            if (_securityMapInitialized)
                return;

            _securityMapInitialized = true;

            try
            {
                // Chờ 1 chút cho JS khởi tạo xong
                await System.Threading.Tasks.Task.Delay(300);

                SendInitMessageToMap();
                ApplyThemeToSecurityMap(_themeService.CurrentTheme);

                Debug.WriteLine("Security map initialized & config sent.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in NavigationCompleted: {ex.Message}");
            }
        }

        /// <summary>
        /// Gửi cấu hình ban đầu (backend, station, token) sang HTML (map.html)
        /// JS trong map.html sẽ nhận qua window.chrome.webview.addEventListener('message', ...)
        /// </summary>
        private void SendInitMessageToMap()
        {
            try
            {
                if (SecurityMapWebView?.CoreWebView2 == null)
                    return;

                var initPayload = new
                {
                    type = "init",
                    backend = BackendBaseUrl,
                    station = StationId,
                    token = MapboxToken
                };

                var json = JsonSerializer.Serialize(initPayload);
                SecurityMapWebView.CoreWebView2.PostWebMessageAsJson(json);

                Debug.WriteLine($"Sent init message to map: {json}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending init message: {ex.Message}");
            }
        }

		private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
		{
			try
			{
				var message = args.TryGetWebMessageAsString();
				if (string.IsNullOrEmpty(message)) return;

				var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
				var data = JsonSerializer.Deserialize<SecurityMapMessage>(message, options);

				if (data == null) return;

				switch (data.Type?.ToLower())
				{
					case "mapready":
						Debug.WriteLine("Security map is ready");
						break;
					case "viewcamera":
						HandleViewCamera(data.CameraId, data.NodeId);
						break;
					case "managedevice":
						HandleManageDevice(data.NodeId);
						break;

					// --- THÊM TRƯỜNG HỢP NÀY ĐỂ LƯU FILE ---
					case "node-moved":
						SaveNodePosition(data.NodeId, data.Lng, data.Lat);
						break;
						// --------------------------------------
				}
			}
			catch (Exception ex) { Debug.WriteLine($"Error: {ex.Message}"); }
		}
		private async void HandleViewCamera(string? cameraId, string? nodeId)
        {
            if (string.IsNullOrEmpty(cameraId))
                return;

            Debug.WriteLine($"View camera: {cameraId} for node: {nodeId}");

            try
            {
                var playbackDialog = new PlaybackDialog(cameraId)
                {
                    XamlRoot = this.XamlRoot
                };

                await playbackDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening PlaybackDialog: {ex.Message}");

                var errorDialog = new ContentDialog
                {
                    Title = "Lỗi",
                    Content = $"Không thể mở camera: {ex.Message}",
                    CloseButtonText = "Đóng",
                    XamlRoot = this.XamlRoot
                };

                await errorDialog.ShowAsync();
            }
        }

        private async void HandleManageDevice(string? nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
                return;

            Debug.WriteLine($"Manage device for node: {nodeId}");

            string deviceType = GetDeviceTypeFromNodeId(nodeId);

            var dialog = new DeviceControlDialog(nodeId, deviceType)
            {
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private string GetDeviceTypeFromNodeId(string nodeId)
        {
            if (nodeId.StartsWith("CAM", StringComparison.OrdinalIgnoreCase) ||
                nodeId.Contains("Camera", StringComparison.OrdinalIgnoreCase))
            {
                return "Camera";
            }
            else if (nodeId.StartsWith("SEN", StringComparison.OrdinalIgnoreCase) ||
                     nodeId.Contains("Sensor", StringComparison.OrdinalIgnoreCase))
            {
                return "Sensor";
            }
            else if (nodeId.StartsWith("RAD", StringComparison.OrdinalIgnoreCase) ||
                     nodeId.Contains("Radar", StringComparison.OrdinalIgnoreCase))
            {
                return "Radar";
            }

            return "Sensor";
        }

        /// <summary>
        /// Update 1 node trên map (JS phải có hàm updateNode(node))
        /// </summary>
        public async void UpdateNodeInMap(string nodeId, object nodeData)
        {
            try
            {
                if (SecurityMapWebView?.CoreWebView2 == null)
                    return;

                var json = JsonSerializer.Serialize(nodeData);
                var script = $"updateNode({json})";

                await SecurityMapWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating node in map: {ex.Message}");
            }
        }

        /// <summary>
        /// Update nhiều node trên map (JS phải có hàm updateNodes(nodes))
        /// </summary>
        public async void UpdateNodesInMap(object[] nodesData)
        {
            try
            {
                if (SecurityMapWebView?.CoreWebView2 == null)
                    return;

                var json = JsonSerializer.Serialize(nodesData);
                var script = $"updateNodes({json})";

                await SecurityMapWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating nodes in map: {ex.Message}");
            }
        }

        // ==== Relay Station Card Events ====

        private void RelayCard_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderThickness = new Thickness(2);
                border.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DeepSkyBlue);
            }
        }

        private void RelayCard_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderThickness = new Thickness(1);
            }
        }

        private async void RelayCard_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Border border && border.Tag is string relayId)
            {
                Debug.WriteLine($"Relay station clicked: {relayId}");
                await ShowRelayDataDialog(relayId);
            }
        }

        private async System.Threading.Tasks.Task ShowRelayDataDialog(string relayId)
        {
            try
            {
                var dialog = new DeviceDataDialog(relayId)
                {
                    XamlRoot = this.XamlRoot
                };

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening relay data dialog: {ex.Message}");

                var errorDialog = new ContentDialog
                {
                    Title = "Lỗi",
                    Content = $"Không thể mở thông tin trạm: {ex.Message}",
                    CloseButtonText = "Đóng",
                    XamlRoot = this.XamlRoot
                };

                await errorDialog.ShowAsync();
            }
        }

        // ==== Phần menu & theme giữ nguyên ====

        private void DataPanelMenuButton_Click(object sender, RoutedEventArgs e)
        {
            OpenModuleWindow("Giám sát dữ liệu", typeof(SensorChartsPage));
        }

        private void TrendPanelMenuButton_Click(object sender, RoutedEventArgs e)
        {
            OpenModuleWindow("Phân tích xu hướng", typeof(AnalyticsReportPage));
        }

        private void AlertPanelMenuButton_Click(object sender, RoutedEventArgs e)
        {
            OpenModuleWindow("Cảnh báo", typeof(AlertsPage));
        }

        private void CameraMenuButton_Click(object sender, RoutedEventArgs e)
        {
            OpenModuleWindow("Camera giám sát", typeof(LiveVideoPage));
        }

        private void CameraPanelMenuButton_Click(object sender, RoutedEventArgs e)
        {
            OpenModuleWindow("Camera giám sát", typeof(LiveVideoPage));
        }

        private void DevicePanelMenuButton_Click(object sender, RoutedEventArgs e)
        {
            OpenModuleWindow("Thiết bị", typeof(DevicesPage));
        }

        private void ConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            OpenModuleWindow("Cấu hình", typeof(ConfigurationPage));
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _themeService.ToggleTheme();
        }

        private void OnThemeChanged(object? sender, ElementTheme theme)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateThemeIcons(theme);
                ApplyThemeToSecurityMap(theme);
            });
        }

        private async void ApplyThemeToSecurityMap(ElementTheme theme)
        {
            try
            {
                if (SecurityMapWebView?.CoreWebView2 == null)
                    return;

                var themeString = theme == ElementTheme.Light ? "Light" : "Dark";
                var script = $"setTheme('{themeString}')";

                await SecurityMapWebView.CoreWebView2.ExecuteScriptAsync(script);
                Debug.WriteLine($"Applied theme to security map: {themeString}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying theme to security map: {ex.Message}");
            }
        }

        private void UpdateThemeIcons(ElementTheme theme)
        {
            if (theme == ElementTheme.Dark)
            {
                MoonIcon.Visibility = Visibility.Visible;
                SunIcon.Visibility = Visibility.Collapsed;
            }
            else
            {
                MoonIcon.Visibility = Visibility.Collapsed;
                SunIcon.Visibility = Visibility.Visible;
            }
        }

        private void OpenModuleWindow(string title, Type pageType)
        {
            try
            {
                if (Application.Current is App app && app.m_window is MainWindow mainWindow)
                {
                    if (pageType == typeof(SensorChartsPage))
                    {
                        mainWindow.OpenPageInNewWindow<SensorChartsPage>(title);
                    }
                    else if (pageType == typeof(AlertsPage))
                    {
                        mainWindow.OpenPageInNewWindow<AlertsPage>(title);
                    }
                    else if (pageType == typeof(LiveVideoPage))
                    {
                        mainWindow.OpenPageInNewWindow<LiveVideoPage>(title);
                    }
                    else if (pageType == typeof(DevicesPage))
                    {
                        mainWindow.OpenPageInNewWindow<DevicesPage>(title);
                    }
                    else if (pageType == typeof(ConfigurationPage))
                    {
                        mainWindow.OpenPageInNewWindow<ConfigurationPage>(title);
                    }
                    else if (pageType == typeof(UserManagementPage))
                    {
                        mainWindow.OpenPageInNewWindow<UserManagementPage>(title);
                    }
                    else if (pageType == typeof(AnalyticsReportPage))
                    {
                        mainWindow.OpenPageInNewWindow<AnalyticsReportPage>(title);
                    }
                    else
                    {
                        Debug.WriteLine($"Unknown page type: {pageType.Name}");
                    }
                }
                else
                {
                    Debug.WriteLine("Could not get MainWindow instance");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening module window: {ex.Message}");
            }
        }

		private void SaveNodePosition(string nodeId, double lng, double lat)
		{
			try
			{
				string filePath = "";

#if DEBUG
				// 1. Lấy vị trí Bản Photo đang chạy (Thường nằm tít trong bin/x64/Debug/...)
				DirectoryInfo? currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

				// 2. Tự động leo ngược lên trên cho đến khi tìm thấy thư mục gốc chứa "Assets"
				while (currentDir != null)
				{
					// Kiểm tra xem ở đây có thư mục "Assets/Map" chưa?
					string checkPath = Path.Combine(currentDir.FullName, "Assets", "Map", "nodes.json");

					// Nếu tìm thấy file nodes.json ở đây, nghĩa là đã về tới nhà (Bản Gốc)
					if (File.Exists(checkPath))
					{
						filePath = checkPath;
						break; // Dừng việc leo ngược lại
					}

					// Nếu chưa thấy, lùi lên một cấp thư mục (Back)
					currentDir = currentDir.Parent;
				}

				// Nếu leo hết cỡ vẫn không thấy (hiếm khi xảy ra), dùng tạm Bản Photo
				if (string.IsNullOrEmpty(filePath))
				{
					filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Map", "nodes.json");
				}
#else
        // Khi đóng gói đem cài cho khách hàng (Release) thì dùng đường dẫn cài đặt
        filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Map", "nodes.json");
#endif

				// 3. Tiến hành ghi đè tọa độ mới vào file đã tìm được
				if (!File.Exists(filePath)) return;

				string jsonContent = File.ReadAllText(filePath);
				using var doc = JsonDocument.Parse(jsonContent);
				var root = doc.RootElement.Clone();

				var features = root.GetProperty("features").EnumerateArray().Select(f => {
					var props = f.GetProperty("properties");
					if (props.GetProperty("id").GetString() == nodeId)
					{
						// Thay thế tọa độ cũ bằng tọa độ mới (lng, lat)
						return new
						{
							type = "Feature",
							geometry = new { type = "Point", coordinates = new[] { lng, lat } },
							properties = f.GetProperty("properties")
						};
					}
					return (object)f;
				}).ToList();

				var updatedData = new { type = "FeatureCollection", features = features };

				// Ghi lại vào file với định dạng đẹp (xuống dòng, lùi lề)
				string updatedJson = JsonSerializer.Serialize(updatedData, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(filePath, updatedJson);

				Debug.WriteLine($"✅ Đã lưu CẬP NHẬT vào: {filePath}");
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"❌ Lỗi: {ex.Message}");
			}
		}
		private class SecurityMapMessage
		{
			[JsonPropertyName("type")]
			public string? Type { get; set; }

			[JsonPropertyName("cameraId")]
			public string? CameraId { get; set; }

			[JsonPropertyName("nodeId")]
			public string? NodeId { get; set; }

			// Thêm 2 dòng này
			[JsonPropertyName("lng")]
			public double Lng { get; set; }

			[JsonPropertyName("lat")]
			public double Lat { get; set; }
		}
		#region Camera Rotation Methods

		private void InitializeCameraRotation()
        {
            // Timer for camera rotation
            _cameraRotationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _cameraRotationTimer.Tick += CameraRotationTimer_Tick;
            _cameraRotationTimer.Start();

            // Timer for camera time display
            _cameraTimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _cameraTimeTimer.Tick += CameraTimeTimer_Tick;
            _cameraTimeTimer.Start();

            // Display first camera
            UpdateCurrentCamera();

            // Simulate alert detection after 15 seconds
            SimulateAlertDetection();
        }

        private void CameraRotationTimer_Tick(object sender, object e)
        {
            if (_isPaused || _focusedCamera != null)
                return;

            var cameras = _mockForCameras.Cameras;
            if (cameras.Count == 0) return;

            _rotationCountdown--;

            if (_rotationCountdown <= 0)
            {
                _currentCameraIndex = (_currentCameraIndex + 1) % cameras.Count;
                UpdateCurrentCamera();
                _rotationCountdown = 10;
            }

            var nextIndex = (_currentCameraIndex + 1) % cameras.Count;
            NextCameraInfo.Text = $"Tiếp: {cameras[nextIndex].CameraId} ({_rotationCountdown}s)";
        }

        private void CameraTimeTimer_Tick(object sender, object e)
        {
            CurrentCameraTime.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        private void UpdateCurrentCamera()
        {
            var cameras = _mockForCameras.Cameras;
            if (cameras.Count == 0) return;

            Services.SimulatedCamera cam;
            if (_focusedCamera != null)
            {
                cam = cameras.FirstOrDefault(c => c.CameraId == _focusedCamera)
                      ?? cameras[_currentCameraIndex % cameras.Count];
            }
            else
            {
                cam = cameras[_currentCameraIndex % cameras.Count];
            }

            CurrentCameraName.Text = $"{cam.CameraId} — {cam.Location}";
            bool isOnline = cam.IsOnline;

            // Update status badge
            if (isOnline)
            {
                CurrentCameraStatus.Text = "Online";
                CurrentCameraStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94)); // Green
                NoSignalOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                CurrentCameraStatus.Text = "Offline";
                CurrentCameraStatusBadge.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)); // Red
                NoSignalOverlay.Visibility = Visibility.Visible;
            }

            // Update rotation info
            if (_focusedCamera != null)
            {
                CameraRotationInfo.Text = "🔴 Đang focus (Cảnh báo)";
                NextCameraInfo.Text = "Chờ xử lý...";
            }
            else if (_isPaused)
            {
                CameraRotationInfo.Text = "⏸️ Đã tạm dừng";
                NextCameraInfo.Text = "";
            }
            else
            {
                CameraRotationInfo.Text = "Tự động: 10s";
            }
        }

        private void CameraPauseButton_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;

            if (_isPaused)
            {
                CameraPauseIcon.Glyph = "\uE768"; // Play icon
                CameraRotationInfo.Text = "⏸️ Đã tạm dừng";
                NextCameraInfo.Text = "";
            }
            else
            {
                CameraPauseIcon.Glyph = "\uE769"; // Pause icon
                CameraRotationInfo.Text = "Tự động: 10s";
                _rotationCountdown = 10;
            }
        }

        private async void SimulateAlertDetection()
        {
            await System.Threading.Tasks.Task.Delay(15000);

            // Focus on first online camera from MockDataService
            var firstOnline = _mockForCameras.Cameras.FirstOrDefault(c => c.IsOnline);
            if (firstOnline == null) return;
            FocusOnCameraAlert(firstOnline.CameraId, "Phát hiện chuyển động bất thường");

            // After 10 seconds, clear focus and resume rotation
            await System.Threading.Tasks.Task.Delay(10000);
            ClearCameraFocus();
        }

        private void FocusOnCameraAlert(string cameraName, string alertMessage)
        {
            _focusedCamera = cameraName;
            var cameras = _mockForCameras.Cameras;
            int idx = 0;
            for (int i = 0; i < cameras.Count; i++)
            {
                if (cameras[i].CameraId == cameraName) { idx = i; break; }
            }
            _currentCameraIndex = idx;

            UpdateCurrentCamera();

            // Show alert overlay
            AlertMessageText.Text = $"⚠️ {alertMessage.ToUpper()}";
            AlertOverlay.Visibility = Visibility.Visible;

            Debug.WriteLine($"Camera focus: {cameraName} - {alertMessage}");
        }

        private void ClearCameraFocus()
        {
            _focusedCamera = null;
            AlertOverlay.Visibility = Visibility.Collapsed;
            _rotationCountdown = 10;
            UpdateCurrentCamera();

            Debug.WriteLine("Camera focus cleared, resuming rotation");
        }

		#endregion

		#region Alert Filter Handlers - Removed (UI redesigned)
		// Old alert filter methods removed as UI was redesigned
		#endregion

		#region Alert Notification (Flash + Badge)

		private void OnAlertGeneratedForUI(object? sender, Station.Services.AlertGeneratedEventArgs e)
		{
			DispatcherQueue.TryEnqueue(() =>
			{
				// Thêm vào hàng đợi và xếp hạng ưu tiên (Khẩn cấp lên đầu)
				_pendingAlerts.Add(e.Alert);
				_pendingAlerts = _pendingAlerts.OrderByDescending(a => (int)a.Severity).ToList();
				_pendingAlertCount = _pendingAlerts.Count;

				// 1. Kích hoạt hiệu ứng UI (Badge)
				if (e.Alert.Severity != Station.Models.AlertSeverity.Low)
				{
					AlertBadgeCountText.Text = _pendingAlertCount.ToString();
					AlertNotificationBadge.Visibility = Visibility.Visible;
				}

				if (!string.IsNullOrEmpty(e.Alert.CameraId))
					FocusOnCameraAlert(e.Alert.CameraId, e.Alert.Title);

				// 2. BẮN LỆNH BẬT NHÁY XUỐNG BẢN ĐỒ
				if (_securityMapInitialized && SecurityMapWebView != null && e.Alert.NodeId != null)
				{
					var payload = new
					{
						type = "highlight-node",
						nodeId = e.Alert.NodeId,
						severity = e.Alert.Severity.ToString()
					};
					var json = JsonSerializer.Serialize(payload);
					SecurityMapWebView.CoreWebView2.PostWebMessageAsJson(json);
				}

				// 3. Nếu là lỗi Khẩn Cấp -> Ép bật Popup luôn
				if (e.Alert.Severity == Station.Models.AlertSeverity.Critical)
				{
					RedFlashStoryboard.Begin();
					if (!_alertDialogOpen)
					{
						AlertNotificationBadge_Click(null, null);
					}
				}
			});
		}

		private async void AlertNotificationBadge_Click(object sender, RoutedEventArgs e)
		{
			if (_alertDialogOpen || _pendingAlerts.Count == 0) return;
			_alertDialogOpen = true;

			RedFlashStoryboard.Stop();
			RedFlashOverlay.Opacity = 0;

			// Xử lý lần lượt từng cảnh báo trong hàng đợi
			while (_pendingAlerts.Count > 0)
			{
				var currentAlert = _pendingAlerts.First();

				// Mức Thấp (Low) không cần xem Popup
				if (currentAlert.Severity == Station.Models.AlertSeverity.Low)
				{
					_pendingAlerts.Remove(currentAlert);
					continue;
				}

				var dialog = new Station.Dialogs.AlertVideoDialog(currentAlert)
				{
					XamlRoot = this.XamlRoot
				};

				await dialog.ShowAsync();

				if (dialog.WasAcknowledged)
				{
					// Đã bấm Xác nhận -> Xóa khỏi hàng đợi
					_pendingAlerts.Remove(currentAlert);

					// BẮN LỆNH TẮT NHÁY, TRẢ VỀ XANH CHO BẢN ĐỒ
					if (_securityMapInitialized && SecurityMapWebView != null && currentAlert.NodeId != null)
					{
						var payload = new { type = "restore-node", nodeId = currentAlert.NodeId };
						var json = JsonSerializer.Serialize(payload);
						SecurityMapWebView.CoreWebView2.PostWebMessageAsJson(json);
					}
				}
				else
				{
					// Bấm Hủy/Đóng -> Tạm dừng xem, để lại trong Badge
					break;
				}
			}

			// Tính toán lại trạng thái Badge
			_pendingAlertCount = _pendingAlerts.Count;
			if (_pendingAlertCount == 0)
			{
				AlertNotificationBadge.Visibility = Visibility.Collapsed;
			}
			else
			{
				AlertBadgeCountText.Text = _pendingAlertCount.ToString();
				if (_pendingAlerts.Any(a => a.Severity == Station.Models.AlertSeverity.Critical))
				{
					RedFlashStoryboard.Begin();
				}
			}

			_alertDialogOpen = false;
		}

        #endregion
  
		private void AlertBadge_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
                Microsoft.UI.Input.InputSystemCursorShape.Hand);
        }

        private void AlertBadge_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            this.ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(
                Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        }

      

        public class SystemLogItem
        {
            public string Icon { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public string Level { get; set; } = string.Empty;
            public string Time { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }

            public SolidColorBrush LevelBrush
            {
                get => Level switch
                {
                    "SUCCESS" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 34, 197, 94)), // #22C55E Green
                    "INFO" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 59, 130, 246)), // #3B82F6 Blue
                    "WARNING" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 245, 158, 11)), // #F59E0B Orange
                    "ERROR" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 239, 68, 68)), // #EF4444 Red
                    "ALERT" => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 234, 179, 8)), // #EAB308 Yellow
                    _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 148, 163, 184)) // #94A3B8 Gray
                };
            }
        }
    }


}