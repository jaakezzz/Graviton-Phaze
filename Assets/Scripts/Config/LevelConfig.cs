using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Graviton/Level Config")]
public class LevelConfig : ScriptableObject
{
    [Header("Metadata")]
    public int levelID = 0;
    public string levelName = "New Level";
    public string description = "Level description goes here.";

    [Header("Scoring")]
    [Min(0)] public int minActionsForFiveStars = 2;   // “par” for this level

    [Header("Ship")]
    [Min(0f)] public float startFuel = 5f;            // per-level starting fuel

    [Header("Core Objects")]
    public Vector2 playerSpawnPosition = new Vector2(0f, -4.15f);
    public Vector2 goalPosition = new Vector2(0f, 4.44f);

    [Header("Level Content")]
    public List<GravityWellData> gravityWells = new();
    public List<DockNodeData> dockNodes = new();

    [System.Serializable]
    public class GravityWellData
    {
        public Vector2 position;
        public float strength = 6f;   // maps to GravityWell2D.S
        public float epsilon = 0.1f;  // maps to GravityWell2D.eps
        public float accelClamp = 0f; // maps to GravityWell2D.aMax
    }

    [System.Serializable]
    public class DockNodeData
    {
        public Vector2 position;

        [Header("Accepted Probe Types")]
        public bool acceptStabilizer = true;
        public bool acceptRepulsor = true;
        public bool acceptJetstream = true;
        public bool acceptVortex = true;

        [Header("Optional Overrides")]
        public bool overrideStabilizer = false;
        public float stabilizerU0 = 5f;
        public float stabilizerR = 1.5f;
        public float stabilizerAccelClamp = 0f;

        [Space]

        public bool overrideRepulsor = false;
        public float repulsorS = -6f;
        public float repulsorEpsilon = 0.1f;

        [Space]

        public bool overrideJetstream = false;
        public Vector2 jetstreamE = new Vector2(0f, 3f);
        public float jetstreamRadius = 2f;
        public bool jetstreamSmoothEdges = true;
        public float jetstreamR = 2f;

        [Space]

        public bool overrideVortex = false;
        public float vortexOmega = 2f;
        public float vortexRadius = 1f;
        public float vortexAMax = 3f;
        public bool vortexClockwise = true;
    }
}