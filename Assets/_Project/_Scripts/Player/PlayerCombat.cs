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

    public override void OnStartLocalPlayer()
    {
        // Crosshair KALDIRILDI (User Request)
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
                // Alan Hasarı (Eski sistem)
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
        // Server side cooldown check
        if (Time.time - lastAttackTime < attackCooldown) return;
        lastAttackTime = Time.time;

        // 1. Animasyonu Oynat
        networkAnimator.SetTrigger(ATTACK_TRIGGER);

        // 2. Hasar ver (Gecikmeli & Alan)
        StartCoroutine(DealDamageRoutine());
    }

    [Server]
    private IEnumerator DealDamageRoutine()
    {
        yield return new WaitForSeconds(impactDelay);

        // 1. Adayları Bul (AttackPoint etrafında)
        Collider[] hitEnemies = Physics.OverlapSphere(attackPoint.position, attackRange, enemyLayer);
        
        Collider bestTarget = null;
        float minDistance = float.MaxValue;
        float maxAngle = 60f; // 120 Derecelik görüş açısı (Önündekiler)

        foreach (var enemy in hitEnemies)
        {
            if (enemy.transform == transform) continue;
            
            // 2. Açı Kontrolü (Profesyonel Dokunuş: Sadece baktığın yöndekiler)
            Vector3 dirToEnemy = (enemy.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToEnemy);

            if (angle <= maxAngle)
            {
                // 3. En Yakın Olanı Seç
                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestTarget = enemy;
                }
            }
        }

        // Sadece en uygun hedefe vur
        if(bestTarget != null)
        {
            IDamageable damageable = bestTarget.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, transform.position);
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
