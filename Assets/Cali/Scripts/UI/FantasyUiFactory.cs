using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Builds Fantasy RPG UI canvases from Artsystack Resources prefabs.
    /// </summary>
    public static class FantasyUiFactory
    {
        public const float RefWidth = 3840f;
        public const float RefHeight = 2160f;

        public static Canvas CreateCanvas(string name, int sortOrder = 0)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;
            // TMP needs these channels or text (and some packs) mis-render.
            canvas.additionalShaderChannels =
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.Normal |
                AdditionalCanvasShaderChannels.Tangent;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;

            var rt = go.GetComponent<RectTransform>();
            rt.localScale = Vector3.one;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            var all = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            EventSystem keep = null;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                if (keep == null)
                {
                    keep = all[i];
                    continue;
                }

                // Prefer an already-active EventSystem. MC_Demo_Day ships with a disabled one;
                // keeping that and destroying Fusion's live EventSystem makes every UI click die.
                bool keepLive = keep.gameObject.activeInHierarchy;
                bool otherLive = all[i].gameObject.activeInHierarchy;
                if (!keepLive && otherLive)
                {
                    Object.Destroy(keep.gameObject);
                    keep = all[i];
                    continue;
                }

                Object.Destroy(all[i].gameObject);
            }

            if (keep != null)
            {
                if (!keep.gameObject.activeSelf)
                    keep.gameObject.SetActive(true);
                keep.enabled = true;
                EnsureUiInputModule(keep.gameObject);
                return;
            }

            var es = new GameObject("EventSystem", typeof(EventSystem));
            EnsureUiInputModule(es);
        }

        /// <summary>
        /// Makes the whole button rect clickable: text must not steal hits; a full-rect pad receives clicks.
        /// </summary>
        public static void HardenButtonHits(GameObject buttonRoot)
        {
            if (buttonRoot == null) return;

            buttonRoot.transform.localScale = Vector3.one;

            foreach (var tmp in buttonRoot.GetComponentsInChildren<TMP_Text>(true))
                tmp.raycastTarget = false;

            foreach (var text in buttonRoot.GetComponentsInChildren<Text>(true))
                text.raycastTarget = false;

            var img = buttonRoot.GetComponent<Image>();
            if (img == null)
            {
                img = buttonRoot.AddComponent<Image>();
                img.color = new Color(0.55f, 0.35f, 0.15f, 1f);
            }

            // Prefer the rect, not a tight sprite mesh (those feel like a tiny bottom strip).
            img.useSpriteMesh = false;
            img.preserveAspect = false;
            img.raycastTarget = true;

            // Dedicated full-rect pad on top of visuals so every pixel of sizeDelta is clickable.
            var hitT = buttonRoot.transform.Find("HitPad");
            GameObject hitGo;
            if (hitT == null)
            {
                hitGo = new GameObject("HitPad", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                hitGo.transform.SetParent(buttonRoot.transform, false);
            }
            else
                hitGo = hitT.gameObject;

            StretchFull(hitGo.GetComponent<RectTransform>());
            hitGo.transform.SetAsLastSibling();
            var hitImg = hitGo.GetComponent<Image>();
            hitImg.sprite = null;
            hitImg.color = new Color(1f, 1f, 1f, 0f);
            hitImg.raycastTarget = true;

            var btn = buttonRoot.GetComponent<Button>();
            if (btn == null)
                btn = buttonRoot.AddComponent<Button>();
            btn.targetGraphic = img;
        }

        static void EnsureUiInputModule(GameObject esGo)
        {
            // Prefer Input System UI module with default actions (project uses the new Input System).
            var inputSysType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSysType != null)
            {
                var standalone = esGo.GetComponent<StandaloneInputModule>();
                if (standalone != null)
                    Object.Destroy(standalone);

                var module = esGo.GetComponent(inputSysType);
                if (module == null)
                    module = esGo.AddComponent(inputSysType);

                var assign = inputSysType.GetMethod("AssignDefaultActions", System.Type.EmptyTypes);
                assign?.Invoke(module, null);
                return;
            }

            if (esGo.GetComponent<StandaloneInputModule>() == null)
                esGo.AddComponent<StandaloneInputModule>();
        }

        public static GameObject InstantiateResource(string path, Transform parent)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[FantasyUiFactory] Missing Resources prefab: {path}");
                return null;
            }

            var inst = Object.Instantiate(prefab, parent, false);
            inst.name = prefab.name;
            return inst;
        }

        public static void StretchFull(RectTransform rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
        }

        public static void AnchorTopLeft(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        public static void AnchorTopRight(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        public static void AnchorBottomCenter(RectTransform rt, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }

        public static TMP_Text EnsureLabel(Transform parent, string name, string text, float fontSize = 42f)
        {
            var existing = parent.Find(name);
            TMP_Text tmp;
            if (existing != null)
            {
                tmp = existing.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    tmp.text = text;
                    tmp.raycastTarget = false;
                    return tmp;
                }
            }

            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -8f);
            rt.sizeDelta = new Vector2(0f, 60f);
            return tmp;
        }

        public static void SetButtonLabel(GameObject buttonRoot, string label)
        {
            if (buttonRoot == null) return;
            var tmp = buttonRoot.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
            {
                tmp.text = label;
                tmp.raycastTarget = false;
            }
        }

        public static Slider FindSlider(GameObject root)
        {
            return root != null ? root.GetComponentInChildren<Slider>(true) : null;
        }

        public static Image FindFillImage(GameObject heartRoot)
        {
            if (heartRoot == null) return null;
            var fill = heartRoot.transform.Find("Slider/Fill Area/Fill");
            if (fill != null)
            {
                var img = fill.GetComponent<Image>();
                if (img != null) return img;
            }

            foreach (var img in heartRoot.GetComponentsInChildren<Image>(true))
            {
                if (img != null && img.gameObject.name == "Fill")
                    return img;
            }

            return null;
        }

        public static Image FindAvatarIcon(GameObject avatarRoot)
        {
            if (avatarRoot == null) return null;
            var t = avatarRoot.transform.Find("Mask/Avatar-Icon");
            if (t == null)
                t = FindDeepChild(avatarRoot.transform, "Avatar-Icon");
            return t != null ? t.GetComponent<Image>() : null;
        }

        public static Transform FindDeepChild(Transform parent, string name)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name)
                    return c;
                var nested = FindDeepChild(c, name);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        public static Sprite LoadPortrait(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
                return null;

            var direct = Resources.Load<Sprite>($"Sprites/components/{spriteName}");
            if (direct != null)
                return direct;

            foreach (var s in Resources.LoadAll<Sprite>("Sprites/components"))
            {
                if (s != null && s.name == spriteName)
                    return s;
            }

            return null;
        }

        public static Sprite[] LoadPortraitChoices()
        {
            var names = new[] { "hero_02", "hero_03", "Avatar_BG" };
            var list = new System.Collections.Generic.List<Sprite>();
            foreach (var n in names)
            {
                var s = LoadPortrait(n);
                if (s != null)
                    list.Add(s);
            }

            if (list.Count == 0)
            {
                foreach (var s in Resources.LoadAll<Sprite>("Sprites/colored_icon/1000px"))
                {
                    if (s == null) continue;
                    list.Add(s);
                    if (list.Count >= 8)
                        break;
                }
            }

            return list.ToArray();
        }

        public static GameObject BuildMainMenuRoot()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas("MainMenuRoot", 10);
            var root = canvas.gameObject;

            // Background
            var bgGo = new GameObject("Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGo.transform.SetParent(root.transform, false);
            StretchFull(bgGo.GetComponent<RectTransform>());
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.raycastTarget = false;
            var bgSprite = Resources.Load<Sprite>("Sprites/Background/bg")
                           ?? Resources.Load<Sprite>("Sprites/Background/bg_2");
            if (bgSprite != null)
            {
                bgImg.sprite = bgSprite;
                bgImg.type = Image.Type.Simple;
                bgImg.preserveAspect = false;
            }
            else
                bgImg.color = new Color(0.08f, 0.06f, 0.12f, 1f);

            var dim = new GameObject("Dim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dim.transform.SetParent(root.transform, false);
            StretchFull(dim.GetComponent<RectTransform>());
            var dimImg = dim.GetComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.35f);
            dimImg.raycastTarget = false;

            var title = new GameObject("Title", typeof(RectTransform));
            title.transform.SetParent(root.transform, false);
            var titleRt = title.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 0.72f);
            titleRt.anchorMax = new Vector2(0.5f, 0.72f);
            titleRt.sizeDelta = new Vector2(1600f, 200f);
            var titleTmp = title.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "CALI";
            titleTmp.fontSize = 160f;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = new Color(1f, 0.92f, 0.7f, 1f);
            titleTmp.raycastTarget = false;

            var subtitle = new GameObject("Subtitle", typeof(RectTransform));
            subtitle.transform.SetParent(root.transform, false);
            var subRt = subtitle.GetComponent<RectTransform>();
            subRt.anchorMin = new Vector2(0.5f, 0.62f);
            subRt.anchorMax = new Vector2(0.5f, 0.62f);
            subRt.sizeDelta = new Vector2(1400f, 80f);
            var subTmp = subtitle.AddComponent<TextMeshProUGUI>();
            subTmp.text = "Horse Coop";
            subTmp.fontSize = 56f;
            subTmp.alignment = TextAlignmentOptions.Center;
            subTmp.color = new Color(0.9f, 0.85f, 0.75f, 0.9f);
            subTmp.raycastTarget = false;

            var buttons = new GameObject("Buttons", typeof(RectTransform));
            buttons.transform.SetParent(root.transform, false);
            var buttonsRt = buttons.GetComponent<RectTransform>();
            buttonsRt.anchorMin = new Vector2(0.5f, 0.42f);
            buttonsRt.anchorMax = new Vector2(0.5f, 0.42f);
            buttonsRt.sizeDelta = new Vector2(700f, 580f);

            // Prefer simple guaranteed-clickable buttons for the main menu (Artsystack TMP text can steal hits).
            string[] labels = { "Play", "Host Coop", "Join Coop", "Settings", "Quit" };
            string[] names = { "BtnPlay", "BtnHost", "BtnJoin", "BtnSettings", "BtnQuit" };
            for (int i = 0; i < labels.Length; i++)
            {
                var btn = CreateFallbackButton(buttons.transform, names[i], labels[i]);
                btn.name = names[i];
                var rt = btn.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 1f);
                    rt.anchorMax = new Vector2(0.5f, 1f);
                    rt.pivot = new Vector2(0.5f, 1f);
                    rt.anchoredPosition = new Vector2(0f, -i * 110f);
                    rt.sizeDelta = new Vector2(520f, 90f);
                }

                HardenButtonHits(btn);
            }

            root.transform.localScale = Vector3.one;
            return root;
        }

        public static GameObject BuildHostSetupPanel()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas("HostSetupRoot", 55);
            var root = canvas.gameObject;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            StretchFull(content.GetComponent<RectTransform>());

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            blocker.transform.SetParent(content.transform, false);
            StretchFull(blocker.GetComponent<RectTransform>());
            blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var panel = new GameObject("HostPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(content.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1100f, 920f);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.14f, 0.97f);

            var header = EnsureLabel(panel.transform, "HostTitle", "Host Coop", 64f);
            var headerRt = header.rectTransform;
            headerRt.anchorMin = headerRt.anchorMax = new Vector2(0.5f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -36f);
            headerRt.sizeDelta = new Vector2(900f, 80f);

            CreateLabeledTmpInput(panel.transform, "LobbyName", "Room Code (share with friends)", "MyLobby", new Vector2(0f, 220f), false);
            CreateRegionDropdown(panel.transform, "RegionDropdown", "Region (friends must match)", new Vector2(0f, 40f));
            CreateLabeledTmpInput(panel.transform, "Password", "Password (optional)", "", new Vector2(0f, -140f), true);

            var status = EnsureLabel(panel.transform, "StatusLabel", "Pick a fixed region — not Best — so friends can find you.", 28f);
            var statusRt = status.rectTransform;
            statusRt.anchorMin = statusRt.anchorMax = new Vector2(0.5f, 0f);
            statusRt.pivot = new Vector2(0.5f, 0f);
            statusRt.anchoredPosition = new Vector2(0f, 150f);
            statusRt.sizeDelta = new Vector2(900f, 50f);
            status.color = new Color(1f, 0.6f, 0.45f, 1f);

            var create = InstantiateResource(FantasyUiPaths.ButtonOrange, panel.transform)
                         ?? CreateFallbackButton(panel.transform, "BtnCreate", "Create Room");
            if (create != null)
            {
                create.name = "BtnCreate";
                SetButtonLabel(create, "Create Room");
                var rt = create.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.3f, 0f);
                rt.anchorMax = new Vector2(0.3f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(320f, 80f);
                HardenButtonHits(create);
            }

            var back = InstantiateResource(FantasyUiPaths.ButtonGrey, panel.transform)
                       ?? CreateFallbackButton(panel.transform, "BtnBack", "Back");
            if (back != null)
            {
                back.name = "BtnBack";
                SetButtonLabel(back, "Back");
                var rt = back.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.7f, 0f);
                rt.anchorMax = new Vector2(0.7f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(280f, 80f);
                HardenButtonHits(back);
            }

            content.SetActive(false);
            return root;
        }

        public static GameObject BuildJoinBrowserPanel()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas("JoinBrowserRoot", 55);
            var root = canvas.gameObject;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            StretchFull(content.GetComponent<RectTransform>());

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            blocker.transform.SetParent(content.transform, false);
            StretchFull(blocker.GetComponent<RectTransform>());
            blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var panel = new GameObject("JoinPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(content.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1400f, 1100f);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.14f, 0.97f);

            var header = EnsureLabel(panel.transform, "JoinTitle", "Join Coop", 64f);
            var headerRt = header.rectTransform;
            headerRt.anchorMin = headerRt.anchorMax = new Vector2(0.5f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -28f);
            headerRt.sizeDelta = new Vector2(1200f, 70f);

            CreateRegionDropdown(panel.transform, "RegionDropdown", "Region (must match host)", new Vector2(0f, 360f));
            CreateLabeledTmpInput(panel.transform, "RoomCode", "Room Code (host’s exact code)", "", new Vector2(0f, 240f), false);
            CreateLabeledTmpInput(panel.transform, "Password", "Password (if locked)", "", new Vector2(0f, 120f), true);

            var listHost = new GameObject("RoomList", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            listHost.transform.SetParent(panel.transform, false);
            var listRt = listHost.GetComponent<RectTransform>();
            listRt.anchorMin = new Vector2(0.5f, 0.5f);
            listRt.anchorMax = new Vector2(0.5f, 0.5f);
            listRt.anchoredPosition = new Vector2(0f, -80f);
            listRt.sizeDelta = new Vector2(1280f, 360f);
            listHost.GetComponent<Image>().color = new Color(0.05f, 0.04f, 0.08f, 0.85f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(listHost.transform, false);
            StretchFull(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().color = Color.white;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var contentList = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentList.transform.SetParent(viewport.transform, false);
            var clRt = contentList.GetComponent<RectTransform>();
            clRt.anchorMin = new Vector2(0f, 1f);
            clRt.anchorMax = new Vector2(1f, 1f);
            clRt.pivot = new Vector2(0.5f, 1f);
            clRt.anchoredPosition = Vector2.zero;
            clRt.sizeDelta = new Vector2(0f, 0f);
            var vlg = contentList.GetComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 8f;
            vlg.padding = new RectOffset(12, 12, 12, 12);
            var fitter = contentList.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = listHost.GetComponent<ScrollRect>();
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = clRt;
            scroll.horizontal = false;
            scroll.vertical = true;

            var colHeader = EnsureLabel(panel.transform, "ColumnHeader", "Or pick from list · Lobby                  Players     Region      Ping", 28f);
            var colRt = colHeader.rectTransform;
            colRt.anchorMin = colRt.anchorMax = new Vector2(0.5f, 0.5f);
            colRt.anchoredPosition = new Vector2(0f, 120f);
            colRt.sizeDelta = new Vector2(1200f, 36f);
            colHeader.alignment = TextAlignmentOptions.Left;
            colHeader.color = new Color(0.85f, 0.8f, 0.7f, 0.9f);

            var status = EnsureLabel(panel.transform, "StatusLabel", "Enter the host’s room code + same region, or browse the list.", 28f);
            var statusRt = status.rectTransform;
            statusRt.anchorMin = statusRt.anchorMax = new Vector2(0.5f, 0f);
            statusRt.pivot = new Vector2(0.5f, 0f);
            statusRt.anchoredPosition = new Vector2(0f, 150f);
            statusRt.sizeDelta = new Vector2(1200f, 50f);

            var refresh = InstantiateResource(FantasyUiPaths.ButtonGrey, panel.transform)
                          ?? CreateFallbackButton(panel.transform, "BtnRefresh", "Refresh");
            if (refresh != null)
            {
                refresh.name = "BtnRefresh";
                SetButtonLabel(refresh, "Refresh");
                var rt = refresh.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.12f, 0f);
                rt.anchorMax = new Vector2(0.12f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(200f, 80f);
                HardenButtonHits(refresh);
            }

            var joinCode = CreateFallbackButton(panel.transform, "BtnJoinCode", "Join by Code");
            joinCode.name = "BtnJoinCode";
            var joinCodeRt = joinCode.GetComponent<RectTransform>();
            joinCodeRt.anchorMin = new Vector2(0.38f, 0f);
            joinCodeRt.anchorMax = new Vector2(0.38f, 0f);
            joinCodeRt.pivot = new Vector2(0.5f, 0f);
            joinCodeRt.anchoredPosition = new Vector2(0f, 40f);
            joinCodeRt.sizeDelta = new Vector2(280f, 80f);
            HardenButtonHits(joinCode);

            var join = InstantiateResource(FantasyUiPaths.ButtonOrange, panel.transform)
                       ?? CreateFallbackButton(panel.transform, "BtnJoinRoom", "Join Selected");
            if (join != null)
            {
                join.name = "BtnJoinRoom";
                SetButtonLabel(join, "Join Selected");
                var rt = join.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.64f, 0f);
                rt.anchorMax = new Vector2(0.64f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(280f, 80f);
                HardenButtonHits(join);
            }

            var back = InstantiateResource(FantasyUiPaths.ButtonGrey, panel.transform)
                       ?? CreateFallbackButton(panel.transform, "BtnBack", "Back");
            if (back != null)
            {
                back.name = "BtnBack";
                SetButtonLabel(back, "Back");
                var rt = back.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.9f, 0f);
                rt.anchorMax = new Vector2(0.9f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(200f, 80f);
                HardenButtonHits(back);
            }

            content.SetActive(false);
            return root;
        }

        public static GameObject CreateRoomRow(Transform parent, string lobbyName, string players, string region, string ping, bool locked)
        {
            var row = new GameObject($"Room_{lobbyName}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            row.GetComponent<LayoutElement>().preferredHeight = 72f;
            row.GetComponent<Image>().color = new Color(0.18f, 0.16f, 0.22f, 1f);

            var label = EnsureLabel(row.transform, "RowLabel", "", 30f);
            StretchFull(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(16f, 4f);
            label.rectTransform.offsetMax = new Vector2(-16f, -4f);
            label.alignment = TextAlignmentOptions.Left;
            string lockMark = locked ? "  [LOCK]" : "";
            label.text = $"{lobbyName,-24}  {players,-10}  {region,-10}  {ping}{lockMark}";
            return row;
        }

        static TMP_InputField CreateLabeledTmpInput(Transform parent, string name, string label, string placeholder, Vector2 anchoredPos, bool isPassword)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = anchoredPos;
            rowRt.sizeDelta = new Vector2(900f, 110f);

            var labelTmp = EnsureLabel(row.transform, "Label", label, 32f);
            var lrt = labelTmp.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = new Vector2(0f, 0f);
            lrt.sizeDelta = new Vector2(0f, 36f);
            labelTmp.alignment = TextAlignmentOptions.Left;

            var inputGo = InstantiateResource(FantasyUiPaths.InputField, row.transform);
            TMP_InputField input;
            if (inputGo != null)
            {
                inputGo.name = "Input";
                var irt = inputGo.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0f, 0f);
                irt.anchorMax = new Vector2(1f, 0f);
                irt.pivot = new Vector2(0.5f, 0f);
                irt.anchoredPosition = new Vector2(0f, 0f);
                irt.sizeDelta = new Vector2(0f, 70f);
                input = inputGo.GetComponentInChildren<TMP_InputField>(true) ?? inputGo.GetComponent<TMP_InputField>();
            }
            else
            {
                inputGo = new GameObject("Input", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                inputGo.transform.SetParent(row.transform, false);
                var irt = inputGo.GetComponent<RectTransform>();
                irt.anchorMin = new Vector2(0f, 0f);
                irt.anchorMax = new Vector2(1f, 0f);
                irt.pivot = new Vector2(0.5f, 0f);
                irt.anchoredPosition = Vector2.zero;
                irt.sizeDelta = new Vector2(0f, 70f);
                inputGo.GetComponent<Image>().color = new Color(0.2f, 0.18f, 0.25f, 1f);
                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(inputGo.transform, false);
                StretchFull(textGo.GetComponent<RectTransform>());
                textGo.GetComponent<RectTransform>().offsetMin = new Vector2(12f, 8f);
                textGo.GetComponent<RectTransform>().offsetMax = new Vector2(-12f, -8f);
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 36f;
                input = inputGo.AddComponent<TMP_InputField>();
                input.textComponent = tmp;
                input.textViewport = textGo.GetComponent<RectTransform>();
            }

            if (input != null)
            {
                input.text = placeholder ?? "";
                if (isPassword)
                    input.contentType = TMP_InputField.ContentType.Password;
            }

            return input;
        }

        static TMP_Dropdown CreateRegionDropdown(Transform parent, string name, string label, Vector2 anchoredPos)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = anchoredPos;
            rowRt.sizeDelta = new Vector2(900f, 110f);

            var labelTmp = EnsureLabel(row.transform, "Label", label, 32f);
            var lrt = labelTmp.rectTransform;
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(1f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.anchoredPosition = Vector2.zero;
            lrt.sizeDelta = new Vector2(0f, 36f);
            labelTmp.alignment = TextAlignmentOptions.Left;

            var ddGo = new GameObject("Dropdown", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_Dropdown));
            ddGo.transform.SetParent(row.transform, false);
            var ddRt = ddGo.GetComponent<RectTransform>();
            ddRt.anchorMin = new Vector2(0f, 0f);
            ddRt.anchorMax = new Vector2(1f, 0f);
            ddRt.pivot = new Vector2(0.5f, 0f);
            ddRt.anchoredPosition = Vector2.zero;
            ddRt.sizeDelta = new Vector2(0f, 70f);
            ddGo.GetComponent<Image>().color = new Color(0.22f, 0.2f, 0.28f, 1f);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(ddGo.transform, false);
            StretchFull(labelGo.GetComponent<RectTransform>());
            labelGo.GetComponent<RectTransform>().offsetMin = new Vector2(16f, 8f);
            labelGo.GetComponent<RectTransform>().offsetMax = new Vector2(-40f, -8f);
            var caption = labelGo.AddComponent<TextMeshProUGUI>();
            caption.fontSize = 34f;
            caption.alignment = TextAlignmentOptions.Left;

            var template = new GameObject("Template", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            template.transform.SetParent(ddGo.transform, false);
            var tRt = template.GetComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0f);
            tRt.anchorMax = new Vector2(1f, 0f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = new Vector2(0f, 2f);
            tRt.sizeDelta = new Vector2(0f, 280f);
            template.GetComponent<Image>().color = new Color(0.15f, 0.13f, 0.2f, 1f);
            template.SetActive(false);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(template.transform, false);
            StretchFull(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<Image>().color = Color.white;
            viewport.GetComponent<Mask>().showMaskGraphic = false;

            var itemContent = new GameObject("Content", typeof(RectTransform));
            itemContent.transform.SetParent(viewport.transform, false);
            var icRt = itemContent.GetComponent<RectTransform>();
            icRt.anchorMin = new Vector2(0f, 1f);
            icRt.anchorMax = new Vector2(1f, 1f);
            icRt.pivot = new Vector2(0.5f, 1f);
            icRt.sizeDelta = new Vector2(0f, 56f);

            var item = new GameObject("Item", typeof(RectTransform), typeof(Toggle));
            item.transform.SetParent(itemContent.transform, false);
            StretchFull(item.GetComponent<RectTransform>());
            item.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 56f);

            var itemBg = new GameObject("Item Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            itemBg.transform.SetParent(item.transform, false);
            StretchFull(itemBg.GetComponent<RectTransform>());
            itemBg.GetComponent<Image>().color = new Color(0.25f, 0.22f, 0.32f, 1f);

            var itemLabel = new GameObject("Item Label", typeof(RectTransform));
            itemLabel.transform.SetParent(item.transform, false);
            StretchFull(itemLabel.GetComponent<RectTransform>());
            itemLabel.GetComponent<RectTransform>().offsetMin = new Vector2(16f, 4f);
            itemLabel.GetComponent<RectTransform>().offsetMax = new Vector2(-16f, -4f);
            var itemTmp = itemLabel.AddComponent<TextMeshProUGUI>();
            itemTmp.fontSize = 32f;

            var toggle = item.GetComponent<Toggle>();
            toggle.targetGraphic = itemBg.GetComponent<Image>();
            toggle.isOn = true;

            var scroll = template.GetComponent<ScrollRect>();
            scroll.content = icRt;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.horizontal = false;

            var dropdown = ddGo.GetComponent<TMP_Dropdown>();
            dropdown.captionText = caption;
            dropdown.itemText = itemTmp;
            dropdown.template = tRt;
            dropdown.options.Clear();
            foreach (var opt in Cali.Network.CaliPhotonRegions.Options)
                dropdown.options.Add(new TMP_Dropdown.OptionData(opt.Label));
            dropdown.value = 0;
            dropdown.RefreshShownValue();
            return dropdown;
        }

        public static GameObject BuildPauseMenuRoot()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas("PauseMenuRoot", CaliPauseMenu.PauseSortOrder);
            var root = canvas.gameObject;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            StretchFull(content.GetComponent<RectTransform>());

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            blocker.transform.SetParent(content.transform, false);
            StretchFull(blocker.GetComponent<RectTransform>());
            var blockerImg = blocker.GetComponent<Image>();
            blockerImg.color = new Color(0f, 0f, 0f, 0.55f);
            blockerImg.raycastTarget = true;

            var panel = InstantiateResource(FantasyUiPaths.PopUp, content.transform);
            if (panel == null)
            {
                panel = new GameObject("PausePanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panel.transform.SetParent(content.transform, false);
                panel.GetComponent<Image>().color = new Color(0.12f, 0.1f, 0.16f, 0.95f);
            }

            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(900f, 820f);

            EnsureLabel(panel.transform, "PauseTitle", "Paused", 72f);
            var title = panel.transform.Find("PauseTitle")?.GetComponent<RectTransform>();
            if (title != null)
            {
                title.anchorMin = new Vector2(0.5f, 1f);
                title.anchorMax = new Vector2(0.5f, 1f);
                title.pivot = new Vector2(0.5f, 1f);
                title.anchoredPosition = new Vector2(0f, -40f);
                title.sizeDelta = new Vector2(800f, 90f);
            }

            string[] labels = { "Resume", "Settings", "Add Save Point", "Reset Save", "Quit to Menu" };
            string[] names = { "BtnResume", "BtnSettings", "BtnAddSave", "BtnResetSave", "BtnQuit" };
            for (int i = 0; i < labels.Length; i++)
            {
                var btn = InstantiateResource(FantasyUiPaths.ButtonGreen, panel.transform)
                          ?? CreateFallbackButton(panel.transform, names[i], labels[i]);
                if (btn == null) continue;
                btn.name = names[i];
                SetButtonLabel(btn, labels[i]);
                HardenButtonHits(btn);
                var rt = btn.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0f, 200f - i * 92f);
                    rt.sizeDelta = new Vector2(480f, 82f);
                }
            }

            content.SetActive(false);
            return root;
        }

        public static GameObject BuildSettingsRoot()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas("SettingsRoot", 50);
            var root = canvas.gameObject;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            StretchFull(content.GetComponent<RectTransform>());

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            blocker.transform.SetParent(content.transform, false);
            StretchFull(blocker.GetComponent<RectTransform>());
            blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            var panel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(content.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1100f, 900f);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.14f, 0.96f);

            var header = EnsureLabel(panel.transform, "SettingsTitle", "Settings", 64f);
            var headerRt = header.rectTransform;
            headerRt.anchorMin = new Vector2(0.5f, 1f);
            headerRt.anchorMax = new Vector2(0.5f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -36f);
            headerRt.sizeDelta = new Vector2(900f, 80f);

            CreateLabeledSlider(panel.transform, "MasterVolume", "Master Volume", new Vector2(0f, 220f));
            CreateLabeledSlider(panel.transform, "MusicVolume", "Music", new Vector2(0f, 60f));
            CreateLabeledSlider(panel.transform, "SfxVolume", "SFX", new Vector2(0f, -100f));

            var fullscreen = CreateToggleRow(panel.transform, "Fullscreen", "Fullscreen", new Vector2(0f, -260f));
            var quality = CreateLabeledSlider(panel.transform, "Quality", "Quality", new Vector2(0f, -400f));
            if (quality != null)
            {
                quality.wholeNumbers = true;
                quality.minValue = 0f;
                quality.maxValue = Mathf.Max(0, QualitySettings.names.Length - 1);
            }

            var close = InstantiateResource(FantasyUiPaths.ButtonGrey, panel.transform)
                        ?? CreateFallbackButton(panel.transform, "BtnClose", "Close");
            if (close != null)
            {
                close.name = "BtnClose";
                SetButtonLabel(close, "Close");
                var rt = close.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(360f, 80f);
                HardenButtonHits(close);
            }

            if (fullscreen != null)
                fullscreen.name = "FullscreenToggle";

            content.SetActive(false);
            return root;
        }

        public static GameObject BuildWaitingRoomRoot()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas("WaitingRoomRoot", 70);
            var root = canvas.gameObject;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            StretchFull(content.GetComponent<RectTransform>());

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            blocker.transform.SetParent(content.transform, false);
            StretchFull(blocker.GetComponent<RectTransform>());
            var blockerImg = blocker.GetComponent<Image>();
            blockerImg.color = new Color(0f, 0f, 0f, 0.55f);
            blockerImg.raycastTarget = true;

            var panel = new GameObject("WaitingPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(content.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1000f, 620f);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.14f, 0.97f);

            var header = EnsureLabel(panel.transform, "WaitingTitle", "Waiting Room", 64f);
            var headerRt = header.rectTransform;
            headerRt.anchorMin = headerRt.anchorMax = new Vector2(0.5f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -40f);
            headerRt.sizeDelta = new Vector2(900f, 80f);

            var lobby = EnsureLabel(panel.transform, "LobbyLabel", "Room code: —", 36f);
            var lobbyRt = lobby.rectTransform;
            lobbyRt.anchorMin = lobbyRt.anchorMax = new Vector2(0.5f, 0.5f);
            lobbyRt.pivot = new Vector2(0.5f, 0.5f);
            lobbyRt.anchoredPosition = new Vector2(0f, 120f);
            lobbyRt.sizeDelta = new Vector2(860f, 48f);

            var players = EnsureLabel(panel.transform, "PlayersLabel", "Players: 0 / 2", 44f);
            var playersRt = players.rectTransform;
            playersRt.anchorMin = playersRt.anchorMax = new Vector2(0.5f, 0.5f);
            playersRt.pivot = new Vector2(0.5f, 0.5f);
            playersRt.anchoredPosition = new Vector2(0f, 40f);
            playersRt.sizeDelta = new Vector2(860f, 56f);
            players.fontStyle = FontStyles.Bold;

            var hint = EnsureLabel(panel.transform, "HintLabel", "Waiting for another player…", 32f);
            var hintRt = hint.rectTransform;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
            hintRt.pivot = new Vector2(0.5f, 0.5f);
            hintRt.anchoredPosition = new Vector2(0f, -40f);
            hintRt.sizeDelta = new Vector2(860f, 48f);
            hint.color = new Color(0.9f, 0.85f, 0.75f, 0.95f);

            var start = CreateFallbackButton(panel.transform, "BtnStart", "Start Match");
            start.name = "BtnStart";
            var startRt = start.GetComponent<RectTransform>();
            startRt.anchorMin = new Vector2(0.3f, 0f);
            startRt.anchorMax = new Vector2(0.3f, 0f);
            startRt.pivot = new Vector2(0.5f, 0f);
            startRt.anchoredPosition = new Vector2(0f, 48f);
            startRt.sizeDelta = new Vector2(320f, 80f);
            HardenButtonHits(start);

            var leave = CreateFallbackButton(panel.transform, "BtnLeave", "Leave");
            leave.name = "BtnLeave";
            var leaveRt = leave.GetComponent<RectTransform>();
            leaveRt.anchorMin = new Vector2(0.7f, 0f);
            leaveRt.anchorMax = new Vector2(0.7f, 0f);
            leaveRt.pivot = new Vector2(0.5f, 0f);
            leaveRt.anchoredPosition = new Vector2(0f, 48f);
            leaveRt.sizeDelta = new Vector2(280f, 80f);
            HardenButtonHits(leave);

            content.SetActive(false);
            return root;
        }

        public static GameObject BuildGameplayHudRoot()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas("GameplayHudRoot", 20);
            var root = canvas.gameObject;

            // Horse A — top left
            var slotA = new GameObject("HorseSlotA", typeof(RectTransform));
            slotA.transform.SetParent(root.transform, false);
            var slotARt = slotA.GetComponent<RectTransform>();
            AnchorTopLeft(slotARt, new Vector2(40f, -40f), new Vector2(520f, 280f));

            var barA = InstantiateResource(FantasyUiPaths.HealthBar, slotA.transform)
                       ?? InstantiateResource(FantasyUiPaths.HeartProgress, slotA.transform);
            if (barA != null)
            {
                barA.name = "HealthBarA";
                var hrt = barA.GetComponent<RectTransform>();
                AnchorTopLeft(hrt, new Vector2(0f, 0f), new Vector2(420f, 36f));
            }

            var avatarA = InstantiateResource(FantasyUiPaths.AvatarFrame, slotA.transform);
            if (avatarA != null)
            {
                avatarA.name = "AvatarA";
                var art = avatarA.GetComponent<RectTransform>();
                AnchorTopLeft(art, new Vector2(0f, -50f), new Vector2(180f, 180f));
                EnsureLabel(avatarA.transform, "NameLabel", "Ashmane", 36f);
                if (avatarA.GetComponent<Button>() == null)
                    avatarA.AddComponent<Button>();
            }

            // Horse B — top right
            var slotB = new GameObject("HorseSlotB", typeof(RectTransform));
            slotB.transform.SetParent(root.transform, false);
            var slotBRt = slotB.GetComponent<RectTransform>();
            AnchorTopRight(slotBRt, new Vector2(-40f, -40f), new Vector2(520f, 280f));

            var barB = InstantiateResource(FantasyUiPaths.HealthBar, slotB.transform)
                       ?? InstantiateResource(FantasyUiPaths.HeartProgress, slotB.transform);
            if (barB != null)
            {
                barB.name = "HealthBarB";
                var hrt = barB.GetComponent<RectTransform>();
                AnchorTopRight(hrt, new Vector2(0f, 0f), new Vector2(420f, 36f));
            }

            var avatarB = InstantiateResource(FantasyUiPaths.AvatarFrame, slotB.transform);
            if (avatarB != null)
            {
                avatarB.name = "AvatarB";
                var art = avatarB.GetComponent<RectTransform>();
                AnchorTopRight(art, new Vector2(0f, -50f), new Vector2(180f, 180f));
                EnsureLabel(avatarB.transform, "NameLabel", "Starfall", 36f);
                if (avatarB.GetComponent<Button>() == null)
                    avatarB.AddComponent<Button>();
            }

            // XP readout — bottom center
            var xpGo = new GameObject("XpPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            xpGo.transform.SetParent(root.transform, false);
            var xpRt = xpGo.GetComponent<RectTransform>();
            AnchorBottomCenter(xpRt, new Vector2(0f, 36f), new Vector2(720f, 70f));
            xpGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            var xpLabel = EnsureLabel(xpGo.transform, "XpLabel", "Level 1   XP 0/50", 40f);
            var xpLabelRt = xpLabel.rectTransform;
            StretchFull(xpLabelRt);
            xpLabel.alignment = TextAlignmentOptions.Center;

            var localBadge = new GameObject("LocalSeatHighlightA", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            localBadge.transform.SetParent(slotA.transform, false);
            var lbRt = localBadge.GetComponent<RectTransform>();
            AnchorTopLeft(lbRt, new Vector2(-12f, 12f), new Vector2(200f, 210f));
            var lbImg = localBadge.GetComponent<Image>();
            lbImg.color = new Color(1f, 0.85f, 0.3f, 0.35f);
            lbImg.raycastTarget = false;
            localBadge.SetActive(false);

            var localBadgeB = new GameObject("LocalSeatHighlightB", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            localBadgeB.transform.SetParent(slotB.transform, false);
            var lbBRt = localBadgeB.GetComponent<RectTransform>();
            AnchorTopRight(lbBRt, new Vector2(12f, 12f), new Vector2(200f, 210f));
            var lbBImg = localBadgeB.GetComponent<Image>();
            lbBImg.color = new Color(1f, 0.85f, 0.3f, 0.35f);
            lbBImg.raycastTarget = false;
            localBadgeB.SetActive(false);

            return root;
        }

        public static GameObject BuildAlterHorsePanel()
        {
            EnsureEventSystem();
            var canvas = CreateCanvas("AlterHorseRoot", 60);
            var root = canvas.gameObject;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            StretchFull(content.GetComponent<RectTransform>());

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            blocker.transform.SetParent(content.transform, false);
            StretchFull(blocker.GetComponent<RectTransform>());
            blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var panel = new GameObject("AlterPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(content.transform, false);
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(1000f, 780f);
            panel.GetComponent<Image>().color = new Color(0.1f, 0.09f, 0.14f, 0.97f);

            var title = EnsureLabel(panel.transform, "AlterTitle", "Alter Horse", 56f);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -30f);
            titleRt.sizeDelta = new Vector2(800f, 70f);

            var nameLabel = EnsureLabel(panel.transform, "NamePrompt", "Name", 36f);
            var namePromptRt = nameLabel.rectTransform;
            namePromptRt.anchorMin = namePromptRt.anchorMax = new Vector2(0.5f, 1f);
            namePromptRt.pivot = new Vector2(0.5f, 1f);
            namePromptRt.anchoredPosition = new Vector2(0f, -130f);
            namePromptRt.sizeDelta = new Vector2(700f, 40f);

            var inputGo = InstantiateResource(FantasyUiPaths.InputField, panel.transform);
            TMP_InputField input;
            if (inputGo != null)
            {
                inputGo.name = "NameInput";
                var irt = inputGo.GetComponent<RectTransform>();
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0f, -180f);
                irt.sizeDelta = new Vector2(700f, 90f);
                input = inputGo.GetComponentInChildren<TMP_InputField>(true);
            }
            else
            {
                inputGo = new GameObject("NameInput", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                inputGo.transform.SetParent(panel.transform, false);
                var irt = inputGo.GetComponent<RectTransform>();
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 1f);
                irt.anchoredPosition = new Vector2(0f, -180f);
                irt.sizeDelta = new Vector2(700f, 90f);
                inputGo.GetComponent<Image>().color = new Color(0.2f, 0.18f, 0.25f, 1f);
                var textGo = new GameObject("Text", typeof(RectTransform));
                textGo.transform.SetParent(inputGo.transform, false);
                StretchFull(textGo.GetComponent<RectTransform>());
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 40f;
                input = inputGo.AddComponent<TMP_InputField>();
                input.textComponent = tmp;
                input.textViewport = textGo.GetComponent<RectTransform>();
            }

            var portraits = new GameObject("PortraitRow", typeof(RectTransform));
            portraits.transform.SetParent(panel.transform, false);
            var prt = portraits.GetComponent<RectTransform>();
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = new Vector2(0f, -40f);
            prt.sizeDelta = new Vector2(860f, 200f);

            var save = InstantiateResource(FantasyUiPaths.ButtonOrange, panel.transform)
                       ?? CreateFallbackButton(panel.transform, "BtnSave", "Save");
            if (save != null)
            {
                save.name = "BtnSave";
                SetButtonLabel(save, "Save");
                var rt = save.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.3f, 0f);
                rt.anchorMax = new Vector2(0.3f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(280f, 80f);
            }

            var cancel = InstantiateResource(FantasyUiPaths.ButtonGrey, panel.transform)
                         ?? CreateFallbackButton(panel.transform, "BtnCancel", "Cancel");
            if (cancel != null)
            {
                cancel.name = "BtnCancel";
                SetButtonLabel(cancel, "Cancel");
                var rt = cancel.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.7f, 0f);
                rt.anchorMax = new Vector2(0.7f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 40f);
                rt.sizeDelta = new Vector2(280f, 80f);
            }

            content.SetActive(false);
            return root;
        }

        static Slider CreateLabeledSlider(Transform parent, string name, string label, Vector2 anchoredPos)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = anchoredPos;
            rowRt.sizeDelta = new Vector2(900f, 100f);

            var labelTmp = EnsureLabel(row.transform, "Label", label, 36f);
            var lrt = labelTmp.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0.5f);
            lrt.anchorMax = new Vector2(0f, 0.5f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.anchoredPosition = new Vector2(20f, 24f);
            lrt.sizeDelta = new Vector2(400f, 40f);
            labelTmp.alignment = TextAlignmentOptions.Left;

            var sliderGo = InstantiateResource("Prefabs/Common/Progress/Slider", row.transform);
            Slider slider;
            if (sliderGo != null)
            {
                sliderGo.name = "Slider";
                var srt = sliderGo.GetComponent<RectTransform>();
                srt.anchorMin = new Vector2(0f, 0f);
                srt.anchorMax = new Vector2(1f, 0f);
                srt.pivot = new Vector2(0.5f, 0f);
                srt.anchoredPosition = new Vector2(0f, 8f);
                srt.sizeDelta = new Vector2(-40f, 40f);
                slider = sliderGo.GetComponentInChildren<Slider>(true) ?? sliderGo.GetComponent<Slider>();
            }
            else
            {
                sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
                sliderGo.transform.SetParent(row.transform, false);
                var srt = sliderGo.GetComponent<RectTransform>();
                StretchFull(srt);
                srt.offsetMin = new Vector2(20f, 8f);
                srt.offsetMax = new Vector2(-20f, -40f);
                slider = sliderGo.GetComponent<Slider>();
            }

            if (slider != null)
            {
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.value = 1f;
            }

            return slider;
        }

        static Toggle CreateToggleRow(Transform parent, string name, string label, Vector2 anchoredPos)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var rowRt = row.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowRt.anchoredPosition = anchoredPos;
            rowRt.sizeDelta = new Vector2(900f, 80f);

            var labelTmp = EnsureLabel(row.transform, "Label", label, 36f);
            var lrt = labelTmp.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0.5f);
            lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = new Vector2(20f, -20f);
            lrt.offsetMax = new Vector2(0f, 20f);
            labelTmp.alignment = TextAlignmentOptions.Left;

            var toggleGo = new GameObject("Toggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
            toggleGo.transform.SetParent(row.transform, false);
            var trt = toggleGo.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(1f, 0.5f);
            trt.pivot = new Vector2(1f, 0.5f);
            trt.anchoredPosition = new Vector2(-40f, 0f);
            trt.sizeDelta = new Vector2(64f, 64f);
            var bg = toggleGo.GetComponent<Image>();
            bg.color = new Color(0.25f, 0.22f, 0.3f, 1f);
            var checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            checkGo.transform.SetParent(toggleGo.transform, false);
            StretchFull(checkGo.GetComponent<RectTransform>());
            checkGo.GetComponent<RectTransform>().offsetMin = new Vector2(8f, 8f);
            checkGo.GetComponent<RectTransform>().offsetMax = new Vector2(-8f, -8f);
            var checkImg = checkGo.GetComponent<Image>();
            checkImg.color = new Color(0.4f, 0.9f, 0.5f, 1f);
            var toggle = toggleGo.GetComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = checkImg;
            toggle.isOn = Screen.fullScreen;
            return toggle;
        }

        public static GameObject CreateFallbackButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.55f, 0.35f, 0.15f, 1f);
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var textGo = new GameObject("Text (TMP)", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            StretchFull(textGo.GetComponent<RectTransform>());
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 40f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            HardenButtonHits(go);
            return go;
        }
    }
}
