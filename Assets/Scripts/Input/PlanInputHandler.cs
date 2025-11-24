using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;   // New Input System

public enum ProbeType { Stabilizer, Repulsor, Jetstream, Vortex }

public class PlanInputHandler : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] Canvas uiCanvas;                 // assign your UI Canvas
    [SerializeField] Transform cannon;                // cannon in WORLD space
    [SerializeField] TrajectoryPredictor predictor;   // <-- assign (Step 2)
    [SerializeField] Camera cam;                      // usually Camera.main

    [Header("Probe UI")]
    [SerializeField] ProbeType probeType;
    [SerializeField] Image probeIcon;
    [SerializeField] Sprite[] probeSprites;

    [Header("Aim/Launch Tuning")]
    [SerializeField] float minDragPixels = 10f;       // deadzone
    [SerializeField] float maxDragPixels = 300f;      // pull limit
    [SerializeField] float maxLaunchSpeed = 12f;      // world units per second
    [SerializeField] float powerCurve = 0.85f;        // 0.5=snappy, 1=linear, >1=stiff

    [Header("Spawning")]
    [SerializeField] ProbeController probePrefab;
    [SerializeField] FieldManager fieldManager;

    GraphicRaycaster _raycaster;
    EventSystem _eventSystem;

    bool aiming = false;
    bool aimBlockedByUI = false;

    Vector2 aimOriginScreen;   // SCREEN-space origin
    Vector2 currentPosScreen;  // SCREEN-space touch position

    void Awake()
    {
        if (!cam) cam = Camera.main;
        if (uiCanvas) _raycaster = uiCanvas.GetComponent<GraphicRaycaster>();
        _eventSystem = EventSystem.current;
    }

    // --- UI hit-test ---
    bool IsOverUI(Vector2 screenPos)
    {
        if (_raycaster == null || _eventSystem == null) return false;
        var data = new PointerEventData(_eventSystem) { position = screenPos };
        var results = new List<RaycastResult>();
        _raycaster.Raycast(data, results);
        return results.Count > 0;
    }

    // --- Helpers: screen/world & drag?v0 ---
    Vector2 ScreenToWorld(Vector2 pScreen)
    {
        if (!cam) return pScreen;
        // Use cannon's depth so the conversion is consistent
        float depth = cannon ? (cannon.position.z - cam.transform.position.z) : 0f;
        return cam.ScreenToWorldPoint(new Vector3(pScreen.x, pScreen.y, depth));
    }

    // Map pull-back in SCREEN pixels ? WORLD-space launch velocity (dir * speed)
    Vector2 MapDragToVelocity(Vector2 originScreen, Vector2 currentScreen)
    {
        Vector2 dragScreen = originScreen - currentScreen;          // pull-back
        float pixels = dragScreen.magnitude;

        // 1) power amount [0..1] with deadzone, clamp, easing
        float t = Mathf.InverseLerp(minDragPixels, maxDragPixels, pixels);
        t = Mathf.Clamp01(t);
        t = Mathf.Pow(t, powerCurve);

        float speed = t * maxLaunchSpeed;

        if (speed <= 0.0001f) return Vector2.zero;

        // 2) direction in WORLD space: from two nearby screen points
        //    Take a small step along drag on screen, convert both to world, subtract.
        Vector2 s0 = originScreen;
        Vector2 s1 = originScreen + (dragScreen.normalized * 50f);  // 50px step
        Vector2 w0 = ScreenToWorld(s0);
        Vector2 w1 = ScreenToWorld(s1);
        Vector2 dirWorld = (w1 - w0).normalized;  // same direction as drag

        return dirWorld * speed;
    }

    // ---- PLAN map handlers ----

    // <Touchscreen>/primaryTouch/press
    public void OnAimHold(InputAction.CallbackContext ctx)
    {
        Vector2 screenPos = Touchscreen.current?.primaryTouch.position.ReadValue() ?? currentPosScreen;

        if (ctx.started)
        {
            if (IsOverUI(screenPos)) { aimBlockedByUI = true; aiming = false; predictor?.Clear(); return; }

            aimBlockedByUI = false;
            aiming = true;

            // origin in SCREEN space
            aimOriginScreen = cam && cannon
                ? (Vector2)cam.WorldToScreenPoint(cannon.position)
                : (Vector2)(cannon ? cannon.position : Vector2.zero);

            predictor?.Show();  // start showing dots
            return;
        }

        if (ctx.performed) return; // OnAimPoint drives the preview

        if (ctx.canceled)
        {
            if (aimBlockedByUI) { aimBlockedByUI = false; predictor?.Clear(); return; }
            if (!aiming) return;

            aiming = false;

            // Final launch velocity from the last drag
            Vector2 v0 = MapDragToVelocity(aimOriginScreen, currentPosScreen);
            Vector2 originWorld = ScreenToWorld(aimOriginScreen);
            predictor?.Clear();
            Debug.Log($"[Plan] Fire v0={v0}");
            SpawnProbe(originWorld, v0, probeType);
        }
    }

    // <Touchscreen>/primaryTouch/position
    public void OnAimPoint(InputAction.CallbackContext ctx)
    {
        currentPosScreen = ctx.ReadValue<Vector2>();
        if (!aiming || aimBlockedByUI) return;

        Vector2 v0 = MapDragToVelocity(aimOriginScreen, currentPosScreen);
        Vector2 originWorld = ScreenToWorld(aimOriginScreen);

        // Draw a simple preview (currently coasts; you’ll plug physics in Step 2B)
        predictor?.Draw(originWorld, v0);
    }

    // ---- Probe swap ----
    public void OnSwapProbe(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;
        CycleProbeAndUpdateUI();
    }

    public void OnSwapProbeUI()
    {
        CycleProbeAndUpdateUI();
    }

    void CycleProbeAndUpdateUI()
    {
        probeType = Next(probeType);
        if (probeIcon && probeSprites != null && probeSprites.Length > (int)probeType)
            probeIcon.sprite = probeSprites[(int)probeType];
        Debug.Log($"[Plan] Probe = {probeType}");
    }

    void SpawnProbe(Vector2 originWorld, Vector2 v0, ProbeType type)
    {
        var p = Instantiate(probePrefab, originWorld, Quaternion.identity);
        p.Init(originWorld, v0, type, fieldManager);
    }

    static ProbeType Next(ProbeType p)
        => (ProbeType)(((int)p + 1) % System.Enum.GetValues(typeof(ProbeType)).Length);
}
