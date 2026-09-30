using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace TP04.Survival
{
    /// <summary>
    /// Marks an object as able to stop enemy bolts. <see cref="EnemyProjectile"/> looks for this
    /// component in the parents of whatever it hits, so any collider on the shield works.
    /// </summary>
    public class ShieldBlocker : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Vibration sent to the hand holding the shield when it stops a bolt")]
        float m_HapticAmplitude = 0.6f;

        [SerializeField]
        [Tooltip("Duration of that vibration, in seconds")]
        float m_HapticDuration = 0.12f;

        [SerializeField]
        [Tooltip("Optional sound played when a bolt is stopped")]
        AudioSource m_BlockAudio = null;

        /// <summary>Raised when a bolt is stopped, with the impact point and normal.</summary>
        public event Action<Vector3, Vector3> blocked;

        public int blockCount { get; private set; }

        XRGrabInteractable m_Interactable;

        void Awake()
        {
            m_Interactable = GetComponentInParent<XRGrabInteractable>();
        }

        /// <summary>Called by a bolt that just died against this shield.</summary>
        public void NotifyBlocked(Vector3 point, Vector3 normal)
        {
            blockCount++;

            if (m_BlockAudio != null)
                m_BlockAudio.Play();

            SendHaptics();

            blocked?.Invoke(point, normal);
        }

        void SendHaptics()
        {
            if (m_HapticAmplitude <= 0f || m_Interactable == null || !m_Interactable.isSelected)
                return;

            foreach (var interactor in m_Interactable.interactorsSelecting)
            {
                if (interactor is XRBaseInputInteractor inputInteractor)
                    inputInteractor.SendHapticImpulse(m_HapticAmplitude, m_HapticDuration);
            }
        }
    }
}
