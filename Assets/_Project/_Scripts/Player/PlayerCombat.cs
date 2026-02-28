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

    [Header("Spartan Kick Settings")]
    [SerializeField] private int kickDamage = 40; // Tekme hasarı
    [SerializeField] private float kickRange = 2.5f; // Tekme menzili
    [SerializeField] private float kickAngle = 90f; // Önündeki 90 derecelik koni
    [SerializeField] private float kickCooldown = 10f; // 10 saniye bekleme
    [SerializeField] private float kickImpactDelay = 0.3f; // Tekme animasyonunun temas anı
    [SerializeField] private float kickKnockdownDuration = 0.5f; // Yerde kalma süresi
    [SerializeField] private float dominoRadius = 2.0f; // Domino etkisi yarıçapı (dipdibe olanlar)
    [SerializeField] private int maxDominoChain = 10; // Domino zinciri max 10 kişi
    
    [Header("Debug")]
    [SerializeField] private Transform attackPoint; // Raycast/Sphere merkezi (Inspector'da ayarlanmalı)

    private NetworkAnimator networkAnimator;
    private float lastAttackTime;
    private float lastKickTime = -999f;
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
        
        // ÖLÜYSEK SALDIRI/BLOK YAPMA!
        Health myHealth = GetComponent<Health>();
        if (myHealth != null && !myHealth.IsAlive) return;

        HandleCombatInput();
    }

    private void HandleCombatInput()
    {
        // --- ATTACK (Sol Tık) ---
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (Time.time - lastAttackTime >= attackCooldown && !isBlocking)
            {
                CmdAttack();
            }
        }

        // --- SPARTAN KICK (F Tuşu) ---
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            // Cooldown kontrolü (basit client-side filtre — server'da da kontrol var)
            if (!isBlocking)
            {
                CmdSpartanKick();
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
                // Önce hasar ver
                damageable.TakeDamage(damage, transform.position);
                
                // Vuruş ölümcül müydü kontrol et
                bool isKill = false;
                Health h = bestTarget.GetComponent<Health>();
                if (h != null && !h.IsAlive)
                {
                    isKill = true;
                }
                
                // Sadece saldıran oyuncunun (lokal) ekranında Crosshair/HitMarker göster
                TargetShowHitMarker(connectionToClient, isKill);
            }
        }
    }

    [TargetRpc]
    private void TargetShowHitMarker(NetworkConnection target, bool isKill)
    {
        // HitMarkerUI sahnedeki Canvas'ta Singleton (Instance) olarak bulunmalı
        if (HitMarkerUI.Instance != null)
        {
            HitMarkerUI.Instance.ShowMarker(isKill);
        }
    }


    // ==================== SPARTAN KICK ====================
    [Command]
    private void CmdSpartanKick()
    {
        Debug.Log($"[PlayerCombat] CmdSpartanKick SERVER'A ULAŞTI! Time={Time.time:F1}, lastKick={lastKickTime:F1}");
        
        // Server-side cooldown (exploit koruması)
        // NOT: lastKickTime server'da ayrı tutuluyor, client'taki değerle senkron değil
        if (lastKickTime > 0 && Time.time - lastKickTime < kickCooldown) 
        {
            Debug.Log($"[PlayerCombat] KICK COOLDOWN! Kalan: {kickCooldown - (Time.time - lastKickTime):F1}s");
            return;
        }
        lastKickTime = Time.time;

        Debug.Log($"[PlayerCombat] ★ SPARTAN KICK! 300'e selam!");

        // 1. Animasyonu Oynat ("Kick" trigger'ı Animatörde olmalı)
        networkAnimator.SetTrigger("Kick");

        // 2. Gecikmeli hasar (tekmenin temas anında)
        StartCoroutine(SpartanKickRoutine());
    }

    [Server]
    private IEnumerator SpartanKickRoutine()
    {
        yield return new WaitForSeconds(kickImpactDelay);

        // ======= FAZE 1: Direkt tekme vuruşu (2.5m koni) =======
        Collider[] hits = Physics.OverlapSphere(transform.position, kickRange, enemyLayer);
        
        // Domino zinciri için vurulan düşmanları takip et
        System.Collections.Generic.HashSet<Transform> knockedSet = 
            new System.Collections.Generic.HashSet<Transform>();
        System.Collections.Generic.Queue<Transform> dominoQueue = 
            new System.Collections.Generic.Queue<Transform>();

        foreach (var hit in hits)
        {
            if (hit.transform == transform || hit.transform.IsChildOf(transform)) continue;

            // Açı kontrolü: Sadece önündekiler
            Vector3 dirToEnemy = (hit.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToEnemy);
            if (angle > kickAngle / 2f) continue;

            // Root objeyi bul
            NetworkIdentity netId = hit.GetComponentInParent<NetworkIdentity>();
            Transform root = netId != null ? netId.transform : hit.transform;
            
            if (knockedSet.Contains(root)) continue;

            // Ölü mü kontrolü
            Health hp = root.GetComponent<Health>();
            if (hp != null && !hp.IsAlive) continue;

            // HASAR VER
            IDamageable damageable = root.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(kickDamage, transform.position);
            }

            // KNOCKDOWN UYGULA
            if (hp != null && hp.IsAlive)
            {
                hp.ApplyKnockdown(kickKnockdownDuration);
                knockedSet.Add(root);
                dominoQueue.Enqueue(root); // Domino zinciri için kuyruğa ekle
                Debug.Log($"[PlayerCombat] ★ SPARTAN KICK HIT! {root.name} yere düştü! ({kickDamage} hasar)");
            }
            else
            {
                knockedSet.Add(root);
                Debug.Log($"[PlayerCombat] ★ SPARTAN KICK KILL! {root.name} öldü! ({kickDamage} hasar)");
            }
        }

        // ======= FAZE 2: DOMİNO ETKİSİ (KONİ ŞEKLİNDE) =======
        // Tekmenin yönünde arkadaki düşmanları da devir!
        // Koni açısı: player'ın baktığı yön + 90 derece tolerans
        Vector3 kickDirection = transform.forward; // Tekmenin yönü
        
        while (dominoQueue.Count > 0 && knockedSet.Count < maxDominoChain)
        {
            Transform current = dominoQueue.Dequeue();
            
            // Domino: Hepsi AYNI ANDA düşsün!
            // (Eski: 0.25s gecikme vardı, kaldırıldı)

            // Bu düşmanın ARKASINDA (tekme yönünde) komşuları bul
            Collider[] neighbors = Physics.OverlapSphere(current.position, dominoRadius, enemyLayer);
            
            foreach (var neighbor in neighbors)
            {
                if (knockedSet.Count >= maxDominoChain) break;
                
                NetworkIdentity nId = neighbor.GetComponentInParent<NetworkIdentity>();
                Transform neighborRoot = nId != null ? nId.transform : neighbor.transform;
                
                // Kendisi veya zaten devrilmişse atla
                if (neighborRoot == current) continue;
                if (knockedSet.Contains(neighborRoot)) continue;
                
                // KONİ KONTROLÜ: Komşu, tekmenin yönünde mi?
                // Devrilen düşmandan komşuya doğru vektör
                Vector3 dirToNeighbor = (neighborRoot.position - current.position).normalized;
                float angle = Vector3.Angle(kickDirection, dirToNeighbor);
                
                // 90 derece koni (tekme yönünün sağı-solu 45'er derece)
                if (angle > 90f) continue; // Tekme yönünün arkasındakiler devriliyor, yanlarındakiler değil
                
                // Ölü mü kontrolü
                Health neighborHealth = neighborRoot.GetComponent<Health>();
                if (neighborHealth == null || !neighborHealth.IsAlive) continue;
                
                // DOMİNO! Komşu da devrilsin
                neighborHealth.ApplyKnockdown(kickKnockdownDuration);
                knockedSet.Add(neighborRoot);
                dominoQueue.Enqueue(neighborRoot);
                
                Debug.Log($"[PlayerCombat] 🁡 DOMİNO! {neighborRoot.name} devrildi! (Koni: {angle:F0}°, Zincir: {knockedSet.Count}/{maxDominoChain})");
            }
        }

        // Sonuç raporu
        if (knockedSet.Count == 0)
        {
            Debug.Log($"[PlayerCombat] Spartan Kick MISS! Önünde düşman yok.");
        }
        else
        {
            Debug.Log($"[PlayerCombat] ★ SPARTAN KICK TOPLAM: {knockedSet.Count} düşman devrildi!");
            TargetShowHitMarker(connectionToClient, false);
        }
    }

    [Command]
    private void CmdSetBlocking(bool state)
    {
        isBlocking = state;
        networkAnimator.animator.SetBool(BLOCK_BOOL, state);
        
        // Player kalkan sistemine bildir
        PlayerShield playerShield = GetComponent<PlayerShield>();
        if (playerShield != null)
        {
            playerShield.SetBlocking(state);
        }
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
