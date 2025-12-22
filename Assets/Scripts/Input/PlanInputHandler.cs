using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;   // New Input System

// Types of probes the player can launch; drives which anchor/field gets spawned on dock.
public enum ProbeType { Stabilizer, Repulsor, Jetstream, Vortex }

public class PlanInputHandler : MonoBehaviour
{
    // -----------------------------
    // Scene/Inspector references
    // -----------------------------
    [Header("Refs")]
    [SerializeField] Canvas uiCanvas;                 // UI Canvas used for raycast-blocking (don’t aim/fire over buttons)
    [SerializeField] Transform cannon;                // World-space transform used as the launch origin
    [SerializeField] TrajectoryPredictor predictor;   // Draws the aim/trajectory preview while dragging
    [SerializeField] Camera cam;                      // Camera for screen?world conversion (usually Camera.main)

    // -----------------------------
    // Probe selection UI
    // -----------------------------
    [Header("Probe UI")]
    [SerializeField] ProbeType probeType;             // Currently selected probe type
    [SerializeField] Image probeIcon;                 // UI icon that mirrors the selected probe
    [SerializeField] Sprite[] probeSprites;           // One sprite per ProbeType; array order must match enum

    // -----------------------------
    // Aiming & launch feel (screen-space ? world velocity)
    // -----------------------------
    [Header("Aim/Launch Tuning")]
    [SerializeField] float minDragPixels = 10f;       // Deadzone: tiny drags below this are treated as zero power
    [SerializeField] float maxDragPixels = 300f;      // Clamp: pulling farther than this gives no extra power
    [SerializeField] float maxLaunchSpeed = 12f;      // Max initial speed mapped from a full pull (world units/s)
    [SerializeField] float powerCurve = 0.85f;        // Easing: <1 snappier start, 1 linear, >1 stiffer ramp

    // -----------------------------
    // Spawning
    // -----------------------------
    [Header("Spawning")]
    [SerializeField] ProbeController probePrefab;     // Prefab for the projectile
    [SerializeField] FieldManager fieldManager;       // Passed to the probe so it can query field acceleration

    // Cached helpers for UI hit-testing
    GraphicRaycaster _raycaster;
    EventSystem _eventSystem;

    // Drag state
    bool aiming = false;                              // True while the finger is held and not over UI
    bool aimBlockedByUI = false;                      // True if the press began on UI (ignore this drag entirely)

    // Positions tracked in SCREEN space during drag
    Vector2 aimOriginScreen;                          // Where the aim starts (cannon projected to screen)
    Vector2 currentPosScreen;                         // Current pointer/touch position on screen

    // ----------------------------------------
    // Unity lifecycle: cache references
    // ----------------------------------------
    void Awake()
    {
        if (!cam) cam = Camera.main;                                      // Default to main camera
        if (uiCanvas) _raycaster = uiCanvas.GetComponent<GraphicRaycaster>(); // For IsOverUI checks
        _eventSystem = EventSystem.current;                                // Required for UI raycasts
    }

    // ----------------------------------------
    // UI hit-test: returns true if a screen position is over any raycastable UI element
    // ----------------------------------------
    bool IsOverUI(Vector2 screenPos)
    {
        if (_raycaster == null || _eventSystem == null) return false;     // No raycaster/system ? can’t be over UI
        var data = new PointerEventData(_eventSystem) { position = screenPos };
        var results = new List<RaycastResult>();
        _raycaster.Raycast(data, results);
        return results.Count > 0;                                         // Any hit ? UI is under finger
    }

    // ----------------------------------------
    // Get the current pointer position *from the device that fired the action*
    // More robust than always reading Touchscreen.current (works with mouse in editor too).
    // ----------------------------------------
    Vector2 GetScreenPos(InputAction.CallbackContext ctx)
    {
        var dev = ctx.control?.device;
        if (dev is Touchscreen ts) return ts.primaryTouch.position.ReadValue();
        if (dev is Mouse m) return m.position.ReadValue();
        return Pointer.current != null ? Pointer.current.position.ReadValue() : currentPosScreen;
    }

    // ----------------------------------------
    // Convert a SCREEN-space point to WORLD space at the cannon's depth
    // Ensures direction mapping is consistent regardless of camera setup.
    // ----------------------------------------
    Vector2 ScreenToWorld(Vector2 pScreen)
    {
        if (!cam) return pScreen; // Fallback: treat as already world coords (only in unusual setups)
        float depth = cannon ? (cannon.position.z - cam.transform.position.z) : 0f;
        return cam.ScreenToWorldPoint(new Vector3(pScreen.x, pScreen.y, depth));
    }

    // ----------------------------------------
    // Map a pull-back gesture (in SCREEN pixels) to an initial WORLD-space velocity vector.
    // Steps:
    //  1) Measure drag length in pixels ? normalize to [0..1] with deadzone & clamp ? ease with powerCurve.
    //  2) Convert a small 2D step along the drag to WORLD space to get direction.
    //  3) Multiply direction by maxLaunchSpeed * power to get v0.
    // ----------------------------------------
    Vector2 MapDragToVelocity(Vector2 originScreen, Vector2 currentScreen)
    {
        Vector2 dragScreen = originScreen - currentScreen;          // Pull-back vector in pixels (aim like a slingshot)
        float pixels = dragScreen.magnitude;

        // Power fraction [0..1]: deadzone ? clamp ? easing curve
        float t = Mathf.InverseLerp(minDragPixels, maxDragPixels, pixels);
        t = Mathf.Clamp01(t);
        t = Mathf.Pow(t, powerCurve);

        float speed = t * maxLaunchSpeed;                           // Final speed in world units/s

        if (speed <= 0.0001f) return Vector2.zero;                  // Too small ? treat as no launch

        // Direction in WORLD space:
        // Take two nearby screen points along the drag, project both to world, subtract ? get world direction.
        Vector2 s0 = originScreen;
        Vector2 s1 = originScreen + (dragScreen.normalized * 50f);  // 50 px sample step (tunable)
        Vector2 w0 = ScreenToWorld(s0);
        Vector2 w1 = ScreenToWorld(s1);
        Vector2 dirWorld = (w1 - w0).normalized;                    // World-forward direction of the launch

        return dirWorld * speed;                                    // v0 = direction * speed
    }

    // =========================
    // PLAN action map handlers
    // =========================

    // Button: <Touchscreen>/primaryTouch/press
    // Handles drag lifecycle: started ? begin aim, canceled ? release & fire, performed is ignored (held)
    public void OnAimHold(InputAction.CallbackContext ctx)
    {
        // Diagnostics to help debug bindings/interactions/devices & UI blocking in editor
        string configured = ctx.action?.interactions ?? "(none)";
        string firedBy = ctx.interaction != null ? ctx.interaction.GetType().Name : "(none)";
        string dev = ctx.control?.device?.layout ?? "(no device)";
        Vector2 dbgSp = GetScreenPos(ctx);
        Debug.Log($"[Plan] AimHold phase={ctx.phase} configured=[{configured}] firedBy={firedBy} dev={dev} overUI={IsOverUI(dbgSp)}");

        Vector2 screenPos = dbgSp;

        if (ctx.started)
        {
            // If the press began over UI, block this entire drag/aim session
            if (IsOverUI(screenPos)) { aimBlockedByUI = true; aiming = false; predictor?.Clear(); return; }

            // Begin aiming
            aimBlockedByUI = false;
            aiming = true;

            // Compute the SCREEN-space origin by projecting the cannon into screen space.
            // (If cannon is null, fall back to treating its world pos as screen coords—rare.)
            aimOriginScreen = cam && cannon
                ? (Vector2)cam.WorldToScreenPoint(cannon.position)
                : (Vector2)(cannon ? cannon.position : Vector2.zero);

            predictor?.Show();  // Start showing the preview
            return;
        }

        if (ctx.performed) return; // While holding, we let OnAimPoint drive the live preview

        if (ctx.canceled)
        {
            // If the drag started on UI, ignore the release and clear preview
            if (aimBlockedByUI) { aimBlockedByUI = false; predictor?.Clear(); return; }
            if (!aiming) return; // Defensive: ignore if we weren’t actually aiming

            // End of drag ? compute final v0 and fire
            aiming = false;

            Vector2 v0 = MapDragToVelocity(aimOriginScreen, currentPosScreen);
            Vector2 originWorld = ScreenToWorld(aimOriginScreen);
            predictor?.Clear();                                  // Hide preview
            Debug.Log($"[Plan] Fire v0={v0}");
            SpawnProbe(originWorld, v0, probeType);              // Instantiate probe and initialize it
        }
    }

    // Value: <Touchscreen>/primaryTouch/position
    // Streams pointer/touch position while held; updates preview line in real time.
    public void OnAimPoint(InputAction.CallbackContext ctx)
    {
        currentPosScreen = ctx.ReadValue<Vector2>();             // Track the live screen position
        if (!aiming || aimBlockedByUI) return;                   // Only draw while in a valid drag

        Vector2 v0 = MapDragToVelocity(aimOriginScreen, currentPosScreen); // Live initial velocity guess
        Vector2 originWorld = ScreenToWorld(aimOriginScreen);               // World launch origin

        // Editor-friendly diagnostic for mapping sanity (optional to remove later)
        Debug.Log($"[Plan] AimPoint phase={ctx.phase} screen={currentPosScreen} world={originWorld}");

        // Draw/update the trajectory preview (predictor may simulate or just draw a parametric arc)
        predictor?.Draw(originWorld, v0);
    }

    // -----------------------------
    // Probe type swapping (via action or UI button)
    // -----------------------------

    // Action-bound (e.g., keyboard/gamepad in editor, or future gesture)
    public void OnSwapProbe(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;                 // Only on completed press
        CycleProbeAndUpdateUI();
    }

    // UI Button hook (always available on mobile)
    public void OnSwapProbeUI()
    {
        CycleProbeAndUpdateUI();
    }

    // Advance enum, update icon, and log to console
    void CycleProbeAndUpdateUI()
    {
        probeType = Next(probeType);
        if (probeIcon && probeSprites != null && probeSprites.Length > (int)probeType)
            probeIcon.sprite = probeSprites[(int)probeType];
        Debug.Log($"[Plan] Probe = {probeType}");
    }

    // -----------------------------
    // Spawning helper
    // -----------------------------
    void SpawnProbe(Vector2 originWorld, Vector2 v0, ProbeType type)
    {
        var p = Instantiate(probePrefab, originWorld, Quaternion.identity); // Create the projectile
        p.Init(originWorld, v0, type, fieldManager);                        // Hand it its initial state & Fields ref
    }

    // Cycles enum in a wrap-around fashion (0?1?2?3?0…)
    static ProbeType Next(ProbeType p)
        => (ProbeType)(((int)p + 1) % System.Enum.GetValues(typeof(ProbeType)).Length);
}
