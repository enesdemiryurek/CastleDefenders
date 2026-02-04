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

    [Server]
    public void SetShooter(GameObject shooterObj)
    {
        this.shooter = shooterObj;
    }

    // Server sadece başlatır
    [Server]
    public void Launch(Vector3 targetPosition)
    {
        // Network üzerinden pozisyonu senkronize et
        RpcLaunch(targetPosition);
    }

    [ClientRpc]
    private void RpcLaunch(Vector3 targetPosition)
    {
        hasLaunched = true;
        rb.isKinematic = true; // Fizik motorunu kapat (DOTween yönetecek)
        rb.useGravity = false;
        
        // DOTween/Kinematik hareket için Trigger olması daha garantidir
        Collider col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        float distance = Vector3.Distance(transform.position, targetPosition);
        float duration = distance / launchForce; // Mesafeye göre süre belirle
        if (duration < 0.5f) duration = 0.5f; // Çok kısa sürmesin

        lastPosition = transform.position;

        // DOTween ile Zıplama (Parabol) Hareketi
        transform.DOJump(targetPosition, 2f, 1, duration)
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

    // FixedUpdate'e gerek kalmadı (DOTween yönetiyor)
    private void OnCollisionEnter(Collision collision)
    {
        // Debug
        Debug.Log($"Arrow hit: {collision.gameObject.name} (Tag: {collision.gameObject.tag})");

        if (!isServer) return; // Sadece Server hasar verir

        if (collision.gameObject.GetComponent<BallisticProjectile>()) return; // Oka çarpma
        if (collision.gameObject.GetComponent<BallisticProjectile>()) return; // Oka çarpma
        
        // Vuran kişiye (shooter) çarpma
        if (shooter != null && (collision.gameObject == shooter || collision.transform.IsChildOf(shooter.transform))) return;

        // DOST ATEŞİ KORUMASI:
        // Eğer shooter bir "UnitMovement" ise (Bizim asker), diğer "UnitMovement"lara (Bizim askerler) vurmasın.
        if (shooter != null && shooter.GetComponent<UnitMovement>() != null && collision.gameObject.GetComponentInParent<UnitMovement>() != null) return;
        
        // Eğer shooter bir "EnemyAI" ise, diğer "EnemyAI"lara vurmasın.
        if (shooter != null && shooter.GetComponent<EnemyAI>() != null && collision.gameObject.GetComponentInParent<EnemyAI>() != null) return;

        // IDamageable ara (Kendisinden başlayıp yukarı doğru)
        IDamageable target = collision.gameObject.GetComponentInParent<IDamageable>();
        
        if (target != null)
        {
            Debug.Log($"Arrow damaged: {collision.gameObject.name}");
            target.TakeDamage((int)damage);
            NetworkServer.Destroy(gameObject);
        }
        else
        {
            // Duvara saplanma veya hasar almayan bir objeye çarpma
            Debug.Log($"Arrow hit NON-Damageable: {collision.gameObject.name}");
            NetworkServer.Destroy(gameObject);
        }
    }

    // Trigger Logic (CharacterController bazen Trigger gibi davranabilir veya hit boxlar trigger olabilir)
    private void OnTriggerEnter(Collider other)
    {
        // Debug.Log($"Arrow Trigger Hit: {other.name}");

        if (!isServer) return;

        if (other.GetComponent<BallisticProjectile>()) return;
        if (other.GetComponent<BallisticProjectile>()) return;
        
        // Vuran kişiye (shooter) çarpma
        if (shooter != null && (other.gameObject == shooter || other.transform.IsChildOf(shooter.transform))) return;

        // DOST ATEŞİ KORUMASI:
        if (shooter != null && shooter.GetComponent<UnitMovement>() != null && other.GetComponentInParent<UnitMovement>() != null) return;
        if (shooter != null && shooter.GetComponent<EnemyAI>() != null && other.GetComponentInParent<EnemyAI>() != null) return;

        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            Debug.Log($"Arrow damaged (Trigger): {other.name}");
            target.TakeDamage((int)damage, transform.position);
            NetworkServer.Destroy(gameObject);
        }
    }
}
