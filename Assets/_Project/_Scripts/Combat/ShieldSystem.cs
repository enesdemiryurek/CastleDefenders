using Mirror;
using UnityEngine;

public class ShieldSystem : NetworkBehaviour
{
    [Header("Shield Settings")]
    [SerializeField] private float blockChance = 30f; // %30 Blok şansı
    [SerializeField] private int damageReductionPercent = 50; // %50 Hasar azaltma
    [SerializeField] private float blockAngle = 120f; // Önündeki 120 derecelik açıdan gelenleri blokla

    [Header("Visuals")]
    [SerializeField] private NetworkAnimator networkAnimator;
    [SerializeField] private Animator animator;
    [SerializeField] private string blockTrigger = "Block";
    
    [Header("Audio")]
    [SerializeField] private AudioClip blockSound;
    private AudioSource audioSource;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (networkAnimator == null) networkAnimator = GetComponent<NetworkAnimator>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }

    [Server]
    public int TryBlock(int incomingDamage, Vector3 damageSourcePosition)
    {
        // 1. Açı Kontrolü: Saldırı nereden geliyor?
        Vector3 directionToAttacker = (damageSourcePosition - transform.position).normalized;
        float angle = Vector3.Angle(transform.forward, directionToAttacker);

        // Eğer saldırı ön cepheden (blockAngle/2) geliyorsa bloklayabilir
        if (angle <= blockAngle / 2f)
        {
            // 2. Şans Kontrolü
            // Blok şansı %100 değilse zar at
            if (Random.Range(0f, 100f) <= blockChance)
            {
                // BLOK BAŞARILI!
                TriggerBlockAnimation();

                if (blockSound != null && audioSource != null)
                {
                    audioSource.PlayOneShot(blockSound);
                }
                
                // Hasarı azalt
                float reduction = incomingDamage * (damageReductionPercent / 100f);
                int finalDamage = Mathf.Max(0, incomingDamage - Mathf.RoundToInt(reduction));
                
                Debug.Log($"{name} BLOCKED the attack! Damage reduced from {incomingDamage} to {finalDamage}.");
                return finalDamage;
            }
        }

        // Blok başarısız, hasar aynen devam
        return incomingDamage;
    }

    private void TriggerBlockAnimation()
    {
        if (networkAnimator != null)
        {
            networkAnimator.SetTrigger(blockTrigger);
        }
        else if (animator != null)
        {
            animator.SetTrigger(blockTrigger);
        }
    }
}
