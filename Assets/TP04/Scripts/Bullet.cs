using UnityEngine;

namespace TP04
{
    /// <summary>
    /// Projectile fired by <see cref="PistolShooter"/>. Despawns on impact or after a delay.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Bullet : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Seconds before the bullet despawns on its own, 0 to keep it forever")]
        float m_Lifetime = 5f;

        [SerializeField]
        [Tooltip("Optional effect spawned at the impact point, e.g. a Particle System")]
        GameObject m_ImpactEffectPrefab = null;

        [SerializeField]
        [Tooltip("Seconds before the impact effect despawns")]
        float m_ImpactEffectLifetime = 2f;

        [SerializeField]
        [Tooltip("Whether the bullet is destroyed as soon as it touches something")]
        bool m_DestroyOnImpact = true;

        void Start()
        {
            if (m_Lifetime > 0f)
                Destroy(gameObject, m_Lifetime);
        }

        void OnCollisionEnter(Collision collision)
        {
            if (m_ImpactEffectPrefab != null)
            {
                ContactPoint contact = collision.GetContact(0);
                GameObject effect = Instantiate(m_ImpactEffectPrefab, contact.point, Quaternion.LookRotation(contact.normal));
                Destroy(effect, m_ImpactEffectLifetime);
            }

            if (m_DestroyOnImpact)
                Destroy(gameObject);
        }
    }
}
