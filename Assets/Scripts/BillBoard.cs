using System.Collections; 
using System.Collections.Generic;
using UnityEngine;

public class BillBoard : MonoBehaviour
{
    Transform rig;

    private void Start()
    {
        if (Camera.main != null)
            rig = Camera.main.transform;
        else
            Debug.LogWarning("[BillBoard] Camera.main is null — ensure the main camera has the 'MainCamera' tag.");
    }

    private void Update()
    {
        if (rig == null) return;
        Vector3 direction = (transform.position - rig.position).normalized;
        transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
    }
}
