using System.Collections;
using UnityEngine;

/// <summary>
/// World-space canvas that shows the gesture → spell reference card.
///
/// Show triggers:
///   1. GameStartUI calls ShowForDuration() when the player presses Begin.
///   2. The Reveal (spiral) spell re-opens the card.
///
/// In both cases the card fades in, holds for autoHideSeconds, then fades out.
/// Alpha is driven by a CanvasGroup auto-fetched from the panel.
/// </summary>
public class SpellReferenceUI : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static SpellReferenceUI Instance { get; private set; }

    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Panel")]
    public GameObject panel;           // root panel GameObject

    [Header("Timing")]
    public float autoHideSeconds = 30f;
    public float fadeDuration    = 0.4f;

    // ── Private ───────────────────────────────────────────────────────────────

    enum FadeState { Hidden, FadingIn, Visible, FadingOut }
    FadeState _state = FadeState.Hidden;

    CanvasGroup _canvasGroup;
    Coroutine   _routine;

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (panel != null)
        {
            // Grab or create the CanvasGroup on the panel
            _canvasGroup = panel.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = panel.AddComponent<CanvasGroup>();

            _canvasGroup.alpha          = 0f;
            _canvasGroup.interactable   = false;
            _canvasGroup.blocksRaycasts = false;
            panel.SetActive(false);
        }
    }

    void OnEnable()  => SpellEvents.OnSpellCast += OnSpellCast;
    void OnDisable() => SpellEvents.OnSpellCast -= OnSpellCast;

    // ── Spell listener ────────────────────────────────────────────────────────

    void OnSpellCast(string spell, GameObject target)
    {
        if (spell == "spiral") ShowForDuration();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// Fade in, hold for autoHideSeconds, then fade out.
    /// Safe to call while already visible — restarts the timer.
    public void ShowForDuration()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(ShowRoutine());
    }

    /// Fade out immediately.
    public void Hide()
    {
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(FadeRoutine(FadeState.FadingOut));
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    IEnumerator ShowRoutine()
    {
        yield return FadeRoutine(FadeState.FadingIn);

        _state = FadeState.Visible;
        yield return new WaitForSeconds(autoHideSeconds);

        yield return FadeRoutine(FadeState.FadingOut);
    }

    IEnumerator FadeRoutine(FadeState direction)
    {
        _state = direction;

        if (direction == FadeState.FadingIn)
        {
            _canvasGroup.alpha = 0f;
            panel.SetActive(true);
            _canvasGroup.interactable   = true;
            _canvasGroup.blocksRaycasts = true;

            float from = _canvasGroup.alpha;
            for (float t = 0f; t < fadeDuration; t += Time.deltaTime)
            {
                _canvasGroup.alpha = Mathf.Lerp(from, 1f, t / fadeDuration);
                yield return null;
            }
            _canvasGroup.alpha = 1f;
        }
        else // FadingOut
        {
            _canvasGroup.alpha = 1f;
            float from = _canvasGroup.alpha;
            for (float t = 0f; t < fadeDuration; t += Time.deltaTime)
            {
                _canvasGroup.alpha = Mathf.Lerp(from, 0f, t / fadeDuration);
                yield return null;
            }
            _canvasGroup.alpha          = 0f;
            _canvasGroup.interactable   = false;
            _canvasGroup.blocksRaycasts = false;
            panel.SetActive(false);
            _state = FadeState.Hidden;
        }
    }
}
