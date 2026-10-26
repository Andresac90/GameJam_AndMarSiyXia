using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMove : MonoBehaviour
{
    //[SerializeField] private string horizontalInputName = "Horizontal";
    //[SerializeField] private string verticalInputName = "Vertical";
    [SerializeField] private InputActionReference movementAction;

    [SerializeField] private float movementSpeed = 2f;

    //TEST OPENING DOOR
    [SerializeField] private MoveObjectController testDoor;

    private CharacterController charController;


    private void Awake()
    {
        charController = GetComponent<CharacterController>();
    }

    private void OnEnable()
    {
        if (movementAction == null)
        {
            Debug.LogError("PlayerMove: Movement Action not assigned", this);
            enabled = false;
            return;
        }
        movementAction.action.Enable();
    }

    private void OnDisable()
    {
        if (movementAction != null)
            movementAction.action.Disable();
    }

    private void Update()
    {
        PlayerMovement();

        //TEST OPENING DOOR
        if (testDoor != null && Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            testDoor.OpenDoor();
        }
        if(testDoor != null && Keyboard.current != null && Keyboard.current.yKey.wasPressedThisFrame)
        {
            testDoor.CloseDoor();
        }
    }

    private void PlayerMovement()
    {
        //float vertInput = Input.GetAxis(verticalInputName) * movementSpeed;     //CharacterController.SimpleMove() applies deltaTime
        //float horizInput = Input.GetAxis(horizontalInputName) * movementSpeed;

        Vector2 input = movementAction.action.ReadValue<Vector2>() * movementSpeed;

        //Vector3 forwardMovement = transform.forward * vertInput;
        //Vector3 rightMovement = transform.right * horizInput;

        Vector3 forwardMovement = transform.forward * input.y;
        Vector3 rightMovement = transform.right * input.x;

        //simple move applies delta time automatically
        charController.SimpleMove(forwardMovement + rightMovement);
    }
}
