using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person movement (WASD + mouse look) using the NEW Input System,
/// with field wrap-around. Requires a CharacterController on the same object.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FieldPlayerController : MonoBehaviour
{
    public FieldBounds field;
    public Transform cameraTransform;

    [Header("Movement")]
    public float moveSpeed = 4f;
    public float gravity = -20f;

    [Header("Look")]
    [Tooltip("Mouse delta is in pixels with the new Input System, so this value is much smaller than before.")]
    public float mouseSensitivity = 0.1f;

    [Tooltip("Turn off while a dialogue is open so the player can't walk/look around.")]
    public bool controlsEnabled = true;

    CharacterController controller;
    float verticalVelocity;
    float pitch;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (controlsEnabled)
        {
            Look();
            Move();
        }
        WrapAroundField();
    }

    void Look()
    {
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue() * mouseSensitivity;

        transform.Rotate(Vector3.up * delta.x);
        pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
        if (cameraTransform) cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);

        Vector3 input = transform.right * x + transform.forward * z;
        input = Vector3.ClampMagnitude(input, 1f) * moveSpeed;

        verticalVelocity = controller.isGrounded ? -1f : verticalVelocity + gravity * Time.deltaTime;
        input.y = verticalVelocity;

        controller.Move(input * Time.deltaTime);
    }

    void WrapAroundField()
    {
        if (field == null || !field.IsOutside(transform.position)) return;

        // CharacterController overrides transform.position unless it is disabled during a teleport.
        controller.enabled = false;
        transform.position = field.GetOppositePoint(transform.position);
        controller.enabled = true;
    }
}