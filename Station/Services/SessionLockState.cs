namespace Station.Services
{
    public static class SessionLockState
    {
        private static string? _pendingMessage;

        public static void SetPendingMessage(string message)
        {
            _pendingMessage = string.IsNullOrWhiteSpace(message)
                ? null
                : message.Trim();
        }

        public static string? ConsumePendingMessage()
        {
            var message = _pendingMessage;
            _pendingMessage = null;
            return message;
        }
    }
}
