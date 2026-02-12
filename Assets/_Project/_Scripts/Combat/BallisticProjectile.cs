using Mirror;
using UnityEngine;
using DG.Tweening;

[RequireComponent(typeof(Rigidbody))]
public class BallisticProjectile : NetworkBehaviour
{
    [SerializeField] private float damage = 20f;
    [SerializeField] private float launchForce = 20f; // Hız faktörü
    [SerializeField] private float lifetime = 5f;
    
    // Modeli döndürmek için
    [SerializeField] private Vector3 modelRotationOffset = Vector3.zero;
    [SerializeField] private bool rotateInDirection = true;

    private Rigidbody rb;
    private bool hasLaunched = false;
    private Vector3 lastPosition; // Dönüş hesabı için

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true; 
    }

    private GameObject shooter;
    
    [SyncVar] private Vector3 targetPosition;
    [SyncVar] private float syncedArcHeight = 2f; // Yükseklik parametresi
    [SyncVar(hook = nameof(OnLaunchStateChanged))] private bool isLaunched;

    [Server]
    public void SetShooter(GameObject shooterObj)
    {
        this.shooter = shooterObj;
    }

    // Server sadece başlatır
    [Server]
    public void Launch(Vector3 targetPos, float arcHeight = 2.0f)
    {
        // Network üzerinden pozisyonu senkronize et (SyncVar ile garanti altına al)
        if (hasLaunched) return;
        
        targetPosition = targetPos;
        syncedArcHeight = arcHeight;
        isLaunched = true;
        
        // Host için manuel tetikle (Hook bazen Host'ta çalışmaz)
        // Mirror Hook davranışına göre değişir ama garanti olsun
        if (isServer && isClient) OnLaunchStateChanged(false, true);
    }

    private void OnLaunchStateChanged(bool oldVal, bool newVal)
    {
        if (newVal && !hasLaunched)
        {
            StartProjectileMovement(targetPosition);
        }
    }

    private void StartProjectileMovement(Vector3 targetPos)
    {
        if (hasLaunched) return;
        hasLaunched = true;
        
        // Debug.Log($"[BallisticProjectile] Launching to {targetPos}");

        if(rb == null) rb = GetComponent<Rigidbody>();
        rb.isKinematic = true; // Fizik motorunu kapat (DOTween yönetecek)
        rb.useGravity = false;
        
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        float distance = Vector3.Distance(transform.position, targetPos);
        float duration = distance / launchForce; // Mesafeye göre süre belirle
        if (duration < 0.5f) duration = 0.5f; // Çok kısa sürmesin

        lastPosition = transform.position;

        // DOTween ile Zıplama (Parabol) Hareketi
        transform.DOJump(targetPos, syncedArcHeight, 1, duration)
            .SetEase(Ease.Linear)
            .SetLink(gameObject) // GameObject yok olunca Tween'i de öldür (SAFE MODE Hatalarını Çözer)
            .OnUpdate(() => {
                if (rotateInDirection)
                {
                    Vector3 dir = transform.position - lastPosition;
                    if (dir.sqrMagnitude > 0.001f)
                    {
                        Quaternion lookRot = Quaternion.LookRotation(dir);
                        transform.rotation = lookRot * Quaternion.Euler(modelRotationOffset);
                    }
                    lastPosition = transform.position;
                }
            })
            .OnComplete(() => {
                 // Hedefe vardı, yok et (veya saplanma efekti)
                 if (isServer) NetworkServer.Destroy(gameObject);
            });
    }

    // --- COLLISION LOGIC ---

    private bool hasHit = false;

    private void OnCollisionEnter(Collision collision)
    {
        HandleHit(collision.collider, collision.transform);
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleHit(other, other.transform);
    }

    private void HandleHit(Collider other, Transform hitTransform)
    {
        if (!isServer || hasHit) return;

        // Ignore Self & Shooter
        if (other.GetComponent<BallisticProjectile>()) return;
        if (shooter != null && (other.gameObject == shooter || other.transform.IsChildOf(shooter.transform))) return;

        // --- FRIENDLY FIRE CHECK ---
        bool isShooterPlayerSide = (shooter != null && (shooter.GetComponent<UnitMovement>() != null || shooter.GetComponent<PlayerController>() != null));
        bool isHitPlayerSide = (other.GetComponentInParent<UnitMovement>() != null || other.GetComponentInParent<PlayerController>() != null);
        
        bool isShooterEnemy = (shooter != null && shooter.GetComponent<EnemyAI>() != null);
        bool isHitEnemy = (other.GetComponentInParent<EnemyAI>() != null);

        // Dost Ateşi: Player/Unit -> Player/Unit VURAMAZ
        if (isShooterPlayerSide && isHitPlayerSide) return;

        // Düşman Ateşi: Enemy -> Enemy VURAMAZ
        if (isShooterEnemy && isHitEnemy) return;

        hasHit = true;

        // --- VISUAL SNAP ---
        SnapToCollider(other);

        // --- DAMAGE ---
        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            target.TakeDamage((int)damage, transform.position);
        }

        // --- STICK LOGIC (Saplama) ---
        StickToTarget(hitTransform);
    }
    
    private void SnapToCollider(Collider targetCol)
    {
        // Okun burnu biraz içeride kalmış olabilir veya çok dışarıda durmuş olabilir (Trigger yüzünden)
        // Oku biraz geriye alıp, tekrar ileri Ray atarak tam temas noktasını bulalım.
        
        Vector3 direction = transform.forward; // Okun yönü
        Vector3 startPoint = transform.position - (direction * 0.5f); // 50cm geriden
        
        Ray ray = new Ray(startPoint, direction);
        
        // Sadece hedef collider'a ray at (LayerMask ile uğraşmayalım, direkt instance check)
        if (targetCol.Raycast(ray, out RaycastHit hit, 1.0f))
        {
             // Tam temas noktasına, hafifçe içeri gömülmüş şekilde taşı
             transform.position = hit.point + (direction * 0.1f); 
        }
    }

    private void StickToTarget(Transform target)
    {
        // 1. Fiziği Kapat
        rb.isKinematic = true;
        rb.useGravity = false;
        
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // 2. DOTween'i Durdur (Eğer hala havadaysa)
        transform.DOKill();

        // 3. Hedefe Yapış (Parent)
        // Network objesi olduğu için transform.parent yapmak bazen sorun olabilir ama
        // görsel olarak ClientRpc ile de yapılabilir. Basitçe Server'da yapalım:
        transform.SetParent(target);

        // 4. Yok Olma (Süre tanı)
        // NetworkServer.Destroy yerine 10 saniye sonra yok et
        StartCoroutine(DestroyAfterDelay(10f));
        
        // Clientlara da bildir (Görsel güncellemeler için - Opsiyonel)
        RpcStick(target);
    }

    [ClientRpc]
    private void RpcStick(Transform target)
    {
        rb.isKinematic = true;
        rb.useGravity = false;
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        
        transform.DOKill();
        transform.SetParent(target);
    }

    [Server]
    private System.Collections.IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        NetworkServer.Destroy(gameObject);
    }
}
