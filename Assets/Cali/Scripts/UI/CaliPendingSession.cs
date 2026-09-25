namespace Cali.UI
{
    /// <summary>
    /// Carries Host/Join choices from the main menu into CaliLobby.
    /// </summary>
    public static class CaliPendingSession
    {
        public enum Mode
        {
            None,
            Host,
            Client
        }

        public static Mode PendingMode = Mode.None;
        public static string Region = "";
        public static string SessionName = "CaliHorseCoop";
        public static string Password = "";

        public static bool HasPending => PendingMode != Mode.None;

        public static void SetHost(string region, string sessionName, string password)
        {
            PendingMode = Mode.Host;
            Region = region ?? "";
            SessionName = string.IsNullOrWhiteSpace(sessionName) ? "CaliHorseCoop" : sessionName.Trim();
            Password = password ?? "";
        }

        public static void SetClient(string region, string sessionName, string password)
        {
            PendingMode = Mode.Client;
            Region = region ?? "";
            SessionName = string.IsNullOrWhiteSpace(sessionName) ? "CaliHorseCoop" : sessionName.Trim();
            Password = password ?? "";
        }

        public static void Clear()
        {
            PendingMode = Mode.None;
            Region = "";
            SessionName = "CaliHorseCoop";
            Password = "";
        }
    }
}
