using UnityEngine; 
using UnityEngine.Rendering;
using Meta.XR.MRUtilityKit;
public class PassthroughController : MonoBehaviour
{
    [SerializeField] private GameObject[] gameObjects;
    [SerializeField] private OVRManager ovrManager;
    [SerializeField] private Camera centerEyeCamera;
    [SerializeField] MRUK mruk;
    [SerializeField] GameObject locomotor;

    private bool isPassthrough = false;

    private void Start()
    {
        // Ensure passthrough is off initially
        ovrManager.enableMixedReality = false;
        ovrManager.isInsightPassthroughEnabled = false;

        // Set camera to skybox on start
        if (centerEyeCamera != null)
        {
            centerEyeCamera.clearFlags = CameraClearFlags.Skybox;
        }
    }

    private void TogglePassthrough(bool enable)
    {
        // Toggle model visibility
        foreach (var obj in gameObjects)
        {
            obj.SetActive(!enable);
        }

        // Toggle passthrough
        ovrManager.isInsightPassthroughEnabled = enable;
        ovrManager.enableMixedReality = enable;
        //locomotor.SetActive(!enable);

        mruk.EnableWorldLock = enable;
        // Update camera background
        if (centerEyeCamera != null)
        {
            if (enable)
            {
                centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                centerEyeCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // Transparent black
            }
            else
            {
                centerEyeCamera.clearFlags = CameraClearFlags.Skybox;
            }
        }
    }

    public bool IsPassthrough => isPassthrough;

    /// Set passthrough to an explicit state (safe to call even if already in that state).
    public void SetPassthrough(bool enable)
    {
        if (isPassthrough == enable) return;
        isPassthrough = enable;
        TogglePassthrough(isPassthrough);
    }

    public void TogglePassthrough()
    {
        isPassthrough = !isPassthrough;
        TogglePassthrough(isPassthrough);
    }
}


