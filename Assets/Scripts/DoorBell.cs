using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))]

public class DoorBell : MonoBehaviour
{
    public float reachRange = 1.8f;
    [SerializeField] private MoveObjectController door;

    private AudioSource ringAudio;
    private Camera fpsCamera;
    private Collider myCollider;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ringAudio = GetComponent<AudioSource>();
        myCollider = GetComponent<Collider>();
        fpsCamera = Camera.main;
    }

    // Update is called once per frame
    void Update()
    {
        bool pressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (!pressed && fpsCamera == null) return;

        if (Physics.Raycast(fpsCamera.transform.position, fpsCamera.transform.forward, out RaycastHit hit, reachRange, ~0, QueryTriggerInteraction.Ignore) && hit.collider == myCollider)
        {
            Ring();
        }
    }

    public void Ring()
    {
        ringAudio.Play();
        Debug.Log("Ringing Doorbell");
    }
}
