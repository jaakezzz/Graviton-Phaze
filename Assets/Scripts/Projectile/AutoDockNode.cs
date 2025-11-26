using System.Collections.Generic;
using UnityEngine;

public class AutoDockNode : MonoBehaviour
{
    public static readonly HashSet<AutoDockNode> All = new();

    [Header("Docking")]
    public float snapRadius = 0.5f;

    [Header("On Dock ? Spawn Prefabs (optional)")]
    public GaussStabilizer2D stabilizerPrefab;
    public GravityWell2D repulsorPrefab;   // set S < 0 in this prefab
    public UniformPatch2D jetstreamPrefab;
    public VortexField2D vortexPrefab;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public void SpawnAnchorFor(ProbeType type)
    {
        switch (type)
        {
            case ProbeType.Stabilizer:
                if (stabilizerPrefab) Instantiate(stabilizerPrefab, transform.position, Quaternion.identity);
                break;
            case ProbeType.Repulsor:
                if (repulsorPrefab) Instantiate(repulsorPrefab, transform.position, Quaternion.identity);
                break;
            case ProbeType.Jetstream:
                if (jetstreamPrefab) Instantiate(jetstreamPrefab, transform.position, Quaternion.identity);
                break;
            case ProbeType.Vortex:
                if (vortexPrefab) Instantiate(vortexPrefab, transform.position, Quaternion.identity);
                break;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, snapRadius);
    }
}
