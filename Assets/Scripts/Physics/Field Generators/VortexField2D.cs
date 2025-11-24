using System.Collections.Generic;
using UnityEngine;

public class VortexField2D : MonoBehaviour
{
    public static readonly HashSet<VortexField2D> All = new();

    [Header("Swirl (velocity-dependent)")]
    public float omega = 1.6f;    // turn rate scale (rad/s). Bigger = tighter curve
    public float R = 6f;      // Gaussian radius of influence
    public float aMax = 3f;      // accel clamp for comfort
    public bool clockwise = true; // flip swirl direction

    // a = sign * omega * g(r) * (z? × v)
    public Vector2 AccelAt(Vector2 x, Vector2 v)
    {
        // Gaussian mask to localize the effect
        Vector2 r = (Vector2)transform.position - x;
        float g = Mathf.Exp(-r.sqrMagnitude / (R * R));

        // Perpendicular to velocity (CCW for (-vy, +vx))
        Vector2 perpV = new Vector2(-v.y, v.x);
        float sgn = clockwise ? +1f : -1f;

        Vector2 a = sgn * omega * g * perpV;

        // Clamp
        float m = a.magnitude;
        if (m > aMax) a *= aMax / m;
        return a;
    }

    void OnEnable() { All.Add(this); FieldManager.Instance?.Register(this); }
    void OnDisable() { All.Remove(this); FieldManager.Instance?.Unregister(this); }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.4f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, R);
    }
}
