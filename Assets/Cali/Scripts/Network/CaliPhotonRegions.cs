using System;
using Fusion.Photon.Realtime;

namespace Cali.Network
{
    /// <summary>
    /// Shared Photon region codes + settings helpers for Host/Join.
    /// </summary>
    public static class CaliPhotonRegions
    {
        public const string HasPasswordProperty = "hasPwd";
        public const string PasswordHashProperty = "pwdH";

        public static int PasswordHash(string password)
        {
            if (string.IsNullOrEmpty(password))
                return 0;
            unchecked
            {
                int h = 17;
                for (int i = 0; i < password.Length; i++)
                    h = h * 31 + password[i];
                return h;
            }
        }

        /// <summary>
        /// Fusion Shared lobby name (CloudServicesMetadata.LobbyShared is internal).
        /// Must match the lobby StartGame and the Join browser both query.
        /// </summary>
        public const string SharedLobbyName = "LobbyShared";

        /// <summary>Kept so older Host-mode rooms can still be found if needed.</summary>
        public const string ClientServerLobbyName = SharedLobbyName;

        /// <summary>Display label, Photon FixedRegion code (empty = Best).</summary>
        public static readonly (string Label, string Code)[] Options =
        {
            ("Best (auto)", ""),
            ("US East (us)", "us"),
            ("US West (usw)", "usw"),
            ("Europe (eu)", "eu"),
            ("Asia (asia)", "asia"),
            ("Japan (jp)", "jp"),
            ("Australia (au)", "au"),
            ("South America (sa)", "sa"),
            ("Canada East (cae)", "cae"),
            ("Korea (kr)", "kr"),
            ("India (in)", "in"),
            ("Russia (ru)", "ru"),
        };

        public static FusionAppSettings BuildAppSettings(string fixedRegion)
        {
            if (!PhotonAppSettings.TryGetGlobal(out var global) || global?.AppSettings == null)
                return null;

            var copy = global.AppSettings.GetCopy();
            // Photon treats "" as invalid; use null for "Best Region".
            copy.FixedRegion = string.IsNullOrWhiteSpace(fixedRegion) ? null : fixedRegion.Trim().ToLowerInvariant();
            if (copy.FixedRegion != null && copy.FixedRegion.Length == 0)
                copy.FixedRegion = null;
            return copy;
        }

        public static byte[] PasswordToToken(string password)
        {
            // Fusion treats ConnectionToken default as null. Array.Empty<byte>() crashes
            // NetPeerGroup.AllocateConnection with "Trying to allocate < 0 bytes: 0".
            if (string.IsNullOrEmpty(password))
                return null;
            return System.Text.Encoding.UTF8.GetBytes(password);
        }

        public static bool TokenMatchesPassword(byte[] token, string expectedPassword)
        {
            if (string.IsNullOrEmpty(expectedPassword))
                return true;
            if (token == null || token.Length == 0)
                return false;
            string provided = System.Text.Encoding.UTF8.GetString(token);
            return string.Equals(provided, expectedPassword, StringComparison.Ordinal);
        }
    }
}
