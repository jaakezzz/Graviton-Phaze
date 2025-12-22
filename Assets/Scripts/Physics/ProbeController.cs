using UnityEngine;

public class ProbeController : MonoBehaviour
{
    // -----------------------------
    // Runtime state (set on spawn)
    // -----------------------------

    public Vector2 Vel;          // Current velocity of the probe (world units / second)
    public ProbeType Type;       // Which probe was fired (drives which anchor we deploy on dock)
    public FieldManager Fields;  // Physics hub that returns total acceleration at a point

    // -----------------------------
    // Failure / cleanup thresholds
    // -----------------------------
    [Header("Fail checks")]
    public float vEps = 0.05f;           // Below this speed (|v| <= vEps) counts as "stationary"
    public float stationaryTimeout = 0.6f; // How long we must remain stationary before self-destruct
    public float spawnGrace = 0.15f;     // Ignore stationary checks for this many seconds after spawn

    // Internal timers/state
    float stationaryTimer, age;  // stationaryTimer counts low-speed time; age counts total lifetime
    Camera cam;                  // Cached main camera for simple out-of-bounds culling

    // -------------------------------------------------
    // Called by the spawner (PlanInputHandler.SpawnProbe)
    // Initializes position, initial velocity, type, and field ref
    // -------------------------------------------------
    public void Init(Vector2 pos, Vector2 v0, ProbeType type, FieldManager fields)
    {
        transform.position = pos; // Start position in world space
        Vel = v0;                 // Initial launch velocity
        Type = type;              // What we will deploy when docking
        Fields = fields;          // Where we query accelerations from
        // NOTE: We assume a fresh instance; if you pool, consider resetting age/stationaryTimer here.
    }

    // -------------------------------------------------
    // Cache the main camera reference once (used for bounds checks)
    // -------------------------------------------------
    void Awake() { cam = Camera.main; }

    // -------------------------------------------------
    // Attempts to "auto-dock" into any nearby receptor.
    // If we are within a node's snapRadius, we snap to its center,
    // zero our velocity, ask it to spawn the correct anchor, and destroy the probe.
    // Returns true if docking occurred (caller should early-return from FixedUpdate).
    // -------------------------------------------------
    bool TryDock()
    {
        Vector2 p = transform.position;

        // AutoDockNode.All is assumed to be a static collection of active receptors
        foreach (var node in AutoDockNode.All)
        {
            if (!node) continue;

            // skip if the dock is already filled
            if (node.IsOccupied) continue;

            // skip if this dock doesn’t accept our probe type
            if (!node.Accepts(Type)) continue;

            float r = node.snapRadius;

            // Fast circle check: distance^2 <= radius^2
            if (((Vector2)node.transform.position - p).sqrMagnitude <= r * r)
            {
                // Snap to the receptor center and stop moving
                transform.position = node.transform.position;
                Vel = Vector2.zero;

                // Ask the receptor to spawn the appropriate anchor (field source) for our probe type
                node.SpawnAnchorFor(Type);

                // Probe has served its purpose—remove it from the scene
                Destroy(gameObject);
                return true;
            }
        }
        return false; // No dock this frame
    }

    // -------------------------------------------------
    // Physics tick: integrates motion, handles docking, OOB culling, and stationary cleanup
    // -------------------------------------------------
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime; // Fixed timestep (deterministic integration cadence)
        age += dt;                      // Track lifetime (used for spawnGrace)

        // Read current state
        Vector2 pos = transform.position;

        // Query net field acceleration at our position/velocity from FieldManager
        // If Fields is null (defensive), use zero so we simply coast
        Vector2 a = (Fields != null) ? Fields.AccelAt(pos, Vel, Time.time) : Vector2.zero;

        // Semi-implicit Euler integration:
        // 1) update velocity from acceleration
        Vel += a * dt;
        // 2) update position using the new velocity (more stable than explicit Euler)
        pos += Vel * dt;
        transform.position = pos;

        // Try to dock AFTER we've advanced this frame.
        // If docking happens, we deploy an anchor and remove this probe immediately.
        if (TryDock()) return;

        // If we leave a simple camera box (with margin), kill the probe to avoid leaking objects off-screen
        if (IsOutOfBounds(pos)) { Destroy(gameObject); return; }

        // Stationary self-destruct: after a brief grace period, if we remain nearly motionless
        // for long enough (stationaryTimeout), destroy the probe (prevents dead, stuck probes).
        if (age > spawnGrace)
        {
            if (Vel.sqrMagnitude <= vEps * vEps)     // Compare squared speeds (no sqrt cost)
            {
                stationaryTimer += dt;               // Count time spent below speed threshold
                if (stationaryTimer >= stationaryTimeout) { Destroy(gameObject); return; }
            }
            else
            {
                stationaryTimer = 0f;                // Moving again ? reset the stationary timer
            }
        }
    }

    // -------------------------------------------------
    // Camera-bounds culling: returns true if position p is outside an expanded ortho camera rectangle.
    // Uses a small margin so objects near the edge are culled a bit after leaving the view.
    // If there is no orthographic camera, we conservatively return false (no culling).
    // -------------------------------------------------
    bool IsOutOfBounds(Vector2 p)
    {
        if (!cam || !cam.orthographic) return false; // No ortho camera to reference ? skip culling

        float margin = 2f;                            // Extra space beyond the screen before culling
        float halfH = cam.orthographicSize;          // Half-height in world units
        float halfW = halfH * cam.aspect;            // Half-width in world units
        Vector2 c = cam.transform.position;        // Camera center in world space

        // Outside expanded rectangle?
        return p.x < c.x - halfW - margin || p.x > c.x + halfW + margin
            || p.y < c.y - halfH - margin || p.y > c.y + halfH + margin;
    }
}
