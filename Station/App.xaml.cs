using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Station.Services;
using Station.Views;
using System;
using System.Threading.Tasks;
using Windows.Graphics;

namespace Station
{
    public partial class App : Application
    {
        private const int LoginWindowTargetWidth = 1260;
        private const int LoginWindowTargetHeight = 760;

        public Window? m_window { get; set; }
        private SimulationApiServer? _simServer;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // Start mock data simulation
            MockDataService.Instance.Start();

            // Start web API server
            _simServer = new SimulationApiServer();
            Task.Run(() => _simServer.StartAsync());

            m_window = new Window();

            Frame rootFrame = new Frame();
            rootFrame.Navigate(typeof(LoginPage));

            m_window.Content = rootFrame;
            ConfigureLoginWindow(m_window);
            m_window.Activate();
        }

        public static void ConfigureLoginWindow(Window window)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = true;
                presenter.IsMaximizable = true;
            }

            RectInt32 workArea = displayArea.WorkArea;
            int width = Math.Min(LoginWindowTargetWidth, Math.Max(960, workArea.Width - 80));
            int height = Math.Min(LoginWindowTargetHeight, Math.Max(680, workArea.Height - 80));
            int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
            int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);

            appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
    }
}
