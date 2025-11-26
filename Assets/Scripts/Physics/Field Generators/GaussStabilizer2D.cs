using System.Collections.Generic;
using UnityEngine;

public class GaussStabilizer2D : MonoBehaviour
{
    public static readonly HashSet<GaussStabilizer2D> All = new();

    public float U0 = 13f;   // depth/strength
    public float R = 1f;    // radius scale
    public float aMax = 5f; // accel clamp

    public Vector2 AccelAt(Vector2 x)
    {
        Vector2 c = transform.position;
        Vector2 r = c - x;
        float r2 = r.sqrMagnitude;
        float g = Mathf.Exp(-r2 / (R * R));
        Vector2 a = (2f * U0 / (R * R)) * g * r;
        float m2 = a.sqrMagnitude;
        if (m2 > aMax * aMax) a = a.normalized * aMax;
        return a;
    }

    void OnEnable() { All.Add(this); FieldManager.Instance?.Register(this); }
    void OnDisable() { All.Remove(this); FieldManager.Instance?.Unregister(this); }
}

