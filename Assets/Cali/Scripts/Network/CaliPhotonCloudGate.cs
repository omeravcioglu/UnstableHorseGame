using System.Threading;
using System.Threading.Tasks;

namespace Cali.Network
{
    /// <summary>
    /// Photon NameServer / lobby connects must not overlap (ping + JoinSessionLobby race → ShutdownReason.Error).
    /// </summary>
    public static class CaliPhotonCloudGate
    {
        static readonly SemaphoreSlim Mutex = new SemaphoreSlim(1, 1);

        public static async Task<T> RunAsync<T>(System.Func<Task<T>> action)
        {
            await Mutex.WaitAsync().ConfigureAwait(true);
            try
            {
                return await action().ConfigureAwait(true);
            }
            finally
            {
                Mutex.Release();
            }
        }

        public static async Task RunAsync(System.Func<Task> action)
        {
            await Mutex.WaitAsync().ConfigureAwait(true);
            try
            {
                await action().ConfigureAwait(true);
            }
            finally
            {
                Mutex.Release();
            }
        }
    }
}
