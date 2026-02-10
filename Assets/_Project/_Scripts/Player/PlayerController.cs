using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 2.0f;
    [SerializeField] private float sprintSpeed = 4.0f;
    [SerializeField] private float crouchSpeed = 2.5f; // User requested 2.5f for blocking
    [SerializeField] private float jumpHeight = 0.6f;
    [SerializeField] private float gravity = -20f; // Gravity arttirilabilir, daha tok hissiyat icin
    [SerializeField] private float rotationSpeed = 10f;
    
    [Header("Interaction Settings")]
    [SerializeField] private float interactionDistance = 3f;

    [Header("Climbing Settings")]
    [SerializeField] private float climbSpeed = 4f;
    private bool canClimb = false;
    private bool isClimbing = false;

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

        if (BattleManager.Instance != null) BattleManager.Instance.UnregisterHero(transform);
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

    public override void OnStartServer()
    {
        base.OnStartServer();
        // Server tarafında kendini BattleManager'a kaydet (Düşmanlar seni bulsun)
        if (BattleManager.Instance != null) BattleManager.Instance.RegisterHero(transform);
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

        // --- INTERACTION (E Key) ---
        if (InputEnabled && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
        {
            // 1. Priority: Climbing (Zone Check)
            if (canClimb)
            {
                isClimbing = !isClimbing;
                if(isClimbing) velocity = Vector3.zero; // Hızı sıfırla ki düşmeyelim
            }
            // 2. Priority: General Interaction (Raycast - Gate etc.)
            else
            {
                HandleInteraction();
            }
        }
        
        HandleMovement();
    }

    private void HandleMovement()
    {
        Animator anim = GetComponent<Animator>();

        // --- CLIMBING MOVEMENT ---
        if (isClimbing)
        {
            float vInput = 0;
            if (Keyboard.current.wKey.isPressed) vInput = 1;
            if (Keyboard.current.sKey.isPressed) vInput = -1;

            Vector3 climbDir = new Vector3(0, vInput * climbSpeed, 0);
            characterController.Move(climbDir * Time.deltaTime);
            
            // Tırmanırken Gravity YOK
            velocity = Vector3.zero; 

            // Zıplayıp bırakmak için
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                isClimbing = false;
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity); // Zıpla
            }
            return; // Normal hareketi yapma
        }

        // --- NORMAL MOVEMENT ---

        // Ground Check
        bool isGrounded = characterController.isGrounded;
        
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // --- INPUT ---
        Vector2 input = Vector2.zero;
        bool isBlockingInput = false;

        if (!isDead && InputEnabled) 
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1;
            }

            // Right Click -> BLOCK
            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
            {
                isBlockingInput = true;
            }
        }

        // --- SPRINT, CROUCH & BLOCK LOGIC ---
        float currentSpeed = moveSpeed;
        float targetHeight = normalHeight;

        // Block -> Yavaşla (En öncelikli)
        if (isBlockingInput)
        {
            currentSpeed = crouchSpeed; // Blok yaparken yavaş yürüsün (Crouch hızıyla aynı olsun şimdilik)
        }
        // Shift -> Sprint (Blok yoksa)
        else if (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed && !Keyboard.current.leftCtrlKey.isPressed)
        {
            currentSpeed = sprintSpeed;
        }
        // Ctrl -> Crouch (Blok yoksa)
        else if (Keyboard.current != null && Keyboard.current.leftCtrlKey.isPressed)
        {
            currentSpeed = crouchSpeed;
            targetHeight = crouchHeight;
        }

        // Height Smoothness
        characterController.height = Mathf.Lerp(characterController.height, targetHeight, Time.deltaTime * 10f);

        // (Animation updates moved to end of function)

        // --- ROTATION (TPS) ---
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
            if (anim != null) anim.SetTrigger("Jump");
        }

        // --- GRAVITY ---
        velocity.y += gravity * Time.deltaTime;

        // Combine
        Vector3 finalMove = (moveDir * currentSpeed) + velocity;
        characterController.Move(finalMove * Time.deltaTime);

        // --- ANIMATION SYNC ---
        // Animator'a hızı gönder
        Vector3 horizontalVelocity = new Vector3(characterController.velocity.x, 0, characterController.velocity.z);
        float speed = horizontalVelocity.magnitude;
        
        if (anim != null)
        {
            anim.SetFloat("Speed", speed);
            anim.SetBool("IsBlocking", isBlockingInput); 
            anim.SetBool("Climb", isClimbing);
            anim.SetBool("IsGrounded", isGrounded);
        }
    }

    // --- TRIGGER DETECTION FOR LADDER ---
    private void OnTriggerEnter(Collider other)
    {
        // SiegeLadder scriptine sahip bir objeye (trigger'ına) girdik mi?
        if (other.GetComponentInParent<SiegeLadder>() != null)
        {
            canClimb = true;
            // Debug.Log("Merdiven Alanı: E ile Tırman");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.GetComponentInParent<SiegeLadder>() != null)
        {
            canClimb = false;
            isClimbing = false; // Alandan çıkınca düşersin (veya inersin)
        }
    }

    private void HandleInteraction()
    {
        // Kamera merkezinden ileriye raycast at
        if (PlayerCamera.Instance != null)
        {
            Ray ray = new Ray(PlayerCamera.Instance.transform.position, PlayerCamera.Instance.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, interactionDistance))
            {
                // GateController var mı?
                GateController gate = hit.collider.GetComponentInParent<GateController>();
                if (gate != null)
                {
                    gate.CmdInteract();
                }
            }
        }
    }
}
