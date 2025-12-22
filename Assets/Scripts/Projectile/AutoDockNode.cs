using System.Collections.Generic;
using UnityEngine;

public class AutoDockNode : MonoBehaviour
{
    // Global registry so PhaseDirector/UI can clear all docks quickly.
    public static readonly HashSet<AutoDockNode> All = new();

    [Header("Docking")]
    [Tooltip("Distance within which a probe snap-docks to this node.")]
    public float snapRadius = 0.5f;

    [Header("Spawn Parent (optional)")]
    [Tooltip("If set, spawned anchors will be parented under this transform.")]
    public Transform anchorParent;

    [Header("On Dock ? Spawn Prefabs (optional)")]
    [Tooltip("Prefab to spawn when a Stabilizer probe docks.")]
    public GaussStabilizer2D stabilizerPrefab;
    [Tooltip("Prefab to spawn when a Repulsor probe docks. (Set Strength S < 0 in this prefab by default.)")]
    public GravityWell2D repulsorPrefab;
    [Tooltip("Prefab to spawn when a Jetstream probe docks.")]
    public UniformPatch2D jetstreamPrefab;
    [Tooltip("Prefab to spawn when a Vortex probe docks.")]
    public VortexField2D vortexPrefab;

    // -----------------------------
    // Acceptance toggles per type
    // -----------------------------
    [Header("Acceptance")]
    public bool acceptStabilizer = true;
    public bool acceptRepulsor = true;
    public bool acceptJetstream = true;
    public bool acceptVortex = true;

    // -----------------------------
    // Optional parameter overrides per type
    // Leave 'apply' unchecked to use prefab defaults.
    // -----------------------------
    [System.Serializable]
    public class StabilizerOverrides
    {
        public bool apply = false;
        [Tooltip("Depth U0 of the Gaussian potential.")]
        public float U0 = 5f;
        [Tooltip("Radius R (controls falloff).")]
        public float R = 1.5f;
        [Tooltip("Optional accel clamp near center (0 = no clamp).")]
        public float accelClamp = 0f;
    }

    [System.Serializable]
    public class RepulsorOverrides
    {
        public bool apply = false;
        [Tooltip("Strength S (use negative for repulsion).")]
        public float S = -6f;
        [Tooltip("Soft core epsilon to avoid singularity.")]
        public float epsilon = 0.1f;
    }

    [System.Serializable]
    public class JetstreamOverrides
    {
        public bool apply = false;
        [Tooltip("Uniform acceleration vector inside the patch.")]
        public Vector2 E = new Vector2(0f, 3f);
        [Tooltip("Patch radius (for circular region checks).")]
        public float radius = 2f;
    }

    [System.Serializable]
    public class VortexOverrides
    {
        public bool apply = false;
        [Tooltip("turn rate scale (rad/s). Bigger = tighter curve")]
        public float Omega = 2f;
        [Tooltip("Gaussian radius of influence")]
        public float radius = 1f;
        [Tooltip("accel clamp for comfort")]
        public float aMax = 3f;
        [Tooltip("direction")]
        public bool clockwise = true;
    }

    [Header("Overrides (optional)")]
    public StabilizerOverrides stabilizer = new();
    public RepulsorOverrides repulsor = new();
    public JetstreamOverrides jetstream = new();
    public VortexOverrides vortex = new();

    // Track the currently spawned anchor so we can clear/replace it.
    [HideInInspector] public GameObject activeAnchorGO;

    // Convenience: true if a probe has already occupied this dock.
    public bool IsOccupied => activeAnchorGO != null;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // Returns true if this dock accepts a given probe type.
    public bool Accepts(ProbeType type) => type switch
    {
        ProbeType.Stabilizer => acceptStabilizer,
        ProbeType.Repulsor => acceptRepulsor,
        ProbeType.Jetstream => acceptJetstream,
        ProbeType.Vortex => acceptVortex,
        _ => false
    };

    /// <summary>
    /// Spawns the anchor for the given probe type if this dock accepts it and is not occupied.
    /// Applies optional per-node parameter overrides if enabled.
    /// </summary>
    public void SpawnAnchorFor(ProbeType type)
    {
        if (!Accepts(type))
        {
            Debug.Log($"AutoDockNode '{name}': Rejected probe {type} (not accepted by this dock).");
            return;
        }

        // ?? Do not replace an existing anchor; require a manual ClearAnchor() first.
        if (IsOccupied)
        {
            Debug.Log($"AutoDockNode '{name}': already occupied. Ignoring new {type}.");
            return;
        }

        switch (type)
        {
            case ProbeType.Stabilizer:
                if (stabilizerPrefab)
                {
                    var comp = Instantiate(
                        stabilizerPrefab, transform.position, Quaternion.identity,
                        anchorParent ? anchorParent : null
                    );
                    activeAnchorGO = comp.gameObject;

                    // Apply optional overrides
                    if (stabilizer.apply)
                    {
                        // These field names assume your GaussStabilizer2D exposes them publicly.
                        comp.U0 = stabilizer.U0;
                        comp.R = stabilizer.R;
                        if (HasField(comp, "aMax")) comp.aMax = stabilizer.accelClamp; // if your class names it aMax
                    }
                }
                break;

            case ProbeType.Repulsor:
                if (repulsorPrefab)
                {
                    var comp = Instantiate(
                        repulsorPrefab, transform.position, Quaternion.identity,
                        anchorParent ? anchorParent : null
                    );
                    activeAnchorGO = comp.gameObject;

                    if (repulsor.apply)
                    {
                        comp.S = repulsor.S;         // negative for repulsion
                        comp.eps = repulsor.epsilon;   // assuming 'eps' field exists
                    }
                }
                break;

            case ProbeType.Jetstream:
                if (jetstreamPrefab)
                {
                    var comp = Instantiate(
                        jetstreamPrefab, transform.position, Quaternion.identity,
                        anchorParent ? anchorParent : null
                    );
                    activeAnchorGO = comp.gameObject;

                    if (jetstream.apply)
                    {
                        comp.E = jetstream.E;       // uniform accel inside
                        comp.radius = jetstream.radius;  // circular patch radius
                    }
                }
                break;

            case ProbeType.Vortex:
                if (vortexPrefab)
                {
                    var comp = Instantiate(
                        vortexPrefab, transform.position, Quaternion.identity,
                        anchorParent ? anchorParent : null
                    );
                    activeAnchorGO = comp.gameObject;

                    if (vortex.apply)
                    {
                        comp.omega = vortex.Omega;      // turn rate scale (rad/s)
                        comp.R = vortex.radius;     // gaussian radius
                        comp.aMax = vortex.aMax;       // accel clamp
                        comp.clockwise = vortex.clockwise;  // direction
                    }
                }
                break;
        }

        // NOTE: field scripts should self-register with FieldManager in OnEnable.
        // If they don't, fetch FieldManager.Instance and register here.
    }

    /// <summary>
    /// Destroys the currently spawned anchor (if any) and frees the dock for re-use.
    /// </summary>
    public void ClearAnchor()
    {
        if (!activeAnchorGO) return;

        // If your field sources don't auto-unregister in OnDisable/OnDestroy,
        // you could explicitly call FieldManager.Instance?.Unregister(...) here
        // before destroying the object.
        Destroy(activeAnchorGO);
        activeAnchorGO = null;
    }

    /// <summary>
    /// Clears anchors on every dock node in the scene.
    /// </summary>
    public static void ClearAll()
    {
        foreach (var n in All)
            if (n) n.ClearAnchor();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, snapRadius);
    }

    // Small helper to avoid compile errors if a given field doesn't exist on your component.
    static bool HasField(object obj, string fieldName)
    {
        if (obj == null) return false;
        var t = obj.GetType();
        return t.GetField(fieldName) != null || t.GetProperty(fieldName) != null;
    }
}
