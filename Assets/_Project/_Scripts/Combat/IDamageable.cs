public interface IDamageable
{
    int CurrentHealth { get; }
    void TakeDamage(int amount, UnityEngine.Vector3? damageSource = null);
}
