using UnityEngine;

/// <summary>
/// Drives the first-person hands (PlayerKid.controller) from the candy count.
/// - Walk level follows the candy: 0 = Walk, 1 = Walk Candy 1, 2 = Walk Candy2, 3+ = Walk Candy 3.
/// - Gaining candy plays the matching pickup (GettingCandy, GettingCandy1, Getting Candy 2,
///   Getting Candy 3) and then settles into the next walk level.
/// - Losing candy (a monster steals one) just drops to the lower walk level.
/// The walk cycle only advances while the player moves. The controller has no
/// transitions on purpose: this script decides which state plays.
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerHandsAnimation : MonoBehaviour
{
    [Tooltip("Walk state per candy level. The last one is used for any higher count.")]
    [SerializeField] private string[] walkStates = { "Walk", "Walk Candy 1", "Walk Candy2", "Walk Candy 3" };
    [Tooltip("Pickup played when going from level i to i+1. The last one repeats for higher counts.")]
    [SerializeField] private string[] pickupStates = { "GettingCandy", "GettingCandy1", "Getting Candy 2", "Getting Candy 3" };
    [SerializeField, Min(0f)] private float moveThreshold = 0.1f;
    [SerializeField, Min(0.1f)] private float blendSpeed = 4f;
    [SerializeField, Min(0f)] private float crossFade = 0.15f;
    [Tooltip("Candy fill object inside the bucket. Hidden while the bucket is empty, because the "
             + "Walk / GettingCandy clips (made before the candy levels) keep it visible.")]
    [SerializeField] private string candyFillName = "Candy";

    public int CandyCount { get; private set; }

    private Animator animator;
    private CharacterController controller;
    private FirstPersonController_TEST player;
    private string playingPickup;
    private Renderer[] candyFill;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        controller = GetComponent<CharacterController>();
        player = GetComponent<FirstPersonController_TEST>();

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == candyFillName && t.parent != null && t.parent.name.Contains("Bucket"))
            {
                candyFill = t.GetComponentsInChildren<Renderer>(true);
                break;
            }
        }
    }

    // After the Animator has written this frame's pose: empty bucket = no candy fill.
    private void LateUpdate()
    {
        if (candyFill == null) return;

        bool empty = CandyCount == 0 || playingPickup == PickupState(0);
        foreach (Renderer r in candyFill)
            if (r != null) r.enabled = !empty;
    }

    private void Start()
    {
        animator.Play(WalkState(CandyCount), 0, 0f);
    }

    private void Update()
    {
        if (playingPickup != null)
        {
            // Pickups always play at full speed, even while the dialogue has the controls.
            animator.speed = 1f;

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (!animator.IsInTransition(0) && state.IsName(playingPickup) && state.normalizedTime >= 1f)
            {
                playingPickup = null;
                animator.CrossFade(WalkState(CandyCount), crossFade);
            }
            return;
        }

        // Let blends finish before freezing the walk cycle; a blend frozen at speed 0
        // leaves the pickup's falling candies hanging in the air.
        if (animator.IsInTransition(0))
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

    /// <summary>Sets the candy count; gaining plays a pickup, losing just drops a walk level.</summary>
    public void SetCandyCount(int count)
    {
        count = Mathf.Max(0, count);
        int previous = CandyCount;
        CandyCount = count;

        if (count > previous)
        {
            playingPickup = PickupState(previous);
            animator.speed = 1f;
            animator.CrossFade(playingPickup, crossFade, 0, 0f);
        }
        else if (count < previous || playingPickup == null)
        {
            playingPickup = null;
            animator.CrossFade(WalkState(count), crossFade);
        }
    }

    /// <summary>One more candy (used when there is no GameLoop, e.g. in test scenes).</summary>
    public void PlayCandyPose() => SetCandyCount(CandyCount + 1);

    /// <summary>Back to the walk that matches the current candy count.</summary>
    public void PlayWalk()
    {
        if (playingPickup == null)
            animator.CrossFade(WalkState(CandyCount), crossFade);
    }

    private string WalkState(int count) => walkStates[Mathf.Clamp(count, 0, walkStates.Length - 1)];
    private string PickupState(int fromCount) => pickupStates[Mathf.Clamp(fromCount, 0, pickupStates.Length - 1)];
}
