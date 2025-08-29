using UnityEngine;

public abstract class EntityBase : MonoBehaviour
{
    [Header("Status")]
    public float maxHealth = 100f;
    public float health = 0f;

    protected virtual void Awake()
    {
        health = maxHealth;
    }

    protected void TakeDamage(float damage)
    {
        health -= damage;
        if (health <= 0)
        {
            Die();
        }
    }

    protected abstract void Die();
}
