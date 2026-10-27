
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [Header("References")]
    public FirstPersonController_TEST player;

    [Header("Interaction")]
    public float reach = 2.5f;
    public LayerMask mask = ~(1 << 2);

    private IInteractable current;

    private void Awake()
    {
        if (player == null)
            player = GetComponentInParent<FirstPersonController_TEST>();
    }

    private void Update()
    {
        current = null;

        if (player != null && !player.ControlsEnabled)
            return;

        Ray ray = new Ray(transform.position, transform.forward);

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                reach,
                mask,
                QueryTriggerInteraction.Ignore))
        {
            return;
        }

        IInteractable interactable =
            hit.collider.GetComponentInParent<IInteractable>();

        if (interactable == null || !interactable.CanInteract)
            return;

        current = interactable;

        if (Keyboard.current != null &&
            Keyboard.current.eKey.wasPressedThisFrame)
        {
            current.Interact();
        }
    }

    private void OnGUI()
    {
        if (current == null)
            return;

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22
        };

        GUI.Label(
            new Rect(0, Screen.height * 0.6f, Screen.width, 40),
            current.Prompt,
            style
        );
    }
}
