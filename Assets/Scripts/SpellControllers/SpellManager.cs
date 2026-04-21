using System.Collections;
using UnityEngine;

/// <summary>
/// Central spell config + event router.
///
/// All shared spell parameters live here so only one Inspector object needs
/// to be edited.  SpellInteractable reads values via SpellManager.Instance.
///
/// Reveal (spiral) is owned entirely by SpellManager — it is never forwarded
/// to SpellInteractable.
///
/// Fire bolt is spawned and moved by SpellManager.  WandHandler only
/// detects the button press and calls TryFireBolt().
/// </summary>
public class SpellManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────

    public static SpellManager Instance { get; private set; }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [Header("Reset (applies to all spell objects)")]
    public float resetTimer = 5f;

    // ── Ignite ────────────────────────────────────────────────────────────────

    [Header("Ignite")]
    public GameObject firePrefab;

    // ── Freeze ────────────────────────────────────────────────────────────────

    [Header("Freeze")]
    public Material iceMaterial;         // replaces every material slot on frozen meshes
    public Material iceOverlayMaterial;  // appended as an extra slot (frost rim / outline); optional

    // ── Levitate ──────────────────────────────────────────────────────────────

    [Header("Levitate")]
    public float levitateHeight    = 1.5f;
    public float levitateRiseSpeed = 2f;
    public float levitateFloatFreq = 1.2f;
    public float levitateFloatAmp  = 0.12f;

    // ── Pull / Push ───────────────────────────────────────────────────────────

    [Header("Pull / Push")]
    public float pullDistance = 2f;
    public float pushDistance = 3f;
    public float moveSpeed    = 3f;

    // ── Stun ──────────────────────────────────────────────────────────────────

    [Header("Stun")]
    public float stunPushback  = 0.5f;
    public float stunMoveSpeed = 8f;

    // ── Reveal (spiral) ───────────────────────────────────────────────────────

    [Header("Reveal (spiral)")]
    public GameObject[] revealObjects;
    public float        revealDuration = 5f;

    // ── Fire Bolt ─────────────────────────────────────────────────────────

    [Header("Fire Bolt")]
    public GameObject fireBoltPrefab;
    public float      boltCooldown        = 1.5f;  // seconds between shots
    public float      boltSpeed           = 15f;   // units per second
    public float      boltLifetime        = 3f;    // seconds before auto-vanish (untargeted, no hit)
    public float      boltCollisionRadius = 0.1f;  // SphereCast radius (untargeted mode)
    public LayerMask  boltHitLayers;               // assign "interactible" layer in Inspector

    [Header("Fire Bolt — Impact")]
    public GameObject  boltImpactPrefab;   // explosion VFX spawned at hit position
    public AudioSource boltAudioSource;    // AudioSource on this GameObject
    public AudioClip   boltImpactClip;     // one-shot clip played on impact

    // ── Feedback ──────────────────────────────────────────────────────────────

    [Header("Feedback")]
    public GameObject unknown;  // shown when a spell can't be cast; auto-off handled in Unity

    // ── Private ───────────────────────────────────────────────────────────────

    Coroutine _revealCoroutine;
    float     _lastBoltTime = -999f;

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        SpellEvents.OnSpellCast += HandleSpell;
    }

    void OnDisable()
    {
        SpellEvents.OnSpellCast -= HandleSpell;
    }

    // ── Routing ───────────────────────────────────────────────────────────────

    void HandleSpell(string spell, GameObject target)
    {
        // ── Spells that never need a target ───────────────────────────────────

        // Reveal — always a global air effect
        if (spell == "spiral") { CastReveal(); return; }

        // Fire bolt — handled via TryFireBolt; event is informational only
        if (spell == "fire_bolt") { return; }

        // ── All other gesture spells require a valid SpellInteractable target ─

        if (target == null)
        {
            ShowUnknown();
            return;
        }

        var interactable = target.GetComponent<SpellInteractable>();
        if (interactable == null)
        {
            ShowUnknown();
            return;
        }

        // ── Unlock: object must have isUnlockable = true ──────────────────────

        if (spell == "triangle" && !interactable.isUnlockable)
        {
            ShowUnknown();
            return;
        }

        // ── Stun: object must have isStunnable = true ─────────────────────────

        if (spell == "squiggle" && !interactable.isStunnable)
        {
            ShowUnknown();
            return;
        }

        interactable.ApplySpell(spell);
    }

    void ShowUnknown()
    {
        if (unknown != null) unknown.SetActive(true);
    }

    // ── Fire Bolt ─────────────────────────────────────────────────────────

    /// Called by WandHandler when the bolt button is pressed.
    /// Returns true if the bolt was actually fired (not on cooldown).
    /// WandHandler triggers haptics only on a true return.
    public bool TryFireBolt(Vector3 spawnPos, Quaternion spawnRot, GameObject target)
    {
        if (Time.time - _lastBoltTime < boltCooldown)
        {
            Debug.Log($"[SpellManager] Fire bolt on cooldown " +
                      $"({boltCooldown - (Time.time - _lastBoltTime):F1}s left)");
            return false;
        }
        _lastBoltTime = Time.time;

        if (fireBoltPrefab != null)
        {
            var boltGO = Instantiate(fireBoltPrefab, spawnPos, spawnRot);
            StartCoroutine(BoltRoutine(boltGO, target));
        }

        // Fire event so other systems (UI, audio, stats) can react
        SpellEvents.OnSpellCast?.Invoke("fire_bolt", target);
        Debug.Log("[SpellManager] Fire bolt fired!");
        return true;
    }

    /// Two modes depending on whether a target was locked when the bolt was fired:
    ///
    ///   TARGETED   — bolt flies straight to the target's position (tracks it each frame).
    ///                On arrival: impact effects, destroy bolt.
    ///                If target is destroyed mid-flight: impact at current position and vanish.
    ///
    ///   UNTARGETED — bolt travels forward using SphereCast collision.
    ///                Hits a BoxCollider on boltHitLayers: impact effects, destroy bolt.
    ///                No hit within boltLifetime: silently vanish (no impact effects).
    IEnumerator BoltRoutine(GameObject boltGO, GameObject target)
    {
        if (boltGO == null) yield break;

        // ── TARGETED ─────────────────────────────────────────────────────────
        if (target != null)
        {
            while (boltGO != null)
            {
                // If target was destroyed mid-flight, impact where the bolt currently is
                if (target == null)
                {
                    OnBoltImpact(boltGO.transform.position);
                    Destroy(boltGO);
                    yield break;
                }

                Vector3 targetPos = target.transform.position;
                float   step      = boltSpeed * Time.deltaTime;
                float   dist      = Vector3.Distance(boltGO.transform.position, targetPos);

                if (dist <= step)
                {
                    // Reached the target — snap to it, impact, vanish
                    boltGO.transform.position = targetPos;
                    OnBoltImpact(targetPos, target);
                    Destroy(boltGO);
                    yield break;
                }

                // Move toward target and face the direction of travel
                boltGO.transform.position = Vector3.MoveTowards(
                    boltGO.transform.position, targetPos, step);
                boltGO.transform.LookAt(targetPos);

                yield return null;
            }
        }
        // ── UNTARGETED ───────────────────────────────────────────────────────
        else
        {
            Vector3 direction = boltGO.transform.forward;
            float   elapsed   = 0f;

            while (elapsed < boltLifetime)
            {
                if (boltGO == null) yield break;

                float step = boltSpeed * Time.deltaTime;

                // SphereCast one step ahead — BoxColliders on boltHitLayers stop the bolt
                if (Physics.SphereCast(
                        boltGO.transform.position,
                        boltCollisionRadius,
                        direction,
                        out RaycastHit hit,
                        step,
                        boltHitLayers)
                    && hit.collider is BoxCollider)
                {
                    boltGO.transform.position = hit.point;
                    OnBoltImpact(hit.point, hit.collider.gameObject);
                    Destroy(boltGO);
                    yield break;
                }

                boltGO.transform.position += direction * step;
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Lifetime expired with no hit — silently vanish, no impact effects
            if (boltGO != null) Destroy(boltGO);
        }
    }

    /// Plays the impact sound, spawns explosion VFX, and notifies any
    /// SpellInteractable on the hit object (e.g. spider death on fire-bolt hit).
    void OnBoltImpact(Vector3 position, GameObject hitObject = null)
    {
        if (boltAudioSource != null && boltImpactClip != null)
            boltAudioSource.PlayOneShot(boltImpactClip);

        if (boltImpactPrefab != null)
            Instantiate(boltImpactPrefab, position, Quaternion.identity);

        // Let the hit object react to the bolt (e.g. SpiderController.OnDeath)
        hitObject?.GetComponent<SpellInteractable>()?.ApplySpell("fire_bolt");
    }

    // ── Reveal ────────────────────────────────────────────────────────────────

    void CastReveal()
    {
        // Restart the timer if already active
        if (_revealCoroutine != null)
        {
            StopCoroutine(_revealCoroutine);
            _revealCoroutine = null;
        }
        _revealCoroutine = StartCoroutine(RevealRoutine());
    }

    IEnumerator RevealRoutine()
    {
        if (revealObjects != null)
            foreach (var obj in revealObjects)
                if (obj != null) obj.SetActive(true);

        Debug.Log($"[SpellManager] Reveal cast — {revealDuration}s duration.");
        yield return new WaitForSeconds(revealDuration);

        if (revealObjects != null)
            foreach (var obj in revealObjects)
                if (obj != null) obj.SetActive(false);

        _revealCoroutine = null;
        Debug.Log("[SpellManager] Reveal expired.");
    }

    // ── Air cast fallback ─────────────────────────────────────────────────────

    void CastInAir(string spell)
    {
        // Placeholder for future projectile / area VFX on untargeted gesture spells
        Debug.Log($"[SpellManager] '{spell}' cast into air (no target)");
    }
}
