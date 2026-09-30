using System;
using UnityEngine;

namespace TP04.Survival
{
    /// <summary>
    /// Health of the player. Lives on the XR Origin root, next to the CharacterController, so
    /// that both a body shot and a head shot resolve to it: enemy projectiles look for this
    /// component in the parents of whatever collider they hit.
    /// </summary>
    public class PlayerHealth : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Number of hits the player can take before the run ends")]
        int m_HitsAllowed = 5;

        [SerializeField]
        [Tooltip("Seconds of invulnerability after a hit, so a single burst cannot end the run")]
        float m_InvulnerabilityDuration = 0.75f;

        [SerializeField]
        [Tooltip("Optional effect spawned on the player when hit")]
        GameObject m_HitEffectPrefab = null;

        /// <summary>Raised on each hit taken, with (hits taken, hits allowed).</summary>
        public event Action<int, int> hit;

        /// <summary>Raised once when the last hit is taken.</summary>
        public event Action died;

        public int hitsAllowed => m_HitsAllowed;
        public int hitsTaken { get; private set; }
        public int hitsRemaining => Mathf.Max(0, m_HitsAllowed - hitsTaken);
        public bool isDead => hitsTaken >= m_HitsAllowed;

        float m_InvulnerableUntil;

        public void ResetHealth()
        {
            hitsTaken = 0;
            m_InvulnerableUntil = 0f;
        }

        /// <summary>
        /// Registers one hit. Ignored while invulnerable, once dead, or outside a run.
        /// </summary>
        /// <returns>Whether the hit actually counted.</returns>
        public bool TakeHit()
        {
            var manager = SurvivalGameManager.instance;
            if (manager != null && manager.state != GameState.Playing)
                return false;

            if (isDead || Time.time < m_InvulnerableUntil)
                return false;

            hitsTaken++;
            m_InvulnerableUntil = Time.time + m_InvulnerabilityDuration;

            if (m_HitEffectPrefab != null)
                Destroy(Instantiate(m_HitEffectPrefab, transform.position, transform.rotation), 2f);

            hit?.Invoke(hitsTaken, m_HitsAllowed);

            if (manager != null)
                manager.NotifyPlayerHit(hitsTaken, m_HitsAllowed);

            if (isDead)
                died?.Invoke();

            return true;
        }
    }
}
