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

    [Tooltip("After wrapping, turn the player toward the field center, so walking off the edge " +
             "feels like walking back into the same place instead of facing the empty void.")]
    public bool faceCenterAfterWrap = true;

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

        if (faceCenterAfterWrap)
        {
            Vector3 toCenter = field.Center - transform.position;
            toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(toCenter); // yaw only; camera pitch is untouched
        }

        controller.enabled = true;
    }
}
