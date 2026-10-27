using UnityEngine;

/// <summary>
/// Put this on the house prefab. Your teammate's dialogue system should call
/// house.Finish() when the player is done (after kill/spare is decided).
/// </summary>
public class House : MonoBehaviour
{
    [HideInInspector] public HouseSpawner spawner;

    public void Finish()
    {
        if (spawner != null) spawner.SpawnHouse();   // destroys this house and spawns the next one
        else Destroy(gameObject);
    }
}
