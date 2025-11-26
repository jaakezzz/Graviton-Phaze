using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ShipController : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] FieldManager fields;
    [SerializeField] Camera cam;
    [SerializeField] Canvas uiCanvas;  // to ignore touches on UI (Pause, etc.)

    [Header("Thrust")]
    [SerializeField] float thrustAccel = 10f;     // u/s^2
    [SerializeField] float sustainBoost = 0.5f;   // +50% accel while Sustained is active
    [SerializeField] float burstImpulse = 6f;     // u/s instant push on Burst
    [SerializeField] float maxSpeed = 12f;        // speed clamp
    [SerializeField] float linearDrag = 0.05f;    // gentle damp each tick

    [Header("Steer — rate from twist (Attitude)")]
    [SerializeField] bool invertSteer = true;  // flip if CW/CCW feels wrong
    [SerializeField] float deadzoneDeg = 2f;    // ignore tiny jitters
    [SerializeField] float turnRatePerDeg = 6f;    // deg/s per deg of tilt (e.g., 30° -> 180°/s)
    [SerializeField] float maxTurnRate = 720f;  // deg/s cap (2 rev/s)
    [SerializeField] float tiltSmooth = 12f;   // larger = snappier smoothing

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

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (uiCanvas) raycaster = uiCanvas.GetComponent<GraphicRaycaster>();
        evt = EventSystem.current;
    }

    void OnEnable()
    {
        // Ensure the sensor is on when Fly map enables
        if (AttitudeSensor.current != null && !AttitudeSensor.current.enabled)
            InputSystem.EnableDevice(AttitudeSensor.current);
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
        vel += HeadingDir() * burstImpulse;
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

        // Sum fields
        Vector2 a = fields ? fields.AccelAt(pos, vel, Time.time) : Vector2.zero;

        // Add thrust along heading
        if (thrusting)
        {
            float acc = sustained ? thrustAccel * (1f + sustainBoost) : thrustAccel;
            a += HeadingDir() * acc;
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
