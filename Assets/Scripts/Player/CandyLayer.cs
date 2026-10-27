using UnityEngine;

public class CandyLayer : MonoBehaviour
{
    public Transform candyLayer;
    public int candiesPerLayer = 20;
    public float heightPerLayer = 0.1f;

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
