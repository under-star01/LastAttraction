using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SceneBinder : MonoBehaviour
{
    public static SceneBinder Instance { get; private set; }

    [Header("Spawn Points")]
    [SerializeField] private Transform killerSpawnPoint;
    [SerializeField] private List<Transform> survivorSpawnPoints = new();

    [Header("UI")]
    [SerializeField] private ProgressUI progressUI;
    [SerializeField] private QTEUI qteUI;
    [SerializeField] private CameraSkillUI cameraSkillUI;
    [SerializeField] private Image[] frameUI;

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

    public ProgressUI GetProgressUI()
    {
        return progressUI;
    }

    public QTEUI GetQTEUI()
    {
        return qteUI;
    }

    public CameraSkillUI GetCameraSkillUI()
    {
        return cameraSkillUI;
    }

    public Image[] GetFrameUI()
    {
        return frameUI;
    }
}