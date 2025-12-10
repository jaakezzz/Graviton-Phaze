using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Linq;
using UnityEngine.Events;

public class ShipController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] FieldManager fields;
    [SerializeField] Camera cam;
    [SerializeField] Canvas uiCanvas;  // to ignore touches on UI (Pause, etc.)

    [Header("Thrust")]
    [SerializeField] float thrustAccel = 1f;     // u/s^2
    [SerializeField] float sustainBoost = 0.25f;   // +25% accel while Sustained is active
    [SerializeField] float burstImpulse = 2.25f;     // u/s instant push on Burst
    [SerializeField] float maxSpeed = 2.25f;        // speed clamp
    [SerializeField] float linearDrag = 0.02f;    // gentle damp each tick

    [Header("Steer — rate from twist (Attitude)")]
    [SerializeField] bool invertSteer = true;  // flip if CW/CCW feels wrong
    [SerializeField] float deadzoneDeg = 2f;    // ignore tiny jitters
    [SerializeField] float turnRatePerDeg = 6f;    // deg/s per deg of tilt (e.g., 30° -> 180°/s)
    [SerializeField] float maxTurnRate = 720f;  // deg/s cap (2 rev/s)
    [SerializeField] float tiltSmooth = 12f;   // larger = snappier smoothing

    [Header("Fuel")]
    [SerializeField] float startFuel = 5f;     // set per level (for now; will set via level config later)
    [SerializeField] float fuel = 5f;          // runtime
    [SerializeField] float burnRate = 1.5f;     // units per second while thrusting
    [SerializeField] float sustainedMult = 1.5f;// extra burn when Sustained is active
    [SerializeField] float burstCost = 4.5f;      // one-shot cost on Burst
    [SerializeField] float minFuelToThrust = 0.05f;

    [Header("Launch")]
    [SerializeField] bool lockUntilThrust = true; // start pinned until player uses fuel
    bool launched = false;
    Vector2 spawnPos;


    [System.Serializable] public class FuelEvent : UnityEvent<float, float> { } // (current,max)
    public FuelEvent onFuelChanged = new FuelEvent();
    public UnityEvent onOutOfFuel = new UnityEvent();

    public float Fuel => fuel;
    public float FuelMax => startFuel;

    // runtime
    Vector2 vel;
    bool thrusting, sustained;
    float yawDeg;                 // current heading yaw (deg, +left / -right)

    // twist sensing
    Quaternion rollBaseline;
    bool hasBaseline;
    float tiltDegCurrent;         // from sensor (relative to baseline)
    float tiltDegFiltered;        // smoothed

    GraphicRaycaster raycaster;
    EventSystem evt;

    void ResolveRefs()
    {
        // FieldManager
        if (!fields)
            fields = FieldManager.Instance
                  ?? FindAnyObjectByType<FieldManager>(FindObjectsInactive.Include);

        // Camera
        if (!cam)
            cam = Camera.main
               ?? FindAnyObjectByType<Camera>(FindObjectsInactive.Include);

        // Canvas (prefer Fly HUD via marker)
        if (!uiCanvas)
        {
            var flyMarker = FindAnyObjectByType<FlyHUDMarker>(FindObjectsInactive.Exclude);
            if (flyMarker)
                uiCanvas = flyMarker.GetComponentInParent<Canvas>(true);

            if (!uiCanvas)
            {
                // Fallback: first active Canvas with a GraphicRaycaster
                var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                uiCanvas = canvases.FirstOrDefault(c => c.isActiveAndEnabled && c.GetComponent<GraphicRaycaster>());
            }
        }

        // Support components
        if (uiCanvas && raycaster == null) raycaster = uiCanvas.GetComponent<GraphicRaycaster>();
        if (evt == null) evt = EventSystem.current ?? FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
    }

    void Awake()
    {
        if (!cam) cam = Camera.main;
        ResolveRefs();
        if (uiCanvas) raycaster = uiCanvas.GetComponent<GraphicRaycaster>();
        evt = EventSystem.current;
    }

    public void SetFuel(float amount)
    {
        startFuel = Mathf.Max(0f, amount);
        fuel = Mathf.Clamp(amount, 0f, startFuel);
        onFuelChanged.Invoke(fuel, startFuel);
    }

    void OnEnable()
    {
        ResolveRefs();

        // Ensure the sensor is on when Fly map enables
        if (AttitudeSensor.current != null && !AttitudeSensor.current.enabled)
            InputSystem.EnableDevice(AttitudeSensor.current);

        // ensure fuel initialized when ship spawns/enables
        if (fuel <= 0f || fuel > startFuel) fuel = startFuel;
        onFuelChanged.Invoke(fuel, startFuel);

        // launch gate
        spawnPos = transform.position;
        launched = !lockUntilThrust ? true : false;
    }

    // ===== Input (Fly map) =====

    // Thrust: <Touchscreen>/primaryTouch/press (no interactions)
    public void OnThrust(InputAction.CallbackContext ctx)
    {
        Vector2 screenPos = Touchscreen.current?.primaryTouch.position.ReadValue() ?? Vector2.zero;

        if (ctx.started)
        {
            if (IsOverUI(screenPos)) { thrusting = false; return; }
            thrusting = true;
        }
        else if (ctx.canceled) thrusting = false;
    }

    // Sustained: Hold interaction (MinDuration ~0.35s) on same press control
    public void OnSustained(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) sustained = true;
        if (ctx.canceled) sustained = false;
    }

    // Burst: Tap interaction (TapCount=2) on <Touchscreen>/primaryTouch/tap
    public void OnBurst(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        if (fuel < burstCost) return;                 // not enough juice
        fuel -= burstCost;
        launched = true;
        vel += HeadingDir() * burstImpulse;
        onFuelChanged.Invoke(fuel, startFuel);
    }

    // Turn: Value (Quaternion) from <AttitudeSensor>/attitude
    public void OnTurn(InputAction.CallbackContext ctx)
    {
        var q = ctx.ReadValue<Quaternion>();
        if (!hasBaseline) { rollBaseline = q; hasBaseline = true; }  // auto-calibrate once

        // Relative rotation from baseline to current
        Quaternion qRel = Quaternion.Inverse(rollBaseline) * q;

        // Device RIGHT vector in baseline frame; measure rotation in screen plane (XY)
        Vector3 rightRel = qRel * Vector3.right;
        float rollDeg = Mathf.Atan2(rightRel.y, rightRel.x) * Mathf.Rad2Deg; // +CCW, -CW

        float t = (Mathf.Abs(rollDeg) < deadzoneDeg) ? 0f : rollDeg;
        if (invertSteer) t = -t;

        tiltDegCurrent = t; // this is our "how much to turn" control input (deg)
    }

    // Calibrate button: set current orientation as zero twist
    public void Calibrate()
    {
        if (AttitudeSensor.current == null) return;
        rollBaseline = AttitudeSensor.current.attitude.ReadValue();
        hasBaseline = true;
        tiltDegCurrent = tiltDegFiltered = 0f;
    }

    // Smooth input each frame
    void Update()
    {
        tiltDegFiltered = Mathf.Lerp(
            tiltDegFiltered,
            tiltDegCurrent,
            1f - Mathf.Exp(-tiltSmooth * Time.deltaTime)
        );
    }

    // ===== Physics tick =====
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector2 pos = transform.position;

        // If we’re locked, pin position & zero velocity (but still allow rotation later)
        if (!launched)
        {
            vel = Vector2.zero;
            transform.position = spawnPos;
        }

        // Sum fields
        Vector2 a = fields ? fields.AccelAt(pos, vel, Time.time) : Vector2.zero;

        // thrust only if we have fuel
        bool canThrust = thrusting && fuel > minFuelToThrust;

        // Add thrust along heading
        if (canThrust)
        {
            launched = true;
            float acc = sustained ? thrustAccel * (1f + sustainBoost) : thrustAccel;
            a += HeadingDir() * acc;

            // consume fuel
            float burn = burnRate * (sustained ? sustainedMult : 1f) * dt;
            float before = fuel;
            fuel = Mathf.Max(0f, fuel - burn);
            if (fuel <= 0f && before > 0f) onOutOfFuel.Invoke();
            onFuelChanged.Invoke(fuel, startFuel);
        }

        // Integrate velocity
        vel += a * dt;

        // Clamp + mild linear drag for comfort
        float sp = vel.magnitude;
        if (sp > maxSpeed) vel *= (maxSpeed / sp);
        vel *= 1f / (1f + linearDrag * dt);

        pos += vel * dt;
        transform.position = pos;

        // --- Rate steering: integrate yaw from tilt ---
        float turnRate = Mathf.Clamp(tiltDegFiltered * turnRatePerDeg, -maxTurnRate, +maxTurnRate); // deg/s
        yawDeg += turnRate * dt;
        if (yawDeg > 180f) yawDeg -= 360f;
        if (yawDeg < -180f) yawDeg += 360f;

        // Face heading (purely visual)
        var dir = HeadingDir();
        if (dir.sqrMagnitude > 1e-6f) transform.up = dir;
    }

    // ===== Helpers =====
    Vector2 HeadingDir()
    {
        float rad = yawDeg * Mathf.Deg2Rad;
        // "Forward" is +Y; yaw left/right rotates around Z
        return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
    }

    bool IsOverUI(Vector2 screenPos)
    {
        if (raycaster == null || evt == null) return false;
        var data = new PointerEventData(evt) { position = screenPos };
        var results = new List<RaycastResult>();
        raycaster.Raycast(data, results);
        return results.Count > 0;
    }
}
