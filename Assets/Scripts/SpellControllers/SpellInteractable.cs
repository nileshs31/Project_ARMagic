using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to any object that can receive spells.
///
/// All tunable values (prefabs, distances, speeds, reset timer, materials)
/// live in SpellManager so the whole game is configured from one Inspector
/// object.  Only per-object overrides stay here:
///   • meshesToFreeze  – which renderers the Freeze swap targets
///                       (auto-filled from all child MeshRenderers if empty)
///   • stunCanvas      – the unique stun UI for this object
///
/// Reset rules:
///   • One shared resetTimer drives ALL spell resets simultaneously.
///   • If levitating when a new spell lands the existing session timer is
///     kept — the new spell expires at the same moment levitate would have.
///   • At reset time everything fires at once:
///       - Ignite  → destroy the spawned fire child  (object stays active)
///       - Freeze  → restore original materials      (object stays active)
///       - Everything else → SetActive(false) → snap to origin → SetActive(true)
///
/// Pull / Push accumulation:
///   Cancelling a pull/push does NOT snap XZ back.
///   The new move starts from wherever the object currently sits.
///   The full origin reset only happens when the session timer fires.
///
/// Axis ownership:
///   Levitate → Y only     Pull / Push / Stun → XZ only
/// </summary>
public class SpellInteractable : MonoBehaviour
{
    // ── State ─────────────────────────────────────────────────────────────────

    public enum SpellState { None, Ignite, Freeze, Unlock, Pull, Push, Stun }

    [Header("State  (read-only at runtime)")]
    public SpellState currentSpell = SpellState.None;
    public bool       isLevitating = false;

    // ── Per-object overrides ──────────────────────────────────────────────────

    [Header("Freeze  (auto-filled from children if left empty)")]
    public List<MeshRenderer> meshesToFreeze = new List<MeshRenderer>();

    [Header("Stun")]
    public GameObject stunCanvas;   // unique UI per object; leave null if unused

    // ── Private ───────────────────────────────────────────────────────────────

    Vector3    _origPos;
    Quaternion _origRot;

    Coroutine  _spellCoroutine;
    Coroutine  _levitateCoroutine;
    Coroutine  _sessionCoroutine;

    GameObject   _spawnedEffect;   // fire child (Ignite only)
    Animator     _animator;
    SpellManager _sm;              // config source — set in Start()

    // Original materials per renderer, cached in Start() before any freeze swap
    readonly Dictionary<MeshRenderer, Material[]> _freezeOriginalMats =
        new Dictionary<MeshRenderer, Material[]>();

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        _origPos  = transform.position;
        _origRot  = transform.rotation;
        _animator = GetComponent<Animator>();
        _sm       = SpellManager.Instance;

        // ── Auto-fill freeze mesh list ────────────────────────────────────────
        if (meshesToFreeze == null) meshesToFreeze = new List<MeshRenderer>();

        if (meshesToFreeze.Count == 0)
        {
            // Prefer all descendant renderers (handles multi-mesh models)
            var found = GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            meshesToFreeze.AddRange(found);

            // Fallback: self only
            if (meshesToFreeze.Count == 0)
            {
                var self = GetComponent<MeshRenderer>();
                if (self != null) meshesToFreeze.Add(self);
            }
        }

        // Cache original materials now, before any runtime swaps
        foreach (var mr in meshesToFreeze)
            if (mr != null) _freezeOriginalMats[mr] = mr.materials;
    }

    // ── Public entry point ────────────────────────────────────────────────────

    public void ApplySpell(string spell)
    {
        switch (spell)
        {
            case "circle_cw":  BeginSpell(SpellState.Ignite, IgniteRoutine());  break;
            case "circle_ccw": BeginSpell(SpellState.Freeze, FreezeRoutine());  break;
            case "swipe_up":   BeginLevitate();                                 break;
            case "triangle":   BeginSpell(SpellState.Unlock, UnlockRoutine());  break;
            case "square":     BeginSpell(SpellState.Pull,   PullRoutine());    break;
            case "swipe_down": BeginSpell(SpellState.Push,   PushRoutine());    break;
            case "squiggle":   BeginSpell(SpellState.Stun,   StunRoutine());    break;
            // "spiral" (reveal) is handled in SpellManager, not here
            default: Debug.Log($"[SpellInteractable] No handler for '{spell}' on {name}"); break;
        }
    }

    // ── Session / dispatch ────────────────────────────────────────────────────

    /// Start a non-levitate spell.
    /// Pull / Push always restart the session timer (even while levitating) so
    ///   the object gets the full resetTimer after a move, not whatever is left
    ///   on the levitate countdown.
    /// All other spells while levitating inherit the remaining levitate time.
    /// While not levitating: always restart the session timer.
    void BeginSpell(SpellState state, IEnumerator routine)
    {
        CancelActiveSpell();

        bool restartTimer = !isLevitating
                         || state == SpellState.Pull
                         || state == SpellState.Push;

        if (restartTimer)
        {
            if (_sessionCoroutine != null) StopCoroutine(_sessionCoroutine);
            _sessionCoroutine = StartCoroutine(SessionResetRoutine());
        }
        // Other spells while levitating: existing _sessionCoroutine keeps
        // counting — the new spell expires when levitate would have.

        currentSpell    = state;
        _spellCoroutine = StartCoroutine(routine);
    }

    /// Levitate always owns (or restarts) the session timer.
    void BeginLevitate()
    {
        if (_levitateCoroutine != null) StopCoroutine(_levitateCoroutine);

        if (_sessionCoroutine != null) StopCoroutine(_sessionCoroutine);
        _sessionCoroutine = StartCoroutine(SessionResetRoutine());

        isLevitating       = true;
        _levitateCoroutine = StartCoroutine(LevitateRoutine());
    }

    /// Master timer: fires PerformReset() so ALL effects end simultaneously.
    IEnumerator SessionResetRoutine()
    {
        float duration = _sm != null ? _sm.resetTimer : 5f;
        yield return new WaitForSeconds(duration);
        PerformReset();
        _sessionCoroutine = null;
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    /// Called at the natural end of a session — all spells expire at once.
    void PerformReset()
    {
        if (_spellCoroutine    != null) { StopCoroutine(_spellCoroutine);    _spellCoroutine    = null; }
        if (_levitateCoroutine != null) { StopCoroutine(_levitateCoroutine); _levitateCoroutine = null; }

        bool hadIgniteEffect  = _spawnedEffect != null;           // fire child exists
        bool hadFreezeEffect  = currentSpell == SpellState.Freeze; // material-swap active
        bool hadPositionalEffect = isLevitating
                                || currentSpell == SpellState.Pull
                                || currentSpell == SpellState.Push
                                || currentSpell == SpellState.Stun;

        // Clean up Ignite child
        if (hadIgniteEffect) DestroySpawnedEffect();

        // Restore Freeze materials
        if (hadFreezeEffect) RemoveFreezeMaterials();

        // Stun canvas off
        if (stunCanvas != null) stunCanvas.SetActive(false);

        // Unlock → return to locked state
        if (currentSpell == SpellState.Unlock && _animator != null)
            _animator.Play("locking");

        // Position reset:
        //   Pure ignite or pure freeze (no positional movement) → just clean up
        //   the visual effect; object stays active, no blink.
        //   Anything else → blink off → snap to origin → blink on.
        bool effectOnlySpell = (hadIgniteEffect || hadFreezeEffect) && !hadPositionalEffect;
        if (!effectOnlySpell)
        {
            gameObject.SetActive(false);
            transform.position = _origPos;
            transform.rotation = _origRot;
            gameObject.SetActive(true);
        }

        currentSpell = SpellState.None;
        isLevitating = false;
    }

    /// Immediately stop the active (non-levitate) spell and clean up its visual.
    /// Called before a new spell starts — does NOT blink-reset the object.
    ///
    /// Pull / Push: intentionally NOT snapped back so the next move accumulates.
    void CancelActiveSpell()
    {
        if (_spellCoroutine != null)
        {
            StopCoroutine(_spellCoroutine);
            _spellCoroutine = null;
        }

        switch (currentSpell)
        {
            case SpellState.Ignite:
                DestroySpawnedEffect();
                break;

            case SpellState.Freeze:
                RemoveFreezeMaterials();
                break;

            case SpellState.Unlock:
                if (_animator != null) _animator.Play("locking");
                break;

            case SpellState.Pull:
            case SpellState.Push:
                // No XZ snap — object stays where it is.
                // Full origin reset happens only when the session timer fires.
                break;

            case SpellState.Stun:
                if (stunCanvas != null) stunCanvas.SetActive(false);
                ResetXZ();   // stun pushback is small; snap back before next spell
                break;
        }

        currentSpell = SpellState.None;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Reset horizontal axes only. Y is left untouched so levitate continues.
    void ResetXZ()
    {
        transform.position = new Vector3(_origPos.x, transform.position.y, _origPos.z);
        transform.rotation = _origRot;
    }

    void DestroySpawnedEffect()
    {
        if (_spawnedEffect != null) { Destroy(_spawnedEffect); _spawnedEffect = null; }
    }

    /// Replace materials on all freeze meshes with the ice materials from SpellManager.
    void ApplyFreezeMaterials()
    {
        if (_sm == null) return;

        Material iceMat     = _sm.iceMaterial;
        Material iceOverlay = _sm.iceOverlayMaterial;

        foreach (var mr in meshesToFreeze)
        {
            if (mr == null || !_freezeOriginalMats.ContainsKey(mr)) continue;

            int slotCount = _freezeOriginalMats[mr].Length;
            var newMats   = new List<Material>(slotCount + 1);

            // Replace every existing slot with iceMaterial.
            // If iceMaterial is null, keep the original slot (no visual change for that mesh).
            for (int i = 0; i < slotCount; i++)
                newMats.Add(iceMat != null ? iceMat : _freezeOriginalMats[mr][i]);

            // Append overlay as an extra slot (e.g. frost rim / outline).
            if (iceOverlay != null)
                newMats.Add(iceOverlay);

            mr.materials = newMats.ToArray();
        }
    }

    /// Restore all freeze meshes to their original materials.
    void RemoveFreezeMaterials()
    {
        foreach (var mr in meshesToFreeze)
        {
            if (mr == null || !_freezeOriginalMats.ContainsKey(mr)) continue;
            mr.materials = _freezeOriginalMats[mr];
        }

        // If the object is still inside the targeting cone, ObjectHighlighter may
        // be highlighted with a stale cache (ice mats recorded as "originals").
        // RefreshHighlight re-builds the highlight on top of the now-restored
        // true materials so RemoveHighlight will return to the correct state.
        GetComponent<ObjectHighlighter>()?.RefreshHighlight();
    }

    static Vector3 GetHorizontalDirToCamera(Vector3 from)
    {
        if (Camera.main == null) return Vector3.forward;
        Vector3 cam = Camera.main.transform.position;
        Vector3 dir = new Vector3(cam.x - from.x, 0f, cam.z - from.z);
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
    }

    // ── Spell Coroutines ──────────────────────────────────────────────────────
    // Each coroutine performs its effect then suspends indefinitely.
    // PerformReset() (called by SessionResetRoutine) stops them all at once.

    // IGNITE ──────────────────────────────────────────────────────────────────
    IEnumerator IgniteRoutine()
    {
        if (_sm != null && _sm.firePrefab != null)
        {
            _spawnedEffect = Instantiate(_sm.firePrefab, transform);
            _spawnedEffect.transform.localPosition = Vector3.zero;
            _spawnedEffect.transform.localRotation = Quaternion.identity;
        }
        yield return SuspendUntilReset();
    }

    // FREEZE ──────────────────────────────────────────────────────────────────
    // Swaps materials on all meshesToFreeze; no spawned child needed.
    IEnumerator FreezeRoutine()
    {
        ApplyFreezeMaterials();
        yield return SuspendUntilReset();
        // RemoveFreezeMaterials() is called by PerformReset() / CancelActiveSpell()
    }

    // LEVITATE ────────────────────────────────────────────────────────────────
    // Owns the Y axis only. Rises then bobs until PerformReset() stops it.
    IEnumerator LevitateRoutine()
    {
        float levH  = _sm != null ? _sm.levitateHeight    : 1.5f;
        float levRS = _sm != null ? _sm.levitateRiseSpeed : 2f;
        float levFF = _sm != null ? _sm.levitateFloatFreq : 1.2f;
        float levFA = _sm != null ? _sm.levitateFloatAmp  : 0.12f;

        float startY = transform.position.y;
        float floatY = _origPos.y + levH;

        // Rise
        float riseTime = Mathf.Max(0.01f, Mathf.Abs(floatY - startY) / levRS);
        for (float t = 0f; t < riseTime; t += Time.deltaTime)
        {
            float y = Mathf.Lerp(startY, floatY, Mathf.SmoothStep(0f, 1f, t / riseTime));
            transform.position = new Vector3(transform.position.x, y, transform.position.z);
            yield return null;
        }
        transform.position = new Vector3(transform.position.x, floatY, transform.position.z);

        // Bob indefinitely — stopped externally by PerformReset()
        float elapsed = 0f;
        while (true)
        {
            elapsed += Time.deltaTime;
            float bob = Mathf.Sin(elapsed * levFF * Mathf.PI * 2f) * levFA;
            transform.position = new Vector3(transform.position.x, floatY + bob, transform.position.z);
            yield return null;
        }
    }

    // UNLOCK ──────────────────────────────────────────────────────────────────
    IEnumerator UnlockRoutine()
    {
        if (_animator != null) _animator.Play("unlocking");
        yield return SuspendUntilReset();
        // PerformReset() plays "locking" at the end
    }

    // PULL ────────────────────────────────────────────────────────────────────
    IEnumerator PullRoutine()
    {
        float dist = _sm != null ? _sm.pullDistance : 2f;
        float spd  = _sm != null ? _sm.moveSpeed    : 3f;

        // Start from current position — may already be displaced from a prior pull
        Vector3 startPos = transform.position;
        Vector3 target   = startPos + GetHorizontalDirToCamera(startPos) * dist;

        for (float t = 0f; t < 1f; t += Time.deltaTime * spd)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.position = new Vector3(
                Mathf.Lerp(startPos.x, target.x, s),
                transform.position.y,
                Mathf.Lerp(startPos.z, target.z, s));
            yield return null;
        }
        transform.position = new Vector3(target.x, transform.position.y, target.z);

        yield return SuspendUntilReset();
    }

    // PUSH ────────────────────────────────────────────────────────────────────
    IEnumerator PushRoutine()
    {
        float dist = _sm != null ? _sm.pushDistance : 3f;
        float spd  = _sm != null ? _sm.moveSpeed    : 3f;

        // Start from current position — may already be displaced from a prior push
        Vector3 startPos = transform.position;
        Vector3 target   = startPos + (-GetHorizontalDirToCamera(startPos)) * dist;

        for (float t = 0f; t < 1f; t += Time.deltaTime * spd)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.position = new Vector3(
                Mathf.Lerp(startPos.x, target.x, s),
                transform.position.y,
                Mathf.Lerp(startPos.z, target.z, s));
            yield return null;
        }
        transform.position = new Vector3(target.x, transform.position.y, target.z);

        yield return SuspendUntilReset();
    }

    // STUN ────────────────────────────────────────────────────────────────────
    IEnumerator StunRoutine()
    {
        float pushback = _sm != null ? _sm.stunPushback  : 0.5f;
        float spd      = _sm != null ? _sm.stunMoveSpeed : 8f;

        if (stunCanvas != null) stunCanvas.SetActive(true);

        Vector3 startPos = transform.position;
        Vector3 target   = startPos + (-GetHorizontalDirToCamera(startPos)) * pushback;

        for (float t = 0f; t < 1f; t += Time.deltaTime * spd)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.position = new Vector3(
                Mathf.Lerp(startPos.x, target.x, s),
                transform.position.y,
                Mathf.Lerp(startPos.z, target.z, s));
            yield return null;
        }
        transform.position = new Vector3(target.x, transform.position.y, target.z);

        yield return SuspendUntilReset();
        // stunCanvas is hidden by PerformReset()
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    static IEnumerator SuspendUntilReset()
    {
        while (true) yield return null;
    }
}
