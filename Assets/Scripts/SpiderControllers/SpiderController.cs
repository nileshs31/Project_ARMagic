using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spider AI that patrols between waypoints and reacts to every spell.
/// Inherits from SpellInteractable so the wand can target it naturally.
///
/// ── Multi-spell safety ────────────────────────────────────────────────────────
/// Every ApplySpell call goes through CancelEffect() first, which:
///   • Stops any running effect coroutine
///   • Destroys fire VFX child (ignite)
///   • Restores original materials (freeze)
///   • Re-enables mesh renderers + colliders (death state)
///   • Snaps Y back to ground level (levitate interrupted mid-air)
/// This means ANY spell can interrupt ANY other spell cleanly, in any order,
/// any number of times.
///
/// ── Death / fire bolt ────────────────────────────────────────────────────────
/// Only mesh renderers and non-trigger colliders are disabled — the
/// GameObject stays active so impact VFX parented to it can finish playing.
///
/// ── Patrol ───────────────────────────────────────────────────────────────────
/// patrolPoints   World-space Transforms visited in a loop.
/// moveSpeed      Walking speed (units/sec).
/// walkParam      Animator Bool parameter name driving the walk cycle.
///                Set to "" if the walk state is the Animator's default clip.
///
/// ── Spell reactions ──────────────────────────────────────────────────────────
///   stun (squiggle)     Stop → dizzy spin/sway for stunDuration → resume
///   fire bolt           Hide meshes + disable colliders → respawn after delay
///   freeze (circle_ccw) Stop → ice materials → thaw after resetTimer → resume
///   ignite (circle_cw)  Fire VFX child, keeps patrolling, removed after resetTimer
///   pull (square)       Glide toward camera → walk back → resume
///   push (swipe_down)   Glide away from camera → walk back → resume
///   levitate (swipe_up) Rise (legs wriggling) → float → descend → resume
///   unlock (triangle)   Ignored
///   reveal (spiral)     Ignored (global effect in SpellManager)
/// </summary>
public class SpiderController : SpellInteractable
{
    // ── Patrol ────────────────────────────────────────────────────────────────

    [Header("Patrol")]
    public List<Transform> patrolPoints = new List<Transform>();
    public float           moveSpeed    = 1.2f;
    public float           turnSpeed    = 270f;  // degrees per second

    // ── Animation ─────────────────────────────────────────────────────────────

    [Header("Animation")]
    public Animator spiderAnimator;
    /// Animator Bool parameter that plays the walk cycle.
    /// Leave blank if the walk state is the Animator's default.
    public string walkParam = "isWalking";

    // ── Respawn ───────────────────────────────────────────────────────────────

    [Header("Respawn  (fire bolt)")]
    public float respawnDelay = 5f;

    // ── Stun ──────────────────────────────────────────────────────────────────

    [Header("Stun")]
    public float stunDuration   = 3f;
    public float dizzyRotSpeed  = 180f;    // deg/sec rotation while dizzy
    public float dizzySwayWidth = 0.004f;  // XZ sway amplitude

    // ── Surface crawling ──────────────────────────────────────────────────────

    [Header("Surface Crawling")]
    public LayerMask surfaceLayers     = ~0;    // which layers count as walkable surfaces
    public float     surfaceDetectDist = 0.3f;  // raycast length for surface sticking
    public float     surfaceAlignSpeed = 12f;   // deg/sec normal-alignment rate

    // ── Private ───────────────────────────────────────────────────────────────

    enum SpiderPhase { Patrolling, Stunned, Frozen, Displaced, Dead }
    SpiderPhase _phase = SpiderPhase.Patrolling;

    int        _patrolIndex = 0;
    Vector3    _spawnPos;
    Quaternion _spawnRot;
    float      _groundY;     // world Y at spawn — used to snap back after levitate

    Collider[] _colliders;   // cached in Start; toggled on death

    Coroutine  _patrolCoroutine;
    Coroutine  _effectCoroutine;

    GameObject _igniteEffect;

    // Unified renderer list — MeshRenderer or SkinnedMeshRenderer, whichever the spider uses.
    // Built in Start(); used for hide/show/freeze material swaps.
    List<Renderer> _renderers = new List<Renderer>();

    // Spider-local freeze-material cache (keyed on Renderer for SMR support)
    readonly Dictionary<Renderer, Material[]> _origMats =
        new Dictionary<Renderer, Material[]>();

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    new void Start()
    {
        base.Start();   // auto-fills meshesToFreeze from all child MeshRenderers

        _spawnPos  = transform.position;
        _spawnRot  = transform.rotation;
        _groundY   = transform.position.y;

        // Cache non-trigger colliders (used to prevent targeting during death)
        _colliders = GetComponentsInChildren<Collider>(true);

        // Build unified renderer list — prefer MeshRenderer, fall back to SkinnedMeshRenderer.
        // base.Start() auto-fills meshesToFreeze from MeshRenderers; spiders have none,
        // so we search for SkinnedMeshRenderers instead.
        _renderers.Clear();
        _origMats.Clear();

        if (meshesToFreeze != null && meshesToFreeze.Count > 0)
        {
            foreach (var mr in meshesToFreeze)
                if (mr != null) _renderers.Add(mr);
        }
        else
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                _renderers.Add(smr);
        }

        foreach (var r in _renderers)
            _origMats[r] = r.materials;

        SetWalk(true);
        _patrolCoroutine = StartCoroutine(PatrolLoop());
    }

    // ── Spell dispatch ────────────────────────────────────────────────────────

    public override void ApplySpell(string spell)
    {
        if (_phase == SpiderPhase.Dead) return;  // hidden spider can't be targeted,
                                                  // but guard just in case

        switch (spell)
        {
            case "squiggle":   StartStun();     break;
            case "fire_bolt":  StartDeath();    break;
            case "circle_ccw": StartFreeze();   break;
            case "circle_cw":  StartIgnite();   break;
            case "square":     StartPull();     break;
            case "swipe_down": StartPush();     break;
            case "swipe_up":   StartLevitate(); break;
            // triangle (unlock) and spiral (reveal) intentionally ignored
        }
    }

    // ── Patrol ────────────────────────────────────────────────────────────────

    IEnumerator PatrolLoop()
    {
        while (true)
        {
            if (patrolPoints == null || patrolPoints.Count == 0)
            {
                yield return null;
                continue;
            }

            Transform wp = patrolPoints[_patrolIndex];
            if (wp == null)
            {
                AdvanceIndex();
                yield return null;
                continue;
            }

            // Walk toward waypoint in full 3D space (supports walls + ground)
            while (Vector3.Distance(transform.position, wp.position) > 0.15f)
            {
                SteerToward(wp.position, moveSpeed);
                yield return null;
            }

            AdvanceIndex();
        }
    }

    void AdvanceIndex()
    {
        if (patrolPoints != null && patrolPoints.Count > 0)
            _patrolIndex = (_patrolIndex + 1) % patrolPoints.Count;
    }

    void SteerToward(Vector3 worldTarget, float speed)
    {
        Vector3 dir = worldTarget - transform.position;
        if (dir.sqrMagnitude < 0.0001f) return;
        dir = dir.normalized;

        transform.position += dir * speed * Time.deltaTime;

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            Quaternion.LookRotation(dir, transform.up),
            turnSpeed * Time.deltaTime);
    }

    // ── Patrol control ────────────────────────────────────────────────────────

    void StopPatrol()
    {
        if (_patrolCoroutine != null)
        {
            StopCoroutine(_patrolCoroutine);
            _patrolCoroutine = null;
        }
    }

    /// Raycast along local -up to find the nearest surface and snap to it.
    /// Falls back to spawn transform if nothing is found within 2 m.
    void SnapToSurface()
    {
        Vector3 origin = transform.position + transform.up * surfaceDetectDist;
        if (Physics.Raycast(origin, -transform.up, out RaycastHit hit, surfaceDetectDist + 1.5f, surfaceLayers))
        {
            transform.position = hit.point;
            transform.rotation = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
        }
        else
        {
            // No surface found — reset to spawn (handles edge cases like being flung off geometry)
            transform.position = _spawnPos;
            transform.rotation = _spawnRot;
        }
    }

    /// Stick the spider to the surface every movement frame:
    /// projects onto the hit point and smoothly aligns the normal.
    void StickToSurface()
    {
        Vector3 origin = transform.position + transform.up * surfaceDetectDist;
        if (Physics.Raycast(origin, -transform.up, out RaycastHit hit, surfaceDetectDist * 2f, surfaceLayers))
        {
            transform.position = hit.point;
            Quaternion alignRot = Quaternion.FromToRotation(transform.up, hit.normal) * transform.rotation;
            transform.rotation  = Quaternion.Slerp(transform.rotation, alignRot, Time.deltaTime * surfaceAlignSpeed);
        }
    }

    /// Stop current patrol (if any) and restart it fresh.
    /// Snaps to nearest surface first so spells that left the spider airborne
    /// (levitate, freeze mid-air, stun mid-air) don't patrol at the wrong position.
    void ResumePatrol()
    {
        StopPatrol();
        SnapToSurface();
        _phase           = SpiderPhase.Patrolling;
        SetWalk(true);
        _patrolCoroutine = StartCoroutine(PatrolLoop());
    }

    // ── CancelEffect — the universal spell-interrupt reset ────────────────────
    //
    // Must be called at the top of every StartXxx() method.
    // Guarantees a clean slate no matter what state the spider was in:
    //   • Stops the current effect coroutine
    //   • Destroys ignite VFX child
    //   • Restores original materials (no-op if not frozen)
    //   • Re-enables meshes + colliders (no-op if not dead)
    //   • Snaps Y to _groundY (no-op if not levitating)
    //
    // Does NOT stop patrol — callers decide that based on whether the new
    // spell requires movement or not.
    void CancelEffect()
    {
        if (_effectCoroutine != null)
        {
            StopCoroutine(_effectCoroutine);
            _effectCoroutine = null;
        }

        // ── Ignite cleanup ────────────────────────────────────────────────────
        if (_igniteEffect != null) { Destroy(_igniteEffect); _igniteEffect = null; }

        // ── Freeze cleanup ────────────────────────────────────────────────────
        RestoreMaterials();     // safe no-op when materials are already original

        // ── Death cleanup ─────────────────────────────────────────────────────
        ShowMeshes();           // safe no-op when meshes are already visible
        EnableColliders();      // safe no-op when colliders are already enabled

        // Levitate cleanup: do NOT snap to ground here.
        // Freeze / stun / ignite should apply at whatever height the spider is at.
        // SnapToGround() is called only in ResumePatrol() so the spider grounds
        // itself exactly when it starts walking again.
    }

    // ── Mesh / collider helpers ───────────────────────────────────────────────

    void HideMeshes()
    {
        foreach (var r in _renderers)
            if (r != null) r.enabled = false;
    }

    void ShowMeshes()
    {
        foreach (var r in _renderers)
            if (r != null) r.enabled = true;
    }

    void DisableColliders()
    {
        foreach (var c in _colliders)
            if (c != null) c.enabled = false;
    }

    void EnableColliders()
    {
        foreach (var c in _colliders)
            if (c != null) c.enabled = true;
    }

    // ── Material helpers ──────────────────────────────────────────────────────

    void ApplyIceMaterials()
    {
        if (SpellManager.Instance == null) return;
        Material ice     = SpellManager.Instance.iceMaterial;
        Material overlay = SpellManager.Instance.iceOverlayMaterial;

        foreach (var r in _renderers)
        {
            if (r == null || !_origMats.ContainsKey(r)) continue;
            int  count   = _origMats[r].Length;
            var  newMats = new List<Material>(count + 1);
            for (int i = 0; i < count; i++)
                newMats.Add(ice != null ? ice : _origMats[r][i]);
            if (overlay != null) newMats.Add(overlay);
            r.materials = newMats.ToArray();
        }
    }

    void RestoreMaterials()
    {
        foreach (var r in _renderers)
        {
            if (r == null || !_origMats.ContainsKey(r)) continue;
            r.materials = _origMats[r];
        }
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    void SetWalk(bool on)
    {
        if (spiderAnimator != null && !string.IsNullOrEmpty(walkParam))
            spiderAnimator.SetBool(walkParam, on);
    }

    static Vector3 HorizontalDirToCamera(Vector3 from)
    {
        if (Camera.main == null) return Vector3.forward;
        Vector3 cam = Camera.main.transform.position;
        Vector3 dir = new Vector3(cam.x - from.x, 0f, cam.z - from.z);
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.forward;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  SPELL EFFECTS
    // ═════════════════════════════════════════════════════════════════════════

    // ── STUN ─────────────────────────────────────────────────────────────────

    void StartStun()
    {
        CancelEffect();
        StopPatrol();
        _phase           = SpiderPhase.Stunned;
        _effectCoroutine = StartCoroutine(StunEffect());
    }

    IEnumerator StunEffect()
    {
        SetWalk(false);   // legs stop while dizzy

        float elapsed = 0f;
        while (elapsed < stunDuration)
        {
            transform.Rotate(Vector3.up, dizzyRotSpeed * Time.deltaTime);
            transform.position += transform.right * (Mathf.Sin(elapsed * 8f) * dizzySwayWidth);
            elapsed += Time.deltaTime;
            yield return null;
        }

        ResumePatrol();
    }

    // ── FIRE BOLT (death + respawn) ───────────────────────────────────────────
    // Only meshes and colliders are hidden/disabled so the bolt impact VFX
    // (which may be a child of this GameObject) can finish playing.

    void StartDeath()
    {
        CancelEffect();
        StopPatrol();
        _phase           = SpiderPhase.Dead;
        _effectCoroutine = StartCoroutine(DeathEffect());
    }

    IEnumerator DeathEffect()
    {
        SetWalk(false);
        HideMeshes();
        DisableColliders();

        yield return new WaitForSeconds(respawnDelay);

        // Respawn
        transform.position = _spawnPos;
        //transform.rotation = _spawnRot;
        _patrolIndex       = 0;
        SnapToSurface();
        ShowMeshes();
        EnableColliders();
        ResumePatrol();
    }

    // ── FREEZE ───────────────────────────────────────────────────────────────

    void StartFreeze()
    {
        CancelEffect();
        StopPatrol();
        _phase           = SpiderPhase.Frozen;
        _effectCoroutine = StartCoroutine(FreezeEffect());
    }

    IEnumerator FreezeEffect()
    {
        SetWalk(false);
        ApplyIceMaterials();

        float dur = SpellManager.Instance != null ? SpellManager.Instance.resetTimer : 5f;
        yield return new WaitForSeconds(dur);

        RestoreMaterials();
        ResumePatrol();
    }

    // ── IGNITE ───────────────────────────────────────────────────────────────
    // Spider keeps patrolling. Fire is a visual rider only.

    void StartIgnite()
    {
        CancelEffect();
        // Ensure spider is walking regardless of what was interrupted
        // (e.g. frozen → ignite should resume movement)
        ResumePatrol();
        _effectCoroutine = StartCoroutine(IgniteEffect());
    }

    IEnumerator IgniteEffect()
    {
        if (SpellManager.Instance?.firePrefab != null)
        {
            _igniteEffect = Instantiate(SpellManager.Instance.firePrefab, transform);
            _igniteEffect.transform.localPosition = Vector3.zero;
            _igniteEffect.transform.localRotation = Quaternion.identity;
        }

        float dur = SpellManager.Instance != null ? SpellManager.Instance.resetTimer : 5f;
        yield return new WaitForSeconds(dur);

        if (_igniteEffect != null) { Destroy(_igniteEffect); _igniteEffect = null; }
        // Patrol already running — nothing else to clean up
    }

    // ── PULL ─────────────────────────────────────────────────────────────────

    void StartPull()
    {
        CancelEffect();
        StopPatrol();
        _phase           = SpiderPhase.Displaced;
        _effectCoroutine = StartCoroutine(PullEffect());
    }

    IEnumerator PullEffect()
    {
        SetWalk(true);   // legs animate throughout the glide
        var   sm       = SpellManager.Instance;
        float dist     = sm != null ? sm.pullDistance : 2f;
        float spellSpd = sm != null ? sm.moveSpeed    : 3f;

        Vector3 origin = transform.position;
        Vector3 pulled = origin + HorizontalDirToCamera(origin) * dist;

        yield return GlideTo(pulled, spellSpd);   // fast spell-speed pull
        yield return GlideTo(origin, moveSpeed);  // slow walk back

        ResumePatrol();
    }

    // ── PUSH ─────────────────────────────────────────────────────────────────

    void StartPush()
    {
        CancelEffect();
        StopPatrol();
        _phase           = SpiderPhase.Displaced;
        _effectCoroutine = StartCoroutine(PushEffect());
    }

    IEnumerator PushEffect()
    {
        SetWalk(true);
        var   sm       = SpellManager.Instance;
        float dist     = sm != null ? sm.pushDistance : 3f;
        float spellSpd = sm != null ? sm.moveSpeed    : 3f;

        Vector3 origin = transform.position;
        Vector3 pushed = origin + (-HorizontalDirToCamera(origin)) * dist;

        yield return GlideTo(pushed, spellSpd);
        yield return GlideTo(origin, moveSpeed);

        ResumePatrol();
    }

    // ── LEVITATE ─────────────────────────────────────────────────────────────

    void StartLevitate()
    {
        CancelEffect();
        StopPatrol();
        _phase           = SpiderPhase.Displaced;
        _effectCoroutine = StartCoroutine(LevitateEffect());
    }

    IEnumerator LevitateEffect()
    {
        var   sm        = SpellManager.Instance;
        float height    = sm != null ? sm.levitateHeight    : 1.5f;
        float riseSpd   = sm != null ? sm.levitateRiseSpeed : 2f;
        float floatFreq = sm != null ? sm.levitateFloatFreq : 1.2f;
        float floatAmp  = sm != null ? sm.levitateFloatAmp  : 0.12f;
        float holdDur   = sm != null ? sm.resetTimer        : 5f;

        SetWalk(true);   // legs wriggle helplessly in the air

        Vector3 groundPos = transform.position;   // _groundY already snapped in CancelEffect
        Vector3 floatPos  = groundPos + Vector3.up * height;
        float   riseTime  = Mathf.Max(0.01f, height / riseSpd);

        // Rise
        for (float t = 0f; t < riseTime; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(
                groundPos, floatPos, Mathf.SmoothStep(0f, 1f, t / riseTime));
            yield return null;
        }
        transform.position = floatPos;

        // Float + bob
        for (float elapsed = 0f; elapsed < holdDur; elapsed += Time.deltaTime)
        {
            float bob = Mathf.Sin(elapsed * floatFreq * Mathf.PI * 2f) * floatAmp;
            transform.position = new Vector3(floatPos.x, floatPos.y + bob, floatPos.z);
            yield return null;
        }

        // Descend
        Vector3 topPos = transform.position;
        for (float t = 0f; t < riseTime; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(
                topPos, groundPos, Mathf.SmoothStep(0f, 1f, t / riseTime));
            yield return null;
        }
        transform.position = groundPos;

        ResumePatrol();
    }

    // ── GlideTo ───────────────────────────────────────────────────────────────
    // Moves in full 3D space at the given speed, sticking to whatever surface
    // is beneath the spider each frame.  Works on walls, ceilings and ground.

    IEnumerator GlideTo(Vector3 target, float speed)
    {
        Vector3 from = transform.position;
        float   dist = Vector3.Distance(from, target);
        if (dist < 0.01f) yield break;

        float   dur = dist / Mathf.Max(0.01f, speed);
        Vector3 dir = (target - from).normalized;

        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            transform.position = Vector3.Lerp(from, target, s);

            if (dir != Vector3.zero)
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    Quaternion.LookRotation(dir, transform.up),
                    turnSpeed * Time.deltaTime);

            yield return null;
        }

        transform.position = target;
    }
}
