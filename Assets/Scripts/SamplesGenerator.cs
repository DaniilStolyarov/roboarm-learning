using System.Collections.Generic;
using UnityEngine;
public class SamplesGenerator : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public List<GameObject> Prefabs;
    public float PositionStandard;
    public int TotalObjectsCount;
    public bool RandomizeSizes;
    public float MaxScaleFactor;
    public float MinScaleFactor;
    public bool UseRuntimeGenerator;

    public Stack<GameObject> SpawnedGameObjects;
    void Start()
    {
        SpawnedGameObjects = new Stack<GameObject>();
        if (UseRuntimeGenerator) return;
        for (int i = 0; i < TotalObjectsCount; i++)
        {
            SpawnObject();
        }
    }
    static Vector3 LinearMultiply(Vector3 a, Vector3 b)
    {
        return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
    }
    static Vector3 RandomPositiveVector()
    {
        return new Vector3(
            Random.value, Random.value, Random.value
            );
    }

    public void SpawnObject()
    {
        int prefabIndex = Random.Range(0, Prefabs.Count);
        Vector3 prefabPosition = Random.insideUnitSphere * PositionStandard + transform.position;
        GameObject instance = Instantiate(Prefabs[prefabIndex], prefabPosition, Random.rotationUniform);
        if (RandomizeSizes)
        {
            Vector3 scaleFactor = RandomPositiveVector() * (MaxScaleFactor - MinScaleFactor) + Vector3.one * MinScaleFactor;
            instance.transform.localScale = LinearMultiply(instance.transform.localScale, scaleFactor);
        }
        SpawnedGameObjects.Push(instance);
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKey(KeyCode.I))
        {
            SpawnObject();
        }
    }
}
