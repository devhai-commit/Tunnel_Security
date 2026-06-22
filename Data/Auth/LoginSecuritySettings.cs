namespace TunnelSecurity.Auth
{
    public class LoginSecuritySettings
    {
        public int MaxFailedLoginAttempts { get; set; } = 5;
        public int FailedAttemptWindowMinutes { get; set; } = 15;
        public int LockoutMinutes { get; set; } = 15;
    }
}
