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

    void Start()
    {
        _stylusHandler = this.GetComponent<StylusHandler>();
    }
    Vector3 GetTipPosition()
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

                //SEND STROKE FOR RECOGNITION LATER
                Debug.Log($"Stroke finished. Points collected = {strokePoints.Count}");

                /*
                var result = ShapeRecognizer.Analyze(strokePoints);
                Debug.Log($"Shape: {result.Shape}, cw: {result.Clockwise}, revs: {result.Revolutions}, corners: {result.Corners}, conf: {result.Confidence}");
                if (result.Shape == ShapeRecognizer.ShapeType.Circle) Debug.Log("Circle");
                else if (result.Shape == ShapeRecognizer.ShapeType.Triangle) Debug.Log("Triangle");*/
            }
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
