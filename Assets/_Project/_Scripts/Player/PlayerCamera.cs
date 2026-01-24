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
    private float pitch; // Vertical rotation (Look Up/Down)
    private float yaw;   // Horizontal rotation (Look Left/Right)

    // Singleton-like access for Player to register itself
    public static PlayerCamera Instance;

    private void Awake()
    {
        Instance = this;
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

        // Read Mouse Delta
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        
        // Yaw (Horizontal): Rotates the TARGET (Player) directly for TPS feel? 
        // OR Rotates Camera around Target?
        // STRATEGY: 
        // - Camera rotates Yaw and Pitch. 
        // - Player rotates to match Camera Yaw (usually handled in PlayerController for Network sync).
        // Let's keep Camera independent first.
        
        yaw += mouseDelta.x * mouseSensitivity * 0.1f;
        pitch -= mouseDelta.y * mouseSensitivity * 0.1f;
        
        // Clamp Vertical
        pitch = Mathf.Clamp(pitch, verticalClampMin, verticalClampMax);

        // Apply Rotation
        transform.eulerAngles = new Vector3(pitch, yaw, 0f);
    }

    private void HandleFollow()
    {
        // Calculate desired position based on rotation and offset
        // Quaternion * Vector3 applies the rotation to the offset vector
        Vector3 targetPosition = target.position + (transform.rotation * offset);
        
        // Smooth Follow
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);
    }
    
    // Helper to get logic forward (mostly for PlayerController movement direction)
    public Quaternion GetCameraRotation()
    {
        return Quaternion.Euler(0, yaw, 0);
    }
}
