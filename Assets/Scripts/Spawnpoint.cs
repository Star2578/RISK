using UnityEngine;

public class Spawnpoint : MonoBehaviour
{
    public float area = 1f;
    
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, area);
    }
}
