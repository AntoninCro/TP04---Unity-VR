using UnityEngine;

namespace TP04.Survival
{
    /// <summary>
    /// Bolt fired by a robot at the player.
    /// </summary>
    /// <remarks>
    /// It does not use a Rigidbody: it sweeps a sphere along the segment it travels each frame.
    /// At these speeds a physics projectile would tunnel straight through the shield, which is
    /// only a few centimetres thick.
    /// </remarks>
    public class EnemyProjectile : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Travel speed, in meters per second")]
        float m_Speed = 12f;

        [SerializeField]
        [Tooltip("Radius of the sweep used to detect hits, in meters")]
        float m_Radius = 0.06f;

        [SerializeField]
        [Tooltip("Seconds before the bolt despawns on its own")]
        float m_Lifetime = 6f;

        [SerializeField]
        [Tooltip("Layers the bolt can hit")]
        LayerMask m_HitMask = ~0;

        [SerializeField]
        [Tooltip("Optional effect spawned when the bolt hits the player or a wall")]
        GameObject m_ImpactEffectPrefab = null;

        [SerializeField]
        [Tooltip("Optional effect spawned when the shield stops the bolt")]
        GameObject m_BlockEffectPrefab = null;

        [SerializeField]
        [Tooltip("Seconds before those effects despawn")]
        float m_EffectLifetime = 2f;

        Transform m_Shooter;

        readonly RaycastHit[] m_Hits = new RaycastHit[16];

        void Start()
        {
            if (m_Lifetime > 0f)
                Destroy(gameObject, m_Lifetime);
        }

        /// <summary>Tells the bolt which robot fired it, so it does not hit its own shooter.</summary>
        public void SetShooter(Transform shooter)
        {
            m_Shooter = shooter;
        }

        void Update()
        {
            float step = m_Speed * Time.deltaTime;
            if (step <= 0f)
                return;

            Vector3 origin = transform.position;
            Vector3 direction = transform.forward;

            if (TryGetFirstHit(origin, direction, step, out RaycastHit hit))
            {
                HandleHit(hit);
                return;
            }

            transform.position = origin + direction * step;
        }

        bool TryGetFirstHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit first)
        {
            first = default;

            int count = Physics.SphereCastNonAlloc(origin, m_Radius, direction, m_Hits, distance,
                m_HitMask, QueryTriggerInteraction.Collide);

            bool found = false;
            float best = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = m_Hits[i];

                if (candidate.collider == null)
                    continue;

                // Ignore the robot that fired it.
                if (m_Shooter != null && candidate.collider.transform.IsChildOf(m_Shooter))
                    continue;

                // Ignore other bolts so a crossfire does not cancel itself out.
                if (candidate.collider.GetComponentInParent<EnemyProjectile>() != null)
                    continue;

                if (candidate.distance < best)
                {
                    best = candidate.distance;
                    first = candidate;
                    found = true;
                }
            }

            return found;
        }

        void HandleHit(RaycastHit hit)
        {
            Vector3 point = hit.point != Vector3.zero ? hit.point : transform.position;
            Vector3 normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : -transform.forward;

            var shield = hit.collider.GetComponentInParent<ShieldBlocker>();
            if (shield != null)
            {
                shield.NotifyBlocked(point, normal);
                Spawn(m_BlockEffectPrefab, point, normal);
                Destroy(gameObject);
                return;
            }

            var player = hit.collider.GetComponentInParent<PlayerHealth>();
            if (player != null)
                player.TakeHit();

            Spawn(m_ImpactEffectPrefab, point, normal);
            Destroy(gameObject);
        }

        void Spawn(GameObject prefab, Vector3 point, Vector3 normal)
        {
            if (prefab == null)
                return;

            GameObject effect = Instantiate(prefab, point, Quaternion.LookRotation(normal));
            Destroy(effect, m_EffectLifetime);
        }
    }
}
