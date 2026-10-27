using UnityEngine;

public class CandyLayer : MonoBehaviour
{
    public Transform candyLayer;
    public int candiesPerLayer = 1;
    public float heightPerLayer = 0.10f;

    private Vector3 startPosition;

    void Start()
    {
        startPosition = candyLayer.localPosition;
    }

    public void UpdateCandyCount(int candyCount)
    {
        int layers = candyCount / candiesPerLayer;

        candyLayer.localPosition = startPosition + new Vector3(0, layers * heightPerLayer, 0);
    }
}
