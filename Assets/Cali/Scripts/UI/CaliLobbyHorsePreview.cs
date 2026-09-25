using Cali.Network;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.UI
{
    /// <summary>
    /// Lobby pad: host horse always visible; guest horse appears when player 2 joins.
    /// </summary>
    public class CaliLobbyHorsePreview : MonoBehaviour
    {
        public MAnimal horseA;
        public MAnimal horseB;

        void Awake()
        {
            if (horseA == null)
                horseA = FindNamed("Horse Realistic");
            if (horseB == null)
                horseB = FindNamed("Horse Unicorn");

            if (horseA != null)
                horseA.gameObject.SetActive(true);
            if (horseB != null)
                horseB.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            CoopSessionStarter.PlayersChanged -= Refresh;
            CoopSessionStarter.PlayersChanged += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            CoopSessionStarter.PlayersChanged -= Refresh;
        }

        void Refresh()
        {
            int count = CoopSessionStarter.Instance != null ? CoopSessionStarter.Instance.PlayerCount : 0;
            if (horseA != null && !horseA.gameObject.activeSelf)
                horseA.gameObject.SetActive(true);

            if (horseB == null)
                return;

            bool showB = count >= 2;
            if (horseB.gameObject.activeSelf != showB)
                horseB.gameObject.SetActive(showB);
        }

        static MAnimal FindNamed(string objectName)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                if (animals[i] != null && animals[i].gameObject.name == objectName)
                    return animals[i];
            }

            return null;
        }
    }
}
