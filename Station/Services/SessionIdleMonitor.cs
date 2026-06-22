using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;

namespace Station.Services
{
    public sealed class SessionIdleMonitor : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly TimeSpan _idleTimeout;
        private readonly Dictionary<Window, FrameworkElement> _registeredWindows = new();
        private DateTimeOffset _lastActivityAt;
        private bool _isTriggered;

        public SessionIdleMonitor(TimeSpan idleTimeout)
        {
            _idleTimeout = idleTimeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(15) : idleTimeout;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _timer.Tick += Timer_Tick;
        }

        public event EventHandler? IdleTimeoutReached;

        public void Start()
        {
            _isTriggered = false;
            Touch();
            if (!_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        public void Stop()
        {
            if (_timer.IsEnabled)
            {
                _timer.Stop();
            }
        }

        public void RegisterWindow(Window window)
        {
            if (window == null || window.Content is not FrameworkElement root || _registeredWindows.ContainsKey(window))
            {
                return;
            }

            AttachActivityHandlers(root);
            _registeredWindows[window] = root;
            window.Closed += Window_Closed;
        }

        public void UnregisterWindow(Window window)
        {
            if (!_registeredWindows.TryGetValue(window, out var root))
            {
                return;
            }

            DetachActivityHandlers(root);
            _registeredWindows.Remove(window);
            window.Closed -= Window_Closed;
        }

        public void Touch()
        {
            _lastActivityAt = DateTimeOffset.UtcNow;
        }

        private void Timer_Tick(object? sender, object e)
        {
            if (_isTriggered)
            {
                return;
            }

            if (DateTimeOffset.UtcNow - _lastActivityAt < _idleTimeout)
            {
                return;
            }

            _isTriggered = true;
            Stop();
            IdleTimeoutReached?.Invoke(this, EventArgs.Empty);
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            if (sender is Window window)
            {
                UnregisterWindow(window);
            }
        }

        private void AttachActivityHandlers(FrameworkElement root)
        {
            root.PointerMoved += ActivityDetected;
            root.PointerPressed += ActivityDetected;
            root.PointerWheelChanged += ActivityDetected;
            root.KeyDown += ActivityDetected;
            root.Tapped += ActivityDetected;
        }

        private void DetachActivityHandlers(FrameworkElement root)
        {
            root.PointerMoved -= ActivityDetected;
            root.PointerPressed -= ActivityDetected;
            root.PointerWheelChanged -= ActivityDetected;
            root.KeyDown -= ActivityDetected;
            root.Tapped -= ActivityDetected;
        }

        private void ActivityDetected(object sender, object e)
        {
            if (_isTriggered)
            {
                return;
            }

            Touch();
        }

        public void Dispose()
        {
            Stop();

            foreach (var entry in _registeredWindows)
            {
                DetachActivityHandlers(entry.Value);
                entry.Key.Closed -= Window_Closed;
            }

            _registeredWindows.Clear();
            _timer.Tick -= Timer_Tick;
        }
    }
}
