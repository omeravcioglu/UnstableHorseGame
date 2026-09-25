using UnityEngine;

namespace Cali.UI
{
    /// <summary>
    /// Prefab seed: fills empty roots from the factory. Does not wipe customized prefabs.
    /// </summary>
    public class FantasyUiPrefabSeed : MonoBehaviour
    {
        public enum Kind
        {
            MainMenu,
            Pause,
            Settings,
            GameplayHud,
            AlterHorse,
            HostSetup,
            JoinBrowser
        }

        public Kind kind = Kind.GameplayHud;
        public bool buildOnAwake = true;

        void Awake()
        {
            if (buildOnAwake)
                EnsureBuilt();
        }

        public GameObject EnsureBuilt()
        {
            switch (kind)
            {
                case Kind.MainMenu:
                    if (GetComponent<CaliMainMenuController>() != null)
                        return FixScale(gameObject);
                    return ReplaceWith(FantasyUiFactory.BuildMainMenuRoot(), go =>
                    {
                        if (go.GetComponent<CaliMainMenuController>() == null)
                            go.AddComponent<CaliMainMenuController>();
                    });
                case Kind.Pause:
                    if (GetComponent<CaliPauseMenu>() != null)
                        return FixScale(gameObject);
                    return ReplaceWith(FantasyUiFactory.BuildPauseMenuRoot(), go =>
                    {
                        var c = go.AddComponent<CaliPauseMenu>();
                        c.Bind(go);
                    });
                case Kind.Settings:
                    if (GetComponent<CaliSettingsMenu>() != null)
                        return FixScale(gameObject);
                    return ReplaceWith(FantasyUiFactory.BuildSettingsRoot(), go =>
                    {
                        var c = go.AddComponent<CaliSettingsMenu>();
                        c.Bind(go);
                    });
                case Kind.GameplayHud:
                    if (GetComponent<CaliGameplayHud>() != null)
                        return FixScale(gameObject);
                    return ReplaceWith(FantasyUiFactory.BuildGameplayHudRoot(), go =>
                    {
                        var c = go.AddComponent<CaliGameplayHud>();
                        c.Bind(go);
                    });
                case Kind.AlterHorse:
                    if (GetComponent<CaliAlterHorsePanel>() != null)
                        return FixScale(gameObject);
                    return ReplaceWith(FantasyUiFactory.BuildAlterHorsePanel(), go =>
                    {
                        var c = go.AddComponent<CaliAlterHorsePanel>();
                        c.Bind(go);
                    });
                case Kind.HostSetup:
                    if (GetComponent<CaliHostSetupPanel>() != null)
                        return FixScale(gameObject);
                    return ReplaceWith(FantasyUiFactory.BuildHostSetupPanel(), go =>
                    {
                        var c = go.AddComponent<CaliHostSetupPanel>();
                        c.Bind(go);
                    });
                case Kind.JoinBrowser:
                    if (GetComponent<CaliJoinBrowserPanel>() != null)
                        return FixScale(gameObject);
                    return ReplaceWith(FantasyUiFactory.BuildJoinBrowserPanel(), go =>
                    {
                        var c = go.AddComponent<CaliJoinBrowserPanel>();
                        c.Bind(go);
                    });
            }

            return gameObject;
        }

        static GameObject FixScale(GameObject go)
        {
            if (go == null)
                return go;
            if (go.GetComponent<Canvas>() is Canvas c && c.renderMode == RenderMode.WorldSpace)
                return go;
            go.transform.localScale = Vector3.one;
            return go;
        }

        GameObject ReplaceWith(GameObject built, System.Action<GameObject> configure)
        {
            built.transform.SetParent(transform.parent, false);
            built.name = gameObject.name;
            built.transform.localScale = Vector3.one;
            configure?.Invoke(built);
            Destroy(gameObject);
            return built;
        }
    }
}
