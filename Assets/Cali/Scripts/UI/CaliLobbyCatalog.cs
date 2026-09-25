using UnityEngine;

namespace Cali.UI
{
    [CreateAssetMenu(menuName = "Cali/Lobby Catalog", fileName = "CaliLobbyCatalog")]
    public class CaliLobbyCatalog : ScriptableObject
    {
        public const string ResourcesPath = "Lobby/CaliLobbyCatalog";

        public GameObject horseAPrefab;
        public GameObject horseBPrefab;

        public static CaliLobbyCatalog Get()
        {
            return Resources.Load<CaliLobbyCatalog>(ResourcesPath);
        }
    }
}
