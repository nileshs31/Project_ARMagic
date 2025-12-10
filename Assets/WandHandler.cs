using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WandHandler : MonoBehaviour
{
    StylusHandler _stylusHandler;
    bool isSwirling;
    public TrailRenderer trailRenderer;
    // Start is called before the first frame update
    void Start()
    {
        _stylusHandler = this.GetComponent<StylusHandler>();
    }

    // Update is called once per frame
    void Update()
    {

        float analogInput = Mathf.Max(_stylusHandler.CurrentState.cluster_middle_value);

        if (analogInput > 0)
        {
            if (!isSwirling)
            {
                isSwirling = true;
                trailRenderer.enabled = true;
            }
            TriggerHaptics();
        }
        else
        {
            isSwirling = false; 
            trailRenderer.enabled = false;
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
