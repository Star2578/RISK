using UnityEngine;

public class VillageArea : MonoBehaviour
{
    public float x = 1f;
    public float z = 1f;
    void OnDrawGizmos()
    {
        // draw box
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, new Vector3(x, 0.1f, z));
    }
}
