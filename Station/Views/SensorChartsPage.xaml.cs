using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinUI;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Windows.UI;
using Station.Services;

namespace Station.Views;

public sealed partial class SensorChartsPage : Page
{
    private readonly IDataService _dataService = DataServiceLocator.Current;
    private readonly Dictionary<string, SensorChartState> _sensorStates = new();
    private readonly Dictionary<string, ObservableCollection<double>> _chartHistories = new();
    private readonly HashSet<string> _enabledTypes = new()
    {
        "temperature", "humidity", "light", "infrared", "vibration"
    };

    private DispatcherTimer? _renderTimer;
    private const int RenderIntervalMs = 200;

    private int _columns = 2;
    private int _historyLength = 60;
    private int _totalUpdates;
    private bool _isLoaded;

    public SensorChartsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ─────────── Lifecycle ───────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        _dataService.TopologyLoaded += OnTopologyLoaded;
        _dataService.SensorTick += OnSensorTick;

        _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RenderIntervalMs) };
        _renderTimer.Tick += OnRenderTick;
        _renderTimer.Start();

        BuildCharts();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _dataService.TopologyLoaded -= OnTopologyLoaded;
        _dataService.SensorTick -= OnSensorTick;

        _renderTimer?.Stop();
        _renderTimer = null;
    }

    private void OnTopologyLoaded(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(BuildCharts);
    }

    private void OnSensorTick(object? sender, SensorTickEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            try { ProcessSensorTick(e); }
            catch (Exception ex) { Debug.WriteLine($"[SensorChartsPage] Tick error: {ex.Message}"); }
        }
        else
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try { ProcessSensorTick(e); }
                catch (Exception ex) { Debug.WriteLine($"[SensorChartsPage] Tick error: {ex.Message}"); }
            });
        }
    }

    // ─────────── Render loop ───────────

    private void OnRenderTick(object? sender, object e)
    {
        foreach (var state in _sensorStates.Values)
        {
            state.ChartValues.Add(state.LastValue);
            if (state.ChartValues.Count > _historyLength)
                state.ChartValues.RemoveAt(0);
        }
    }

    // ─────────── Real-time update ───────────

    private void ProcessSensorTick(SensorTickEventArgs e)
    {
        _totalUpdates++;
        TotalUpdatesText.Text = _totalUpdates.ToString("N0");

        if (!_sensorStates.TryGetValue(e.Sensor.SensorId, out var state)) return;

        state.LastValue = e.NewValue;
        state.ValueText.Text = $"{e.NewValue:F1}";

        var (dotColor, statusLabel) = e.Sensor.CurrentLevel switch
        {
            SensorAlertLevel.Critical => (Color.FromArgb(255, 255, 64, 64), "CRITICAL"),
            SensorAlertLevel.Warning  => (Color.FromArgb(255, 255, 176, 32), "WARNING"),
            SensorAlertLevel.Offline  => (Color.FromArgb(255, 61, 96, 112), "OFFLINE"),
            _                         => (Color.FromArgb(255, 0, 200, 138), "NORMAL")
        };

        var brush = new SolidColorBrush(dotColor);
        state.StatusDot.Background = brush;
        state.StatusText.Text = statusLabel;
        state.StatusText.Foreground = brush;
    }

    // ─────────── Chart building ───────────

    private void BuildCharts()
    {
        ChartsHost.Children.Clear();
        ChartsHost.RowDefinitions.Clear();
        ChartsHost.ColumnDefinitions.Clear();
        _sensorStates.Clear();

        var sensors = _dataService.Sensors
            .Where(s => _enabledTypes.Contains(SensorTypeString(s.Category)))
            .OrderBy(s => s.LineId)
            .ThenBy(s => s.NodeId)
            .ToList();

        // Prune histories for sensors no longer in scope
        var activeIds = sensors.Select(s => s.SensorId).ToHashSet();
        foreach (var id in _chartHistories.Keys.Where(k => !activeIds.Contains(k)).ToList())
            _chartHistories.Remove(id);

        if (sensors.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            EmptyStateLabel.Text = _dataService.Sensors.Count == 0
                ? "ĐANG KẾT NỐI VỚI BACKEND..."
                : "CHỌN LOẠI CẢM BIẾN ĐỂ HIỂN THỊ";
            ChartCountBadge.Text = "0 biểu đồ";
            SensorCountText.Text = "0";
            SetConnectedStatus(_dataService.Sensors.Count > 0);
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        SensorCountText.Text = sensors.Count.ToString();
        ChartCountBadge.Text = $"{sensors.Count} biểu đồ";
        SetConnectedStatus(true);

        var columns = Math.Max(1, _columns);
        for (var i = 0; i < columns; i++)
        {
            ChartsHost.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
        }

        for (var i = 0; i < sensors.Count; i++)
        {
            var sensor = sensors[i];
            var type = SensorTypeString(sensor.Category);
            var state = CreateChartState(sensor.SensorId, sensor.SensorName, type, sensor.CurrentValue);
            _sensorStates[sensor.SensorId] = state;

            var row = i / columns;
            var column = i % columns;

            while (ChartsHost.RowDefinitions.Count <= row)
            {
                ChartsHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            state.Card.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetRow(state.Card, row);
            Grid.SetColumn(state.Card, column);
            ChartsHost.Children.Add(state.Card);
        }
    }

    private SensorChartState CreateChartState(
        string sensorId, string sensorName, string type, double initialValue)
    {
        var chartColor = ChartColor(type);
        var accentColor = AccentColor(type);

        // Reuse existing history so data survives topology rebuilds
        if (!_chartHistories.TryGetValue(sensorId, out var chartValues))
        {
            chartValues = new ObservableCollection<double>(
                Enumerable.Repeat(initialValue, Math.Min(10, _historyLength)));
            _chartHistories[sensorId] = chartValues;
        }

        ISeries series;

        if (type == "vibration")
        {
            var fillColor = new SKColor(chartColor.Red, chartColor.Green, chartColor.Blue, 180);
            series = new ColumnSeries<double>
            {
                Values = chartValues,
                Fill = new SolidColorPaint(fillColor),
                Stroke = new SolidColorPaint(SKColors.Transparent),
                MaxBarWidth = 4,
                IgnoresBarPosition = true
            };
        }
        else
        {
            var fillColor = new SKColor(chartColor.Red, chartColor.Green, chartColor.Blue, 20);
            series = new LineSeries<double>
            {
                Values = chartValues,
                Fill = new SolidColorPaint(fillColor),
                Stroke = new SolidColorPaint(chartColor) { StrokeThickness = 1.5f },
                GeometrySize = 0,
                LineSmoothness = 0.5
            };
        }

        var axisLabelPaint = new SolidColorPaint(new SKColor(61, 96, 112));
        var gridPaint = new SolidColorPaint(new SKColor(18, 38, 54, 200)) { StrokeThickness = 1 };

        var chart = new CartesianChart
        {
            Height = 150,
            Series = new[] { series },
            AnimationsSpeed = TimeSpan.Zero,
            EasingFunction = null,
            XAxes = new[] { new Axis { LabelsPaint = null, SeparatorsPaint = gridPaint, TextSize = 0 } },
            YAxes = new[]
            {
                new Axis
                {
                    LabelsPaint = axisLabelPaint,
                    SeparatorsPaint = gridPaint,
                    TextSize = 9
                }
            }
        };

        var valueTb = new TextBlock
        {
            Text = $"{initialValue:F1}",
            FontSize = 24,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 192, 216, 234))
        };

        var statusDot = new Border
        {
            Width = 6,
            Height = 6,
            Background = new SolidColorBrush(Color.FromArgb(255, 0, 200, 138)),
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center
        };

        var statusTb = new TextBlock
        {
            Text = "NORMAL",
            FontSize = 8,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 200, 138)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var card = BuildCard(sensorName, type, chart, valueTb, statusDot, statusTb, accentColor);

        return new SensorChartState
        {
            Card = card,
            ChartValues = chartValues,
            ValueText = valueTb,
            StatusDot = statusDot,
            StatusText = statusTb,
            LastValue = initialValue
        };
    }

    private Border BuildCard(
        string sensorName, string type, CartesianChart chart,
        TextBlock valueTb, Border statusDot, TextBlock statusTb, Color accent)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 10, 19, 32)),
            CornerRadius = new CornerRadius(6),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 22, 45, 65)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var outer = new Grid();
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accentBar = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(5, 0, 0, 5)
        };
        Grid.SetColumn(accentBar, 0);

        var content = new Grid { Margin = new Thickness(12, 10, 12, 10) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(content, 1);

        // Row 0 — header
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var typeLabel = new TextBlock
        {
            Text = SensorLabel(type),
            FontSize = 9,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(typeLabel, 0);

        var nameLabel = new TextBlock
        {
            Text = sensorName,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 61, 96, 112)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 120
        };
        Grid.SetColumn(nameLabel, 1);

        headerGrid.Children.Add(typeLabel);
        headerGrid.Children.Add(nameLabel);
        Grid.SetRow(headerGrid, 0);

        // Row 1 — chart
        Grid.SetRow(chart, 1);

        // Row 2 — value + status
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var valueStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        valueStack.Children.Add(valueTb);
        valueStack.Children.Add(new TextBlock
        {
            Text = SensorUnit(type),
            FontSize = 10,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(accent),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(2, 0, 0, 3)
        });
        Grid.SetColumn(valueStack, 0);

        var statusStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        statusStack.Children.Add(statusDot);
        statusStack.Children.Add(statusTb);
        Grid.SetColumn(statusStack, 1);

        footer.Children.Add(valueStack);
        footer.Children.Add(statusStack);
        Grid.SetRow(footer, 2);

        content.Children.Add(headerGrid);
        content.Children.Add(chart);
        content.Children.Add(footer);

        outer.Children.Add(accentBar);
        outer.Children.Add(content);
        card.Child = outer;
        return card;
    }

    private void SetConnectedStatus(bool connected)
    {
        if (connected)
        {
            ConnectionDot.Fill = new SolidColorBrush(Color.FromArgb(255, 0, 255, 136));
            ConnectionText.Text = "CONNECTED";
            ConnectionText.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 229, 255));
        }
        else
        {
            ConnectionDot.Fill = new SolidColorBrush(Color.FromArgb(255, 61, 96, 112));
            ConnectionText.Text = "CONNECTING...";
            ConnectionText.Foreground = new SolidColorBrush(Color.FromArgb(255, 61, 96, 112));
        }
    }

    // ─────────── Helpers ───────────

    private static string SensorTypeString(Models.AlertCategory cat) => cat switch
    {
        Models.AlertCategory.Temperature  => "temperature",
        Models.AlertCategory.Humidity     => "humidity",
        Models.AlertCategory.Light        => "light",
        Models.AlertCategory.Infrared     => "infrared",
        Models.AlertCategory.Accelerometer => "vibration",
        _                                 => "other"
    };

    private static SKColor ChartColor(string type) => type switch
    {
        "temperature" => new SKColor(255, 77, 77),
        "humidity"    => new SKColor(0, 200, 255),
        "light"       => new SKColor(255, 184, 0),
        "vibration"   => new SKColor(255, 140, 0),
        "infrared"    => new SKColor(255, 105, 180),
        _             => new SKColor(0, 229, 255)
    };

    private static Color AccentColor(string type) => type switch
    {
        "temperature" => Color.FromArgb(255, 255, 77, 77),
        "humidity"    => Color.FromArgb(255, 0, 200, 255),
        "light"       => Color.FromArgb(255, 255, 184, 0),
        "vibration"   => Color.FromArgb(255, 255, 140, 0),
        "infrared"    => Color.FromArgb(255, 255, 105, 180),
        _             => Color.FromArgb(255, 0, 229, 255)
    };

    private static string SensorLabel(string type) => type switch
    {
        "temperature" => "NHIỆT ĐỘ",
        "humidity"    => "ĐỘ ẨM",
        "light"       => "ÁNH SÁNG",
        "vibration"   => "RUNG ĐỘNG",
        "infrared"    => "HỒNG NGOẠI",
        _             => type.ToUpperInvariant()
    };

    private static string SensorUnit(string type) => type switch
    {
        "temperature" => "°C",
        "humidity"    => "%RH",
        "light"       => "lux",
        "infrared"    => "%",
        "vibration"   => "m/s²",
        _             => ""
    };

    // ─────────── Event handlers ───────────

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _enabledTypes.Clear();
        if (ChkTemperature.IsChecked == true) _enabledTypes.Add("temperature");
        if (ChkHumidity.IsChecked == true) _enabledTypes.Add("humidity");
        if (ChkLight.IsChecked == true) _enabledTypes.Add("light");
        if (ChkInfrared.IsChecked == true) _enabledTypes.Add("infrared");
        if (ChkVibration.IsChecked == true) _enabledTypes.Add("vibration");
        BuildCharts();
    }

    private void Layout_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _columns = Layout1Col.IsChecked == true ? 1
                 : Layout3Col.IsChecked == true ? 3
                 : 2;
        BuildCharts();
    }

    private void HistoryLength_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _historyLength = History30.IsChecked == true ? 30
                       : History100.IsChecked == true ? 100
                       : 60;

        foreach (var state in _sensorStates.Values)
        {
            while (state.ChartValues.Count > _historyLength)
                state.ChartValues.RemoveAt(0);
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _totalUpdates = 0;
        TotalUpdatesText.Text = "0";
        foreach (var state in _sensorStates.Values)
        {
            state.ChartValues.Clear();
        }
    }

    // ─────────── Inner types ───────────

    private sealed class SensorChartState
    {
        public required Border Card { get; init; }
        public required ObservableCollection<double> ChartValues { get; init; }
        public required TextBlock ValueText { get; init; }
        public required Border StatusDot { get; init; }
        public required TextBlock StatusText { get; init; }
        public double LastValue { get; set; }
    }
}
