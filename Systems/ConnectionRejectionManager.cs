namespace CharacterVault.Systems
{
    /// <summary>Stores the server's explicit rejection text until Valheim opens its connection error panel.</summary>
    public static class ConnectionRejectionManager
    {
        private static string? _reason;

        public static void SetReason(string reason) => _reason = reason;

        public static string? ConsumeReason()
        {
            string? reason = _reason;
            _reason = null;
            return reason;
        }
    }
}