using System.Collections.Generic;               // (optional) used earlier for UI raycast helpers
using UnityEngine;
using UnityEngine.EventSystems;                 // For checking if a touch is over UI (to avoid thrust)
using UnityEngine.InputSystem;                  // New Input System types (InputAction, AttitudeSensor, Touchscreen)
using UnityEngine.UI;                           // UI Image/Text (not directly used here but often referenced)
using System.Linq;                              // For simple LINQ finds in ResolveRefs()
using UnityEngine.Events;                       // For UnityEvent fuel callbacks

public class ShipController : MonoBehaviour
{
    // -----------------------------
    // References provided via Inspector (or auto-resolved at runtime)
    // -----------------------------
    [Header("Refs")]
    [SerializeField] FieldManager fields;       // Central physics hub: sums all active field accelerations
    [SerializeField] Camera cam;                // Main camera (not strictly required in this script)
    [SerializeField] Canvas uiCanvas;           // Fly HUD canvas, used only to ignore touches on UI

    [Header("Lose / Bounds")]
    [SerializeField] float cameraMargin = 1.5f;   // extra space beyond view before counting as OOB

    // -----------------------------
    // Thrust & motion feel
    // -----------------------------
    [Header("Thrust")]
    [SerializeField] float thrustAccel = 1f;        // Forward acceleration while thrusting (world units / s^2)
    [SerializeField] float sustainBoost = 0.25f;    // Extra acceleration fraction during Sustained (e.g., 0.25 ? +25%)
    [SerializeField] float burstImpulse = 2.25f;    // Instant velocity bump on double-tap burst (world units / s)
    [SerializeField] float maxSpeed = 2.25f;        // Speed clamp to keep handling comfortable
    [SerializeField] float linearDrag = 0.02f;      // Gentle velocity damping each physics tick (comfort/friction)

    // -----------------------------
    // Steering: phone tilt ? turn rate
    // -----------------------------
    [Header("Steer — rate from twist (Attitude)")]
    [SerializeField] bool invertSteer = true;       // If true, flips CW/CCW mapping if it feels backwards
    [SerializeField] float deadzoneDeg = 2f;        // Ignores small jitters in device tilt (degrees)
    [SerializeField] float turnRatePerDeg = 6f;     // Degrees/second of yaw per degree of phone tilt
    [SerializeField] float maxTurnRate = 720f;      // Hard cap on yaw rate (deg/s); 720 = two revs per second
    [SerializeField] float tiltSmooth = 12f;        // Smoothing factor for tilt input (higher = snappier)

    // -----------------------------
    // Fuel model
    // -----------------------------
    [Header("Fuel")]
    [SerializeField] float startFuel = 5f;          // Initial fuel; later overridden by LevelConfig
    [SerializeField] float fuel = 5f;               // Runtime fuel (current)
    [SerializeField] float burnRate = 1.5f;         // Units of fuel drained per second while thrusting
    [SerializeField] float sustainedMult = 1.5f;    // Multiplier to burnRate while Sustained is active
    [SerializeField] float burstCost = 4.5f;        // One-shot fuel subtraction when double-tap burst triggers
    [SerializeField] float minFuelToThrust = 0.05f; // Threshold: below this, thrust doesn’t engage

    // -----------------------------
    // Launch gating: pin ship at spawn until player spends fuel
    // -----------------------------
    [Header("Launch")]
    [SerializeField] bool lockUntilThrust = true;   // If true, ignore fields & pin at spawn until thrust/burst used
    bool launched = false;                          // Becomes true after first thrust or burst
    Vector2 spawnPos;                               // Position recorded on enable; used while pinned

    // -----------------------------
    // Fuel events for UI / gameplay hooks
    // -----------------------------
    [System.Serializable] public class FuelEvent : UnityEvent<float, float> { } // (current, max)
    public FuelEvent onFuelChanged = new FuelEvent(); // Invoked on any fuel change
    public UnityEvent onOutOfFuel = new UnityEvent(); // Invoked at the instant fuel reaches 0

    // -----------------------------
    // Game events
    // -----------------------------
    public UnityEvent onLose = new UnityEvent();    // wire this to PhaseDirector.UI_RestartFly in Inspector


    // Convenience accessors (read-only)
    public float Fuel => fuel;
    public float FuelMax => startFuel;

    // Reusable hit list to avoid GC
    static readonly List<RaycastResult> _uiHits = new List<RaycastResult>(16);

    // -----------------------------
    // Runtime state for motion & steering
    // -----------------------------
    Vector2 vel;                                    // Current velocity (world units / s)
    bool thrusting, sustained;                      // Input state flags (press/hold; long-press promoted)
    float yawDeg;                                   // Visual heading angle (degrees; +left / –right)

    // Gyro/attitude handling
    Quaternion rollBaseline;                        // Captured reference orientation (calibration)
    bool hasBaseline;                               // True after first read or manual calibrate
    float tiltDegCurrent;                           // Instantaneous tilt reading mapped to degrees
    float tiltDegFiltered;                          // Smoothed tilt, used to compute turn rate

    // UI raycast helpers to avoid thrust when touching buttons
    GraphicRaycaster raycaster;
    EventSystem evt;

    // ----------------------------------------
    // ResolveRefs: finds missing references at runtime (nice for spawned prefabs)
    // ----------------------------------------
    void ResolveRefs()
    {
        // Ensure we have the FieldManager
        if (!fields)
            fields = FieldManager.Instance
                  ?? FindAnyObjectByType<FieldManager>(FindObjectsInactive.Include);

        // Ensure we have a camera (MainCamera tag preferred)
        if (!cam)
            cam = Camera.main
               ?? FindAnyObjectByType<Camera>(FindObjectsInactive.Include);

        // Prefer a canvas marked via FlyHUDMarker; otherwise pick the first active Canvas with a GraphicRaycaster
        if (!uiCanvas)
        {
            var flyMarker = FindAnyObjectByType<FlyHUDMarker>(FindObjectsInactive.Exclude);
            if (flyMarker)
                uiCanvas = flyMarker.GetComponentInParent<Canvas>(true);

            if (!uiCanvas)
            {
                var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                uiCanvas = canvases.FirstOrDefault(c => c.isActiveAndEnabled && c.GetComponent<GraphicRaycaster>());
            }
        }

        // Cache supporting components for UI hit testing
        if (uiCanvas && raycaster == null) raycaster = uiCanvas.GetComponent<GraphicRaycaster>();
        if (evt == null) evt = EventSystem.current ?? FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
    }

    // ----------------------------------------
    // Unity lifecycle: Awake
    // ----------------------------------------
    void Awake()
    {
        if (!cam) cam = Camera.main;                // Fast path for Camera if set in scene
        ResolveRefs();                               // Fill any missing refs
        if (uiCanvas) raycaster = uiCanvas.GetComponent<GraphicRaycaster>();
        evt = EventSystem.current;                   // Cache EventSystem (may be null in some test scenes)
    }

    // ----------------------------------------
    // Public API: Set initial fuel (used later by LevelConfig/LevelDirector)
    // ----------------------------------------
    public void SetFuel(float amount)
    {
        startFuel = Mathf.Max(0f, amount);          // Keep max non-negative
        fuel = Mathf.Clamp(amount, 0f, startFuel);  // Clamp current within 0..max
        onFuelChanged.Invoke(fuel, startFuel);      // Notify UI
    }

    // ----------------------------------------
    // Unity lifecycle: OnEnable (ship spawned/enabled for Fly phase)
    // ----------------------------------------
    void OnEnable()
    {
        ResolveRefs();                               // Safety: make sure refs are valid

        // Ensure device attitude sensor is enabled for gyro-based steering
        if (AttitudeSensor.current != null && !AttitudeSensor.current.enabled)
            InputSystem.EnableDevice(AttitudeSensor.current);

        // Adopt menu-stored baseline if present (prevents spin)
        if (GyroCalibrationService.TryGetBaseline(out var q))
        {
            rollBaseline = q;
            hasBaseline = true;
            tiltDegCurrent = tiltDegFiltered = 0f;
        }

        // Initialize fuel on (re)enable and notify UI
        if (fuel <= 0f || fuel > startFuel) fuel = startFuel;
        onFuelChanged.Invoke(fuel, startFuel);

        // Launch lock setup: record spawn position and set launched flag based on setting
        spawnPos = transform.position;
        launched = !lockUntilThrust ? true : false;  // If lockUntilThrust is false, start launched immediately
    }

    // -----------------------------
    // Public Ship Restart: reset variables and position
    // -----------------------------
    public void RestartAt(Vector2 pos, bool resetFuel = true, bool relockUntilThrust = true)
    {
        // position & kinematics
        vel = Vector2.zero;
        transform.position = pos;

        // relock launch gate (so gravity doesn’t yank the ship before thrust)
        spawnPos = pos;
        launched = !relockUntilThrust;

        // heading & input smoothing
        yawDeg = 0f;
        tiltDegCurrent = 0f;
        tiltDegFiltered = 0f;

        // fuel
        if (resetFuel)
        {
            fuel = startFuel;
            onFuelChanged.Invoke(fuel, startFuel);
        }
    }

    // =========================
    // Input callbacks (Fly map)
    // =========================

    // Touch press/hold to thrust; ignores touches over UI
    public void OnThrust(InputAction.CallbackContext ctx)
    {
        // Read the current touch position (used only for UI blocking)
        //Vector2 screenPos = Touchscreen.current?.primaryTouch.position.ReadValue() ?? Vector2.zero;
        Vector2 screenPos = GetScreenPos(ctx);

        if (ctx.started)
        {
            if (IsOverUI(screenPos)) { thrusting = false; return; } // If tapping a button, don’t thrust

            // If the press started on any UI element, don't engage thrust.
            if (IsPointerOverUIAnywhere(screenPos)) { thrusting = false; return; }
            thrusting = true;                                       // Begin thrusting on press

        }
        else if (ctx.canceled) thrusting = false;                   // Stop thrusting on release
    }

    // Long-press promotion (~0.35s via Hold interaction) toggles the sustained mode
    public void OnSustained(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) sustained = true;                        // Enter sustained after hold threshold
        if (ctx.canceled) sustained = false;                       // Exit sustained when finger lifts
    }

    // Double-tap burst: instant impulse + fuel cost; also unlocks launch gate
    public void OnBurst(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;                                 // Ignore unless interaction completed

        // Block burst if the tap is over any UI (buttons, sliders, etc.)
        Vector2 sp = GetScreenPos(ctx);
        if (IsPointerOverUIAnywhere(sp)) return;

        if (fuel < burstCost) return;                               // Not enough fuel ? no burst

        fuel -= burstCost;                                          // Pay fuel cost
        launched = true;                                            // Unlock from pad immediately
        vel += HeadingDir() * burstImpulse;                         // Add forward impulse
        onFuelChanged.Invoke(fuel, startFuel);                      // Notify UI
    }

    // Gyro tilt ? computes a signed "roll" angle in the screen plane (XY) relative to baseline
    public void OnTurn(InputAction.CallbackContext ctx)
    {
        var q = ctx.ReadValue<Quaternion>();                        // Device attitude (world orientation of handset)
        if (!hasBaseline) { rollBaseline = q; hasBaseline = true; } // First time: capture baseline (auto-calibrate)

        // Compute rotation from baseline to current: qRel = inverse(baseline) * current
        Quaternion qRel = Quaternion.Inverse(rollBaseline) * q;

        // Take the device's RIGHT vector under relative rotation and project into XY (screen plane)
        Vector3 rightRel = qRel * Vector3.right;

        // Signed angle of that vector in XY; +CCW, -CW; this acts as our "tilt amount"
        float rollDeg = Mathf.Atan2(rightRel.y, rightRel.x) * Mathf.Rad2Deg;

        // Apply deadzone; optionally invert if steering feels opposite
        float t = (Mathf.Abs(rollDeg) < deadzoneDeg) ? 0f : rollDeg;
        if (invertSteer) t = -t;

        tiltDegCurrent = t;                                         // Save instantaneous tilt (deg)
    }

    // Manual calibration via UI: sets current phone orientation as zero tilt and clears smoothing
    public void Calibrate()
    {
        if (AttitudeSensor.current == null) return;
        rollBaseline = AttitudeSensor.current.attitude.ReadValue(); // Replace baseline with current attitude
        hasBaseline = true;
        tiltDegCurrent = tiltDegFiltered = 0f;                      // Clear integral/smoothing for a clean feel
    }

    // Out of bounds check
    bool IsOutOfBounds(Vector2 p)
    {
        if (!cam || !cam.orthographic) return false;
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector2 c = cam.transform.position;
        float mx = cameraMargin;
        return p.x < c.x - halfW - mx || p.x > c.x + halfW + mx
            || p.y < c.y - halfH - mx || p.y > c.y + halfH + mx;
    }

    // Gravity Well death — only for positive-S (attractors), ignore player repulsors (S < 0)
    void OnTriggerEnter2D(Collider2D other)
    {
        // Be robust to colliders on child objects
        var well = other.GetComponent<GravityWell2D>() ?? other.GetComponentInParent<GravityWell2D>();
        if (well == null) return;

        // small epsilon in case S is ~0 due to floats
        const float eps = 1e-5f;
        if (well.S > eps)
        {
            Lose("hit gravity well");
        }
        // else: S <= 0 ? repulsor or neutral, do nothing
    }


    // -----------------------------
    // Frame update: smooth the tilt for pleasant steering (visual input filtering)
    // -----------------------------
    void Update()
    {
        // Exponential smoothing towards the current tilt;  tiltSmooth controls response speed
        tiltDegFiltered = Mathf.Lerp(
            tiltDegFiltered,
            tiltDegCurrent,
            1f - Mathf.Exp(-tiltSmooth * Time.deltaTime)
        );
    }

    // -----------------------------
    // Physics tick: integrate forces, velocity, and position (semi-implicit Euler)
    // -----------------------------
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;             // Fixed delta time (physics step)
        Vector2 pos = transform.position;           // Current position (2D extracted from Transform)

        // If we’re locked pre-launch, pin position & zero velocity (rotation still occurs below)
        if (!launched)
        {
            vel = Vector2.zero;                     // Prevent drift while on the pad
            transform.position = spawnPos;          // Keep exactly at spawn
        }

        // Query total field acceleration from the FieldManager at our current state
        // NOTE: If you want to avoid ALL field pull before launch, gate with (launched && fields) ? ... : Vector2.zero
        Vector2 a = fields ? fields.AccelAt(pos, vel, Time.time) : Vector2.zero;

        // Thrust only engages if the player is pressing AND there’s enough fuel
        bool canThrust = thrusting && fuel > minFuelToThrust;

        // Apply thrust along the ship’s current heading; burn fuel accordingly
        if (canThrust)
        {
            launched = true;                        // First real thrust unlocks from the pad
            float acc = sustained
                ? thrustAccel * (1f + sustainBoost) // Extra acceleration during Sustained
                : thrustAccel;

            a += HeadingDir() * acc;                // Add forward acceleration

            // Fuel consumption per tick; Sustained increases drain by sustainedMult
            float burn = burnRate * (sustained ? sustainedMult : 1f) * dt;
            float before = fuel;
            fuel = Mathf.Max(0f, fuel - burn);
            if (fuel <= 0f && before > 0f) onOutOfFuel.Invoke(); // One-time event when fuel hits zero
            onFuelChanged.Invoke(fuel, startFuel);               // Notify UI of new fuel level
        }

        // --- Integrate velocity (semi-implicit Euler): v(t+dt) = v(t) + a*dt
        vel += a * dt;

        // Clamp top speed and apply mild linear drag (comfort); drag is applied as a simple decay
        float sp = vel.magnitude;
        if (sp > maxSpeed) vel *= (maxSpeed / sp);  // Speed cap keeps handling predictable
        vel *= 1f / (1f + linearDrag * dt);         // Simple linear drag integration (reduces velocity slightly)

        // --- Integrate position: x(t+dt) = x(t) + v(t+dt)*dt
        pos += vel * dt;
        transform.position = pos;

        // Lose if we left the camera bounds (+margin)
        if (IsOutOfBounds(pos))
        {
            Lose("out of bounds");
            return;
        }

        // --- Rate steering: convert filtered tilt to a yaw rate, integrate heading angle
        float turnRate = Mathf.Clamp(tiltDegFiltered * turnRatePerDeg, -maxTurnRate, +maxTurnRate); // deg/s
        yawDeg += turnRate * dt;                   // Accumulate heading
        if (yawDeg > 180f) yawDeg -= 360f;        // Keep yaw in [-180, 180] to avoid large numbers
        if (yawDeg < -180f) yawDeg += 360f;

        // Visually face "forward" (+Y) rotated by yawDeg; transform.up is the ship’s forward axis in 2D
        var dir = HeadingDir();
        if (dir.sqrMagnitude > 1e-6f) transform.up = dir;
    }

    // -----------------------------
    // Helpers
    // -----------------------------

    // Returns a unit vector pointing "forward" in world space based on current yaw (up is forward)
    Vector2 HeadingDir()
    {
        float rad = yawDeg * Mathf.Deg2Rad;         // Convert degrees ? radians
        // Forward is +Y in Unity 2D; heading rotates this around Z:
        // x = sin(yaw), y = cos(yaw) ? up rotated by yaw
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }

    // Get the actual screen position from the device that triggered the action.
    Vector2 GetScreenPos(InputAction.CallbackContext ctx)
    {
        var dev = ctx.control?.device;
        if (dev is Touchscreen ts) return ts.primaryTouch.position.ReadValue();
        if (dev is Mouse m) return m.position.ReadValue();
        return Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
    }

    // Checks if the given screen position is over any UI (buttons/panels) on the Fly HUD canvas
    bool IsOverUI(Vector2 screenPos)
    {
        if (raycaster == null || evt == null) return false;    // No raycaster/system ? nothing to block
        var data = new PointerEventData(evt) { position = screenPos };
        var results = new List<RaycastResult>();
        raycaster.Raycast(data, results);
        return results.Count > 0;                               // Any hit = we’re over UI
    }

    // Raycast ALL active GraphicRaycasters via EventSystem (works across canvases & render modes).
    bool IsPointerOverUIAnywhere(Vector2 screenPos)
    {
        var es = EventSystem.current;
        if (es == null) return false;

        var data = new PointerEventData(es) { position = screenPos };
        _uiHits.Clear();
        es.RaycastAll(data, _uiHits);
        return _uiHits.Count > 0;
    }

    void Lose(string reason)
    {
        Debug.Log($"[Ship] Lose: {reason}");
        // zero motion immediately
        vel = Vector2.zero;
        onLose.Invoke();  // PhaseDirector.UI_RestartFly()
    }

}
