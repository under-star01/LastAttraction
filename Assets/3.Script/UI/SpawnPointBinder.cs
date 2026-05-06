using System.Collections.Generic;
using UnityEngine;

public class SpawnPointBinder : MonoBehaviour
{
    public static SpawnPointBinder Instance { get; private set; }

    [Header("Spawn Points")]
    [SerializeField] private Transform killerSpawnPoint;
    [SerializeField] private List<Transform> survivorSpawnPoints = new();

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public Transform GetKillerSpawnPoint()
    {
        return killerSpawnPoint;
    }

    public Transform GetSurvivorSpawnPoint(int index)
    {
        if (survivorSpawnPoints == null)
            return null;

        if (index < 0 || index >= survivorSpawnPoints.Count)
            return null;

        return survivorSpawnPoints[index];
    }

    public List<Transform> GetSurvivorSpawnPoints()
    {
        return survivorSpawnPoints;
    }
}