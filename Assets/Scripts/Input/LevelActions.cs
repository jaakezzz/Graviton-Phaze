// LevelActions.cs
using UnityEngine;

public class LevelActions : MonoBehaviour
{
    public static LevelActions Instance { get; private set; }

    [Header("Config")]
    [SerializeField] LevelConfig levelConfig;

    [Header("Runtime (read-only)")]
    [SerializeField] int totalActions;
    [SerializeField] int probeFires;
    [SerializeField] int shipLaunches;

    bool launchCountedThisFlight;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetConfig(LevelConfig cfg)
    {
        levelConfig = cfg;
    }

    public void ResetForLevel()
    {
        totalActions = 0;
        probeFires = 0;
        shipLaunches = 0;
        launchCountedThisFlight = false;
    }

    public void RestartFlightGate()
    {
        // allow counting the next “first launch”
        launchCountedThisFlight = false;
    }

    public void CountProbeFire()
    {
        probeFires++;
        totalActions++;
        // Debug.Log($"[Actions] Probe x{probeFires} (total {totalActions})");
    }

    public void CountFirstLaunchThisFlight()
    {
        if (launchCountedThisFlight) return;
        launchCountedThisFlight = true;
        shipLaunches++;
        totalActions++;
        // Debug.Log($"[Actions] Launch x{shipLaunches} (total {totalActions})");
    }

    public int ComputeStars()
    {
        int par = Mathf.Max(0, levelConfig ? levelConfig.minActionsForFiveStars : 0);
        int over = Mathf.Max(0, totalActions - par);
        if (over == 0) return 5;
        if (over == 1) return 4;
        if (over <= 3) return 3;   // 2–3 over
        if (over <= 7) return 2;   // 4–7 over
        return 1;                  // 8+ over
    }

    public (int total, int probes, int launches, int par) Snapshot()
    {
        int par = levelConfig ? levelConfig.minActionsForFiveStars : 0;
        return (totalActions, probeFires, shipLaunches, par);
    }
}
