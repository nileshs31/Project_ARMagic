using UnityEngine;
using System.Collections;

public class ControllerGazeHint : MonoBehaviour
{
    [Header("Refs")]
    public Transform controller;        // controller transform (this.transform if attached there)
    public Camera cam;                  // XR camera (CenterEye)
    public CanvasGroup hint;            // CanvasGroup on the tutorial canvas

    [Header("Behavior")]
    [Range(1f, 25f)] public float maxAngle = 10f;   // view cone around the center of the screen
    public float maxDistance = 2.0f;               // optional distance gate
    public float dwellTime = 1.0f;                 // time to look before showing
    public float hideDelay = 0.25f;                // small hysteresis so it doesn't flicker
    public float fadeDuration = 0.2f;
    public LayerMask losMask = ~0;                 // world layers that can occlude (exclude UI)

    float timer;
    bool visible;

    public GameObject[] mesh;

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (!controller) controller = transform;
        if (hint)
        {
            hint.alpha = 0f;
            //hint.gameObject.SetActive(false);
        }
    }

    void Update()
    {
        if (!cam || !controller || !hint) return;

        Vector3 toTarget = controller.position - cam.transform.position;
        float angle = Vector3.Angle(cam.transform.forward, toTarget);
        bool inFront = Vector3.Dot(cam.transform.forward, toTarget) > 0f;
        bool inCone = angle <= maxAngle;
        bool distanceOK = toTarget.magnitude <= maxDistance;

        bool hasLoS = true;
        if (losMask != 0)
            hasLoS = !Physics.Raycast(cam.transform.position, toTarget.normalized,
                                      toTarget.magnitude, losMask, QueryTriggerInteraction.Ignore);

        bool looking = inFront && inCone && distanceOK && hasLoS;

        // dwell / hysteresis
        if (looking) timer += Time.deltaTime;
        else timer = Mathf.Max(0f, timer - Time.deltaTime * 2f);

        if (!visible && timer >= dwellTime) SetVisible(true);
        if (visible && timer <= 0f) StartCoroutine(DelayHide());
    }

    IEnumerator DelayHide() { yield return new WaitForSeconds(hideDelay); SetVisible(false); }

    void SetVisible(bool on)
    {
        if (visible == on) return;
        visible = on;
        StopAllCoroutines();
        if (mesh.Length != 0)
        {
            foreach(var i in mesh)
            {
                i.SetActive(on);
            }

        }
        StartCoroutine(Fade(on));
    }

    IEnumerator Fade(bool on)
    {
        //hint.gameObject.SetActive(true);
        float start = hint.alpha, end = on ? 1f : 0f, t = 0f;
        while (t < fadeDuration) { t += Time.deltaTime; hint.alpha = Mathf.Lerp(start, end, t / fadeDuration); yield return null; }
        hint.alpha = end;
        //if (!on) hint.gameObject.SetActive(false);
    }
}
