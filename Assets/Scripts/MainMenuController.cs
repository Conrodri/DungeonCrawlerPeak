using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
        ReturnToMenu();
    }

    // Unlike PauseMenuUI (which already closes its own settings panel on Escape - see its
    // Update()), this menu had NO Escape handling at all until now (2026-09-21 bug report:
    // "impossible de fermer le menu de son une fois ouvert") - the "Retour" button was the only
    // way out. Kept as a safety net alongside that button rather than a replacement for it.
    void Update()
    {
        if (settingsPanel == null || !settingsPanel.activeSelf) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) settingsPanel.SetActive(false);
    }

    // Tears down any in-progress run and rebuilds the menu UI. Used both for the game's real entry
    // point (Start(), where a leftover DungeonRoot only exists because Dungeon/Generate Floor - an
    // Editor-only preview shortcut - baked one into the scene) and to return here mid-session (see
    // DeathScreenUI, shown after the player dies).
    public void ReturnToMenu()
    {
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
        // Purely decorative (no Button/pointer handling of its own) - left raycastTarget at its
        // Image default of true, this full-screen background was silently winning the raycast
        // over SettingsPanel once reactivated (2026-09-21 bug report: "impossible de fermer le
        // menu de son une fois ouvert" - confirmed live: SettingsPanel's own CanvasRenderer.
        // absoluteDepth was correctly higher, 12 vs 0, yet EventSystem.RaycastAll still returned
        // only Panel - disabling Panel's raycastTarget here was the only thing that fixed it in
        // testing). A non-interactive background never needs to intercept clicks anyway.
        bg.raycastTarget = false;
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
        CreateButton(panel.transform, "Salle d'entrainement", font, y, OnTrainingRoom);

        y -= 80f;
        CreateButton(panel.transform, "Parametres", font, y, OnToggleSettings);

        y -= 80f;
        CreateButton(panel.transform, "Quitter", font, y, OnQuit);

        settingsPanel = BuildSettingsPanel(menuCanvas.transform, font);
    }

    // Shared with PauseMenuUI's construction (see DungeonGenerator.Build) - the in-game pause menu
    // needs the exact same button/settings-panel look, so this stays static and parameter-only
    // (no instance field references) instead of duplicating ~60 lines of UI boilerplate. See
    // feedback_code_reuse_library.
    public static Button CreateButton(Transform parent, string label, Font font, float y, UnityEngine.Events.UnityAction onClick)
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

    // Static/shared for the same reason as CreateButton above - PauseMenuUI reuses this verbatim
    // for its in-game settings panel instead of a duplicate copy.
    public static GameObject BuildSettingsPanel(Transform parent, Font font)
    {
        GameObject settingsPanel = new GameObject("SettingsPanel", typeof(Image));
        settingsPanel.transform.SetParent(parent, false);
        Image bg = settingsPanel.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.98f);
        RectTransform bgRect = bg.rectTransform;
        bgRect.anchorMin = bgRect.anchorMax = new Vector2(0.5f, 0.5f);
        bgRect.pivot = new Vector2(0.5f, 0.5f);
        // Taller than before (was 340) - room for the audio module below (2026-09-21 request:
        // master/voice volume + an output-device shortcut) on top of the existing Touches/Retour.
        bgRect.sizeDelta = new Vector2(420f, 460f);

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

        // Master volume - persisted (see AudioSettingsManager), not just AudioListener.volume for
        // the current session like before.
        BuildVolumeRow(settingsPanel.transform, font, "Volume general", -90f,
            AudioSettingsManager.MasterVolume, v => AudioSettingsManager.MasterVolume = v);

        // Voice volume - the only other audio channel that exists today (achievement/boss-intro AI
        // narration, see AchievementVoice/BossIntroVoice) - kept separate from master so a player
        // who wants the narration quieter (or off) doesn't have to also mute sfx/music once those
        // exist.
        BuildVolumeRow(settingsPanel.transform, font, "Volume voix", -140f,
            AudioSettingsManager.VoiceVolume, v => AudioSettingsManager.VoiceVolume = v);

        // No in-engine output-device picker: Unity's AudioSettings API has no device enumeration/
        // selection on Standalone (confirmed against this project's own Editor - there is no such
        // method to call). Windows' own Sound settings page is the actual place that choice lives,
        // so this opens it directly rather than faking a dropdown that couldn't do anything.
        GameObject deviceLabelGO = new GameObject("DeviceLabel", typeof(Text));
        deviceLabelGO.transform.SetParent(settingsPanel.transform, false);
        Text deviceLabel = deviceLabelGO.GetComponent<Text>();
        deviceLabel.text = "Sortie audio : geree par Windows";
        deviceLabel.font = font;
        deviceLabel.fontSize = 18;
        deviceLabel.alignment = TextAnchor.MiddleCenter;
        deviceLabel.color = new Color(0.7f, 0.7f, 0.7f);
        RectTransform deviceLabelRect = deviceLabel.rectTransform;
        deviceLabelRect.anchorMin = deviceLabelRect.anchorMax = new Vector2(0.5f, 1f);
        deviceLabelRect.pivot = new Vector2(0.5f, 1f);
        deviceLabelRect.anchoredPosition = new Vector2(0f, -195f);
        deviceLabelRect.sizeDelta = new Vector2(380f, 30f);

        CreateButton(settingsPanel.transform, "Ouvrir les parametres son Windows", font, -250f, OnOpenWindowsSoundSettings);
        RebindKeysUI rebindKeys = RebindKeysUI.Build(parent, font);
        CreateButton(settingsPanel.transform, "Touches", font, -330f, rebindKeys.Open);
        CreateButton(settingsPanel.transform, "Retour", font, -400f, () => settingsPanel.SetActive(false));

        settingsPanel.SetActive(false);
        return settingsPanel;
    }

    // One labeled slider row (used for master + voice volume above) - factored out since the two
    // are otherwise identical apart from label/position/getter/setter.
    static void BuildVolumeRow(Transform parent, Font font, string label, float y, float initialValue, UnityEngine.Events.UnityAction<float> onChanged)
    {
        GameObject labelGO = new GameObject(label.Replace(" ", "") + "Label", typeof(Text));
        labelGO.transform.SetParent(parent, false);
        Text labelText = labelGO.GetComponent<Text>();
        labelText.text = label;
        labelText.font = font;
        labelText.fontSize = 22;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.color = Color.white;
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 1f);
        labelRect.pivot = new Vector2(0.5f, 1f);
        labelRect.anchoredPosition = new Vector2(-140f, y);
        labelRect.sizeDelta = new Vector2(120f, 40f);

        GameObject sliderGO = new GameObject(label.Replace(" ", "") + "Slider", typeof(Slider));
        sliderGO.transform.SetParent(parent, false);
        Slider slider = sliderGO.GetComponent<Slider>();
        RectTransform sliderRect = slider.GetComponent<RectTransform>();
        sliderRect.anchorMin = sliderRect.anchorMax = new Vector2(0.5f, 1f);
        sliderRect.pivot = new Vector2(0.5f, 1f);
        sliderRect.anchoredPosition = new Vector2(60f, y);
        sliderRect.sizeDelta = new Vector2(160f, 20f);
        BuildSliderVisuals(slider);
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = initialValue;
        slider.onValueChanged.AddListener(onChanged);
    }

    static void OnOpenWindowsSoundSettings()
    {
        Application.OpenURL("ms-settings:sound");
    }

    public static void BuildSliderVisuals(Slider slider)
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

    // A throwaway sandbox floor (see DungeonGenerator.BuildTrainingRoom) - full gear, a stack of
    // every item worth testing, and a stationary punching ball with huge HP. Doesn't touch
    // SaveManager at all, same as this being a dead end with no progression to save.
    void OnTrainingRoom()
    {
        DungeonGenerator.BuildTrainingRoom(Random.Range(int.MinValue, int.MaxValue));
        Destroy(menuCanvas);
    }

    void OnContinue()
    {
        SaveData data = SaveManager.Load();
        if (data == null) return;

        DungeonGenerator.Build(data.seed, data.floor > 0 ? data.floor : 1, data.clearedRooms, data.bossDefeated, data.usedBossBiomesBeforeFloor);

        GameObject player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            SaveManager.Apply(data,
                player.GetComponent<PlayerInventory>(),
                player.GetComponent<PlayerStats>(),
                player.GetComponent<Health>(),
                player.GetComponent<Stamina>(),
                player.GetComponent<PlayerController>(),
                player.GetComponent<PlayerEquipment>(),
                player.GetComponent<PlayerLimbs>());
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
