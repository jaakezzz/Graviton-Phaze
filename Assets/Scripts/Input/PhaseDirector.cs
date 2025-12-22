using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PhaseDirector : MonoBehaviour
{
    public enum Phase { Plan, Fly, Win, Lose }

    [Header("Core")]
    [SerializeField] PlayerInput playerInput;

    [Header("Prefabs & Spawns")]
    [SerializeField] GameObject planPrefab;        // contains PlanInputHandler + cannon
    [SerializeField] Transform planSpawn;
    [SerializeField] GameObject flyPrefab;         // contains ShipController + ship visuals
    [SerializeField] Transform flySpawn;

    [Header("HUD Roots (optional)")]
    [SerializeField] GameObject planHUD;
    [SerializeField] GameObject flyHUD;
    [SerializeField] GameObject winHUD;

    [Header("Startup")]
    [SerializeField] Phase startPhase = Phase.Plan;

    // runtime
    GameObject planGO, flyGO;
    PlanInputHandler planHandler;
    ShipController shipHandler;
    Phase current;

    void Awake()
    {
        // hard-disable all maps; we’ll explicitly enable one
        if (!playerInput) playerInput = FindAnyObjectByType<PlayerInput>();
        foreach (var m in playerInput.actions.actionMaps) m.Disable();
    }

    void Start()
    {
        if (startPhase == Phase.Fly) EnterFly(); else EnterPlan();
    }

    // ---------- Spawning / enabling ----------
    void EnsurePlan()
    {
        if (planGO == null)
        {
            planGO = Instantiate(planPrefab, planSpawn ? planSpawn.position : Vector3.zero,
                                              planSpawn ? planSpawn.rotation : Quaternion.identity);
        }

        // Resolve the PlanInputHandler from the scene (the handler lives on InputRouter)
        if (planHandler == null)
        {
            planHandler = FindAnyObjectByType<PlanInputHandler>(FindObjectsInactive.Include);
            if (planHandler == null)
                Debug.LogWarning("PhaseDirector: PlanInputHandler not found in scene. Plan inputs will be ignored.");
        }
    }

    void EnsureFly()
    {
        if (flyGO == null)
        {
            flyGO = Instantiate(flyPrefab,
                                flySpawn ? flySpawn.position : Vector3.zero,
                                flySpawn ? flySpawn.rotation : Quaternion.identity);

            shipHandler = flyGO.GetComponentInChildren<ShipController>(true);

            // Add listener for onLose events
            if (shipHandler != null)
            {
                shipHandler.onLose.RemoveListener(UI_RestartFly); // guard against dupes
                shipHandler.onLose.AddListener(UI_RestartFly);
            }
        }
        else if (shipHandler != null)
        {
            // Safety: ensure the listener exists even if EnsureFly() is called again later
            shipHandler.onLose.RemoveListener(UI_RestartFly);
            shipHandler.onLose.AddListener(UI_RestartFly);
        }
    }

    // ---------- Phase switches ----------
    public void EnterPlan()
    {
        current = Phase.Plan;
        EnsurePlan();

        // visuals / instances
        if (flyGO) flyGO.SetActive(false);
        planGO.SetActive(true);

        if (planHUD) planHUD.SetActive(true);
        if (flyHUD) flyHUD.SetActive(false);

        // input maps
        EnableOnlyMap("Plan");

        // sensors
        if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
            InputSystem.DisableDevice(AttitudeSensor.current);
    }

    public void EnterFly()
    {
        current = Phase.Fly;
        EnsureFly();

        if (shipHandler != null)    // ensure onLose event is being listened to
        {
            shipHandler.onLose.RemoveListener(UI_RestartFly);
            shipHandler.onLose.AddListener(UI_RestartFly);
        }

        // visuals / instances
        if (planGO) planGO.SetActive(false);
        flyGO.SetActive(true);

        if (planHUD) planHUD.SetActive(false);
        if (flyHUD) flyHUD.SetActive(true);

        // input maps
        EnableOnlyMap("Fly");

        // sensors
        if (AttitudeSensor.current != null && !AttitudeSensor.current.enabled)
            InputSystem.EnableDevice(AttitudeSensor.current);
    }

    public void EnterWin()
    {
        current = Phase.Win;

        // Keep ship visible but frozen in place (no physics drift).
        EnsureFly();
        if (shipHandler)
        {
            // Freeze at current position, keep fuel & heading; lock until next thrust.
            var here = (Vector2)shipHandler.transform.position;
            shipHandler.RestartAt(here, resetFuel: false, relockUntilThrust: true);
        }

        // Hide gameplay HUDs, show Win HUD
        if (planHUD) planHUD.SetActive(false);
        if (flyHUD) flyHUD.SetActive(false);
        if (winHUD) winHUD.SetActive(true);

        // Disable gameplay action maps (UI still works through EventSystem UI module)
        foreach (var m in playerInput.actions.actionMaps) m.Disable();

        // Optional: turn off attitude sensor while in Win
        if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
            InputSystem.DisableDevice(AttitudeSensor.current);
    }
    public void EnterLose() { /* later */ }

    void EnableOnlyMap(string mapName)
    {
        foreach (var m in playerInput.actions.actionMaps) m.Disable();
        playerInput.SwitchCurrentActionMap(mapName);
        playerInput.currentActionMap.Enable();
    }

    // ---------- INPUT RELAY ----------
    // Bind PlayerInput Unity Events to THESE methods (not directly to handlers).
    // This keeps events stable even when we spawn/disable prefabs.

    // PLAN map
    public void Plan_AimHold(InputAction.CallbackContext ctx) { if (current == Phase.Plan) planHandler?.OnAimHold(ctx); }
    public void Plan_AimPoint(InputAction.CallbackContext ctx) { if (current == Phase.Plan) planHandler?.OnAimPoint(ctx); }
    public void Plan_SwapProbe(InputAction.CallbackContext ctx) { if (current == Phase.Plan) planHandler?.OnSwapProbe(ctx); }

    // FLY map
    public void Fly_Thrust(InputAction.CallbackContext ctx) { if (current == Phase.Fly) shipHandler?.OnThrust(ctx); }
    public void Fly_Sustained(InputAction.CallbackContext ctx) { if (current == Phase.Fly) shipHandler?.OnSustained(ctx); }
    public void Fly_Burst(InputAction.CallbackContext ctx) { if (current == Phase.Fly) shipHandler?.OnBurst(ctx); }
    public void Fly_Turn(InputAction.CallbackContext ctx) { if (current == Phase.Fly) shipHandler?.OnTurn(ctx); }

    // ---- UI Button hook (Fly HUD) ----
    public void Fly_CalibrateButton()
    {
        if (current != Phase.Fly) return;
        shipHandler?.Calibrate();
    }

    public void UI_RestartFly()
    {
        // Make sure we have the ship
        EnsureFly();

        // Reset ship at the fly spawn point (keeps placed anchors intact)
        var pos = flySpawn ? (Vector2)flySpawn.position : Vector2.zero;
        shipHandler?.RestartAt(pos, resetFuel: true, relockUntilThrust: true);

        // Keep the current action map/hud as Fly
        EnableOnlyMap("Fly");
    }

    // ---- UI Button hook (Plan HUD) ----
    public void Plan_ClearAllDocks()
    {
        // Only allow clearing while in Plan (optional guard)
        if (/* current phase tracking */ true) // or: if (current == Phase.Plan)
        {
            AutoDockNode.ClearAll();
            // Optional: also clear any prediction line currently visible
            // FindObjectOfType<TrajectoryPredictor>()?.Clear();
        }
    }

    // ---- UI Button hook (Win HUD) ----
    // Called by the "Retry" button on the Win HUD.
    public void UI_RestartLevel()
    {
        // Clear placed anchors/docks so the player gets a clean retry
        AutoDockNode.ClearAll();

        // disable win HUD
        if (winHUD) winHUD.SetActive(false);

        UI_RestartFly();

        // Go back to planning with normal controls/HUD
        EnterPlan();
    }

    // Called by the "Menu" button on the Win HUD.
    public void UI_ReturnToMenu()
    {
        UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
    }

}
