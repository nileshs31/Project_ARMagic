using System.Collections; 
using System.Collections.Generic;
using UnityEngine;

public class BillBoard : MonoBehaviour
{
    Transform rig;

    private void Start()
    {
        rig = Camera.main.transform;
    }
    /* private void LateUpdate()
     {
         transform.LookAt(rig.position + rig.forward /*+ new Vector3(0, 0, -0.25f));
     }*/

    private void Update()
    {
        Vector3 direction = (transform.position - rig.position).normalized;
        transform.rotation = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));

    }
}
