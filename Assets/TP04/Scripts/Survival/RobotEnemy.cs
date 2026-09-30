using UnityEngine;

namespace TP04.Survival
{
    /// <summary>
    /// Enemy robot: walks toward the player until it is in range, then shoots at them.
    /// Dies when it has taken enough damage from the player's bullets.
    /// </summary>
    public class RobotEnemy : MonoBehaviour, IDamageable
    {
        [Header("Déplacement")]
        [SerializeField]
        [Tooltip("Walking speed, in meters per second")]
        float m_MoveSpeed = 1.2f;

        [SerializeField]
        [Tooltip("Distance at which the robot stops walking and starts shooting")]
        float m_StopDistance = 4f;

        [SerializeField]
        [Tooltip("How fast the robot turns to face the player, in degrees per second")]
        float m_TurnSpeed = 180f;

        [SerializeField]
        [Tooltip("Layers the robot refuses to walk through: walls, cover, crates, other robots")]
        LayerMask m_ObstacleMask = ~0;

        [SerializeField]
        [Tooltip("Height the obstacle probe starts at, so the ground is not mistaken for a wall")]
        float m_StepHeight = 0.3f;

        [SerializeField]
        [Tooltip("Clearance kept against obstacles, in meters")]
        float m_ObstacleSkin = 0.05f;

        [Header("Tir")]
        [SerializeField]
        [Tooltip("Projectile fired at the player")]
        GameObject m_ProjectilePrefab = null;

        [SerializeField]
        [Tooltip("Muzzle of the robot's weapon. Falls back to a point in front of the chest")]
        Transform m_FirePoint = null;

        [SerializeField]
        [Tooltip("Seconds between two shots")]
        float m_FireInterval = 2.2f;

        [SerializeField]
        [Tooltip("Extra delay before the first shot, so a fresh robot is not instantly lethal")]
        float m_FirstShotDelay = 1.5f;

        [SerializeField]
        [Tooltip("Aiming error in degrees. 0 means the robot never misses")]
        float m_AimSpread = 4f;

        [SerializeField]
        [Tooltip("Maximum distance at which the robot bothers shooting")]
        float m_FireRange = 14f;

        [Header("Vie")]
        [SerializeField]
        [Tooltip("Damage the robot absorbs before dying. A bullet deals 1 by default")]
        float m_MaxHealth = 2f;

        [SerializeField]
        [Tooltip("Seconds the corpse stays in the scene before being removed")]
        float m_DespawnDelay = 3f;

        [SerializeField]
        [Tooltip("Optional effect spawned where a bullet lands")]
        GameObject m_HitEffectPrefab = null;

        [Header("Animations (états de l'Animator)")]
        [SerializeField]
        string m_IdleState = "Idle_Shoot_Ar";

        [SerializeField]
        string m_WalkState = "WalkFront_Shoot_AR";

        [SerializeField]
        string m_ShootState = "Shoot_SingleShot_AR";

        [SerializeField]
        string m_DieState = "Die";

        [SerializeField]
        [Tooltip("Seconds the shooting animation is allowed to play before idle takes over again")]
        float m_ShootStateDuration = 0.6f;

        public bool isDead { get; private set; }

        // Straight ahead first, then wider and wider on both sides until something is free.
        static readonly float[] k_SteeringAngles = { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f };

        readonly RaycastHit[] m_ObstacleHits = new RaycastHit[8];

        EnemySpawner m_Spawner;
        Transform m_Target;
        Animator m_Animator;
        Collider[] m_Colliders;
        CapsuleCollider m_Body;
        Rigidbody m_Rigidbody;
        float m_Health;
        float m_NextFireTime;
        float m_AnimationLockedUntil;
        string m_CurrentState;

        void Awake()
        {
            m_Animator = GetComponentInChildren<Animator>();
            m_Colliders = GetComponentsInChildren<Collider>();
            m_Body = GetComponent<CapsuleCollider>();
            m_Rigidbody = GetComponent<Rigidbody>();
            m_Health = m_MaxHealth;
        }

        void Start()
        {
            m_NextFireTime = Time.time + m_FirstShotDelay;

            if (m_Target == null)
            {
                var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (origin != null && origin.Camera != null)
                    m_Target = origin.Camera.transform;
            }

            PlayState(m_IdleState);
        }

        /// <summary>Called by the spawner right after instantiation.</summary>
        public void Initialize(EnemySpawner spawner, Transform target)
        {
            m_Spawner = spawner;
            m_Target = target;
        }

        void Update()
        {
            if (isDead || m_Target == null)
                return;

            var manager = SurvivalGameManager.instance;
            if (manager != null && manager.state != GameState.Playing)
                return;

            Vector3 toTarget = m_Target.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            FaceTarget(toTarget);

            bool walking = distance > m_StopDistance;
            if (walking)
                walking = TryWalk(toTarget / Mathf.Max(distance, 0.0001f), m_MoveSpeed * Time.deltaTime);

            if (Time.time >= m_AnimationLockedUntil)
                PlayState(walking ? m_WalkState : m_IdleState);

            if (!walking && distance <= m_FireRange && Time.time >= m_NextFireTime)
                Fire();
        }

        /// <summary>
        /// Moves the robot by <paramref name="step"/> without going through anything solid.
        /// If the straight line to the player is blocked it tries increasingly wide angles on
        /// both sides, which makes the robot skirt walls, cover and the other robots.
        /// </summary>
        /// <returns>Whether the robot actually moved, so the walk animation matches.</returns>
        bool TryWalk(Vector3 desiredDirection, float step)
        {
            if (step <= 0f)
                return false;

            foreach (float angle in k_SteeringAngles)
            {
                Vector3 direction = angle == 0f
                    ? desiredDirection
                    : Quaternion.AngleAxis(angle, Vector3.up) * desiredDirection;

                if (IsBlocked(direction, step + m_ObstacleSkin))
                    continue;

                transform.position += direction * step;
                return true;
            }

            return false;
        }

        bool IsBlocked(Vector3 direction, float distance)
        {
            if (m_Body == null)
                return false;

            // Capsule in world space, shrunk a little so the robot does not scrape every surface.
            float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
            float radius = Mathf.Max(0.05f, m_Body.radius * scale - m_ObstacleSkin);
            Vector3 centre = transform.TransformPoint(m_Body.center);
            float height = m_Body.height * transform.lossyScale.y;

            // Start the capsule above the feet, otherwise the ground and the teleport area
            // register as walls and the robot never takes a single step.
            float bottomY = centre.y - height * 0.5f + m_StepHeight + radius;
            float topY = Mathf.Max(bottomY, centre.y + height * 0.5f - radius);

            var bottom = new Vector3(centre.x, bottomY, centre.z);
            var top = new Vector3(centre.x, topY, centre.z);

            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, direction, m_ObstacleHits,
                distance, m_ObstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = m_ObstacleHits[i].collider;

                if (hit == null || hit.transform.IsChildOf(transform))
                    continue;

                // The player is not an obstacle, the robot already stops at its firing distance.
                if (hit.GetComponentInParent<PlayerHealth>() != null)
                    continue;

                return true;
            }

            return false;
        }

        void FaceTarget(Vector3 flatDirection)
        {
            if (flatDirection.sqrMagnitude < 0.0001f)
                return;

            Quaternion wanted = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, m_TurnSpeed * Time.deltaTime);
        }

        void Fire()
        {
            m_NextFireTime = Time.time + m_FireInterval;

            if (m_ProjectilePrefab == null)
                return;

            Vector3 origin = FirePosition();
            Vector3 direction = (m_Target.position - origin).normalized;

            if (m_AimSpread > 0f)
            {
                direction = Quaternion.Euler(
                    Random.Range(-m_AimSpread, m_AimSpread),
                    Random.Range(-m_AimSpread, m_AimSpread),
                    0f) * direction;
            }

            GameObject projectile = Instantiate(m_ProjectilePrefab, origin, Quaternion.LookRotation(direction));

            // The bolt sweeps a sphere rather than using physics, so it skips its shooter by
            // hierarchy rather than through Physics.IgnoreCollision.
            if (projectile.TryGetComponent(out EnemyProjectile bolt))
                bolt.SetShooter(transform);

            PlayState(m_ShootState, forceRestart: true);
            m_AnimationLockedUntil = Time.time + m_ShootStateDuration;
        }

        Vector3 FirePosition()
        {
            if (m_FirePoint != null)
                return m_FirePoint.position;

            return transform.position + Vector3.up * 1.4f + transform.forward * 0.3f;
        }

        public void TakeDamage(float amount, Vector3 point, Vector3 normal)
        {
            if (isDead)
                return;

            if (m_HitEffectPrefab != null)
            {
                Quaternion rotation = normal.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(normal)
                    : Quaternion.identity;
                Destroy(Instantiate(m_HitEffectPrefab, point, rotation), 2f);
            }

            m_Health -= amount;

            if (m_Health <= 0f)
                Die();
        }

        void Die()
        {
            isDead = true;

            foreach (Collider own in m_Colliders)
            {
                if (own != null)
                    own.enabled = false;
            }

            if (m_Rigidbody != null)
                m_Rigidbody.isKinematic = true;

            PlayState(m_DieState, forceRestart: true);

            var manager = SurvivalGameManager.instance;
            if (manager != null)
                manager.NotifyRobotKilled();

            if (m_Spawner != null)
                m_Spawner.NotifyRobotRemoved(this);

            Destroy(gameObject, m_DespawnDelay);
        }

        // The pack's controller has no parameters, so states are played by name instead of
        // being driven by triggers. Unknown names are skipped rather than spamming warnings.
        void PlayState(string stateName, bool forceRestart = false)
        {
            if (m_Animator == null || string.IsNullOrEmpty(stateName))
                return;

            if (!forceRestart && m_CurrentState == stateName)
                return;

            int hash = Animator.StringToHash(stateName);
            if (!m_Animator.HasState(0, hash))
                return;

            m_CurrentState = stateName;
            m_Animator.CrossFadeInFixedTime(hash, 0.15f, 0);
        }

        void OnDestroy()
        {
            if (m_Spawner != null)
                m_Spawner.NotifyRobotRemoved(this);
        }
    }
}
