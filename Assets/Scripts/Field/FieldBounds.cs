using UnityEngine;

/// <summary>
/// Defines the elliptical field (centered on this object's position, on the XZ plane)
/// and provides helpers for containment, random points and wrap-around.
/// </summary>
public class FieldBounds : MonoBehaviour
{
    [Header("Field size (half-widths)")]
    public float radiusX = 50f;   // left-right
    public float radiusZ = 40f;   // front-back

    [Tooltip("How far inside the edge the player lands after wrapping (0.95 = 5% inside). Prevents instant re-wrapping.")]
    [Range(0.8f, 0.99f)] public float wrapInset = 0.95f;

    public Vector3 Center => transform.position;

    // Normalized ellipse value: <1 inside, =1 on the edge, >1 outside.
    public float EllipseValue(Vector3 worldPos)
    {
        float dx = (worldPos.x - Center.x) / radiusX;
        float dz = (worldPos.z - Center.z) / radiusZ;
        return dx * dx + dz * dz;
    }

    public bool IsOutside(Vector3 worldPos) => EllipseValue(worldPos) > 1f;

    /// <summary>Returns the point on the opposite side of the field (mirrored through the center).</summary>
    public Vector3 GetOppositePoint(Vector3 worldPos)
    {
        Vector3 offset = worldPos - Center;
        Vector3 mirrored = new Vector3(-offset.x, 0f, -offset.z);

        // Pull it back inside the ellipse so the player doesn't instantly wrap again.
        float e = Mathf.Sqrt(Mathf.Max(EllipseValue(worldPos), 0.0001f));
        mirrored = mirrored / e * wrapInset;

        return new Vector3(Center.x + mirrored.x, worldPos.y, Center.z + mirrored.z);
    }

    /// <summary>Uniformly random point inside the field. margin = 0..1 keeps it away from the edge.</summary>
    public Vector3 GetRandomPoint(float margin = 0.85f)
    {
        float angle = Random.value * Mathf.PI * 2f;
        float r = Mathf.Sqrt(Random.value) * margin; // sqrt = uniform distribution over area
        return new Vector3(
            Center.x + Mathf.Cos(angle) * radiusX * r,
            Center.y,
            Center.z + Mathf.Sin(angle) * radiusZ * r);
    }

    // Draws the field outline in the Scene view.
    void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        const int steps = 64;
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= steps; i++)
        {
            float a = i / (float)steps * Mathf.PI * 2f;
            Vector3 p = transform.position + new Vector3(Mathf.Cos(a) * radiusX, 0, Mathf.Sin(a) * radiusZ);
            if (i > 0) Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}
