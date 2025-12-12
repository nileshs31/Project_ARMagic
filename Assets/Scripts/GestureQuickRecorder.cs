using System.Collections.Generic;
using UnityEngine;
using System.IO;

/// <summary>
/// Runtime gesture recorder with label-lock mode.
/// Click a label once  every stylus press+release records a gesture for that label
/// until another label is chosen or Play Mode stops.
/// Uses WandHandler.strokePoints (recommended).
/// </summary>
public class GestureQuickRecorder : MonoBehaviour
{
    [Header("References")]
    public MonoBehaviour stylusHandlerBehaviour; // VrStylusHandler
    public WandHandler wandHandler;
    public DtwKnnRecognizer recognizer;

    [Header("Recording Options")]
    public bool useWandStrokePoints = true;
    public bool autoSaveAfterAdd = true;
    public string templatesFileName = "gesture_templates.json";

    [Header("Debug / Status")]
    [HideInInspector] public string activeLabel = null;
    [HideInInspector] public string status = "Idle";

    // internal
    VrStylusHandler _stylusHandler;
    bool _wasPressedLastFrame = false;

    void Awake()
    {
        if (recognizer == null)
            recognizer = new DtwKnnRecognizer();

        if (stylusHandlerBehaviour != null)
            _stylusHandler = stylusHandlerBehaviour as VrStylusHandler;

        if (_stylusHandler == null)
            _stylusHandler = GetComponent<VrStylusHandler>();

        if (_stylusHandler == null)
            Debug.LogWarning("GestureQuickRecorder: VrStylusHandler not assigned.");

        LoadTemplatesFromFile();

    }

    void Update()
    {
        // No label selected  do nothing
        if (string.IsNullOrEmpty(activeLabel)) return;
        if (_stylusHandler == null || wandHandler == null) return;

        float input = Mathf.Max(
            _stylusHandler.CurrentState.cluster_middle_value,
            _stylusHandler.CurrentState.tip_value
        );

        bool isPressed = input > 0f;

        // Detect PRESS RELEASE
        if (_wasPressedLastFrame && !isPressed)
        {
            OnStrokeReleased();
        }
        else if (isPressed)
        {
            status = $"Recording '{activeLabel}'...";
        }
        else
        {
            status = $"Ready for '{activeLabel}' (press stylus button)";
        }

        _wasPressedLastFrame = isPressed;
    }

    void OnStrokeReleased()
    {
        if (!useWandStrokePoints)
        {
            status = "useWandStrokePoints disabled.";
            return;
        }

        if (wandHandler.strokePoints == null || wandHandler.strokePoints.Count < 6)
        {
            status = "Stroke too short. Try again.";
            return;
        }

        // Add template
        recognizer.AddTemplate(activeLabel, new List<Vector3>(wandHandler.strokePoints));
        status = $"Saved '{activeLabel}' (pts={wandHandler.strokePoints.Count})";

        if (autoSaveAfterAdd)
            SaveTemplatesToFile();
    }

    // -------------------------
    // Save / Load
    // -------------------------

    public void SaveTemplatesToFile(string filename = null)
    {
        string name = string.IsNullOrEmpty(filename) ? templatesFileName : filename;
        string path = Path.Combine(Application.persistentDataPath, name);
        recognizer.SaveTemplates(path);
        Debug.Log("GestureQuickRecorder: Templates saved to " + path);
    }

    public void LoadTemplatesFromFile(string filename = null)
    {
        string name = string.IsNullOrEmpty(filename) ? templatesFileName : filename;
        string path = Path.Combine(Application.persistentDataPath, name);
        recognizer.LoadTemplates(path);
        Debug.Log("GestureQuickRecorder: Templates loaded from " + path);


        foreach (var names in recognizer.GetTemplateNames())
        {
            Debug.Log($"Loaded gesture label: {names}");
        }
    }

    // -------------------------
    // Utility (optional)
    // -------------------------

    public void StopRecording()
    {
        activeLabel = null;
        status = "Recording stopped.";
    }
}
