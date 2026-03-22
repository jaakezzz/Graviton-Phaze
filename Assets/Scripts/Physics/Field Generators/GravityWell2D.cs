using System.Collections.Generic;
using UnityEngine;

public class GravityWell2D : MonoBehaviour
{
    public static readonly HashSet<GravityWell2D> All = new();

    [Header("Well Params")]
    public float S = 12f;      // strength
    public float eps = 0.3f;     // soft-core (prevents blowup)
    public float aMax = 9f;   // accel clamp (comfort)

    [Header("Visuals")]
    [Tooltip("The child SpriteRenderer containing the outer lines.")]
    [SerializeField] SpriteRenderer linesRenderer;

    [Tooltip("Color tint when S >= 0 (Attractor).")]
    [SerializeField] Color attractColor = Color.green;

    [Tooltip("Color tint when S < 0 (Repulsor).")]
    [SerializeField] Color repulseColor = Color.red;

    [Tooltip("Gradient to evaluate alpha based on |S|. (Only the Alpha channel of this gradient is used).")]
    [SerializeField] Gradient strengthAlphaGradient;

    [Tooltip("The absolute strength value |S| that maps to the far right (1.0) of the gradient.")]
    [SerializeField] float maxExpectedStrength = 20f;

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

    void OnEnable()
    {
        All.Add(this);
        FieldManager.Instance?.Register(this);
        UpdateVisuals(); // Set on spawn
    }
    void OnDisable() { All.Remove(this); FieldManager.Instance?.Unregister(this); }

#if UNITY_EDITOR
    // Updates the visual in the editor instantly when you change S in the inspector
    void OnValidate()
    {
        UpdateVisuals();
    }
#endif

    void OnDrawGizmosSelected()
    {
        // blue = attractor (S>0), red = repulsor (S<0)
        Gizmos.color = (S >= 0f) ? new Color(0.2f, 0.6f, 1f, 0.7f) : new Color(1f, 0.3f, 0.3f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, 1.0f);
    }

    // Call this to refresh the colors
    public void UpdateVisuals()
    {
        if (linesRenderer == null) return;

        // 1. Determine base tint (1-to-1 swap based on sign)
        Color targetColor = (S >= 0f) ? attractColor : repulseColor;

        // 2. Determine Alpha from Gradient based on the absolute value of S
        if (strengthAlphaGradient != null)
        {
            float safeMax = maxExpectedStrength > 0f ? maxExpectedStrength : 20f; // Fallback
            float t = Mathf.Clamp01(Mathf.Abs(S) / safeMax);

            // Extract just the alpha from the evaluated gradient point
            float targetAlpha = strengthAlphaGradient.Evaluate(t).a;
            targetColor.a = targetAlpha;
        }

        linesRenderer.color = targetColor;
    }
}
