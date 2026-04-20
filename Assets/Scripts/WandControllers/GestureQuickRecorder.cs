using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

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

        LoadTemplatesFromStreamingAssets();

    }

    void Update()
    {
        // No label selected  do nothing
        if (string.IsNullOrEmpty(activeLabel)) return;
        if (_stylusHandler == null || wandHandler == null) return;

        float input = _stylusHandler.CurrentState.cluster_middle_value;

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

        if (wandHandler.strokePoints == null || wandHandler.strokePoints.Count < 12)
        {
            status = "Stroke too short. Try again.";
            return;
        }

        float totalDist = 0f;
        for (int i = 1; i < wandHandler.strokePoints.Count; i++)
        {
            totalDist += Vector3.Distance(
                wandHandler.strokePoints[i - 1],
                wandHandler.strokePoints[i]
            );
        }

        if (totalDist < 0.05f) // tweak (5cm)
        {
            status = "Stroke too small.";
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
#if UNITY_EDITOR
        string name = string.IsNullOrEmpty(filename) ? templatesFileName : filename;

        string dir = Path.Combine(Application.dataPath, "StreamingAssets");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, name);

        recognizer.SaveTemplates(path);
        Debug.Log("Saved DEFAULT gesture templates to StreamingAssets: " + path);

        UnityEditor.AssetDatabase.Refresh();
#else
    Debug.LogWarning("Saving gesture templates is disabled in builds.");
#endif
    }


    public void LoadTemplatesFromFile(string filename = null)
    {
        string name = string.IsNullOrEmpty(filename) ? templatesFileName : filename;
        string path = Path.Combine(Application.streamingAssetsPath, name);

        if (!File.Exists(path))
        {
            Debug.LogWarning("Gesture template file not found in StreamingAssets.");
            return;
        }

        recognizer.LoadTemplates(path);
        Debug.Log("Loaded gesture templates from StreamingAssets: " + path);

        foreach (var label in recognizer.GetTemplateNames())
            Debug.Log($"Loaded gesture label: {label}");
    }


    // -------------------------
    // Utility (optional)
    // -------------------------

    public void StopRecording()
    {
        activeLabel = null;
        status = "Recording stopped.";
    }


    void LoadTemplatesFromStreamingAssets()
    {
        StartCoroutine(LoadTemplatesCoroutine());
    }

    IEnumerator LoadTemplatesCoroutine()
    {
        string fileName = templatesFileName;
        string path = Path.Combine(Application.streamingAssetsPath, fileName);

#if UNITY_ANDROID && !UNITY_EDITOR
    using (UnityEngine.Networking.UnityWebRequest www =
           UnityEngine.Networking.UnityWebRequest.Get(path))
    {
        yield return www.SendWebRequest();

        if (www.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            Debug.LogError("Failed to load gesture templates: " + www.error);
            yield break;
        }

        string json = www.downloadHandler.text;
        recognizer.LoadTemplatesFromJson(json);
        Debug.Log("Loaded gesture templates from StreamingAssets (Android)");
    }
#else
        // Editor / PC
        if (!File.Exists(path))
        {
            Debug.LogError("Gesture template file not found: " + path);
            yield break;
        }

        string json = File.ReadAllText(path);
        recognizer.LoadTemplatesFromJson(json);
        Debug.Log("Loaded gesture templates from StreamingAssets (Editor/PC)");
#endif

        foreach (var name in recognizer.GetTemplateNames())
            Debug.Log("Loaded gesture label: " + name);
    }

}
