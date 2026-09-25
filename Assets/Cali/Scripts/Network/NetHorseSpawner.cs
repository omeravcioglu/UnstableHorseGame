using Fusion;
using UnityEngine;

namespace Cali.Network
{
    /// <summary>
    /// Owns seat assignment and spawning of <see cref="NetHorse"/>. Every peer spawns only the
    /// seat it controls, so the owner holds State Authority from the first tick and never has
    /// to take it over from anyone.
    /// </summary>
    public static class NetHorseSpawner
    {
        /// <summary>Seconds to wait before retrying a spawn, so a slow snapshot cannot cause a flood.</summary>
        const float SpawnRetryDelay = 0.5f;

        static readonly float[] NextAttempt = new float[2];

        /// <summary>Spawns the local player's horse, plus seat 1 while the host is alone.</summary>
        public static void EnsureLocal(NetworkRunner runner)
        {
            if (runner == null || !runner.IsRunning)
                return;

            int seat = CoopInput.GetLocalSeat(runner);
            if (NetHorse.Find(seat) == null)
                Spawn(runner, seat);

            bool solo = runner.IsSharedModeMasterClient && runner.SessionInfo.PlayerCount <= 1;
            if (solo && NetHorse.Find(1) == null)
                Spawn(runner, 1);
        }

        /// <summary>
        /// Hands seat 1 over when a second player arrives: the host drops the horse it was
        /// driving on the arrow keys so the joiner can spawn its own and own it outright.
        /// </summary>
        public static void Tick(NetworkRunner runner)
        {
            if (runner == null || !runner.IsRunning)
                return;

            if (runner.IsSharedModeMasterClient && runner.SessionInfo.PlayerCount >= 2
                && CoopInput.GetLocalSeat(runner) != 1)
            {
                var guest = NetHorse.Find(1);
                if (guest != null && guest.HasStateAuthority && guest.Object != null && guest.Object.IsValid)
                {
                    Debug.Log("[NetHorseSpawner] Second player joined — releasing host-spawned seat 1.");
                    runner.Despawn(guest.Object);
                }
            }

            EnsureLocal(runner);
        }

        public static void Reset()
        {
            NextAttempt[0] = 0f;
            NextAttempt[1] = 0f;
        }

        static void Spawn(NetworkRunner runner, int seat)
        {
            if (seat < 0 || seat > 1 || Time.unscaledTime < NextAttempt[seat])
                return;

            NextAttempt[seat] = Time.unscaledTime + SpawnRetryDelay;

            var prefab = Resources.Load<NetworkObject>(NetHorse.ResourcesPath);
            if (prefab == null)
            {
                Debug.LogWarning("[NetHorseSpawner] Missing Resources/" + NetHorse.ResourcesPath + " prefab.");
                return;
            }

            ResolveScenePose(seat, out Vector3 pos, out Quaternion rot);
            var obj = runner.Spawn(
                prefab,
                pos,
                rot,
                runner.LocalPlayer,
                (_, spawned) =>
                {
                    spawned.Flags &= ~NetworkObjectFlags.MasterClientObject;
                    spawned.Flags |= NetworkObjectFlags.AllowStateAuthorityOverride;
                    var horse = spawned.GetComponent<NetHorse>();
                    if (horse != null)
                        horse.Seat = seat;
                },
                NetworkSpawnFlags.DontDestroyOnLoad);

            if (obj == null)
            {
                Debug.LogError("[NetHorseSpawner] Failed to spawn NetHorse seat=" + seat);
                return;
            }

            if (CoopSessionStarter.Instance != null)
                obj.transform.SetParent(CoopSessionStarter.Instance.transform, true);

            Debug.Log($"[NetHorseSpawner] Spawned NetHorse seat={seat} id={obj.Id} SA={obj.HasStateAuthority}");
        }

        static void ResolveScenePose(int seat, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            var animal = seat == 1
                ? CoopGameController.FindPlayAnimal("Horse Unicorn")
                : CoopGameController.FindPlayAnimal("Horse Realistic");
            if (animal == null)
                return;
            pos = animal.transform.position;
            rot = animal.transform.rotation;
        }
    }
}
