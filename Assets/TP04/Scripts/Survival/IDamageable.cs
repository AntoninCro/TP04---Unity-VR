using UnityEngine;

namespace TP04.Survival
{
    /// <summary>
    /// Anything the player's bullets can hurt. <see cref="Bullet"/> looks for this in the
    /// parents of whatever it collides with.
    /// </summary>
    public interface IDamageable
    {
        bool isDead { get; }

        void TakeDamage(float amount, Vector3 point, Vector3 normal);
    }
}
