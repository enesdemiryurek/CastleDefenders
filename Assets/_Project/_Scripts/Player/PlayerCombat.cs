using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

[RequireComponent(typeof(NetworkAnimator))]
public class PlayerCombat : NetworkBehaviour
{
    [Header("Combat Settings")]
    [SerializeField] private int damage = 25;
    [SerializeField] private float attackRange = 2.5f;
    [SerializeField] private float attackCooldown = 1.0f;
    [SerializeField] private float impactDelay = 0.4f; // Animasyonun vurma anı (tahmini)
    [SerializeField] private LayerMask enemyLayer;
    
    [Header("Debug")]
    [SerializeField] private Transform attackPoint; // Raycast/Sphere merkezi (Inspector'da ayarlanmalı)

    private NetworkAnimator networkAnimator;
    private float lastAttackTime;
    private bool isBlocking = false;

    // Animator Parametre İsimleri
    private const string ATTACK_TRIGGER = "Attack";
    private const string BLOCK_BOOL = "IsBlocking";

    private void Awake()
    {
        networkAnimator = GetComponent<NetworkAnimator>();
        
        // Eğer attackPoint atanmadıysa, karakterin önüne sanal bir nokta koy
        if (attackPoint == null)
        {
            GameObject point = new GameObject("AttackPoint");
            point.transform.SetParent(transform);
            point.transform.localPosition = new Vector3(0, 1, 1); // 1 metre öne, 1 metre yukarı
            attackPoint = point.transform;
        }
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        HandleCombatInput();
    }

    private void HandleCombatInput()
    {
        // --- ATTACK (Sol Tık) ---
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (Time.time - lastAttackTime >= attackCooldown && !isBlocking)
            {
                // Client hemen animasyonu görsün diye lokalde de tetikleyebiliriz ama 
                // NetworkAnimator zaten sync ediyor. Sadece komutu göndermemiz yeterli.
                CmdAttack();
            }
        }

        // --- BLOCK (Sağ Tık Basılı Tutma) ---
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CmdSetBlocking(true);
        }
        else if (Mouse.current.rightButton.wasReleasedThisFrame)
        {
            CmdSetBlocking(false);
        }
    }

    [Command]
    private void CmdAttack()
    {
        // Server side cooldown check (Hile koruması)
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;

        // 1. Animasyonu Oynat (Herkese gider)
        networkAnimator.SetTrigger(ATTACK_TRIGGER);

        // 2. Hasar ver (Gecikmeli)
        StartCoroutine(DealDamageRoutine());
    }

    [Server]
    private IEnumerator DealDamageRoutine()
    {
        yield return new WaitForSeconds(impactDelay);

        // Alan Hasarı (Kılıcı savurunca önündeki herkese vurur)
        Collider[] hitEnemies = Physics.OverlapSphere(attackPoint.position, attackRange, enemyLayer);

        foreach (var enemy in hitEnemies)
        {
            // Kendine vurmasın (LayerMask zaten engeller ama yine de check)
            if (enemy.transform == transform) continue;

            IDamageable damageable = enemy.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, transform.position);
                Debug.Log($"Hit enemy: {enemy.name}");
            }
        }
    }

    [Command]
    private void CmdSetBlocking(bool state)
    {
        isBlocking = state;
        networkAnimator.animator.SetBool(BLOCK_BOOL, state);
    }

    private void OnDrawGizmosSelected()
    {
        if (attackPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attackPoint.position, attackRange);
        }
    }
}
