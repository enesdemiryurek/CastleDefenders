using Mirror;
using UnityEngine;

public class Projectile : NetworkBehaviour
{
    [SerializeField] private float speed = 20f;
    [SerializeField] private int damage = 10;
    [SerializeField] private float lifetime = 5f;

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        if (isServer)
        {
            transform.Translate(Vector3.forward * speed * Time.deltaTime);
        }
    }

    [ServerCallback]
    private void OnTriggerEnter(Collider other)
    {
        // Kendimize (Unit) veya başka oklara çarpmasın
        if (other.GetComponent<UnitMovement>() != null || other.GetComponent<Projectile>() != null) return;

        IDamageable target = other.GetComponent<IDamageable>();
        if (target != null)
        {
            target.TakeDamage(damage);
            NetworkServer.Destroy(gameObject); // Oku yok et
        }
        else
        {
            // Duvara vs çarparsa da yok olsun
            // NetworkServer.Destroy(gameObject);
        }
    }
}
