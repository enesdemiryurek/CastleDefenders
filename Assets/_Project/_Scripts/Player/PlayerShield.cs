using Mirror;
using UnityEngine;

/// <summary>
/// Player kahramanı için kalkan sistemi.
/// Sağ tık ile kalkan kaldırılır, önden gelen hasarı kalkan HP'si emer.
/// Arkadan gelene kalkan tutmaz. Kalkan HP'si bitince kırılır.
/// NOT: Bu komponent sadece Player prefab'ına eklenir! Üniteler ShieldSystem kullanır.
/// </summary>
public class PlayerShield : NetworkBehaviour
{
    [Header("Kalkan HP")]
    [SerializeField] private int maxShieldHP = 500;
    [SyncVar(hook = nameof(OnShieldHPChanged))]
    private int currentShieldHP;

    [Header("Kalkan Ayarları")]
    [SerializeField] private float blockAngle = 120f; // Önündeki 120 derecelik açı

    [Header("Görseller")]
    [SerializeField] private GameObject shieldVisual; // Kalkan mesh (kırılınca gizlenir)

    [Header("Ses")]
    [SerializeField] private AudioClip blockSound;
    [SerializeField] private AudioClip shieldBreakSound;
    private AudioSource audioSource;

    // Public erişim
    public bool IsShieldBroken => currentShieldHP <= 0;
    public int CurrentShieldHP => currentShieldHP;
    public int MaxShieldHP => maxShieldHP;

    // Kalkan aktif mi? (Sağ tık basılı)
    [SyncVar]
    private bool isBlocking = false;

    // Event: UI bar için
    public event System.Action<int, int> OnShieldHPChangedEvent;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }

    public override void OnStartServer()
    {
        currentShieldHP = maxShieldHP;
    }

    /// <summary>
    /// PlayerCombat.CmdSetBlocking'den çağrılır
    /// </summary>
    [Server]
    public void SetBlocking(bool state)
    {
        if (IsShieldBroken)
        {
            isBlocking = false;
            return;
        }
        isBlocking = state;
    }

    /// <summary>
    /// Health.TakeDamage'den çağrılır.
    /// Kalkan aktifse ve saldırı önden geliyorsa TÜM hasarı emer.
    /// Return: Vücuda gidecek hasar (0 = tam blok)
    /// </summary>
    [Server]
    public int TryBlock(int incomingDamage, Vector3 damageSourcePosition)
    {
        // 1. Kalkan kırık mı?
        if (IsShieldBroken)
        {
            return incomingDamage;
        }

        // 2. Sağ tık basılı mı?
        if (!isBlocking)
        {
            return incomingDamage;
        }

        // 3. Saldırı nereden geliyor?
        Vector3 dirToAttacker = (damageSourcePosition - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, dirToAttacker);

        if (angle > blockAngle / 2f)
        {
            // ARKADAN SALDIRI → Kalkan tutmaz!
            Debug.Log($"[PlayerShield] {name} ARKADAN SALDIRI! Açı: {angle:F0}° → Kalkan tutmadı!");
            return incomingDamage;
        }

        // ====== BLOK BAŞARILI ======
        // Kalkan TÜM hasarı emer, vücuda 0 gider
        currentShieldHP -= incomingDamage;

        Debug.Log($"[PlayerShield] {name} KALKAN BLOKLADI! Hasar: {incomingDamage}, Kalkan: {currentShieldHP}/{maxShieldHP}");

        // Ses
        if (blockSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(blockSound);
        }

        // Kalkan kırıldı mı?
        if (currentShieldHP <= 0)
        {
            currentShieldHP = 0;
            isBlocking = false;
            Debug.Log($"[PlayerShield] {name} ★ KALKAN KIRILDI!");
            RpcShieldBreak();
        }

        return 0; // Vücuda 0 hasar
    }

    private void OnShieldHPChanged(int oldVal, int newVal)
    {
        OnShieldHPChangedEvent?.Invoke(newVal, maxShieldHP);
    }

    [ClientRpc]
    private void RpcShieldBreak()
    {
        if (shieldVisual != null) shieldVisual.SetActive(false);
        if (shieldBreakSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(shieldBreakSound);
        }
    }
}
