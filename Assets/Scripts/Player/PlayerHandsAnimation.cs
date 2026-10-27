
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerHandsAnimation : MonoBehaviour
{
    [Header("Animation States")]
    [SerializeField] private string walkState = "Walk";

    [SerializeField]
    private string[] candyStates =
    {
        "GettingCandy",
        "GettingCandy1",
        "GettingCandy 2",
        "GettingCandy3"
    };

    [SerializeField]
    private string[] carryStates =
    {
        "Walk Candy1",
        "Walk Candy2",
        "WalkCandy3"
    };

    [SerializeField, Min(0f)] private float moveThreshold = 0.1f;
    [SerializeField, Min(0.1f)] private float blendSpeed = 4f;
    [SerializeField, Min(0f)] private float crossFade = 0.1f;

    private Animator animator;
    private CharacterController controller;
    private FirstPersonController_TEST player;

    private bool posing;
    private int candyCount;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;

        controller = GetComponent<CharacterController>();
        player = GetComponent<FirstPersonController_TEST>();
    }

    private void Update()
    {
        if (posing)
        {
            animator.speed = 1f;
            return;
        }

        bool canMove = player == null || player.ControlsEnabled;
        float speed = 0f;

        if (controller != null)
        {
            Vector3 velocity = controller.velocity;
            speed = new Vector2(velocity.x, velocity.z).magnitude;
        }

        float target = canMove && speed > moveThreshold ? 1f : 0f;

        animator.speed = Mathf.MoveTowards(
            animator.speed,
            target,
            Time.deltaTime * blendSpeed);
    }

    public void PlayCandyPose()
    {
        if (candyStates.Length == 0)
            return;

        posing = true;
        animator.speed = 1f;

        int index = Mathf.Min(candyCount, candyStates.Length - 1);

        animator.CrossFade(candyStates[index], crossFade, 0, 0f);
    }

    public void FinishCandyPose()
    {
        if (candyStates.Length == 0)
            return;

        int index = Mathf.Min(candyCount, candyStates.Length - 1);

        if (index < carryStates.Length)
        {
            animator.CrossFade(carryStates[index], crossFade);
        }
        else
        {
            animator.CrossFade(candyStates[index], crossFade);
        }

        posing = false;
    }

    public void AddCandy()
    {
        PlayCandyPose();
        candyCount = Mathf.Min(candyCount + 1, candyStates.Length - 1);
    }

    public void PlayWalk()
    {
        posing = false;
        animator.CrossFade(walkState, crossFade);
    }
}
