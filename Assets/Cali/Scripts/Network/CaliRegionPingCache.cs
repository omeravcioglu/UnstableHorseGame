using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion.Photon.Realtime;
using Photon.Realtime;
using UnityEngine;

namespace Cali.Network
{
    /// <summary>
    /// Caches Photon Cloud region ping results for the Join browser.
    /// </summary>
    public static class CaliRegionPingCache
    {
        static readonly Dictionary<string, int> PingsMs = new Dictionary<string, int>();
        static Task _pingTask;

        public static IReadOnlyDictionary<string, int> Pings => PingsMs;

        public static int GetPingMs(string regionCode)
        {
            if (string.IsNullOrEmpty(regionCode))
                return -1;

            string code = regionCode.ToLowerInvariant();
            int slash = code.IndexOf('/');
            if (slash > 0)
                code = code.Substring(0, slash);

            return PingsMs.TryGetValue(code, out int ms) ? ms : -1;
        }

        public static Task EnsurePingedAsync()
        {
            if (_pingTask != null && !_pingTask.IsCompleted)
                return _pingTask;

            _pingTask = PingInternalAsync();
            return _pingTask;
        }

        static async Task PingInternalAsync()
        {
            if (!PhotonAppSettings.TryGetGlobal(out var global) || global?.AppSettings == null)
            {
                Debug.LogWarning("[CaliRegionPingCache] PhotonAppSettings missing.");
                return;
            }

            await CaliPhotonCloudGate.RunAsync(async () =>
            {
                var settings = global.AppSettings.GetCopy();
                settings.FixedRegion = null;

                var host = new GameObject("<<<Cali Region Ping>>>");
                Object.DontDestroyOnLoad(host);
                var driver = host.AddComponent<RegionPingDriver>();

                try
                {
                    var handler = await driver.PingRegionsAsync(settings);
                    PingsMs.Clear();
                    if (handler?.EnabledRegions != null)
                    {
                        foreach (var region in handler.EnabledRegions)
                        {
                            if (region == null || string.IsNullOrEmpty(region.Code))
                                continue;
                            PingsMs[region.Code.ToLowerInvariant()] = region.WasPinged ? region.Ping : -1;
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[CaliRegionPingCache] Region ping failed: {e.Message}");
                }
                finally
                {
                    if (host != null)
                        Object.Destroy(host);
                }
            });
        }

        class RegionPingDriver : MonoBehaviour
        {
            RealtimeClient _client;
            bool _running;

            void Update()
            {
                if (_running && _client != null)
                    _client.Service();
            }

            public async Task<RegionHandler> PingRegionsAsync(FusionAppSettings settings)
            {
                _client = new RealtimeClient();
                _running = true;
                try
                {
                    return await _client.ConnectToNameserverAndWaitForRegionsAsync(settings, pingRegions: true);
                }
                finally
                {
                    _running = false;
                    try
                    {
                        if (_client != null && _client.IsConnected)
                            await _client.DisconnectAsync();
                    }
                    catch
                    {
                        // ignored
                    }

                    _client = null;
                }
            }
        }
    }
}
