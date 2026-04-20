using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WandHandler : MonoBehaviour
{
    public VrStylusHandler _stylusHandler;
    public bool isSwirling;
    public TrailRenderer controllerTrailRenderer, stylusTrailRenderer;
    private TrailRenderer trailRenderer;
    public GameObject spellCanvas;
    public bool useStylus = false;
    
    // NEW: store recorded points here
    public List<Vector3> strokePoints = new List<Vector3>();

    // sampling thresholds
    public float minSampleDistance = 0.01f;    // record only if moved at least 1cm
    public float maxSamples = 300;              // safety cap
    Vector3 lastSamplePos;

    float hapticTimer = 0f;
    float hapticInterval = 0.0013f; // time between pulses

    public GestureQuickRecorder gestureRecorder;

    public GameObject triangle, circle, circle_ccw, square, swipedown, swipeup, squiggle, spiral, bolt;

    public WandTargeting targeting;

    bool _backButtonLastFrame = false;  // edge detection for stylus back button (bolt)
    void Start()
    {
        SwitchToController();
        //trailRenderer = controllerTrailRenderer;
    }
    public Vector3 GetTipPosition()
    {
        return trailRenderer.transform.position;
    }
    bool IsStylusActive()
    {
        return(_stylusHandler.isStylusActive);
    }
    void RecordStrokePoint()
    {
        if (strokePoints.Count >= maxSamples) return;

        Vector3 tip = GetTipPosition();
        if (Vector3.Distance(tip, lastSamplePos) >= minSampleDistance)
        {
            strokePoints.Add(tip);
            lastSamplePos = tip;
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Detect device
        if (IsStylusActive())
        {
            if (!useStylus)
            {
                useStylus = true;
                SwitchToStylus();
            }
        }
        else
        {
            if (useStylus)
            {
                useStylus = false;
                SwitchToController();
            }
        }

        //float analogInput = _stylusHandler.CurrentState.cluster_middle_value;

        float analogInput = GetCurrentInput();

        if (analogInput > 0.25f)
        {
            if (!isSwirling)
            {
                isSwirling = true;
                //targeting.LockTarget();
                trailRenderer.enabled = true;

                strokePoints.Clear();
                Vector3 tip = GetTipPosition();
                strokePoints.Add(tip);
                lastSamplePos = tip;
            }

            RecordStrokePoint();
            TriggerHaptics(analogInput);
        }
        else if (analogInput < 0.15f)
        {
            if (isSwirling)
            {
                isSwirling = false;
                trailRenderer.enabled = false;
                OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
                hapticTimer = 0f;
                RecognizeStroke();
            }
        }

        // ── Fire bolt: single tap, separate from gesture drawing ─────────────
        // Stylus : back button (cluster_back)
        // Controller : A button (Button.One) — index trigger is already used for
        //              gesture drawing so we map bolt to the face button instead.
        //              Change OVRInput.Button.One to .Two (B) if you prefer.
        CheckBoltInput();
    }

    void CheckBoltInput()
    {
        bool boltPressed = false;

        if (useStylus)
        {
            bool backNow = _stylusHandler.CurrentState.cluster_back_value;
            boltPressed           = backNow && !_backButtonLastFrame; // rising edge only
            _backButtonLastFrame  = backNow;
        }
        else
        {
            // GetDown fires only on the frame the button is first pressed
            boltPressed = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
        }

        if (boltPressed)
            FireBolt();
    }
    void SwitchToStylus()
    {
        trailRenderer = stylusTrailRenderer;

        controllerTrailRenderer.enabled = false;
        stylusTrailRenderer.enabled = false;

        Debug.Log("Switched to Stylus");

        spellCanvas.transform.SetParent(stylusTrailRenderer.transform, false);
        RectTransform rt = spellCanvas.GetComponent<RectTransform>();
       // rt.localPosition = new Vector3(0, 0.25f, 0.25f);
        Vector3 euler = rt.localEulerAngles;
        euler.x = -55f;
        euler.z = 180f;
        rt.localEulerAngles = euler; 
        targeting.SetTip(stylusTrailRenderer.transform);
    }

    void SwitchToController()
    {
        trailRenderer = controllerTrailRenderer;

        controllerTrailRenderer.enabled = false;
        stylusTrailRenderer.enabled = false;

        Debug.Log("Switched to Controller");

        spellCanvas.transform.SetParent(controllerTrailRenderer.transform, false);
        RectTransform rt = spellCanvas.GetComponent<RectTransform>();
        Vector3 euler = rt.localEulerAngles;
        euler.x = 55f;
        euler.z = -180f;
        rt.localEulerAngles = euler;
        targeting.SetTip(controllerTrailRenderer.transform);
    }

    float GetCurrentInput()
    {
        if (useStylus)
        {
            return _stylusHandler.CurrentState.cluster_middle_value;
        }
        else
        {
            return OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        }
    }
    void RecognizeStroke()
    {
        if (gestureRecorder == null || gestureRecorder.recognizer == null)
        {
            Debug.LogWarning("Gesture recognizer not assigned.");
            return;
        }

        if (strokePoints == null || strokePoints.Count < 6)
        {
            Debug.Log("Stroke too short to recognize.");
            return;
        }

        var result = gestureRecorder.recognizer.Recognize(strokePoints);
        bool isRecognized = result.label != "Unknown";
        Debug.Log(
            $"GESTURE RESULT == {result.label} | score={result.score:F2} | margin={result.margin:F2}"
        );

        // TEMP: simple routing
        switch (result.label)
        {
            case "circle_cw":
                circle.SetActive(true);
                Debug.Log("circle clock wise");
                break;

            case "triangle":
                triangle.SetActive(true);
                Debug.Log("triangle");
                break;
            case "circle_ccw":
                circle_ccw.SetActive(true);
                Debug.Log("Anti Clockwise Circle aaaaa");
                break;
            case "spiral":
                spiral.SetActive(true);
                Debug.Log("Spiral bbbbb");
                break;
            case "squiggle":
                squiggle.SetActive(true);
                Debug.Log("Squiggle ccccccc");
                break;
            case "swipe_down":
                swipedown.SetActive(true);
                Debug.Log("Swipe_down dddddddd");
                break;
            case "swipe_up":
                swipeup.SetActive(true);
                Debug.Log("swipe_up eeeeee");
                break;
            case "square":
                square.SetActive(true);
                Debug.Log("Square fffffff");
                break;
            case "Unknown":
                Debug.Log("Unknown gesture");
                break;
        }
        // Capture target and remove highlight BEFORE firing the spell event.
        // This ensures spell effects (e.g. freeze material swap) always land on
        // the object's clean original materials, not on the fresnel highlight.
        var target = targeting.GetLockedTarget();
        targeting.UnlockTarget();

        if (isRecognized)
        {
            TriggerSuccessHaptics();
            SpellEvents.OnSpellCast?.Invoke(result.label, target);
        }
    }


    void FireBolt()
    {
        if (SpellManager.Instance == null) return;

        // For fire bolt there is no gesture swirl, so lockedTarget is never set.
        // Fall back to currentTarget — whatever is currently in the targeting cone.
        var target = targeting.GetLockedTarget() ?? targeting.currentTarget;
        targeting.UnlockTarget();

        bool fired = SpellManager.Instance.TryFireBolt(
            GetTipPosition(),
            trailRenderer.transform.rotation,
            target);

        if (fired)
        {
            if (bolt != null) bolt.SetActive(true);
            TriggerSuccessHaptics();
        }
    }

    private void TriggerHaptics(float input)
    {
        const float dampingFactor = 0.6f;
        const float pulseDuration = 0.01f;

        float pressure = input * dampingFactor;

        if (useStylus)  // Stylus
        {
            if (_stylusHandler is VrStylusHandler vrStylus)
            {
                vrStylus.TriggerHapticPulse(pressure, pulseDuration);
            }
        }
        else // Controller (NEW)
        {
            hapticTimer += Time.deltaTime;

            if (hapticTimer >= hapticInterval)
            {
                OVRInput.SetControllerVibration(1f, pressure, OVRInput.Controller.RTouch);
                StartCoroutine(StopControllerHapticsAfter(pulseDuration));
                hapticTimer = 0f;
            }
        }
        
    }

    void TriggerSuccessHaptics(float strength = 1f)
    {
        float duration = 0.5f;

        if (useStylus) // Stylus (strong pulse)
        {
            if (_stylusHandler is VrStylusHandler vrStylus)
            {
                vrStylus.TriggerHapticPulse(strength, duration);
            }
        }
        else  // Controller
        {
            OVRInput.SetControllerVibration(strength, strength, OVRInput.Controller.RTouch);
            StartCoroutine(StopControllerHapticsAfter(duration));
        }
    }

    IEnumerator StopControllerHapticsAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        OVRInput.SetControllerVibration(0, 0, OVRInput.Controller.RTouch);
    }
}
