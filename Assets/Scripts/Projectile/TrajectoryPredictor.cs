using UnityEngine;

[RequireComponent(typeof(LineRenderer))]  // Ensures a LineRenderer is present on this GameObject
public class TrajectoryPredictor : MonoBehaviour
{
    public FieldManager fields;           // Physics hub used to query total acceleration at a point

    [Header("Style")]
    public int maxPoints = 120;           // Max number of points the preview line will draw
    public float pointStep = 0.02f;       // Simulation timestep per point (seconds)
    public float fadeAfter = 2.0f;        // Stop drawing after this simulated time (keeps preview short)

    [Header("Physics plug")]
    public bool usePhysics = true;        // If true, sample FieldManager; if false, draw straight inertial path
    public float speedFloor = 0.05f;      // Don’t draw when initial speed is below this (prevents jitter)

    LineRenderer lr;                      // Cached LineRenderer reference

    void Awake()
    {
        lr = GetComponent<LineRenderer>(); // Cache the LR
        lr.positionCount = 0;              // Start hidden (no points)
    }

    // Make sure there is at least one point allocated so the line shows up
    public void Show()
    {
        if (lr.positionCount == 0) lr.positionCount = 1;
    }

    // Hide/clear the preview line
    public void Clear()
    {
        lr.positionCount = 0;
    }

    // Draw a predicted trajectory starting at 'origin' with initial velocity 'v0'
    // Uses the same semi-implicit Euler style as gameplay for consistency.
    public void Draw(Vector2 origin, Vector2 v0)
    {
        // Skip drawing if the initial speed is too small (avoid dot-soup at the muzzle)
        if (v0.sqrMagnitude < speedFloor * speedFloor) { Clear(); return; }

        if (lr == null) lr = GetComponent<LineRenderer>(); // Defensive cache

        // Initialize simulation state
        Vector3 pos = new Vector3(origin.x, origin.y, 0f); // Current simulated position (3D for LR)
        Vector2 v = v0;                                    // Current simulated velocity (2D)

        lr.positionCount = 0;                              // Reset the line to start fresh
        float t = 0f;                                      // Simulated time accumulator

        for (int i = 0; i < maxPoints; i++)
        {
            // 1) Emit current point to the line
            if (lr.positionCount == i) lr.positionCount = i + 1; // Grow the line by one slot if needed
            lr.SetPosition(i, pos);                               // Write current position

            // 2) Advance simulated time and compute step size
            float dt = pointStep;
            t += dt;

            // 3) Sample acceleration from fields (or zero for inertial straight-line mode)
            Vector2 a = Vector2.zero;
            if (usePhysics && fields != null)
            {
                // Note: we pass (pos.x, pos.y) and current v. 't' is local sim time (not absolute).
                a = fields.AccelAt(new Vector2(pos.x, pos.y), v, t);
            }

            // 4) Semi-implicit Euler integration (same pattern as runtime):
            //    v = v + a*dt;  x = x + v*dt
            v += a * dt;
            pos += new Vector3(v.x, v.y, 0f) * dt;

            // 5) Early exit after 'fadeAfter' seconds of simulated time to keep preview short
            if (t > fadeAfter) break;
        }
    }
}
