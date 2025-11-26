using System.Collections.Generic;
using UnityEngine;

public class GravityWell2D : MonoBehaviour
{
    public static readonly HashSet<GravityWell2D> All = new();

    [Header("Well Params")]
    public float S = 12f;      // strength
    public float eps = 0.3f;     // soft-core (prevents blowup)
    public float aMax = 9f;   // accel clamp (comfort)

    public Vector2 AccelAt(Vector2 x)
    {
        Vector2 r = (Vector2)transform.position - x;
        float d2 = r.sqrMagnitude + eps;
        // inverse-square with soft-core
        Vector2 a = S * r / Mathf.Pow(d2, 1.5f);
        // clamp
        float m2 = a.sqrMagnitude;
        if (m2 > aMax * aMax) a = a.normalized * aMax;
        return a;
    }

    void OnEnable() { All.Add(this); FieldManager.Instance?.Register(this); }
    void OnDisable() { All.Remove(this); FieldManager.Instance?.Unregister(this); }

    void OnDrawGizmosSelected()
    {
        // blue = attractor (S>0), red = repulsor (S<0)
        Gizmos.color = (S >= 0f) ? new Color(0.2f, 0.6f, 1f, 0.7f) : new Color(1f, 0.3f, 0.3f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, 1.0f);
    }
}
