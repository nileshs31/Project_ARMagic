using UnityEngine;

public class WandTargeting : MonoBehaviour
{
    public Transform tipTransform;
    public float maxAngle = 20f;
    public float maxDistance = 10f;
    public LayerMask targetLayer;

    public GameObject currentTarget;
    GameObject lockedTarget;

    public bool isLocked = false;
    ObjectHighlighter lastHighlighter;

    ObjectHighlighter currentHighlighted;
    WandHandler wandHandler;
    bool wasSwirling = false;

    private void Awake()
    {
        wandHandler = GetComponent<WandHandler>();
    }
    void Update()
    {
        if (!wasSwirling && wandHandler.isSwirling)
        {
            OnSwirlStart();
        }

        wasSwirling = wandHandler.isSwirling;

        if (!isLocked && !wandHandler.isSwirling)
        {
            currentTarget = FindTarget();
            HandleHighlight(currentTarget);
        }
    }
    void OnSwirlStart()
    {
        // Freeze current target
        if (currentTarget != null)
        {
            LockTarget();
        }
    }
    GameObject FindTarget()
    {
        Collider[] hits = Physics.OverlapSphere(tipTransform.position, maxDistance, targetLayer);

        GameObject best = null;
        float bestScore = float.MaxValue;

        foreach (var hit in hits)
        {
            Vector3 toTarget = hit.transform.position - tipTransform.position;
            float angle = Vector3.Angle(tipTransform.forward, toTarget.normalized);
            if (angle > maxAngle) continue;

            float dist = toTarget.magnitude;
            float score = angle + dist * 0.1f;

            if (score < bestScore)
            {
                bestScore = score;
                best = hit.gameObject;
            }
        }

        return best;
    }
    public void SetTip(Transform newTip)
    {
        tipTransform = newTip;
    }
    void HandleHighlight(GameObject target)
    {
        ObjectHighlighter newHighlighter = null;

        if (target != null)
            newHighlighter = target.GetComponent<ObjectHighlighter>();

        // If SAME target do NOTHING
        if (newHighlighter == lastHighlighter)
            return;

        // Remove previous ONLY if different
        if (lastHighlighter != null)
        {
            lastHighlighter.HighlightMesh(false);
        }

        // Apply new
        if (newHighlighter != null)
        {
            newHighlighter.HighlightMesh(true);
        }

        lastHighlighter = newHighlighter;
    }
    public void LockTarget()
    {
        if (currentTarget != null)
        {
            lockedTarget = currentTarget;
            isLocked = true;
        }
    }

    public void UnlockTarget()
    {
        isLocked = false;
        lockedTarget = null; 
        HandleHighlight(null);
    }

    public GameObject GetLockedTarget()
    {
        return lockedTarget;
    }
}