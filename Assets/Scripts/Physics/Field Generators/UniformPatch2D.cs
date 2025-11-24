using System.Collections.Generic;
using UnityEngine;

public class UniformPatch2D : MonoBehaviour
{
    public static readonly HashSet<UniformPatch2D> All = new();

    [Header("Uniform acceleration")]
    public Vector2 E = new Vector2(0f, 2.0f);   // up-flow by default (u/s^2)

    [Header("Region")]
    public float radius = 6f;                   // circular patch
    public bool smoothEdges = true;             // Gaussian falloff if true
    public float R = 6f;                        // falloff scale when smooth

    public Vector2 AccelAt(Vector2 x)
    {
        Vector2 c = transform.position;
        Vector2 r = c - x;
        float r2 = r.sqrMagnitude;

        if (smoothEdges)
        {
            float g = Mathf.Exp(-r2 / (R * R));     // soft “wind tunnel”
            return E * g;
        }
        else
        {
            return (r2 <= radius * radius) ? E : Vector2.zero;
        }
    }

    void OnEnable() { All.Add(this); FieldManager.Instance?.Register(this); }
    void OnDisable() { All.Remove(this); FieldManager.Instance?.Unregister(this); }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.75f);
        Gizmos.DrawWireSphere(transform.position, smoothEdges ? R : radius);
    }
}
