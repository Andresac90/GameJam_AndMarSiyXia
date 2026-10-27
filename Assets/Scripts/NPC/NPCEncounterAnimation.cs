using UnityEngine;

/// <summary>
/// Animates the NPC at the door (NPCEncounter.controller has no transitions on purpose:
/// this script picks the state).
/// - Talking while the conversation runs (the NPC is only active during an encounter).
/// - Spared human: hands over the candy, then idles.
/// - Spared monster: folds its arms (it just stole from you).
/// - Kill chosen: idles, watching you raise the gun.
/// </summary>
[RequireComponent(typeof(Animator))]
public class NPCEncounterAnimation : MonoBehaviour
{
    [SerializeField] private string talkingState = "Talking";
    [SerializeField] private string idleState = "Idle";
    [SerializeField] private string giveCandyState = "GiveCandy";
    [SerializeField] private string smugState = "FoldArms";
    [SerializeField, Min(0f)] private float crossFade = 0.25f;

    private Animator animator;
    private NPCConversationTest conversation;
    private string oneShot;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private void OnEnable()
    {
        if (conversation == null)
            conversation = FindAnyObjectByType<NPCConversationTest>();

        if (conversation != null)
        {
            conversation.CandyChosen += OnCandyChosen;
            conversation.KillChosen += OnKillChosen;
        }

        // Shown when the door opens: the conversation starts right away.
        oneShot = null;
        animator.Play(talkingState, 0, Random.value);
    }

    private void OnDisable()
    {
        if (conversation == null) return;
        conversation.CandyChosen -= OnCandyChosen;
        conversation.KillChosen -= OnKillChosen;
    }

    private void Update()
    {
        if (oneShot == null) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!animator.IsInTransition(0) && state.IsName(oneShot) && state.normalizedTime >= 1f)
        {
            oneShot = null;
            animator.CrossFade(idleState, crossFade);
        }
    }

    private void OnCandyChosen()
    {
        if (conversation.CurrentNPCIsMonster)
        {
            animator.CrossFade(smugState, crossFade);
            return;
        }

        oneShot = giveCandyState;
        animator.CrossFade(giveCandyState, crossFade, 0, 0f);
    }

    private void OnKillChosen()
    {
        oneShot = null;
        animator.CrossFade(idleState, crossFade);
    }
}
