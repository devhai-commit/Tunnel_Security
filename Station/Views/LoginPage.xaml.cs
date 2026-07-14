using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Station.Services;

namespace Station.Views
{
    public sealed partial class LoginPage : Page
    {
        public LoginPage()
        {
            this.InitializeComponent();
            Loaded += LoginPage_Loaded;
        }

        private void LoginPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= LoginPage_Loaded;

            var pendingMessage = SessionLockState.ConsumePendingMessage();
            if (!string.IsNullOrWhiteSpace(pendingMessage))
            {
                ShowMessage(
                    pendingMessage,
                    "Phiên làm việc đã được khoá",
                    InfoBarSeverity.Warning);
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorInfoBar.IsOpen = false;

            var username = UsernameBox.Text;
            var password = PasswordBox.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowMessage("Vui lòng nhập đầy đủ thông tin.");
                return;
            }

            LoginButton.IsEnabled = false;
            LoadingRing.Visibility = Visibility.Visible;
            LoadingRing.IsActive = true;

            try
            {
                var (success, errorMessage) = await Login(username, password);

                if (success)
                {
                    var loginWindow = (Application.Current as App)?.m_window;
                    var mainWindow = new MainWindow();
                    if (Application.Current is App app)
                    {
                        app.m_window = mainWindow;
                    }
                    mainWindow.Activate();
                    loginWindow?.Close();
                }
                else
                {
                    ShowMessage(string.IsNullOrWhiteSpace(errorMessage) ? "Sai username hoặc mật khẩu." : errorMessage);
                }
            }
            catch (HttpRequestException ex)
            {
                ShowMessage("HTTP ERROR: " + ex.Message);
            }
            catch (Exception ex)
            {
                ShowMessage("OTHER ERROR: " + ex.Message);
            }

            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            LoginButton.IsEnabled = true;
        }

        private async Task<(bool Success, string? ErrorMessage)> Login(string username, string password)
        {
            using var client = new HttpClient();

            var body = new
            {
                username = username,
                password = password
            };

            var json = JsonSerializer.Serialize(body);

            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            var baseUrl = Environment.GetEnvironmentVariable("BACKENDV2_BASE_URL") ?? "http://localhost:5080";
            System.Diagnostics.Debug.WriteLine($"CALLING API: {baseUrl}/api/Auth/login");
            System.Diagnostics.Debug.WriteLine("REQUEST BODY: " + json);

            var response = await client.PostAsync(
                $"{baseUrl}/api/Auth/login",
                content);

            var result = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine("STATUS: " + response.StatusCode);
            System.Diagnostics.Debug.WriteLine("RESPONSE: " + result);

            if (response.IsSuccessStatusCode)
            {
                try
                {
                    using var document = JsonDocument.Parse(result);
                    string? accessToken = document.RootElement.TryGetProperty("accessToken", out var accessTokenElement)
                        ? accessTokenElement.GetString()
                        : null;
                    string? refreshToken = document.RootElement.TryGetProperty("refreshToken", out var refreshTokenElement)
                        ? refreshTokenElement.GetString()
                        : null;

                    DateTimeOffset? expiresAt = null;
                    if (document.RootElement.TryGetProperty("expiresAt", out var expiresElement) &&
                        expiresElement.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(expiresElement.GetString(), out var parsedExpires))
                    {
                        expiresAt = parsedExpires;
                    }

                    if (!string.IsNullOrWhiteSpace(accessToken))
                    {
                        AuthSession.SignIn(accessToken, refreshToken, expiresAt ?? DateTimeOffset.UtcNow.AddHours(1));
                        CurrentUserSession.Instance.SetSession(accessToken, expiresAt);
                    }
                }
                catch
                {
                }

                return (true, null);
            }

            try
            {
                using var document = JsonDocument.Parse(result);
                if (document.RootElement.TryGetProperty("message", out var messageElement))
                {
                    return (false, messageElement.GetString());
                }
            }
            catch
            {
            }

            return (false, null);
        }

        private void ShowMessage(
            string message,
            string title = "Đăng nhập không thành công",
            InfoBarSeverity severity = InfoBarSeverity.Error)
        {
            ErrorInfoBar.Title = title;
            ErrorInfoBar.Severity = severity;
            ErrorInfoBar.Message = message;
            ErrorInfoBar.IsOpen = true;
        }
    }
}
