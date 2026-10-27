
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonController_TEST : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField] private Transform cameraTransform;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float gravity = -20f;

    [Header("Look")]
    [SerializeField] private float minPitch = -80f;
    [SerializeField] private float maxPitch = 80f;

    private CharacterController controller;
    private float pitch;
    private float verticalVelocity;
    private bool controlsEnabled = true;

    public bool ControlsEnabled => controlsEnabled;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (cameraTransform == null)
        {
            Camera childCamera = GetComponentInChildren<Camera>();
            if (childCamera != null)
                cameraTransform = childCamera.transform;
        }

        if (cameraTransform != null)
        {
            pitch = cameraTransform.localEulerAngles.x;

            if (pitch > 180f)
                pitch -= 360f;

            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }
    }

    private void Start()
    {
        UpdateCursor();
    }

    private void Update()
    {
        if (!controlsEnabled)
        {
            UpdateCursor();
            return;
        }

        UpdateCursor();
        HandleMouseLook();
        HandleMovement();
    }

    private void HandleMouseLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || !CursorLock.IsLocked) // WebGL: wait for the click that captures the mouse
            return;

        Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;

        transform.Rotate(0f, delta.x, 0f);

        pitch = Mathf.Clamp(
            pitch - delta.y,
            minPitch,
            maxPitch
        );

        if (cameraTransform != null)
        {
            cameraTransform.localRotation =
                Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void HandleMovement()
    {
        if (controller == null || !controller.enabled)
            return;

        Keyboard keyboard = Keyboard.current;

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard != null)
        {
            horizontal =
                (keyboard.dKey.isPressed ? 1f : 0f) -
                (keyboard.aKey.isPressed ? 1f : 0f);

            vertical =
                (keyboard.wKey.isPressed ? 1f : 0f) -
                (keyboard.sKey.isPressed ? 1f : 0f);
        }

        Vector3 movement =
            transform.right * horizontal +
            transform.forward * vertical;

        movement =
            Vector3.ClampMagnitude(movement, 1f) * moveSpeed;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        movement.y = verticalVelocity;
        controller.Move(movement * Time.deltaTime);
    }

    public void SetControlsEnabled(bool enabled)
    {
        controlsEnabled = enabled;
        UpdateCursor();
    }

    public void AddPitch(float degrees)
    {
        pitch = Mathf.Clamp(
            pitch - degrees,
            minPitch,
            maxPitch
        );

        if (cameraTransform != null)
        {
            cameraTransform.localRotation =
                Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void UpdateCursor()
    {
        CursorLock.Set(controlsEnabled);
    }

    private void OnGUI()
    {
        // WebGL only: the browser needs a click before it hands over the mouse.
        if (!controlsEnabled || CursorLock.IsLocked)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(28 * Screen.height / 1080f),
            fontStyle = FontStyle.Bold
        };
        GUI.Label(new Rect(0, Screen.height * 0.45f, Screen.width, 60), "Click to look around", style);
    }

    private void OnDisable()
    {
        CursorLock.Set(false);
    }
}
