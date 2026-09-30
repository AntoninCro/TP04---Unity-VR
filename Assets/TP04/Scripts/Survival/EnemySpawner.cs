using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TP04.Survival
{
    /// <summary>
    /// Spawns robots on a ring around the player, inside an arc centred on the direction the
    /// player is currently facing. The spawn rate ramps up the longer the run lasts.
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Robot prefab instantiated on each spawn")]
        GameObject m_RobotPrefab = null;

        [SerializeField]
        [Tooltip("Transform the robots spawn around, usually the XR Origin camera")]
        Transform m_Player = null;

        [SerializeField]
        [Tooltip("Distance from the player, in meters")]
        float m_SpawnRadius = 7f;

        [SerializeField]
        [Tooltip("Random variation added to the radius, in meters")]
        float m_RadiusJitter = 1.5f;

        [SerializeField]
        [Tooltip("Seconds between spawns at the beginning of a run")]
        float m_StartInterval = 4f;

        [SerializeField]
        [Tooltip("Shortest possible delay between two spawns")]
        float m_MinInterval = 1.2f;

        [SerializeField]
        [Tooltip("Seconds of survival needed to reach the shortest delay")]
        float m_RampDuration = 90f;

        [SerializeField]
        [Tooltip("Hard cap on how many robots can be alive at once")]
        int m_MaxAlive = 8;

        [SerializeField]
        [Tooltip("Ground height the robots are placed at. Left empty, the spawner uses its own Y")]
        float m_GroundY = 0f;

        [Header("Limites de l'arène")]
        [SerializeField]
        [Tooltip("Centre of the arena. Left empty, the spawner uses its own position")]
        Transform m_ArenaCenter = null;

        [SerializeField]
        [Tooltip("No robot is ever placed further than this from the centre. Keep it inside the walls")]
        float m_ArenaRadius = 9.5f;

        [SerializeField]
        [Tooltip("A robot never appears closer than this to the player")]
        float m_MinDistanceFromPlayer = 4f;

        [SerializeField]
        [Tooltip("Layers that make a spawn point unusable: walls, cover, crates, other robots")]
        LayerMask m_BlockedMask = ~0;

        [SerializeField]
        [Tooltip("Free space needed around a spawn point, in meters")]
        float m_ClearanceRadius = 0.6f;

        [SerializeField]
        [Tooltip("How many positions are tried before the spawn is skipped for this turn")]
        int m_PlacementAttempts = 12;

        readonly List<RobotEnemy> m_Alive = new List<RobotEnemy>();

        SpawnArc m_Arc = SpawnArc.Front180;
        Coroutine m_Routine;

        public int aliveCount => m_Alive.Count;

        void Awake()
        {
            if (m_Player == null)
            {
                var origin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
                if (origin != null && origin.Camera != null)
                    m_Player = origin.Camera.transform;
            }
        }

        public void BeginSpawning(SpawnArc arc, float startDelay)
        {
            m_Arc = arc;
            StopSpawning();
            m_Routine = StartCoroutine(SpawnLoop(startDelay));
        }

        public void StopSpawning()
        {
            if (m_Routine != null)
            {
                StopCoroutine(m_Routine);
                m_Routine = null;
            }
        }

        public void DespawnAll()
        {
            for (int i = m_Alive.Count - 1; i >= 0; i--)
            {
                if (m_Alive[i] != null)
                    Destroy(m_Alive[i].gameObject);
            }

            m_Alive.Clear();
        }

        /// <summary>Called by a robot when it dies or is removed, so the cap stays accurate.</summary>
        public void NotifyRobotRemoved(RobotEnemy robot)
        {
            m_Alive.Remove(robot);
        }

        IEnumerator SpawnLoop(float startDelay)
        {
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            var manager = SurvivalGameManager.instance;

            while (manager == null || manager.state == GameState.Playing)
            {
                m_Alive.RemoveAll(r => r == null);

                if (m_Alive.Count < m_MaxAlive)
                    Spawn();

                yield return new WaitForSeconds(CurrentInterval());
            }

            m_Routine = null;
        }

        float CurrentInterval()
        {
            var manager = SurvivalGameManager.instance;
            float elapsed = manager != null ? manager.survivedTime : 0f;
            float t = m_RampDuration > 0f ? Mathf.Clamp01(elapsed / m_RampDuration) : 1f;
            return Mathf.Lerp(m_StartInterval, m_MinInterval, t);
        }

        void Spawn()
        {
            if (m_RobotPrefab == null || m_Player == null)
                return;

            if (!TryGetSpawnPosition(out Vector3 position))
                return;

            GameObject robot = Instantiate(m_RobotPrefab, position, Quaternion.identity);

            if (robot.TryGetComponent(out RobotEnemy enemy))
            {
                enemy.Initialize(this, m_Player);
                m_Alive.Add(enemy);
            }
        }

        Vector3 arenaCenter
        {
            get
            {
                Vector3 centre = m_ArenaCenter != null ? m_ArenaCenter.position : transform.position;
                centre.y = m_GroundY;
                return centre;
            }
        }

        /// <summary>
        /// Picks a point on the ring around the player, inside the arc centred on the direction
        /// they are facing, then keeps it inside the arena and clear of any obstacle. Several
        /// angles are tried because the player can stand right against a wall.
        /// </summary>
        public bool TryGetSpawnPosition(out Vector3 position)
        {
            Vector3 forward = Vector3.ProjectOnPlane(m_Player.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 centre = arenaCenter;
            Vector3 player = m_Player.position;
            player.y = m_GroundY;

            float half = (float)m_Arc * 0.5f;
            position = centre;

            for (int attempt = 0; attempt < Mathf.Max(1, m_PlacementAttempts); attempt++)
            {
                float angle = Random.Range(-half, half);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                float radius = m_SpawnRadius + Random.Range(-m_RadiusJitter, m_RadiusJitter);

                Vector3 candidate = player + direction * radius;
                candidate.y = m_GroundY;

                // Pull the point back inside the arena rather than letting it land in a wall.
                Vector3 fromCentre = candidate - centre;
                if (fromCentre.magnitude > m_ArenaRadius)
                    candidate = centre + fromCentre.normalized * m_ArenaRadius;

                if (Vector3.Distance(candidate, player) < m_MinDistanceFromPlayer)
                    continue;

                if (Physics.CheckSphere(candidate + Vector3.up * 1f, m_ClearanceRadius,
                        m_BlockedMask, QueryTriggerInteraction.Ignore))
                    continue;

                position = candidate;
                return true;
            }

            return false;
        }

        void OnDrawGizmosSelected()
        {
            Transform player = m_Player != null ? m_Player : transform;

            Vector3 centre = player.position;
            centre.y = m_GroundY;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(centre, m_SpawnRadius);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(arenaCenter, m_ArenaRadius);

            Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;

            float half = (float)m_Arc * 0.5f;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(centre, centre + Quaternion.AngleAxis(-half, Vector3.up) * forward * m_SpawnRadius);
            Gizmos.DrawLine(centre, centre + Quaternion.AngleAxis(half, Vector3.up) * forward * m_SpawnRadius);
        }
    }
}
