using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads a LevelConfig asset and builds the runtime level content.
/// 
/// For now:
/// - Moves an existing PlayerSpawn marker in the scene
/// - Spawns Goal prefab
/// - Spawns GravityWell prefabs
/// - Spawns AutoDockNode prefabs
/// 
/// This keeps things modular and is a good foundation for:
/// - selected levels from the main menu
/// - future in-game level editor saves
/// - cloud/user-made levels later
/// </summary>
[DefaultExecutionOrder(-200)] // Build before most scene scripts Start()
public class LevelBuilder : MonoBehaviour
{
    [Header("Level Source")]
    [Tooltip("The LevelConfig asset that defines this level.")]
    [SerializeField] LevelConfig levelConfig;

    [Tooltip("If true, the level builds automatically in Awake().")]
    [SerializeField] bool buildOnAwake = true;

    [Header("Scene Anchors")]
    [Tooltip("Existing PlayerSpawn transform used by PhaseDirector. The builder will move this to the config position.")]
    [SerializeField] Transform playerSpawnMarker;

    [Tooltip("Existing Goal transform. The builder will move this to the config position.")]
    [SerializeField] Transform goalMarker;

    [Header("Prefabs")]
    [Tooltip("Gravity well prefab to spawn for each gravity well entry in the config.")]
    [SerializeField] GravityWell2D gravityWellPrefab;

    [Tooltip("Dock node prefab to spawn for each dock node entry in the config.")]
    [SerializeField] AutoDockNode dockNodePrefab;

    [Header("Optional Parents")]
    [Tooltip("Optional parent for spawned goal object.")]
    [SerializeField] Transform goalRoot;

    [Tooltip("Optional parent for spawned gravity wells.")]
    [SerializeField] Transform wellsRoot;

    [Tooltip("Optional parent for spawned dock nodes.")]
    [SerializeField] Transform docksRoot;

    // Track everything this builder spawned so we can clear/rebuild cleanly.
    readonly List<GameObject> spawnedObjects = new();

    // Optional public access if other systems want to ask what config was used.
    public LevelConfig CurrentConfig => levelConfig;

    void Awake()
    {
        if (buildOnAwake)
            BuildLevel();
    }

    /// <summary>
    /// Builds the level from the currently assigned LevelConfig.
    /// </summary>
    [ContextMenu("Build Level")]
    public void BuildLevel()
    {
        var cfg = GetResolvedLevelConfig();

        if (!cfg)
        {
            Debug.LogWarning("LevelBuilder: No LevelConfig assigned or selected.");
            return;
        }

        // Clear anything previously built by this builder.
        ClearBuiltLevel();

        // Move the existing PlayerSpawn marker so PhaseDirector can keep using it.
        if (playerSpawnMarker)
        {
            playerSpawnMarker.position = cfg.playerSpawnPosition;
        }
        else
        {
            Debug.LogWarning("LevelBuilder: Player Spawn Marker is not assigned.");
        }

        MoveGoal(cfg);
        BuildGravityWells(cfg);
        BuildDockNodes(cfg);
    }

    /// <summary>
    /// Clears only the objects spawned by this builder.
    /// </summary>
    [ContextMenu("Clear Built Level")]
    public void ClearBuiltLevel()
    {
        for (int i = spawnedObjects.Count - 1; i >= 0; i--)
        {
            var go = spawnedObjects[i];
            if (!go) continue;

            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        spawnedObjects.Clear();
    }

    void MoveGoal(LevelConfig cfg)
    {
        if (!goalMarker)
        {
            Debug.LogWarning("LevelBuilder: Goal Marker is not assigned.");
            return;
        }

        goalMarker.position = cfg.goalPosition;
    }

    void BuildGravityWells(LevelConfig cfg)
    {
        if (!gravityWellPrefab)
        {
            if (cfg.gravityWells != null && cfg.gravityWells.Count > 0)
                Debug.LogWarning("LevelBuilder: Gravity Well Prefab is not assigned.");
            return;
        }

        if (cfg.gravityWells == null) return;

        foreach (var data in cfg.gravityWells)
        {
            var well = Instantiate(
                gravityWellPrefab,
                data.position,
                Quaternion.identity,
                wellsRoot
            );

            // Map config data into the actual GravityWell2D fields.
            well.S = data.strength;
            well.eps = data.epsilon;

            well.UpdateVisuals();

            spawnedObjects.Add(well.gameObject);
        }
    }

    void BuildDockNodes(LevelConfig cfg)
    {
        if (!dockNodePrefab)
        {
            if (cfg.dockNodes != null && cfg.dockNodes.Count > 0)
                Debug.LogWarning("LevelBuilder: Dock Node Prefab is not assigned.");
            return;
        }

        if (cfg.dockNodes == null) return;

        foreach (var data in cfg.dockNodes)
        {
            var dock = Instantiate(
                dockNodePrefab,
                data.position,
                Quaternion.identity,
                docksRoot
            );

            ApplyDockData(dock, data);

            spawnedObjects.Add(dock.gameObject);
        }
    }

    void ApplyDockData(AutoDockNode dock, LevelConfig.DockNodeData data)
    {
        // Accepted probe types
        dock.acceptStabilizer = data.acceptStabilizer;
        dock.acceptRepulsor = data.acceptRepulsor;
        dock.acceptJetstream = data.acceptJetstream;
        dock.acceptVortex = data.acceptVortex;

        // Stabilizer overrides
        dock.stabilizer.apply = data.overrideStabilizer;
        dock.stabilizer.U0 = data.stabilizerU0;
        dock.stabilizer.R = data.stabilizerR;
        dock.stabilizer.accelClamp = data.stabilizerAccelClamp;

        // Repulsor overrides
        dock.repulsor.apply = data.overrideRepulsor;
        dock.repulsor.S = data.repulsorS;
        dock.repulsor.epsilon = data.repulsorEpsilon;

        // Jetstream overrides
        dock.jetstream.apply = data.overrideJetstream;
        dock.jetstream.E = data.jetstreamE;
        dock.jetstream.radius = data.jetstreamRadius;
        dock.jetstream.smoothEdges = data.jetstreamSmoothEdges;
        dock.jetstream.R = data.jetstreamR;

        // Vortex overrides
        dock.vortex.apply = data.overrideVortex;
        dock.vortex.Omega = data.vortexOmega;
        dock.vortex.radius = data.vortexRadius;
        dock.vortex.aMax = data.vortexAMax;
        dock.vortex.clockwise = data.vortexClockwise;

        // Refresh dock visuals after applying runtime data.
        // AutoDockNode already refreshes in OnEnable, but that happens before
        // we finish overriding everything here, so we force one more refresh.
        dock.RefreshDockVisuals();

        // Toggle enabled to force its OnEnable() visual refresh path as well
        // (useful for jetstream arrow / runtime-applied settings).
        dock.enabled = false;
        dock.enabled = true;
    }

    // HELPERS
    LevelConfig GetResolvedLevelConfig()
    {
        if (LevelSelectionService.SelectedLevel != null)
            return LevelSelectionService.SelectedLevel;

        return levelConfig;
    }
}