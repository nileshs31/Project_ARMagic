using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WandHandler : MonoBehaviour
{
    StylusHandler _stylusHandler;
    bool isSwirling;
    public TrailRenderer trailRenderer;

    // NEW: store recorded points here
    public List<Vector3> strokePoints = new List<Vector3>();

    // sampling thresholds
    public float minSampleDistance = 0.01f;    // record only if moved at least 1cm
    public float maxSamples = 300;              // safety cap
    Vector3 lastSamplePos;

    public GestureQuickRecorder gestureRecorder;

    public GameObject trianlge, cirlce, square;
    void Start()
    {
        _stylusHandler = this.GetComponent<StylusHandler>();
    }
    public Vector3 GetTipPosition()
    {
        return trailRenderer.transform.position;
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

        float analogInput = _stylusHandler.CurrentState.cluster_middle_value;

        if (analogInput > 0)
        {
            if (!isSwirling)
            {
                isSwirling = true;
                trailRenderer.enabled = true;

                strokePoints.Clear();
                Vector3 tip = GetTipPosition();
                strokePoints.Add(tip);
                lastSamplePos = tip;
            }
            RecordStrokePoint();
            TriggerHaptics();
        }
        else
        {
            if (isSwirling)
            {
                isSwirling = false;
                trailRenderer.enabled = false;

                //SEND STROKE FOR RECOGNITION
                RecognizeStroke();
            }
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

        Debug.Log(
            $"GESTURE RESULT == {result.label} | score={result.score:F2} | margin={result.margin:F2}"
        );

        // TEMP: simple routing
        switch (result.label)
        {
            case "circle_cw":
                cirlce.SetActive(true);
                Debug.Log("circle clock wise ACCIO");
                break;

            case "triangle":
                trianlge.SetActive(true);
                Debug.Log("triangle ALOHOMORA");
                break;
            case "square":
                square.SetActive(true);
                Debug.Log("Square aaaaa");
                break;
            case "Unknown":
                Debug.Log("Unknown gesture");
                break;
        }
    }


    private void TriggerHaptics()
    {
        const float dampingFactor = 0.6f;
        const float duration = 0.01f;
        float middleButtonPressure = _stylusHandler.CurrentState.cluster_middle_value * dampingFactor;
        ((VrStylusHandler)_stylusHandler).TriggerHapticPulse(middleButtonPressure, duration);
    }
}
