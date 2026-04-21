using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shown when the game first loads.
///
/// When the player presses Begin:
///   • This canvas is hidden.
///   • The settings panel is made visible (stays on for the rest of the game).
///   • The spell reference card is shown for its auto-hide period.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class GameStartUI : MonoBehaviour
{
    [Header("References")]
    public Button      beginButton;
    public GameObject  settingsPanel;   // shown on Begin and left on for the session
    public WandHandler wandHandler;     // input is gated until Begin is pressed

    [Header("Audio")]
    public AudioClip  introClip;       // plays when the screen appears; optional

    AudioSource _audio;
    //public GameObject spellWorks;
    void Awake()
    {
        _audio = GetComponent<AudioSource>();
        _audio.playOnAwake = false;
    }

    void Start()
    {
        if (introClip != null)
            _audio.PlayOneShot(introClip);

        if (beginButton != null)
            beginButton.onClick.AddListener(OnBeginPressed);
    }

    void OnBeginPressed()
    {
        // Unlock spell casting
        if (wandHandler != null) wandHandler.EnableInput();

        // Show the settings panel — stays visible for the rest of the game
        if (settingsPanel != null) settingsPanel.SetActive(true);

        // Show the gesture reference card for its auto-hide window
        SpellReferenceUI.Instance?.ShowForDuration();
        //spellWorks.SetActive(true);
        gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (beginButton != null)
            beginButton.onClick.RemoveListener(OnBeginPressed);
    }
}
