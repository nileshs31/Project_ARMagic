using System;
using UnityEngine;
using InionVR.AI;

/// <summary>
/// Thin wrapper around VoiceBot (OpenAI Whisper + assistant).
///
/// The assistant is instructed to return ONLY one of the exact spell labels
/// (circle_cw, circle_ccw, swipe_up, triangle, square, swipe_down,
///  squiggle, spiral) or "Unknown".  No mapping is needed here — the result
/// is forwarded as-is via onSpellTranscribed.
///
/// UI feedback (listening / loading indicators) is handled by WandHandler.
/// Button input is handled by WandHandler.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VoiceBot_Wrapper : MonoBehaviour
{
    [SerializeField] string a, k;
    [SerializeField] int micNumber;
    VoiceBot bot;

    // ── Public event ──────────────────────────────────────────────────────────

    /// Fired with the assistant's exact response string.
    /// Value is one of the spell labels or "Unknown".
    /// WandHandler subscribes here.
    public Action<string> onSpellTranscribed;

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        GetComponent<AudioSource>().playOnAwake = false;

        bot = new VoiceBot(a, k, micNumber);

        foreach (var mic in bot.GetMicrophoneList())
            Debug.Log(mic);

        bot.onListening  += () => { };

        bot.onThinking   += (result) =>
        {
            string spell = result?.Trim();
            Debug.Log("[VoiceBot] Result: " + spell);
            onSpellTranscribed?.Invoke(spell);
        };

        bot.onSpeaking   += (response)     => { };
        bot.onCompleted  += (responseClip) => { };
    }

    // ── Called by WandHandler ─────────────────────────────────────────────────

    public void StartVoiceRecording(int maxSeconds = 15) => bot.Record(maxSeconds);
    public void StopVoiceRecording()                     => bot.StopRecording();
    /// Cancel any in-flight Whisper request so a fresh recording can start immediately.
    public void CancelRecording()                        => bot.ForceReset();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void OnDestroy() => bot.Destroy();

    [ContextMenu("StopRecording")]
    public void StopRecording() => bot.StopRecording();

    [ContextMenu("Record")]
    public void session() => bot.Record(14);
}
