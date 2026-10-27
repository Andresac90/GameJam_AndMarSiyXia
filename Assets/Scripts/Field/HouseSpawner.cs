using UnityEngine;

/// <summary>
/// Keeps exactly one house in the field. When the current house is finished,
/// it is destroyed and a new one spawns at a random spot.
/// </summary>
public class HouseSpawner : MonoBehaviour
{
    public FieldBounds field;
    public Transform player;
    public GameObject housePrefab;

    [Header("Spawn rules")]
    [Tooltip("Minimum distance from the player so the house never appears right on top of them.")]
    public float minDistanceFromPlayer = 20f;
    [Range(0.3f, 0.95f)] public float edgeMargin = 0.85f;
    public LayerMask groundMask = ~0;   // set to your ground layer if you have uneven terrain

    GameObject currentHouse;

    void Start() => SpawnHouse();

    public void SpawnHouse()
    {
        if (currentHouse != null) Destroy(currentHouse);

        Vector3 pos = FindSpawnPoint();
        currentHouse = Instantiate(housePrefab, pos, Quaternion.Euler(0, Random.Range(0f, 360f), 0));

        // Let the house tell us when it's done.
        var house = currentHouse.GetComponent<House>();
        if (house == null) house = currentHouse.AddComponent<House>();
        house.spawner = this;
    }

    Vector3 FindSpawnPoint()
    {
        Vector3 p = field.GetRandomPoint(edgeMargin);

        // Retry until far enough from the player (max 30 tries so it can never freeze).
        for (int i = 0; i < 30; i++)
        {
            p = field.GetRandomPoint(edgeMargin);
            Vector3 flat = p - player.position; flat.y = 0;
            if (flat.magnitude >= minDistanceFromPlayer) break;
        }

        // Snap to the ground.
        if (Physics.Raycast(p + Vector3.up * 100f, Vector3.down, out RaycastHit hit, 300f, groundMask))
            p.y = hit.point.y;

        return p;
    }
}
