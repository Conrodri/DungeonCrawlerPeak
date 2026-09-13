using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// The real entry point for an actual launch (not the Dungeon/Generate Floor Editor menu, which is
// a dev-only preview shortcut that bakes a floor straight into the saved scene). Lives on its own
// GameObject at the scene root, outside DungeonRoot, so it survives DungeonGenerator.Build()
// destroying/recreating that container. Nothing generates until the player picks a menu option.
public class MainMenuController : MonoBehaviour
{
    GameObject menuCanvas;
    GameObject settingsPanel;

    void Start()
    {
        // A leftover DungeonRoot only exists here because Dungeon/Generate Floor (an Editor-only
        // preview shortcut) baked one into the scene - a real launch always starts at the menu.
        // DestroyImmediate, not Destroy: Destroy is deferred to end of frame, so BuildUI()'s
        // GameObject.Find("EventSystem") check right below would still find the old root's
        // EventSystem (about to be destroyed along with it), skip creating a new one, and leave
        // the scene with none at all once the deferred destroy actually runs - nothing clickable.
        GameObject existingRoot = GameObject.Find("DungeonRoot");
        if (existingRoot != null) DestroyImmediate(existingRoot);

        BuildUI();
    }

    void BuildUI()
    {
        // Required for the menu's own buttons - DungeonGenerator normally creates this, but that
        // only happens once a floor is actually generated, which is exactly what hasn't happened yet.
        if (GameObject.Find("EventSystem") == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        menuCanvas = new GameObject("MainMenuCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        menuCanvas.transform.SetParent(transform);
        menuCanvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        GameObject panel = new GameObject("Panel", typeof(Image));
        panel.transform.SetParent(menuCanvas.transform, false);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.06f, 0.97f);
        RectTransform bgRect = bg.rectTransform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        Font font = Font.CreateDynamicFontFromOSFont("Arial", 28);

        GameObject titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(panel.transform, false);
        Text title = titleGO.GetComponent<Text>();
        title.text = "DungeonCrawlerPeak";
        title.font = font;
        title.fontSize = 56;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -100f);
        titleRect.sizeDelta = new Vector2(800f, 100f);

        float y = -260f;
        Button continueButton = CreateButton(panel.transform, "Continuer", font, y, OnContinue);
        bool hasSave = SaveManager.HasSave();
        continueButton.interactable = hasSave;
        if (!hasSave) continueButton.GetComponentInChildren<Text>().color = new Color(1f, 1f, 1f, 0.35f);

        y -= 80f;
        CreateButton(panel.transform, "Nouvelle Partie", font, y, OnNewGame);

        y -= 80f;
        CreateButton(panel.transform, "Parametres", font, y, OnToggleSettings);

        y -= 80f;
        CreateButton(panel.transform, "Quitter", font, y, OnQuit);

        BuildSettingsPanel(menuCanvas.transform, font);
    }

    Button CreateButton(Transform parent, string label, Font font, float y, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(label + "Button", typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = new Color(0.2f, 0.2f, 0.24f, 0.9f);
        RectTransform rect = img.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(320f, 64f);

        GameObject textGO = new GameObject("Text", typeof(Text));
        textGO.transform.SetParent(go.transform, false);
        Text text = textGO.GetComponent<Text>();
        text.text = label;
        text.font = font;
        text.fontSize = 28;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);
        return button;
    }

    void BuildSettingsPanel(Transform parent, Font font)
    {
        settingsPanel = new GameObject("SettingsPanel", typeof(Image));
        settingsPanel.transform.SetParent(parent, false);
        Image bg = settingsPanel.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.98f);
        RectTransform bgRect = bg.rectTransform;
        bgRect.anchorMin = bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        bgRect.sizeDelta = new Vector2(420f, 260f);

        GameObject titleGO = new GameObject("Title", typeof(Text));
        titleGO.transform.SetParent(settingsPanel.transform, false);
        Text title = titleGO.GetComponent<Text>();
        title.text = "Parametres";
        title.font = font;
        title.fontSize = 30;
        title.alignment = TextAnchor.MiddleCenter;
        title.color = Color.white;
        RectTransform titleRect = title.rectTransform;
        titleRect.anchorMin = titleRect.anchorMax = new Vector2(0.5f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.anchoredPosition = new Vector2(0f, -20f);
        titleRect.sizeDelta = new Vector2(380f, 50f);

        GameObject volumeLabelGO = new GameObject("VolumeLabel", typeof(Text));
        volumeLabelGO.transform.SetParent(settingsPanel.transform, false);
        Text volumeLabel = volumeLabelGO.GetComponent<Text>();
        volumeLabel.text = "Volume";
        volumeLabel.font = font;
        volumeLabel.fontSize = 22;
        volumeLabel.alignment = TextAnchor.MiddleLeft;
        volumeLabel.color = Color.white;
        RectTransform volumeLabelRect = volumeLabel.rectTransform;
        volumeLabelRect.anchorMin = volumeLabelRect.anchorMax = new Vector2(0.5f, 1f);
        volumeLabelRect.pivot = new Vector2(0.5f, 1f);
        volumeLabelRect.anchoredPosition = new Vector2(-140f, -90f);
        volumeLabelRect.sizeDelta = new Vector2(120f, 40f);

        // No music/SFX exists in the project yet - this at least wires AudioListener.volume for
        // real (not a dead placeholder), ready for whenever a first sound is actually added.
        GameObject sliderGO = new GameObject("VolumeSlider", typeof(Slider));
        sliderGO.transform.SetParent(settingsPanel.transform, false);
        Slider slider = sliderGO.GetComponent<Slider>();
        RectTransform sliderRect = slider.GetComponent<RectTransform>();
        sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0.5f, 1f);
        sliderRect.pivot = new Vector2(0.5f, 1f);
        sliderRect.anchoredPosition = new Vector2(60f, -90f);
        sliderRect.sizeDelta = new Vector2(160f, 20f);
        BuildSliderVisuals(slider);
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = AudioListener.volume;
        slider.onValueChanged.AddListener(v => AudioListener.volume = v);

        CreateButton(settingsPanel.transform, "Retour", font, -170f, () => settingsPanel.SetActive(false));

        settingsPanel.SetActive(false);
    }

    void BuildSliderVisuals(Slider slider)
    {
        GameObject bgGO = new GameObject("Background", typeof(Image));
        bgGO.transform.SetParent(slider.transform, false);
        Image bg = bgGO.GetComponent<Image>();
        bg.color = new Color(0.3f, 0.3f, 0.33f);
        RectTransform bgRect = bg.rectTransform;
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        GameObject fillAreaGO = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGO.transform.SetParent(slider.transform, false);
        RectTransform fillAreaRect = fillAreaGO.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = Vector2.zero;
        fillAreaRect.offsetMax = Vector2.zero;

        GameObject fillGO = new GameObject("Fill", typeof(Image));
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        Image fill = fillGO.GetComponent<Image>();
        fill.color = new Color(0.75f, 0.7f, 0.15f);
        RectTransform fillRect = fill.rectTransform;
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        slider.fillRect = fillRect;
        slider.targetGraphic = fill;
        slider.direction = Slider.Direction.LeftToRight;
    }

    void OnNewGame()
    {
        // A brand new run always starts in the tutorial room, not straight into floor 1 - see
        // DungeonGenerator.BuildTutorial. "Continuer" skips it (a resumed save is already past it).
        DungeonGenerator.BuildTutorial(Random.Range(int.MinValue, int.MaxValue));
        Destroy(menuCanvas);
    }

    void OnContinue()
    {
        SaveData data = SaveManager.Load();
        if (data == null) return;

        DungeonGenerator.Build(data.seed, data.floor > 0 ? data.floor : 1);

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            SaveManager.Apply(data,
                player.GetComponent<PlayerInventory>(),
                player.GetComponent<PlayerStats>(),
                player.GetComponent<Health>(),
                player.GetComponent<Stamina>(),
                player.GetComponent<PlayerController>());
        }

        Destroy(menuCanvas);
    }

    void OnToggleSettings()
    {
        settingsPanel.SetActive(!settingsPanel.activeSelf);
    }

    void OnQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
