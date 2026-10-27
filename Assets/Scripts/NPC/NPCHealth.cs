using System;
using UnityEngine;

/// <summary>
/// NPC damage/death. Every bullet sprays blood; death blows the body apart via GoreFX.
/// Hits above headHeight (fraction of the body height) count as headshots.
/// </summary>
public class NPCHealth : MonoBehaviour
{
    [Tooltip("Any bullet kills instantly and blows the body apart.")]
    [SerializeField] private bool oneShotKill = true;
    [SerializeField, Min(1f)] private float maxHealth = 100f;
    [SerializeField, Range(0.5f, 0.95f)] private float headHeight = 0.8f;
    [SerializeField, Min(1f)] private float headshotMultiplier = 3f;
    [SerializeField] private Animator animator;

    public event Action Died;

    public bool IsDead => dead;

    private float health;
    private bool dead;
    private Renderer[] bodyRenderers;
    private Collider[] bodyColliders;

    private void Awake()
    {
        health = maxHealth;
        if (animator == null) animator = GetComponentInChildren<Animator>();
        CacheBody();
    }

    public void TakeDamage(float amount, RaycastHit hit) => TakeDamage(amount, hit, -hit.normal);

    public void TakeDamage(float amount, RaycastHit hit, Vector3 shotDirection)
    {
        if (dead || amount <= 0f) return;

        bool headshot = IsHeadshot(hit.point);
        GoreFX.Instance.BulletWound(hit, shotDirection, transform, headshot);

        health -= oneShotKill ? health : amount * (headshot ? headshotMultiplier : 1f);
        if (health <= 0f) Die(hit.point, shotDirection, headshot);
    }

    public void Die(Vector3 hitPoint, Vector3 shotDirection) => Die(hitPoint, shotDirection, false);

    private void Die(Vector3 hitPoint, Vector3 shotDirection, bool headshot)
    {
        if (dead) return;
        dead = true;

        if (animator != null && animator.isActiveAndEnabled && HasParameter(animator, "Die"))
            animator.SetTrigger("Die");

        // Measure the body before hiding it; disabled renderers report empty bounds.
        Bounds bounds = GetBodyBounds();
        Material clothes = GetClothesMaterial();

        SetBodyVisible(false);
        RemoveWounds();
        GoreFX.Instance.Dismember(bounds, hitPoint, shotDirection, headshot, clothes);

        Died?.Invoke();
    }

    /// <summary>Restores the NPC for the next encounter and removes old wounds.</summary>
    public void ResetHealth()
    {
        health = maxHealth;
        dead = false;
        RemoveWounds();
        SetBodyVisible(true);
    }

    private void RemoveWounds()
    {
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child != transform && (child.name == "BloodDecal" || child.name == "BloodSpray"))
                Destroy(child.gameObject);
        }
    }

    private static bool HasParameter(Animator target, string name)
    {
        foreach (AnimatorControllerParameter parameter in target.parameters)
            if (parameter.name == name) return true;
        return false;
    }

    private bool IsHeadshot(Vector3 point)
    {
        Bounds bounds = GetBodyBounds();
        return point.y > bounds.min.y + bounds.size.y * headHeight;
    }

    private void CacheBody()
    {
        if (bodyRenderers != null) return;
        bodyRenderers = GetComponentsInChildren<Renderer>(true);
        bodyColliders = GetComponentsInChildren<Collider>(true);
    }

    private void SetBodyVisible(bool visible)
    {
        CacheBody();
        foreach (Renderer r in bodyRenderers) if (r != null) r.enabled = visible;
        foreach (Collider c in bodyColliders) if (c != null) c.enabled = visible;
    }

    private Bounds GetBodyBounds()
    {
        CacheBody();
        Bounds bounds = new Bounds(transform.position, Vector3.one * 0.5f);
        bool first = true;

        foreach (Renderer r in bodyRenderers)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
            if (first) { bounds = r.bounds; first = false; }
            else bounds.Encapsulate(r.bounds);
        }

        return bounds;
    }

    private Material GetClothesMaterial()
    {
        foreach (Renderer r in bodyRenderers)
            if (r != null && !(r is ParticleSystemRenderer) && r.sharedMaterial != null)
                return r.sharedMaterial;
        return null;
    }
}
