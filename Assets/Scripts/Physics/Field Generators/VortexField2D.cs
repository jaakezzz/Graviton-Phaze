using System.Collections.Generic;
using UnityEngine;

public class VortexField2D : MonoBehaviour
{
    public static readonly HashSet<VortexField2D> All = new();

    [Header("Swirl (velocity-dependent)")]
    public float omega = 2f;    // turn rate scale (rad/s). Bigger = tighter curve
    public float R = 1f;      // Gaussian radius of influence
    public float aMax = 3f;      // accel clamp for comfort
    public bool clockwise = true; // flip swirl direction

    public Vector2 AccelAt(Vector2 x, Vector2 v)
    {
        // 1. Vector FROM center TO the ship
        Vector2 r = x - (Vector2)transform.position;
        float distSqr = r.sqrMagnitude;

        // Gaussian mask to localize the effect
        float g = Mathf.Exp(-distSqr / (R * R));

        // 2. Calculate the tangent vector based on position, not velocity
        // (-r.y, r.x) creates a perfect Counter-Clockwise tangent around the center
        // Magnitude naturally equals the distance 'r', creating a calm "eye" at the center.
        Vector2 tangent = new Vector2(-r.y, r.x);

        // 3. Apply correct sign (-1 for Clockwise, +1 for CCW)
        float sgn = clockwise ? -1f : 1f;

        // 4. Calculate final acceleration
        Vector2 a = sgn * omega * g * tangent;

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
