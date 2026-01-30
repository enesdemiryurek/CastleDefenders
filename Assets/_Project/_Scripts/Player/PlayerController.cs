using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;
    [SerializeField] private float crouchSpeed = 2.5f;
    [SerializeField] private float jumpHeight = 2f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float rotationSpeed = 10f;
    
    [Header("Crouch Settings")]
    [SerializeField] private float normalHeight = 2f;
    [SerializeField] private float crouchHeight = 1f;

    private CharacterController characterController;
    private Vector3 velocity; // Stores vertical velocity

    private bool isDead = false;
    public bool InputEnabled { get; set; } = true; // Dışarıdan kontrol edilebilir (Command Mode için)

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        
        // Health eventine abone ol
        Health health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath += OnDeathHandler;
        }
    }

    private void OnDestroy()
    {
        Health health = GetComponent<Health>();
        if (health != null)
        {
            health.OnDeath -= OnDeathHandler;
        }
    }

    private void OnDeathHandler()
    {
        isDead = true;
        
        // Hareketi Sıfırla
        // velocity = Vector3.zero; // Local velocity
        
        // Animasyon Tetikle
        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.SetTrigger("Die");
        }

        Debug.Log("Player Controller: Input Disabled (DEAD)");
    } 

    public override void OnStartLocalPlayer()
    {
        // 1. Kamerayı bul ve kendine kitle
        if (PlayerCamera.Instance != null)
        {
            PlayerCamera.Instance.SetTarget(transform);
        }
    }

    private void Update()
    {
        // Ölü olsa bile yerçekimi çalışmalı, o yüzden return etmiyoruz.
        // Sadece Input'u keseceğiz (HandleMovement içinde).
        
        // 1. Client Authority Check
        if (!isLocalPlayer) return;
        
        HandleMovement();
    }

    private void HandleMovement()
    {
        // Ground Check
        bool isGrounded = characterController.isGrounded;
        
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // --- INPUT ---
        Vector2 input = Vector2.zero;
        if (!isDead && InputEnabled && Keyboard.current != null) // Ölü değilse ve Input açıksa
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1;
        }

        // --- SPRINT & CROUCH LOGIC ---
        float currentSpeed = moveSpeed;
        float targetHeight = normalHeight;

        // Shift -> Sprint
        if (Keyboard.current.leftShiftKey.isPressed && !Keyboard.current.leftCtrlKey.isPressed)
        {
            currentSpeed = sprintSpeed;
        }

        // Ctrl -> Crouch
        if (Keyboard.current.leftCtrlKey.isPressed)
        {
            currentSpeed = crouchSpeed;
            targetHeight = crouchHeight;
        }

        // Height Smoothness
        characterController.height = Mathf.Lerp(characterController.height, targetHeight, Time.deltaTime * 10f);

        // --- ROTATION (TPS) ---
        // --- MOVE & ROTATION ---
        Vector3 direction = new Vector3(input.x, 0f, input.y).normalized;
        Vector3 moveDir = Vector3.zero;
 
        if (PlayerCamera.Instance != null && input.magnitude >= 0.1f)
        {
             // 1. Hedef açıyı hesapla (Input + Camera Y)
             float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + PlayerCamera.Instance.transform.eulerAngles.y;
             
             // 2. Hedef rotasyonu oluştur
             Quaternion targetRotation = Quaternion.Euler(0f, targetAngle, 0f);
             
             // 3. Karakteri o yöne çevir (Yumuşak geçiş)
             transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

             // 4. Hareket vektörünü al
             moveDir = targetRotation * Vector3.forward;
        }
        else
        {
             moveDir = transform.right * input.x + transform.forward * input.y;
        }

        // --- JUMP ---
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // --- GRAVITY ---
        velocity.y += gravity * Time.deltaTime;

        // Combine
        Vector3 finalMove = (moveDir * currentSpeed) + velocity;
        characterController.Move(finalMove * Time.deltaTime);

        // --- ANIMATION SYNC ---
        // Animator'a hızı gönder (Yürüme/Koşma için)
        // Sadece yatay hızın büyüklüğünü alıyoruz (gravity hariç)
        Vector3 horizontalVelocity = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
        float speed = horizontalVelocity.magnitude;
        
        // Eğer Animator varsa parametreyi güncelle
        Animator anim = GetComponent<Animator>();
        if (anim != null)
        {
            anim.SetFloat("Speed", speed);
        }
    }
}
