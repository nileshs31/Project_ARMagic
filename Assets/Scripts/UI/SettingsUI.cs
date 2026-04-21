using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Floating settings panel.
///
/// ── Layout expected in the scene ────────────────────────────────────────────
///   settingsToggleButton   Always-visible button that opens / closes the panel.
///   settingsPanel          Root GameObject of the settings content.
///     arToggle             Toggle  — on = AR (passthrough), off = VR
///     voiceToggle          Toggle  — on = voice input, off = wand / controller
///     muteToggle           Toggle  — on = muted (AudioListener.volume = 0)
///     creditsButton        Button  — opens the credits panel
///   creditsPanel           Root GameObject of the credits content.
///     creditsPanelCloseButton  Button — closes credits panel
///
/// ── Scene wiring ─────────────────────────────────────────────────────────────
///   passthroughController  Drag the PassthroughController MonoBehaviour here.
///   wandHandler            Drag the WandHandler MonoBehaviour here.
/// </summary>
public class SettingsUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Toggles inside the panel")]
    public Toggle arToggle;      // on = AR passthrough, off = VR
    public Toggle voiceToggle;   // on = voice input, off = wand
    public Toggle muteToggle;    // on = muted

    [Header("Credits")]
    public Button     creditsButton;
    public GameObject creditsPanel;
    public Button     creditsPanelCloseButton;

    [Header("Scene References")]
    public PassthroughController passthroughController;
    public WandHandler           wandHandler;

    // ── Private ───────────────────────────────────────────────────────────────

    bool _panelOpen = false;

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        // Start with both panels hidden
        if (creditsPanel  != null) creditsPanel.SetActive(false);

        // Wire buttons and toggles
        if (arToggle                != null) arToggle.onValueChanged.AddListener(OnArToggle);
        if (voiceToggle             != null) voiceToggle.onValueChanged.AddListener(OnVoiceToggle);
        if (muteToggle              != null) muteToggle.onValueChanged.AddListener(OnMuteToggle);
        if (creditsButton           != null) creditsButton.onClick.AddListener(ShowCredits);
        if (creditsPanelCloseButton != null) creditsPanelCloseButton.onClick.AddListener(HideCredits);

        // Sync toggle visuals to actual state without triggering callbacks
        SyncToggleStates();
    }


    // ── Sync ──────────────────────────────────────────────────────────────────

    /// Reflect the true runtime state onto each toggle without firing callbacks.
    void SyncToggleStates()
    {
        if (arToggle    != null)
            arToggle.SetIsOnWithoutNotify(
                passthroughController != null && passthroughController.IsPassthrough);

        if (voiceToggle != null)
            voiceToggle.SetIsOnWithoutNotify(
                wandHandler != null && wandHandler.isVoiceControlled);

        if (muteToggle  != null)
            muteToggle.SetIsOnWithoutNotify(AudioListener.volume < 0.1f);
    }

    // ── Toggle callbacks ──────────────────────────────────────────────────────

    void OnArToggle(bool isOn)
    {
        passthroughController?.SetPassthrough(isOn);
    }

    void OnVoiceToggle(bool isOn)
    {
        if (wandHandler != null) wandHandler.isVoiceControlled = isOn;
    }

    void OnMuteToggle(bool isOn)
    {
        AudioListener.volume = isOn ? 0f : 1f;
    }

    // ── Credits ───────────────────────────────────────────────────────────────

    void ShowCredits()
    {
        if (creditsPanel != null) creditsPanel.SetActive(true);
    }

    void HideCredits()
    {
        if (creditsPanel != null) creditsPanel.SetActive(false);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    void OnDestroy()
    {
        if (arToggle                != null) arToggle.onValueChanged.RemoveListener(OnArToggle);
        if (voiceToggle             != null) voiceToggle.onValueChanged.RemoveListener(OnVoiceToggle);
        if (muteToggle              != null) muteToggle.onValueChanged.RemoveListener(OnMuteToggle);
        if (creditsButton           != null) creditsButton.onClick.RemoveListener(ShowCredits);
        if (creditsPanelCloseButton != null) creditsPanelCloseButton.onClick.RemoveListener(HideCredits);
    }
}
