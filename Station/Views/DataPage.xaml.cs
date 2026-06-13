using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinUI;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Windows.UI;
using Station.Controls;
using Station.Services;

namespace Station.Views
{
    public sealed partial class DataPage : Page
    {
        private List<LineData> _lines = new();
        private string _selectedLineId = "all";
        private string _selectedStatus = "all";
        private string _searchText = "";
        private HashSet<string> _selectedNodeIds = new();
        private HashSet<string> _selectedTypes = new() { "infrared", "temperature", "humidity", "light", "vibration" };
        private int _columnsPerRow = 2;
        private readonly Random _random = new();

        private Dictionary<string, List<double>> _sensorHistoricalData = new();
        private Dictionary<string, CartesianChart> _chartInstances = new();
        private Dictionary<string, TextBlock> _sensorValueTexts = new();
        private Dictionary<string, ProgressBar> _sensorProgressBars = new();

        private readonly IDataService _dataService = DataServiceLocator.Current;
        private DispatcherQueueTimer? _chartRefreshTimer;

        public DataPage()
        {
            this.InitializeComponent();
            InitializeMockData();
            this.Loaded += DataPage_Loaded;
            this.Unloaded += DataPage_Unloaded;
        }

        // ──────────────── Lifecycle ────────────────

        private void DataPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _dataService.TopologyLoaded += OnTopologyLoaded;
                _dataService.SensorTick += OnMockSensorTick;
                RebuildCharts();
            }
            catch (Exception ex) { Debug.WriteLine($"[DataPage_Loaded ERROR] {ex}"); }
        }

        private void DataPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _dataService.TopologyLoaded -= OnTopologyLoaded;
            _dataService.SensorTick -= OnMockSensorTick;
            _chartRefreshTimer?.Stop();
            _chartRefreshTimer = null;
        }

        private void OnTopologyLoaded(object? sender, EventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try { IncrementalRebuildCharts(); }
                catch (Exception ex) { Debug.WriteLine($"[OnTopologyLoaded ERROR] {ex}"); }
            });
        }

        private void OnMockSensorTick(object? sender, SensorTickEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    UpdateChartWithMockData(e.Sensor.SensorId, e.NewValue);
                    UpdateCharts();
                }
                catch (Exception ex) { Debug.WriteLine($"[OnMockSensorTick ERROR] {ex}"); }
            });
        }

        // ──────────────── Chart rebuild ────────────────

        private void RebuildCharts()
        {
            try
            {
                InitializeMockData();
                _sensorHistoricalData.Clear();
                InitializeHistoricalData();
                BuildNodeFilterComboBox();
                LoadChartsForAllNodes();
            }
            catch (Exception ex) { Debug.WriteLine($"[RebuildCharts ERROR] {ex}"); }
        }

        private void IncrementalRebuildCharts()
        {
            try
            {
                InitializeMockData();
                InitializeHistoricalData();
                BuildNodeFilterComboBox();
                LoadChartsForAllNodes();
            }
            catch (Exception ex) { Debug.WriteLine($"[IncrementalRebuildCharts ERROR] {ex}"); }
        }

        private void InitializeHistoricalData()
        {
            foreach (var sensor in _lines.SelectMany(l => l.Nodes).SelectMany(n => n.Sensors))
            {
                if (!sensor.Value.HasValue || _sensorHistoricalData.ContainsKey(sensor.Id)) continue;

                var init = sensor.Value.Value;
                var history = new List<double>();

                if (sensor.Type == "vibration")
                {
                    for (int i = 0; i < 50; i++)
                        history.Add(init + (_random.NextDouble() - 0.5) * init * 2);
                }
                else
                {
                    for (int i = 0; i < 24; i++) history.Add(init);
                }

                _sensorHistoricalData[sensor.Id] = history;
            }
        }

        private void UpdateChartWithMockData(string sensorId, double newValue)
        {
            var sensor = _lines.SelectMany(l => l.Nodes)
                               .SelectMany(n => n.Sensors)
                               .FirstOrDefault(s => string.Equals(s.Id, sensorId, StringComparison.OrdinalIgnoreCase));

            if (sensor != null)
            {
                sensor.Value = newValue;
                var ms = _dataService.Sensors.FirstOrDefault(s => s.SensorId == sensorId);
                if (ms != null)
                    sensor.Status = ms.CurrentLevel switch
                    {
                        SensorAlertLevel.Critical => "critical",
                        SensorAlertLevel.Warning => "warning",
                        _ => "normal"
                    };
            }

            if (!_sensorHistoricalData.ContainsKey(sensorId))
            {
                _sensorHistoricalData[sensorId] = new List<double>();
                for (int i = 0; i < 24; i++) _sensorHistoricalData[sensorId].Add(newValue);
            }

            var history = _sensorHistoricalData[sensorId];
            history.Add(newValue);
            if (history.Count > 50) history.RemoveAt(0);
        }

        private void UpdateCharts()
        {
            foreach (var (sensorId, chart) in _chartInstances)
            {
                if (!_sensorHistoricalData.ContainsKey(sensorId)) continue;

                var sensor = _lines.SelectMany(l => l.Nodes)
                                   .SelectMany(n => n.Sensors)
                                   .FirstOrDefault(s => s.Id == sensorId);

                if (sensor == null || chart.Series == null) continue;

                // Large value text
                if (_sensorValueTexts.TryGetValue(sensorId, out var tb))
                    tb.Text = $"{(sensor.Value ?? 0):F1}";

                // Progress bar + level color
                if (_sensorProgressBars.TryGetValue(sensorId, out var pb))
                {
                    pb.Value = CalculateProgressPercent(sensor);
                    var ms = _dataService.Sensors.FirstOrDefault(s => s.SensorId == sensorId);
                    if (ms != null)
                        pb.Foreground = new SolidColorBrush(ms.CurrentLevel switch
                        {
                            SensorAlertLevel.Critical => Color.FromArgb(255, 255, 64, 64),
                            SensorAlertLevel.Warning => Color.FromArgb(255, 255, 176, 32),
                            _ => Color.FromArgb(255, 0, 200, 138)
                        });
                }

                // Chart series
                var series = chart.Series.ToArray();
                if (series.Length == 0) continue;
                if (sensor.Type == "vibration" && series[0] is ColumnSeries<double> col)
                    col.Values = _sensorHistoricalData[sensorId].ToArray();
                else if (series[0] is LineSeries<double> line)
                    line.Values = _sensorHistoricalData[sensorId].ToArray();
            }

            UpdateHeaderStats();
        }

        // ──────────────── Data init ────────────────

        private void InitializeMockData()
        {
            _lines = new List<LineData>();

            foreach (var lineGroup in _dataService.Sensors.GroupBy(s => s.LineId).OrderBy(g => g.Key))
            {
                var first = lineGroup.First();
                var line = new LineData { Id = lineGroup.Key, Name = first.LineName, Status = "active" };

                foreach (var nodeGroup in lineGroup.GroupBy(s => s.NodeId).OrderBy(g => g.Key))
                {
                    var nFirst = nodeGroup.First();
                    var node = new NodeData { Id = nodeGroup.Key, Name = nFirst.NodeName, Status = "normal" };

                    foreach (var ms in nodeGroup)
                        node.Sensors.Add(new SensorData
                        {
                            Id = ms.SensorId,
                            Name = ms.SensorName,
                            Type = MapCategoryToType(ms.Category),
                            Status = "normal",
                            Value = ms.CurrentValue
                        });

                    line.Nodes.Add(node);
                }
                _lines.Add(line);
            }
        }

        private string MapCategoryToType(Station.Models.AlertCategory cat) => cat switch
        {
            Station.Models.AlertCategory.Radar => "radar",
            Station.Models.AlertCategory.Infrared => "infrared",
            Station.Models.AlertCategory.Temperature => "temperature",
            Station.Models.AlertCategory.Humidity => "humidity",
            Station.Models.AlertCategory.Light => "light",
            Station.Models.AlertCategory.Accelerometer => "vibration",
            Station.Models.AlertCategory.Intrusion => "camera",
            _ => "other"
        };

        // ──────────────── Filters ────────────────

        private void BuildNodeFilterComboBox()
        {
            if (NodeFilterComboBox == null) return;
            NodeFilterComboBox.Items.Clear();
            NodeFilterComboBox.Items.Add(new ComboBoxItem { Content = "Tất cả nodes", Tag = "all" });

            var lines = _selectedLineId == "all" ? _lines : _lines.Where(l => l.Id == _selectedLineId).ToList();
            foreach (var line in lines)
                foreach (var node in line.Nodes)
                    NodeFilterComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = _selectedLineId == "all" ? $"{line.Name} — {node.Name}" : node.Name,
                        Tag = node.Id
                    });

            NodeFilterComboBox.SelectedIndex = 0;
            _selectedNodeIds.Clear();
        }

        private List<SensorData> GetFilteredSensors()
        {
            var nodes = _lines.SelectMany(l => l.Nodes).ToList();

            if (_selectedLineId != "all")
            {
                var line = _lines.FirstOrDefault(l => l.Id == _selectedLineId);
                nodes = line?.Nodes ?? new List<NodeData>();
            }

            if (_selectedNodeIds.Count > 0)
                nodes = nodes.Where(n => _selectedNodeIds.Contains(n.Id)).ToList();

            var sensors = nodes.SelectMany(n => n.Sensors).ToList();
            sensors = sensors.Where(s => _selectedTypes.Contains(s.Type)).ToList();

            if (_selectedStatus != "all")
                sensors = sensors.Where(s => s.Status == _selectedStatus).ToList();

            if (!string.IsNullOrEmpty(_searchText))
                sensors = sensors.Where(s =>
                    s.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                    s.Id.Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();

            return sensors;
        }

        // ──────────────── Load charts ────────────────

        private void LoadChartsForAllNodes()
        {
            if (ChartsPanel == null || CameraPanel == null) return;

            ChartsPanel.Children.Clear();
            CameraPanel.Children.Clear();
            _chartInstances.Clear();
            _sensorValueTexts.Clear();
            _sensorProgressBars.Clear();

            UpdateHeaderStats();

            var filtered = GetFilteredSensors();
            var chartSensors = filtered.Where(s => s.Type != "camera" && s.Type != "radar").ToList();
            var cameraSensors = filtered.Where(s => s.Type == "camera").ToList();

            // Charts
            if (chartSensors.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                if (_lines.Count == 0)
                {
                    ChartCountText.Text = "...";
                    if (ChartCountBadge != null) ChartCountBadge.Text = "Đang tải...";
                    EmptyStateText.Text = "ĐANG KẾT NỐI DỮ LIỆU TỪ BACKEND...";
                }
                else
                {
                    ChartCountText.Text = "0";
                    if (ChartCountBadge != null) ChartCountBadge.Text = "0 biểu đồ";
                    EmptyStateText.Text = "CHỌN LOẠI BIỂU ĐỒ TỪ BẢNG ĐIỀU KHIỂN";
                }
            }
            else
            {
                EmptyState.Visibility = Visibility.Collapsed;
                ChartCountText.Text = chartSensors.Count.ToString();
                if (ChartCountBadge != null) ChartCountBadge.Text = $"{chartSensors.Count} biểu đồ";

                if (_columnsPerRow == 1)
                {
                    foreach (var sensor in chartSensors)
                    {
                        try
                        {
                            var card = CreateChartCard(sensor);
                            card.HorizontalAlignment = HorizontalAlignment.Stretch;
                            ChartsPanel.Children.Add(card);
                        }
                        catch (Exception ex) { Debug.WriteLine($"[Card ERROR] {sensor.Id}: {ex.Message}"); }
                    }
                }
                else
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
                    int count = 0;

                    foreach (var sensor in chartSensors)
                    {
                        try
                        {
                            row.Children.Add(CreateChartCard(sensor));
                            count++;
                            if (count >= _columnsPerRow)
                            {
                                ChartsPanel.Children.Add(row);
                                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
                                count = 0;
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[Card ERROR] {sensor.Id}: {ex.Message}"); }
                    }

                    if (row.Children.Count > 0) ChartsPanel.Children.Add(row);
                }
            }

            // Cameras
            if (cameraSensors.Count == 0)
            {
                CameraEmptyState.Visibility = Visibility.Visible;
                CameraCountText.Text = "0";
            }
            else
            {
                CameraEmptyState.Visibility = Visibility.Collapsed;
                CameraCountText.Text = cameraSensors.Count.ToString();
                foreach (var s in cameraSensors) CameraPanel.Children.Add(CreateCameraCard(s));
            }
        }

        private void UpdateHeaderStats()
        {
            var total = _lines.Sum(l => l.Nodes.Sum(n => n.Sensors.Count));
            if (ActiveSensorCountText != null) ActiveSensorCountText.Text = total.ToString();
            if (ActiveAlertCountText != null) ActiveAlertCountText.Text = _dataService.ActiveAlerts.Count.ToString();
        }

        // ──────────────── Card builders ────────────────

        private Border CreateChartCard(SensorData sensor)
        {
            var accent = GetSensorAccentColor(sensor.Type);

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 10, 19, 32)),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 22, 45, 65)),
                BorderThickness = new Thickness(1),
                MinWidth = _columnsPerRow switch { 1 => 0, 3 => 220, _ => 340 },
                HorizontalAlignment = _columnsPerRow == 1 ? HorizontalAlignment.Stretch : HorizontalAlignment.Left
            };

            // Outer: accent bar | content
            var outer = new Grid();
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accentBar = new Border
            {
                Background = new SolidColorBrush(accent),
                CornerRadius = new CornerRadius(5, 0, 0, 5)
            };
            Grid.SetColumn(accentBar, 0);

            // Content: header | chart | progress | value
            var content = new Grid { Margin = new Thickness(12, 10, 12, 10) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(160) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(content, 1);

            // Row 0 — header
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var typeLabel = new TextBlock
            {
                Text = GetSensorLabel(sensor.Type),
                FontSize = 9,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accent),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(typeLabel, 0);

            var nodeLabel = new TextBlock
            {
                Text = sensor.Name,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 61, 96, 112)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 110
            };
            Grid.SetColumn(nodeLabel, 1);

            headerGrid.Children.Add(typeLabel);
            headerGrid.Children.Add(nodeLabel);
            Grid.SetRow(headerGrid, 0);

            // Row 1 — chart
            FrameworkElement chart = sensor.Type == "radar" ? CreateRadarChart(sensor) : CreateChart(sensor);
            Grid.SetRow(chart, 1);

            // Row 2 — progress bar
            var progRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            progRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            progRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pb = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = CalculateProgressPercent(sensor),
                Height = 3,
                Foreground = new SolidColorBrush(GetLevelColor(sensor.Status)),
                Background = new SolidColorBrush(Color.FromArgb(255, 18, 38, 54)),
                VerticalAlignment = VerticalAlignment.Center
            };
            _sensorProgressBars[sensor.Id] = pb;
            Grid.SetColumn(pb, 0);

            var pctLabel = new TextBlock
            {
                Text = $"{pb.Value:F0}%",
                FontSize = 8,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 61, 96, 112)),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(pctLabel, 1);

            progRow.Children.Add(pb);
            progRow.Children.Add(pctLabel);
            Grid.SetRow(progRow, 2);

            // Row 3 — large value + unit
            var valueGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            valueGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var valueTb = new TextBlock
            {
                Text = $"{(sensor.Value ?? 0):F1}",
                FontSize = 26,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 216, 234)),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            _sensorValueTexts[sensor.Id] = valueTb;
            Grid.SetColumn(valueTb, 0);

            var unitTb = new TextBlock
            {
                Text = GetUnit(sensor.Type),
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(accent),
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(4, 0, 0, 3)
            };
            Grid.SetColumn(unitTb, 1);

            valueGrid.Children.Add(valueTb);
            valueGrid.Children.Add(unitTb);
            Grid.SetRow(valueGrid, 3);

            content.Children.Add(headerGrid);
            content.Children.Add(chart);
            content.Children.Add(progRow);
            content.Children.Add(valueGrid);

            outer.Children.Add(accentBar);
            outer.Children.Add(content);
            card.Child = outer;
            return card;
        }

        private RadarChartControl CreateRadarChart(SensorData sensor)
        {
            var detections = new List<RadarDetection>();
            var count = (int)(sensor.Value ?? 0);
            for (int i = 0; i < count; i++)
                detections.Add(new RadarDetection
                {
                    angle = 60 + _random.Next(61),
                    distance = 10 + _random.Next(35),
                    intensity = 50 + _random.Next(50),
                    objectType = "Person"
                });

            var rc = new RadarChartControl();
            rc.Loaded += async (s, e) => await rc.UpdateDetectionsAsync(detections);
            return rc;
        }

        private CartesianChart CreateChart(SensorData sensor)
        {
            var baseValue = sensor.Value ?? 0;
            var showGrid = ChkShowGrid?.IsChecked ?? true;
            var axisColor = new SKColor(61, 96, 112);
            var gridColor = new SKColor(18, 38, 54, 200);

            if (!_sensorHistoricalData.ContainsKey(sensor.Id))
            {
                _sensorHistoricalData[sensor.Id] = new List<double>();
                for (int i = 0; i < 24; i++)
                    _sensorHistoricalData[sensor.Id].Add(
                        Math.Max(0, baseValue + (_random.NextDouble() - 0.5) * baseValue * 0.2));
            }

            SolidColorPaint? gridPaint = showGrid ? new SolidColorPaint(gridColor) { StrokeThickness = 1 } : null;

            if (sensor.Type == "vibration")
            {
                var values = _sensorHistoricalData[sensor.Id].ToArray();
                var absMax = Math.Max(baseValue * 3, 1);

                var chart = new CartesianChart
                {
                    Series = new ISeries[]
                    {
                        new ColumnSeries<double>
                        {
                            Values      = values,
                            Fill        = new SolidColorPaint(GetChartColor(sensor.Type, 160)),
                            Stroke      = null,
                            MaxBarWidth = 3,
                            IgnoresBarPosition = true
                        }
                    },
                    XAxes = new[] { new Axis {
                        Labels          = Enumerable.Range(0, 6).Select(i => $"{i*10}s").ToArray(),
                        LabelsPaint     = new SolidColorPaint(axisColor),
                        SeparatorsPaint = gridPaint,
                        TextSize        = 9
                    }},
                    YAxes = new[] { new Axis {
                        LabelsPaint     = new SolidColorPaint(axisColor),
                        SeparatorsPaint = gridPaint,
                        TextSize        = 9,
                        MinLimit        = -absMax,
                        MaxLimit        = absMax
                    }}
                };
                _chartInstances[sensor.Id] = chart;
                return chart;
            }
            else
            {
                var values = _sensorHistoricalData[sensor.Id].ToArray();
                var showPts = ChkShowDataPoints?.IsChecked ?? true;
                var smooth = (ChkSmoothLine?.IsChecked ?? true) ? 0.5 : 0;
                var stroke = GetChartColor(sensor.Type);
                var fill = GetChartColor(sensor.Type, 18);
                var dot = new SKColor(10, 19, 32);

                var chart = new CartesianChart
                {
                    Series = new ISeries[]
                    {
                        new LineSeries<double>
                        {
                            Values         = values,
                            Fill           = new SolidColorPaint(fill),
                            Stroke         = new SolidColorPaint(stroke) { StrokeThickness = 1.5f },
                            GeometrySize   = showPts ? 4 : 0,
                            GeometryStroke = showPts ? new SolidColorPaint(stroke) { StrokeThickness = 1.5f } : null,
                            GeometryFill   = showPts ? new SolidColorPaint(dot) : null,
                            LineSmoothness = smooth
                        }
                    },
                    XAxes = new[] { new Axis {
                        Labels          = new[] { "00h", "04h", "08h", "12h", "16h", "20h", "24h" },
                        LabelsPaint     = new SolidColorPaint(axisColor),
                        SeparatorsPaint = gridPaint,
                        TextSize        = 9
                    }},
                    YAxes = new[] { new Axis {
                        LabelsPaint     = new SolidColorPaint(axisColor),
                        SeparatorsPaint = gridPaint,
                        TextSize        = 9
                    }}
                };
                _chartInstances[sensor.Id] = chart;
                return chart;
            }
        }

        private Border CreateCameraCard(SensorData sensor)
        {
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 10, 19, 32)),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromArgb(255, 22, 45, 65)),
                BorderThickness = new Thickness(1),
                MinHeight = 160
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new StackPanel { Margin = new Thickness(10, 8, 10, 6), Spacing = 2 };
            header.Children.Add(new TextBlock
            {
                Text = "CAMERA",
                FontSize = 8,
                FontFamily = new FontFamily("Consolas"),
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 229, 255))
            });
            header.Children.Add(new TextBlock
            {
                Text = sensor.Name,
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 216, 234))
            });
            Grid.SetRow(header, 0);

            var placeholder = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 14, 27, 42)),
                CornerRadius = new CornerRadius(0, 0, 5, 5),
                Margin = new Thickness(1, 0, 1, 1)
            };
            placeholder.Child = new FontIcon
            {
                Glyph = "",
                FontSize = 28,
                Foreground = new SolidColorBrush(Color.FromArgb(60, 0, 229, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(placeholder, 1);

            grid.Children.Add(header);
            grid.Children.Add(placeholder);
            card.Child = grid;
            return card;
        }

        // ──────────────── Color / label helpers ────────────────

        private SKColor GetChartColor(string type, byte alpha = 255) => type switch
        {
            "temperature" => new SKColor(255, 77, 77, alpha),
            "humidity" => new SKColor(0, 200, 255, alpha),
            "light" => new SKColor(255, 184, 0, alpha),
            "vibration" => new SKColor(255, 140, 0, alpha),
            "infrared" => new SKColor(255, 105, 180, alpha),
            "radar" => new SKColor(0, 255, 136, alpha),
            "waterlevel" => new SKColor(80, 160, 255, alpha),
            _ => new SKColor(0, 229, 255, alpha)
        };

        private Color GetSensorAccentColor(string type) => type switch
        {
            "temperature" => Color.FromArgb(255, 255, 77, 77),
            "humidity" => Color.FromArgb(255, 0, 200, 255),
            "light" => Color.FromArgb(255, 255, 184, 0),
            "vibration" => Color.FromArgb(255, 255, 140, 0),
            "infrared" => Color.FromArgb(255, 255, 105, 180),
            "radar" => Color.FromArgb(255, 0, 255, 136),
            "waterlevel" => Color.FromArgb(255, 80, 160, 255),
            _ => Color.FromArgb(255, 0, 229, 255)
        };

        private string GetSensorLabel(string type) => type switch
        {
            "temperature" => "NHIỆT ĐỘ",
            "humidity" => "ĐỘ ẨM",
            "light" => "ÁNH SÁNG",
            "vibration" => "RUNG ĐỘNG",
            "infrared" => "HỒNG NGOẠI",
            "radar" => "RADAR",
            "waterlevel" => "MỰC NƯỚC",
            _ => type.ToUpperInvariant()
        };

        private Color GetLevelColor(string status) => status switch
        {
            "warning" => Color.FromArgb(255, 255, 176, 32),
            "critical" => Color.FromArgb(255, 255, 64, 64),
            _ => Color.FromArgb(255, 0, 200, 138)
        };

        private double CalculateProgressPercent(SensorData sensor)
        {
            if (!sensor.Value.HasValue) return 0;
            var ms = _dataService.Sensors.FirstOrDefault(s => s.SensorId == sensor.Id);
            var max = ms?.AbsoluteMax > 0 ? ms.AbsoluteMax
                    : ms?.WarnThreshold > 0 ? ms.WarnThreshold * 2
                    : 100.0;
            return Math.Clamp(sensor.Value.Value / max * 100.0, 0, 100);
        }

        private string GetUnit(string? type) => type?.ToLower() switch
        {
            "temperature" => "°C",
            "humidity" => "%RH",
            "light" => "lux",
            "infrared" => "%",
            "vibration" => "m/s²",
            "radar" => "%",
            "waterlevel" => "mm",
            _ => ""
        };

        // ──────────────── Event handlers ────────────────

        private void LineFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedLineId = LineFilterComboBox.SelectedIndex switch
            {
                1 => "LINE-01",
                2 => "LINE-02",
                3 => "LINE-03",
                _ => "all"
            };
            BuildNodeFilterComboBox();
            LoadChartsForAllNodes();
        }

        private void NodeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NodeFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _selectedNodeIds.Clear();
                if (tag != "all") _selectedNodeIds.Add(tag);
                LoadChartsForAllNodes();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchTextBox.Text;
            LoadChartsForAllNodes();
        }

        private void ChartType_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string type)
            {
                if (cb.IsChecked == true) _selectedTypes.Add(type);
                else _selectedTypes.Remove(type);
                LoadChartsForAllNodes();
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            ChkRadar.IsChecked = true;
            ChkCamera.IsChecked = true;
            ChkTemperature.IsChecked = true;
            ChkHumidity.IsChecked = true;
            ChkLight.IsChecked = true;
            ChkVibration.IsChecked = true;
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            ChkRadar.IsChecked = false;
            ChkCamera.IsChecked = false;
            ChkTemperature.IsChecked = false;
            ChkHumidity.IsChecked = false;
            ChkLight.IsChecked = false;
            ChkInfrared.IsChecked = false;
            ChkVibration.IsChecked = false;
        }

        private void DisplayOption_Changed(object sender, RoutedEventArgs e) => LoadChartsForAllNodes();

        private void LayoutOption_Changed(object sender, RoutedEventArgs e)
        {
            _columnsPerRow = LayoutSingle?.IsChecked == true ? 1 :
                             LayoutTriple?.IsChecked == true ? 3 : 2;
            LoadChartsForAllNodes();
        }

        private void RefreshData_Click(object sender, RoutedEventArgs e) => LoadChartsForAllNodes();

        private void ExportData_Click(object sender, RoutedEventArgs e) =>
            Debug.WriteLine("[DataPage] Export — to be implemented");

        // ──────────────── Inner types ────────────────

        public class LineData
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public List<NodeData> Nodes { get; set; } = new();
        }

        public class NodeData
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public List<SensorData> Sensors { get; set; } = new();
        }

        public class SensorData
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public string Icon { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public double? Value { get; set; }
        }
    }
}
