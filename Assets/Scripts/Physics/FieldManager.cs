using System.Collections.Generic;
using UnityEngine;

public class FieldManager : MonoBehaviour
{
    public static FieldManager Instance;

    readonly List<GravityWell2D> wells = new();
    readonly List<GaussStabilizer2D> stabilizers = new();
    readonly List<UniformPatch2D> jetstreams = new();
    readonly List<VortexField2D> vortices = new();

    void Awake()
    {
        Instance = this;
        // Seed from static registries in case fields enabled before Instance existed
        foreach (var w in GravityWell2D.All) Register(w);
        foreach (var g in GaussStabilizer2D.All) Register(g);
        foreach (var u in UniformPatch2D.All) Register(u);
        foreach (var v in VortexField2D.All) Register(v);
    }

    // --- Registration helpers ---
    public void Register(GravityWell2D w) { if (w && !wells.Contains(w)) wells.Add(w); }
    public void Unregister(GravityWell2D w) { wells.Remove(w); }

    public void Register(GaussStabilizer2D g) { if (g && !stabilizers.Contains(g)) stabilizers.Add(g); }
    public void Unregister(GaussStabilizer2D g) { stabilizers.Remove(g); }

    public void Register(UniformPatch2D u) { if (u && !jetstreams.Contains(u)) jetstreams.Add(u); }
    public void Unregister(UniformPatch2D u) { jetstreams.Remove(u); }

    public void Register(VortexField2D v) { if (v && !vortices.Contains(v)) vortices.Add(v); }
    public void Unregister(VortexField2D v) { vortices.Remove(v); }

    // --- Acceleration sum ---
    public Vector2 AccelAt(Vector2 x, Vector2 v, float t)
    {
        Vector2 a = Vector2.zero;
        foreach (var w in wells) if (w && w.isActiveAndEnabled) a += w.AccelAt(x);
        foreach (var g in stabilizers) if (g && g.isActiveAndEnabled) a += g.AccelAt(x);
        foreach (var u in jetstreams) if (u && u.isActiveAndEnabled) a += u.AccelAt(x);
        foreach (var vv in vortices) if (vv && vv.isActiveAndEnabled) a += vv.AccelAt(x, v);
        return a;
    }
}
