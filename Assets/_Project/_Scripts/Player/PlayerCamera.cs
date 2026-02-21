using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCamera : MonoBehaviour
{
    [Header("Target Settings")]
    [SerializeField] private Vector3 offset = new Vector3(0, 2f, -4f); // Shoulder offset
    [SerializeField] private float smoothSpeed = 10f;

    [Header("Input Settings")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float verticalClampMin = -30f;
    [SerializeField] private float verticalClampMax = 60f;

    private Transform target;
    private float pitch;
    private float yaw;

    private Vector3 defaultOffset;
    private Vector3 currentOffset;
    [SerializeField] private float commandZoomMultiplier = 1.5f; // F1'de ne kadar uzaklaşsın
    [SerializeField] private float zoomSpeed = 5f;

    // Singleton-like access for Player to register itself
    public static PlayerCamera Instance;

    private void Awake()
    {
        Instance = this;
        defaultOffset = offset;
        currentOffset = offset;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        
        // Initialize rotation based on current state
        Vector3 angles = transform.eulerAngles;
        pitch = angles.x;
        yaw = angles.y;

        // Lock Cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void LateUpdate()
    {
        if (target == null) return;

        HandleRotation();
        HandleFollow();
    }

    private void HandleRotation()
    {
        if (Mouse.current == null) return;
        if (rotationLocked) return; // Komut modunda kamerayı döndürme

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        
        yaw += mouseDelta.x * mouseSensitivity * 0.1f;
        pitch -= mouseDelta.y * mouseSensitivity * 0.1f;
        
        pitch = Mathf.Clamp(pitch, verticalClampMin, verticalClampMax);

        transform.eulerAngles = new Vector3(pitch, yaw, 0f);
    }

    private void HandleFollow()
    {
        // Smooth zoom geçişi
        currentOffset = Vector3.Lerp(currentOffset, offset, zoomSpeed * Time.deltaTime);

        Vector3 targetPosition = target.position + (transform.rotation * currentOffset);
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);
    }

    // F1 komut modu zoom
    public void SetCommandZoom(bool active)
    {
        offset = active ? defaultOffset * commandZoomMultiplier : defaultOffset;
    }

    // Komut modunda kamera dönmesin
    private bool rotationLocked = false;
    public void SetRotationLock(bool locked)
    {
        rotationLocked = locked;
    }
    
    // Helper to get logic forward (mostly for PlayerController movement direction)
    public Quaternion GetCameraRotation()
    {
        return Quaternion.Euler(0, yaw, 0);
    }
}
