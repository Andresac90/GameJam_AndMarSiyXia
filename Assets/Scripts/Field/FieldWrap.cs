using UnityEngine;

/// <summary>
/// Keeps the player inside the elliptical field by sending them to the opposite side
/// when they leave it. Works alongside FirstPersonController_TEST (movement stays there).
/// Put this on the Player root (the object with the CharacterController).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FieldWrap : MonoBehaviour
{
    public FieldBounds field;

    CharacterController controller;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (field == null) field = FindAnyObjectByType<FieldBounds>();
    }

    void LateUpdate()
    {
        if (field == null || !field.IsOutside(transform.position)) return;

        // A CharacterController overrides transform.position unless it is disabled during a teleport.
        controller.enabled = false;
        transform.position = field.GetOppositePoint(transform.position);
        controller.enabled = true;
    }
}
