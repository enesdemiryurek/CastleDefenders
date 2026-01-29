using Mirror;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallisticProjectile : NetworkBehaviour
{
    [SerializeField] private float damage = 20f;
    [SerializeField] private float launchForce = 20f; // Başlangıç hızı
    [SerializeField] private float lifetime = 5f;
    
    // Modeli döndürmek için (Eğer modelin kendisi yamuksa buraya 90,0,0 gibi değer gir)
    [SerializeField] private Vector3 modelRotationOffset = Vector3.zero;
    [SerializeField] private bool rotateInDirection = true;

    private Rigidbody rb;
    private bool hasLaunched = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; // Fırlatılana kadar gravity kapalı
        rb.isKinematic = true; 
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
        rb.isKinematic = false;
        rb.useGravity = true;

        Vector3 force = transform.forward * launchForce;
        rb.AddForce(force, ForceMode.VelocityChange);
        
        Destroy(gameObject, lifetime);
    }

    private void FixedUpdate()
    {
        if (hasLaunched && rotateInDirection && rb.linearVelocity.sqrMagnitude > 0.1f)
        {
            // Oku hareket yönüne çevir (AERODİNAMİK)
            Quaternion lookRot = Quaternion.LookRotation(rb.linearVelocity);
            
            // Offseti uygula (Model yamuksa düzeltir)
            transform.rotation = lookRot * Quaternion.Euler(modelRotationOffset);
        }
    }

    // Hasar Logic
    private void OnCollisionEnter(Collision collision)
    {
        // Debug
        Debug.Log($"Arrow hit: {collision.gameObject.name} (Tag: {collision.gameObject.tag})");

        if (!isServer) return; // Sadece Server hasar verir

        if (collision.gameObject.GetComponent<BallisticProjectile>()) return; // Oka çarpma
        if (collision.gameObject.GetComponent<EnemyAI>()) return; // Atan kişiye (EnemyAI) çarpma

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
        if (other.GetComponent<EnemyAI>()) return;

        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target != null)
        {
            Debug.Log($"Arrow damaged (Trigger): {other.name}");
            target.TakeDamage((int)damage, transform.position);
            NetworkServer.Destroy(gameObject);
        }
    }
}
