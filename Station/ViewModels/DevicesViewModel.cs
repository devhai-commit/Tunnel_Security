using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Station.Models;
using Station.Services;
using System.Threading.Tasks;
using Windows.UI;

namespace Station.ViewModels
{
    public enum DeviceSidebarMode
    {
        Summary,
        Details,
        Edit
    }

    public partial class DevicesViewModel : ObservableObject
    {
        // Active data service (mock or RealDataService backed by Backend API / SQL Server)
        private readonly IDataService _mock = DataServiceLocator.Current;
        private readonly DispatcherQueue? _dispatcher = DispatcherQueue.GetForCurrentThread();

        // Stores user-added nodes so they survive filter changes
        private readonly Dictionary<string, NodeItemViewModel> _userAddedNodes = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSidebarSummaryVisible))]
        [NotifyPropertyChangedFor(nameof(IsSidebarDetailVisible))]
        [NotifyPropertyChangedFor(nameof(IsSidebarEditVisible))]
        private DeviceSidebarMode _sidebarMode = DeviceSidebarMode.Summary;

        public bool IsSidebarSummaryVisible => SidebarMode == DeviceSidebarMode.Summary;
        public bool IsSidebarDetailVisible => SidebarMode == DeviceSidebarMode.Details;
        public bool IsSidebarEditVisible => SidebarMode == DeviceSidebarMode.Edit;

        // Filter properties
        private string? _selectedStatus = "Tất cả trạng thái";
        public string? SelectedStatus
        {
            get => _selectedStatus;
            set
            {
                SetProperty(ref _selectedStatus, value);
                ApplyFilters();
            }
        }

        [ObservableProperty]
        private SensorItemViewModel? _selectedSensor;

        partial void OnSelectedSensorChanged(SensorItemViewModel? value)
        {
            if (value != null)
            {
                SidebarMode = DeviceSidebarMode.Summary;
            }
        }

        [ObservableProperty]
        private NodeItemViewModel? _selectedNode;

        partial void OnSelectedNodeChanged(NodeItemViewModel? value)
        {
            if (value != null)
            {
                SidebarMode = DeviceSidebarMode.Summary;
            }
        }

        private string? _selectedLine = "Tất cả tuyến";
        public string? SelectedLine
        {
            get => _selectedLine;
            set
            {
                SetProperty(ref _selectedLine, value);
                ApplyFilters();
            }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                ApplyFilters();
            }
        }

        // Device collections
        public ObservableCollection<DeviceItemViewModel> AllDevices { get; } = new();
        public ObservableCollection<DeviceItemViewModel> FilteredDevices { get; } = new();
        public ObservableCollection<NodeItemViewModel> FilteredNodes { get; } = new();
        public ObservableCollection<string> StatusFilters { get; } = new();
        public ObservableCollection<string> LineFilters { get; } = new();

        // Statistics
        private int _totalDevices;
        public int TotalDevices
        {
            get => _totalDevices;
            set => SetProperty(ref _totalDevices, value);
        }

        private int _onlineDevices;
        public int OnlineDevices
        {
            get => _onlineDevices;
            set => SetProperty(ref _onlineDevices, value);
        }

        private int _offlineDevices;
        public int OfflineDevices
        {
            get => _offlineDevices;
            set => SetProperty(ref _offlineDevices, value);
        }

        private int _faultDevices;
        public int FaultDevices
        {
            get => _faultDevices;
            set => SetProperty(ref _faultDevices, value);
        }

        public DevicesViewModel()
        {
            ReloadFromDataService();

            // Realtime sensor updates (every 1 second)
            _mock.SensorTick += OnSensorTick;

            // RealDataService loads topology asynchronously from Backend API → SQL Server.
            // Re-pull collections once that finishes (and on later refreshes), otherwise
            // the page stays empty when the VM was constructed before the API responded.
            _mock.TopologyLoaded += OnTopologyLoaded;
        }

        private void OnTopologyLoaded(object? sender, EventArgs e)
        {
            if (_dispatcher != null && !_dispatcher.HasThreadAccess)
                _dispatcher.TryEnqueue(ReloadFromDataService);
            else
                ReloadFromDataService();
        }

        private void ReloadFromDataService()
        {
            AllDevices.Clear();
            LineFilters.Clear();
            StatusFilters.Clear();

            LoadFromDataService();
            ApplyFilters();
            UpdateStatistics();
        }

        private void OnSensorTick(object? sender, SensorTickEventArgs e)
        {
            // Update device value in real-time
            try
            {
                // Find sensor in MockDataService to get updated value
                var sensor = _mock.Sensors.FirstOrDefault(s => s.SensorId == e.Sensor.SensorId);
                if (sensor == null) return;

                // Find matching device in our list
                var device = AllDevices.FirstOrDefault(d => d.DeviceId == e.Sensor.SensorId);
                if (device != null)
                {
                    // Update status based on sensor level
                    device.Status = sensor.CurrentLevel switch
                    {
                        SensorAlertLevel.Critical => DeviceStatus.Fault,
                        SensorAlertLevel.Warning => DeviceStatus.Online,
                        SensorAlertLevel.Offline => DeviceStatus.Offline,
                        _ => DeviceStatus.Online
                    };

                    device.LastOnline = DateTimeOffset.Now;
                }

                // Update the filtered nodes with new sensor values
                RefreshNodeSensorValues(e.Sensor.SensorId, sensor.CurrentValue, sensor.Category, sensor.CurrentLevel);

                // Update statistics
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DevicesVM] Error updating sensor: {ex.Message}");
            }
        }

        private void RefreshNodeSensorValues(string sensorId, double newValue, AlertCategory category, SensorAlertLevel level)
        {
            // Update sensor value in NodeItemViewModel
            foreach (var node in FilteredNodes)
            {
                foreach (var sensor in node.Sensors)
                {
                    if (sensor.SensorId == sensorId)
                    {
                        // Format value based on category
                        sensor.CurrentValue = category switch
                        {
                            AlertCategory.Temperature => $"{newValue:F1}°C",
                            AlertCategory.Humidity => $"{newValue:F1}%RH",
                            AlertCategory.Radar => $"{newValue:F0}%",
                            AlertCategory.Infrared => $"{newValue:F0}%",
                            AlertCategory.Light => $"{newValue:F0} lux",
                            AlertCategory.Accelerometer => $"{newValue:F2} m/s²",
                            _ => $"{newValue:F2}"
                        };

                        // Update status
                        sensor.SensorStatus = level switch
                        {
                            SensorAlertLevel.Critical => DeviceStatus.Fault,
                            SensorAlertLevel.Warning => DeviceStatus.Online,
                            SensorAlertLevel.Offline => DeviceStatus.Offline,
                            _ => DeviceStatus.Online
                        };
                        break;
                    }
                }
            }
        }

        private void LoadFromDataService()
        {
            StatusFilters.Add("Tất cả trạng thái");
            StatusFilters.Add("Hoạt động");
            StatusFilters.Add("Ngoại tuyến");
            StatusFilters.Add("Lỗi");
            StatusFilters.Add("Tắt");

            LineFilters.Add("Tất cả tuyến");
            var mock = Station.Services.DataServiceLocator.Current;
            foreach (var line in mock.Lines)
                LineFilters.Add(line.LineName);

            // Add cameras
            foreach (var cam in mock.Cameras)
            {
                AllDevices.Add(new DeviceItemViewModel
                {
                    Name            = cam.CameraName,
                    DeviceId        = cam.CameraId,
                    Type            = "Camera",
                    TypeDisplay     = "Camera giám sát",
                    Location        = $"{cam.LineName} / {cam.NodeName}",
                    IpAddress       = string.Empty,
                    Status          = cam.IsOnline ? DeviceStatus.Online : DeviceStatus.Offline,
                    LastOnline      = DateTimeOffset.Now.AddMinutes(-1),
                    Manufacturer    = "Hikvision",
                    FirmwareVersion = "V5.7.3",
                    AlertCount      = 0
                });
            }

            // Add sensors
            foreach (var s in mock.Sensors)
            {
                AllDevices.Add(new DeviceItemViewModel
                {
                    Name            = s.SensorName,
                    DeviceId        = s.SensorId,
                    Type            = "Sensor",
                    TypeDisplay     = CategoryToDisplay(s.Category),
                    Location        = $"{s.LineName} / {s.NodeName}",
                    IpAddress       = string.Empty,
                    Status          = s.IsOnline ? DeviceStatus.Online : DeviceStatus.Offline,
                    LastOnline      = DateTimeOffset.Now.AddSeconds(-5),
                    Manufacturer    = "Bosch",
                    FirmwareVersion = "V3.1.0",
                    AlertCount      = 0
                });
            }

            TotalDevices = AllDevices.Count;
        }

        private static string CategoryToDisplay(Station.Models.AlertCategory cat) => cat switch
        {
            Station.Models.AlertCategory.Radar         => "Radar phát hiện người",
            Station.Models.AlertCategory.Infrared      => "Cảm biến hồng ngoại",
            Station.Models.AlertCategory.Temperature   => "Cảm biến nhiệt độ",
            Station.Models.AlertCategory.Humidity      => "Cảm biến độ ẩm",
            Station.Models.AlertCategory.Light         => "Cảm biến ánh sáng",
            Station.Models.AlertCategory.Accelerometer => "Cảm biến gia tốc",
            _                                          => "Cảm biến"
        };

        private static string FormatSensorValue(Station.Services.SimulatedSensor s) =>
            s.Category switch
            {
                Station.Models.AlertCategory.Radar         => $"{s.CurrentValue:F0}%",
                Station.Models.AlertCategory.Infrared      => $"{s.CurrentValue:F0}%",
                Station.Models.AlertCategory.Temperature   => $"{s.CurrentValue:F1}°C",
                Station.Models.AlertCategory.Humidity      => $"{s.CurrentValue:F1}%RH",
                Station.Models.AlertCategory.Light         => $"{s.CurrentValue:F0} lux",
                Station.Models.AlertCategory.Accelerometer => $"{s.CurrentValue:F2} m/s²",
                _ => $"{s.CurrentValue:F2}"
            };

        private static string CategoryIcon(Station.Models.AlertCategory cat) => cat switch
        {
            Station.Models.AlertCategory.Radar         => "\uE701",
            Station.Models.AlertCategory.Infrared      => "\uE7C1",
            Station.Models.AlertCategory.Temperature   => "\uE9CA",
            Station.Models.AlertCategory.Humidity      => "\uE81E",
            Station.Models.AlertCategory.Light         => "\uE706",
            Station.Models.AlertCategory.Accelerometer => "\uEDA4",
            _                                          => "\uE957"
        };

        private void ApplyFilters()
        {
            FilteredDevices.Clear();
            FilteredNodes.Clear();

            var filtered = AllDevices.AsEnumerable();

            // Filter by status
            if (!string.IsNullOrEmpty(SelectedStatus) && SelectedStatus != "Tất cả trạng thái")
            {
                filtered = filtered.Where(d =>
                {
                    return SelectedStatus switch
                    {
                        "Hoạt động" => d.Status == DeviceStatus.Online,
                        "Ngoại tuyến" => d.Status == DeviceStatus.Offline,
                        "Lỗi" => d.Status == DeviceStatus.Fault,
                        "Tắt" => d.Status == DeviceStatus.Disabled,
                        _ => true
                    };
                });
            }

            // Filter by line
            if (!string.IsNullOrEmpty(SelectedLine) && SelectedLine != "Tất cả tuyến")
            {
                filtered = filtered.Where(d => d.Location.StartsWith(SelectedLine));
            }

            // Filter by search text
            if (!string.IsNullOrEmpty(SearchText))
            {
                var searchLower = SearchText.ToLower();
                filtered = filtered.Where(d =>
              d.Name.ToLower().Contains(searchLower) ||
               d.DeviceId.ToLower().Contains(searchLower) ||
                       d.Location.ToLower().Contains(searchLower) ||
                  d.IpAddress.ToLower().Contains(searchLower));
            }

            foreach (var device in filtered)
            {
                FilteredDevices.Add(device);
            }

            // Group by nodes (location-based grouping)
            var mock = Station.Services.DataServiceLocator.Current;

            var nodeGroups = filtered.GroupBy(d =>
            {
                var parts = d.Location.Split('/');
                return parts.Length >= 2
                    ? $"{parts[0].Trim()} / {parts[1].Trim()}"
                    : d.Location;
            });

            foreach (var group in nodeGroups)
            {
                var items    = group.ToList();
                var first    = items.First();
                var locParts = first.Location.Split('/');
                string lineName = locParts.Length >= 1 ? locParts[0].Trim() : "?";
                string nodeName = locParts.Length >= 2 ? locParts[1].Trim() : "?";

                var line = mock.Lines.FirstOrDefault(l => l.LineName == lineName);
                var node = line?.Nodes.FirstOrDefault(n => n.NodeName == nodeName);

                var nodeVm = new NodeItemViewModel
                {
                    NodeName = nodeName,
                    LineName = lineName,
                    Location = $"{lineName} / {nodeName}",
                    Status   = items.Any(d => d.Status == DeviceStatus.Fault)   ? DeviceStatus.Fault   :
                               items.Any(d => d.Status == DeviceStatus.Offline) ? DeviceStatus.Offline :
                               DeviceStatus.Online
                };

                if (node != null)
                {
                    var nodeSensors = mock.Sensors.Where(s => s.NodeId == node.NodeId).ToList();
                    var nodeCam     = mock.Cameras.FirstOrDefault(c => c.NodeId == node.NodeId);

                    if (nodeCam != null)
                        nodeVm.Sensors.Add(new SensorItemViewModel
                        {
                            SensorId       = nodeCam.CameraId,
                            SensorName     = nodeCam.CameraName,
                            SensorType     = "Camera",
                            CurrentValue   = nodeCam.IsOnline ? "Online" : "Offline",
                            Unit           = string.Empty,
                            LastUpdateText = "Vừa xong",
                            SensorStatus   = nodeCam.IsOnline ? DeviceStatus.Online : DeviceStatus.Offline,
                            TypeIcon       = "\uE714",
                            LineName       = lineName,
                            NodeName       = nodeName,
                            Location       = $"{lineName} / {nodeName}"
                        });

                    foreach (var s in nodeSensors)
                    {
                        nodeVm.Sensors.Add(new SensorItemViewModel
                        {
                            SensorId       = s.SensorId,
                            SensorName     = s.SensorName,
                            SensorType     = s.Category.ToString(),
                            CurrentValue   = FormatSensorValue(s),
                            Unit           = s.Unit,
                            LastUpdateText = "Vừa xong",
                            SensorStatus   = s.IsOnline ? DeviceStatus.Online : DeviceStatus.Offline,
                            TypeIcon       = CategoryIcon(s.Category),
                            LineName       = lineName,
                            NodeName       = nodeName,
                            Location       = $"{lineName} / {nodeName}"
                        });
                    }
                }

                FilteredNodes.Add(nodeVm);
            }

            // Re-inject user-added nodes that pass current filters
            foreach (KeyValuePair<string, NodeItemViewModel> entry in _userAddedNodes)
            {
                if (!FilteredNodes.Any(n => n.Location == entry.Key) && NodePassesCurrentFilters(entry.Value))
                    FilteredNodes.Add(entry.Value);
            }

            UpdateStatistics();
        }

        private void UpdateStatistics()
        {
            int uOnline  = _userAddedNodes.Values.Sum(n => n.Sensors.Count(s => s.SensorStatus == DeviceStatus.Online));
            int uOffline = _userAddedNodes.Values.Sum(n => n.Sensors.Count(s => s.SensorStatus == DeviceStatus.Offline));
            int uFault   = _userAddedNodes.Values.Sum(n => n.Sensors.Count(s => s.SensorStatus == DeviceStatus.Fault));
            int uTotal   = _userAddedNodes.Values.Sum(n => n.Sensors.Count);

            TotalDevices   = AllDevices.Count + uTotal;
            OnlineDevices  = AllDevices.Count(d => d.Status == DeviceStatus.Online)  + uOnline;
            OfflineDevices = AllDevices.Count(d => d.Status == DeviceStatus.Offline) + uOffline;
            FaultDevices   = AllDevices.Count(d => d.Status == DeviceStatus.Fault)   + uFault;
        }

        // ─── Public CRUD helpers called by dialogs ────────────────────────

        public void RegisterNewNode(NodeItemViewModel node)
        {
            _userAddedNodes[node.Location] = node;
            if (NodePassesCurrentFilters(node))
                FilteredNodes.Add(node);
            UpdateStatistics();
        }

        public void DeleteNode(NodeItemViewModel node)
        {
            _userAddedNodes.Remove(node.Location);
            FilteredNodes.Remove(node);
            UpdateStatistics();
        }

        public void UpdateNode(NodeItemViewModel node, string newName, string newLineName, string newLocation)
        {
            var oldLocation = node.Location;
            if (_userAddedNodes.Remove(oldLocation))
                _userAddedNodes[newLocation] = node;

            node.NodeName = newName;
            node.LineName = newLineName;
            node.Location = newLocation;
            foreach (var s in node.Sensors)
            {
                s.NodeName = newName;
                s.LineName = newLineName;
                s.Location = newLocation;
            }
        }

        private bool NodePassesCurrentFilters(NodeItemViewModel node)
        {
            if (!string.IsNullOrEmpty(SelectedLine) && SelectedLine != "Tất cả tuyến")
                if (node.LineName != SelectedLine) return false;

            if (!string.IsNullOrEmpty(SelectedStatus) && SelectedStatus != "Tất cả trạng thái")
            {
                bool passes = SelectedStatus switch
                {
                    "Hoạt động"   => node.Status == DeviceStatus.Online,
                    "Ngoại tuyến" => node.Status == DeviceStatus.Offline,
                    "Lỗi"         => node.Status == DeviceStatus.Fault,
                    "Tắt"         => node.Status == DeviceStatus.Disabled,
                    _             => true
                };
                if (!passes) return false;
            }

            if (!string.IsNullOrEmpty(SearchText))
            {
                var sl = SearchText.ToLower();
                if (!node.NodeName.ToLower().Contains(sl) &&
                    !node.Location.ToLower().Contains(sl) &&
                    !node.LineName.ToLower().Contains(sl))
                    return false;
            }
            return true;
        }

        [RelayCommand]
        private void ClearSearch()
        {
            SearchText = string.Empty;
        }

        [RelayCommand]
        private void ShowDetails()
        {
            SidebarMode = DeviceSidebarMode.Details;
        }

        [RelayCommand]
        private void ShowEdit()
        {
            SidebarMode = DeviceSidebarMode.Edit;
        }

        [RelayCommand]
        private void ShowSummary()
        {
            SidebarMode = DeviceSidebarMode.Summary;
        }

        [RelayCommand]
        private void CloseSidebar()
        {
            SelectedNode = null;
            SelectedSensor = null;
        }

        [RelayCommand]
        private void NavigateBack()
        {
            SelectedSensor = null;
        }

        [RelayCommand]
        private void AddDevice()
        {
            // Placeholder for Add Device logic
            System.Diagnostics.Debug.WriteLine("Add Device command executed");
        }
    }

    public partial class DeviceItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _deviceId = string.Empty;

        [ObservableProperty]
        private string _type = string.Empty;

        [ObservableProperty]
        private string _typeDisplay = string.Empty;

        [ObservableProperty]
        private string _location = string.Empty;

        [ObservableProperty]
        private string _ipAddress = string.Empty;

        [ObservableProperty]
        private DeviceStatus _status;

        [ObservableProperty]
        private DateTimeOffset _lastOnline;

        [ObservableProperty]
        private string _manufacturer = string.Empty;

        [ObservableProperty]
        private string _firmwareVersion = string.Empty;

        [ObservableProperty]
        private int _alertCount;

        public string LastOnlineText
        {
            get
            {
                var diff = DateTimeOffset.Now - LastOnline;
                if (diff.TotalMinutes < 1) return "Vừa xong";
                if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} phút trước";
                if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} giờ trước";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays} ngày trước";
                return LastOnline.ToString("dd/MM/yyyy HH:mm");
            }
        }

        // Status badge properties
        public string StatusText
        {
            get => Status switch
            {
                DeviceStatus.Online => "Hoạt động",
                DeviceStatus.Offline => "Ngoại tuyến",
                DeviceStatus.Fault => "Lỗi",
                DeviceStatus.Disabled => "Tắt",
                _ => "Không xác định"
            };
        }

        public SolidColorBrush StatusColor
        {
            get => Status switch
            {
                DeviceStatus.Online => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)), // #22C55E - Green
                DeviceStatus.Offline => new SolidColorBrush(Color.FromArgb(255, 148, 163, 184)), // #94A3B8 - Gray
                DeviceStatus.Fault => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)), // #EF4444 - Red
                DeviceStatus.Disabled => new SolidColorBrush(Color.FromArgb(255, 100, 116, 139)), // #64748B - Slate
                _ => new SolidColorBrush(Color.FromArgb(255, 148, 163, 184))
            };
        }

        public SolidColorBrush StatusBackgroundColor
        {
            get => Status switch
            {
                DeviceStatus.Online => new SolidColorBrush(Color.FromArgb(255, 220, 252, 231)), // #DCFCE7 - Light Green
                DeviceStatus.Offline => new SolidColorBrush(Color.FromArgb(255, 241, 245, 249)), // #F1F5F9 - Light Gray
                DeviceStatus.Fault => new SolidColorBrush(Color.FromArgb(255, 254, 226, 226)), // #FEE2E2 - Light Red
                DeviceStatus.Disabled => new SolidColorBrush(Color.FromArgb(255, 226, 232, 240)), // #E2E8F0 - Light Slate
                _ => new SolidColorBrush(Color.FromArgb(255, 241, 245, 249))
            };
        }

        public string StatusIcon
        {
            get => Status switch
            {
                DeviceStatus.Online => "\uE73E", // Checkmark
                DeviceStatus.Offline => "\uE894", // Disconnect
                DeviceStatus.Fault => "\uE783", // Error
                DeviceStatus.Disabled => "\uE8D8", // Blocked
                _ => "\uE946" // Info
            };
        }

        // Type icon
        public string TypeIcon
        {
            get => Type switch
            {
                "Camera" => "\uE714", // Video camera
                "Sensor" => "\uE957", // Sensor
                "Radar" => "\uE701", // Radar
                _ => "\uE8EA" // Device
            };
        }

        // Alert count display
        public bool HasAlerts => AlertCount > 0;
        public string AlertCountText => AlertCount > 99 ? "99+" : AlertCount.ToString();

        // Device Menu Commands
        [RelayCommand]
        private async Task EditDevice()
        {
            try
            {
                // Create and show the Edit Device Dialog
                var dialog = new Station.Dialogs.EditDeviceDialog(this);

                // Set XamlRoot from App's main window
                if (Microsoft.UI.Xaml.Application.Current is App app && app.m_window is MainWindow mainWindow)
                {
                    dialog.XamlRoot = mainWindow.Content.XamlRoot;
                }

                var result = await dialog.ShowAsync();

                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    System.Diagnostics.Debug.WriteLine($"Device successfully updated: {Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening edit dialog: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ControlDevice()
        {
            try
            {
                // Create and show the Device Control Dialog
                var dialog = new Station.Dialogs.DeviceControlDialog(this);

                // Set XamlRoot from App's main window
                if (Microsoft.UI.Xaml.Application.Current is App app && app.m_window is MainWindow mainWindow)
                {
                    dialog.XamlRoot = mainWindow.Content.XamlRoot;
                }

                var result = await dialog.ShowAsync();

                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    System.Diagnostics.Debug.WriteLine($"Device control completed: {Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening control dialog: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ViewData()
        {
            try
            {
                // Create and show the Edit Device Dialog
                var dialog = new Station.Dialogs.DeviceDataDialog(this);

                // Set XamlRoot from App's main window
                if (Microsoft.UI.Xaml.Application.Current is App app && app.m_window is MainWindow mainWindow)
                {
                    dialog.XamlRoot = mainWindow.Content.XamlRoot;
                }

                var result = await dialog.ShowAsync();

                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    System.Diagnostics.Debug.WriteLine($"Device successfully updated: {Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening edit dialog: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task PlaybackDevice()
        {
            try
            {
                // Create and show the Playback Dialog
                var dialog = new Station.Dialogs.PlaybackDialog(this);

                // Set XamlRoot from App's main window
                if (Microsoft.UI.Xaml.Application.Current is App app && app.m_window is MainWindow mainWindow)
                {
                    dialog.XamlRoot = mainWindow.Content.XamlRoot;
                }

                var result = await dialog.ShowAsync();

                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    System.Diagnostics.Debug.WriteLine($"Playback completed: {Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening playback dialog: {ex.Message}");
            }
        }
    }

    public partial class NodeItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _nodeName = string.Empty;

        [ObservableProperty]
        private string _lineName = string.Empty;

        [ObservableProperty]
        private string _location = string.Empty;

        [ObservableProperty]
        private DeviceStatus _status;

        [ObservableProperty]
        private bool _isExpanded = false;

        public ObservableCollection<SensorItemViewModel> Sensors { get; } = new();

        public string StatusText
        {
            get => Status switch
            {
                DeviceStatus.Online => "Hoạt động",
                DeviceStatus.Offline => "Ngoại tuyến",
                DeviceStatus.Fault => "Lỗi",
                DeviceStatus.Disabled => "Tắt",
                _ => "Không xác định"
            };
        }

        public SolidColorBrush StatusColor
        {
            get => Status switch
            {
                DeviceStatus.Online => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)),
                DeviceStatus.Offline => new SolidColorBrush(Color.FromArgb(255, 148, 163, 184)),
                DeviceStatus.Fault => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),
                DeviceStatus.Disabled => new SolidColorBrush(Color.FromArgb(255, 100, 116, 139)),
                _ => new SolidColorBrush(Color.FromArgb(255, 148, 163, 184))
            };
        }

        public SolidColorBrush StatusBackgroundColor
        {
            get => Status switch
            {
                DeviceStatus.Online => new SolidColorBrush(Color.FromArgb(40, 34, 197, 94)),
                DeviceStatus.Offline => new SolidColorBrush(Color.FromArgb(40, 148, 163, 184)),
                DeviceStatus.Fault => new SolidColorBrush(Color.FromArgb(40, 239, 68, 68)),
                DeviceStatus.Disabled => new SolidColorBrush(Color.FromArgb(40, 100, 116, 139)),
                _ => new SolidColorBrush(Color.FromArgb(40, 148, 163, 184))
            };
        }

        public string SensorCountText => $"{Sensors.Count} cảm biến";
    }

    public partial class SensorItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _sensorId = string.Empty;

        [ObservableProperty]
        private string _sensorName = string.Empty;

        [ObservableProperty]
        private string _sensorType = string.Empty;

        public string SensorTypeDisplay => SensorType switch
        {
            "Radar"         => "RADAR",
            "Infrared"      => "HỒNG NGOẠI",
            "Temperature"   => "NHIỆT ĐỘ",
            "Humidity"      => "ĐỘ ẨM",
            "Light"         => "ÁNH SÁNG",
            "Accelerometer" => "GIA TỐC",
            "Camera"        => "CAMERA",
            _               => SensorType.ToUpper()
        };

        [ObservableProperty]
        private string _currentValue = string.Empty;

        [ObservableProperty]
        private string _unit = string.Empty;

        [ObservableProperty]
        private string _lastUpdateText = string.Empty;

        [ObservableProperty]
        private DeviceStatus _sensorStatus;

        [ObservableProperty]
        private string _typeIcon = string.Empty;

        [ObservableProperty]
        private string _lineName = string.Empty;

        [ObservableProperty]
        private string _nodeName = string.Empty;

        [ObservableProperty]
        private string _location = string.Empty;

        public SolidColorBrush SensorStatusColor
        {
            get => SensorStatus switch
            {
                DeviceStatus.Online => new SolidColorBrush(Color.FromArgb(255, 34, 197, 94)),
                DeviceStatus.Offline => new SolidColorBrush(Color.FromArgb(255, 148, 163, 184)),
                DeviceStatus.Fault => new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),
                DeviceStatus.Disabled => new SolidColorBrush(Color.FromArgb(255, 100, 116, 139)),
                _ => new SolidColorBrush(Color.FromArgb(255, 148, 163, 184))
            };
        }

        [RelayCommand]
        private async Task OpenSensorDetail()
        {
            try
            {
                // Create and show the Sensor Detail Dialog
                var dialog = new Station.Dialogs.SensorDetailDialog(this);

                // Set XamlRoot from App's main window
                if (Microsoft.UI.Xaml.Application.Current is App app && app.m_window is MainWindow mainWindow)
                {
                    dialog.XamlRoot = mainWindow.Content.XamlRoot;
                }

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error opening sensor detail dialog: {ex.Message}");
            }
        }
    }
}
