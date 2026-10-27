using UnityEngine;
[RequireComponent(typeof(Animator))]
public class PlayerHandsAnimation : MonoBehaviour
{
    [SerializeField] private string walkState = "Walk";
    [SerializeField] private string candyState = "GettingCandy";
    [SerializeField, Min(0f)] private float moveThreshold = 0.1f;
    [SerializeField, Min(0.1f)] private float blendSpeed = 4f;
    [SerializeField, Min(0f)] private float crossFade = 0.15f;

    private Animator animator;
    private CharacterController controller;
    private FirstPersonController_TEST player;
    private bool posing;

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
        posing = true;
        animator.speed = 1f;
        animator.CrossFade(candyState, crossFade);
    }

    public void PlayWalk()
    {
        posing = false;
        animator.CrossFade(walkState, crossFade);
    }
}
