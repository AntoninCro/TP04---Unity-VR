using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TP04
{
    /// <summary>
    /// Fires a projectile out of the muzzle when the held weapon is activated
    /// (trigger button of the controller holding it).
    /// </summary>
    [RequireComponent(typeof(XRGrabInteractable))]
    public class PistolShooter : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Point the bullet spawns from. Its blue axis (+Z) is the firing direction")]
        Transform m_Muzzle = null;

        [SerializeField]
        [Tooltip("Prefab instantiated on each shot, must have a Rigidbody")]
        GameObject m_BulletPrefab = null;

        [SerializeField]
        [Tooltip("Initial speed of the bullet, in meters per second")]
        float m_BulletSpeed = 25f;

        [SerializeField]
        [Tooltip("Minimum delay between two shots, in seconds")]
        float m_FireCooldown = 0.15f;

        [SerializeField]
        [Tooltip("Optional muzzle flash played on each shot")]
        ParticleSystem m_MuzzleFlash = null;

        [SerializeField]
        [Tooltip("Optional gunshot sound played on each shot")]
        AudioSource m_FireAudio = null;

        [SerializeField]
        [Tooltip("Strength of the controller vibration on each shot, 0 disables it")]
        float m_HapticAmplitude = 0.4f;

        [SerializeField]
        [Tooltip("Duration of the controller vibration on each shot, in seconds")]
        float m_HapticDuration = 0.1f;

        XRGrabInteractable m_Interactable;
        Collider[] m_OwnColliders;
        float m_NextFireTime;

        void Awake()
        {
            m_Interactable = GetComponent<XRGrabInteractable>();
            m_OwnColliders = GetComponentsInChildren<Collider>();

            if (m_Muzzle == null)
                m_Muzzle = transform;
        }

        void OnEnable() => m_Interactable.activated.AddListener(OnActivated);

        void OnDisable() => m_Interactable.activated.RemoveListener(OnActivated);

        void OnActivated(ActivateEventArgs args)
        {
            if (!Fire())
                return;

            if (m_HapticAmplitude > 0f && args.interactorObject is XRBaseInputInteractor inputInteractor)
                inputInteractor.SendHapticImpulse(m_HapticAmplitude, m_HapticDuration);
        }

        /// <summary>
        /// Spawns a bullet at the muzzle. Also callable from a UnityEvent or another script.
        /// </summary>
        /// <returns>Whether a bullet was actually fired.</returns>
        public bool Fire()
        {
            if (m_BulletPrefab == null || Time.time < m_NextFireTime)
                return false;

            m_NextFireTime = Time.time + m_FireCooldown;

            GameObject bullet = Instantiate(m_BulletPrefab, m_Muzzle.position, m_Muzzle.rotation, null);

            if (bullet.TryGetComponent(out Rigidbody rigidBody))
                rigidBody.linearVelocity = m_Muzzle.forward * m_BulletSpeed;

            IgnoreOwnColliders(bullet);

            if (m_MuzzleFlash != null)
                m_MuzzleFlash.Play();

            if (m_FireAudio != null)
                m_FireAudio.Play();

            return true;
        }

        // Without this the bullet spawns inside the weapon and shoves it out of the hand.
        void IgnoreOwnColliders(GameObject bullet)
        {
            foreach (Collider bulletCollider in bullet.GetComponentsInChildren<Collider>())
            {
                foreach (Collider ownCollider in m_OwnColliders)
                {
                    if (ownCollider != null)
                        Physics.IgnoreCollision(bulletCollider, ownCollider);
                }
            }
        }
    }
}
