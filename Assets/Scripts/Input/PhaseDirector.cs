using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// Central scene/phase coordinator:
/// - Spawns & toggles Plan/Fly prefabs and HUDs
/// - Switches input action maps cleanly
/// - Hooks Ship lose/retry flow
/// - Hooks scoring (LevelConfig + LevelActions) and renders Win HUD
/// </summary>
public class PhaseDirector : MonoBehaviour
{
    public enum Phase { Plan, Fly, Win, Lose }

    // =========================
    // Inspector wiring
    // =========================

    [Header("Core")]
    [SerializeField] PlayerInput playerInput;             // PlayerInput that owns Plan/Fly action maps

    [Header("Prefabs & Spawns")]
    [SerializeField] GameObject planPrefab;               // Prefab with PlanInputHandler + cannon
    [SerializeField] Transform planSpawn;                 // Where Plan prefab spawns
    [SerializeField] GameObject flyPrefab;                // Prefab with ShipController + visuals
    [SerializeField] Transform flySpawn;                  // Where Fly (ship) spawns

    [Header("HUD Roots (optional)")]
    [SerializeField] GameObject planHUD;                  // Plan Canvas root
    [SerializeField] GameObject flyHUD;                   // Fly Canvas root
    [SerializeField] GameObject winHUD;                   // Win Canvas root (panel shown on win)

    [Header("Scoring / Config")]
    [SerializeField] LevelConfig levelConfig;             // ScriptableObject with "par" (min actions)
    [SerializeField] WinHUDController winHUDscript;       // Optional: controller script on winHUD

    [Header("Startup")]
    [SerializeField] Phase startPhase = Phase.Plan;       // Which phase to start in

    [Tooltip("How long the win sequence lasts before the Win HUD appears.")]
    [SerializeField] float winSequenceDuration = 1.2f;    // Length of the Win sequence (delay before showing Win HUD)

    [Tooltip("How many degrees the ship rotates during the win sequence.")]
    [SerializeField] float winSequenceSpin = 360f;

    // ---------- Audio (optional) ----------
    [Header("Audio (optional)")]
    [Tooltip("Play this when entering Plan.")]
    [SerializeField] AudioClip sfxEnterPlan;
    [Tooltip("Play this when entering Fly.")]
    [SerializeField] AudioClip sfxEnterFly;
    [Tooltip("Play this immediately when the player reaches the goal, before the Win HUD appears.")]
    [SerializeField] AudioClip sfxWinLevel;
    [Tooltip("Play this when entering Win.")]
    [SerializeField] AudioClip sfxEnterWin;
    [Tooltip("Play this when a flight is restarted (lose or retry).")]
    [SerializeField] AudioClip sfxRestartFly;
    [Tooltip("Play this when the whole level is restarted from Win HUD.")]
    [SerializeField] AudioClip sfxRestartLevel;
    [Tooltip("Play when clearing all docks in Plan.")]
    [SerializeField] AudioClip sfxClearDocks;
    [Tooltip("Play when tapping Calibrate on the Fly HUD.")]
    [SerializeField] AudioClip sfxCalibrate;
    [Tooltip("Play once the first time the ship actually launches in a flight.")]
    [SerializeField] AudioClip sfxFirstLaunch;
    [Tooltip("Play when returning to the main menu.")]
    [SerializeField] AudioClip sfxReturnToMenu;

    [Space(6)]
    [Tooltip("Music clip to switch to while in Plan.")]
    [SerializeField] AudioClip musicPlan;
    [Tooltip("Music clip to switch to while in Fly.")]
    [SerializeField] AudioClip musicFly;
    [Tooltip("Music clip to switch to while in Win.")]
    [SerializeField] AudioClip musicWin;
    [Tooltip("If true, the director will switch music on phase changes when a clip is assigned.")]
    [SerializeField] bool changeMusicOnPhase = true;

    // =========================
    // Runtime state
    // =========================
    GameObject planGO, flyGO;         // Spawned instances
    PlanInputHandler planHandler;     // Found on Plan instance (or scene InputRouter)
    ShipController shipHandler;       // Found on Fly instance
    Phase current;                    // Current phase

    // Shortcut to global action counter
    LevelActions actions => LevelActions.Instance;

    // =========================
    // Lifecycle
    // =========================

    void Awake()
    {
        // Ensure we have a PlayerInput and hard-disable ALL maps up front.
        if (!playerInput) playerInput = FindAnyObjectByType<PlayerInput>();
        if (playerInput && playerInput.actions != null)
        {
            foreach (var m in playerInput.actions.actionMaps) m.Disable();
        }
    }

    void Start()
    {
        // Provide config to LevelActions and reset counters for this level.
        if (actions) { actions.SetConfig(levelConfig); actions.ResetForLevel(); }

        // Enter the requested start phase.
        if (startPhase == Phase.Fly) EnterFly(); else EnterPlan();
    }

    // =========================
    // Spawning helpers
    // =========================

    /// <summary>
    /// Ensure we have a live Plan instance and a reference to its handler.
    /// </summary>
    void EnsurePlan()
    {
        if (planGO == null)
        {
            planGO = Instantiate(
                planPrefab,
                planSpawn ? planSpawn.position : Vector3.zero,
                planSpawn ? planSpawn.rotation : Quaternion.identity
            );
        }

        // The PlanInputHandler may live on a non-prefab scene object (InputRouter); resolve robustly:
        if (planHandler == null)
        {
            planHandler = FindAnyObjectByType<PlanInputHandler>(FindObjectsInactive.Include);
            if (planHandler == null)
                Debug.LogWarning("PhaseDirector: PlanInputHandler not found. Plan inputs will be ignored.");
        }
    }

    /// <summary>
    /// Ensure we have a live Fly instance and its ShipController, and wire lose/launch events.
    /// </summary>
    void EnsureFly()
    {
        if (flyGO == null)
        {
            flyGO = Instantiate(
                flyPrefab,
                flySpawn ? flySpawn.position : Vector3.zero,
                flySpawn ? flySpawn.rotation : Quaternion.identity
            );

            shipHandler = flyGO.GetComponentInChildren<ShipController>(true);

            // Wire lose ? restart flight
            if (shipHandler != null)
            {
                shipHandler.onLose.RemoveListener(UI_RestartFly);
                shipHandler.onLose.AddListener(UI_RestartFly);

                // Count "first launch" once per flight (if ShipController exposes onFirstLaunch)
                shipHandler.onFirstLaunch?.RemoveListener(OnFirstLaunch);
                shipHandler.onFirstLaunch?.AddListener(OnFirstLaunch);
            }
        }
        else
        {
            // Safety: (re)wire listeners if EnsureFly is called again
            if (shipHandler != null)
            {
                shipHandler.onLose.RemoveListener(UI_RestartFly);
                shipHandler.onLose.AddListener(UI_RestartFly);

                shipHandler.onFirstLaunch?.RemoveListener(OnFirstLaunch);
                shipHandler.onFirstLaunch?.AddListener(OnFirstLaunch);
            }
        }
    }

    // =========================
    // Phase switches
    // =========================

    /// <summary>
    /// Enter planning: place probes, show Plan HUD, switch to Plan map, idle sensors.
    /// </summary>
    public void EnterPlan()
    {
        current = Phase.Plan;
        EnsurePlan();

        // Toggle instances
        if (flyGO) flyGO.SetActive(false);
        if (planGO) planGO.SetActive(true);

        // HUDs
        if (planHUD) planHUD.SetActive(true);
        if (flyHUD) flyHUD.SetActive(false);
        if (winHUD) winHUD.SetActive(false);

        // Inputs
        EnableOnlyMap("Plan");

        // Sensors: no need for attitude during plan
        if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
            InputSystem.DisableDevice(AttitudeSensor.current);

        // --- Audio hooks ---
        SFX(sfxEnterPlan);
        if (changeMusicOnPhase) Music(musicPlan);
    }

    /// <summary>
    /// Enter flight: spawn/enable ship, show Fly HUD, switch to Fly map, enable sensors.
    /// </summary>
    public void EnterFly()
    {
        Debug.Log("[PD] EnterFly()");
        current = Phase.Fly;
        EnsureFly();

        // Make sure lose/launch listeners are wired
        if (shipHandler != null)
        {
            shipHandler.onLose.RemoveListener(UI_RestartFly);
            shipHandler.onLose.AddListener(UI_RestartFly);

            shipHandler.onFirstLaunch?.RemoveListener(OnFirstLaunch);
            shipHandler.onFirstLaunch?.AddListener(OnFirstLaunch);
        }

        // Toggle instances
        if (planGO) planGO.SetActive(false);
        if (flyGO) flyGO.SetActive(true);

        // HUDs
        if (planHUD) planHUD.SetActive(false);
        if (flyHUD) flyHUD.SetActive(true);
        if (winHUD) winHUD.SetActive(false);

        // Inputs
        EnableOnlyMap("Fly");

        // Sensors
        if (AttitudeSensor.current != null && !AttitudeSensor.current.enabled)
            InputSystem.EnableDevice(AttitudeSensor.current);

        // New flight attempt ? allow counting the next "first launch"
        actions?.RestartFlightGate();

        // --- Audio hooks ---
        SFX(sfxEnterFly);
        if (changeMusicOnPhase) Music(musicFly);
    }

    /// <summary>
    /// Enter win: begin the short win sequence, then compute stars, show Win HUD, disable gameplay maps.
    /// </summary>
    public void EnterWin()
    {
        // Fallback if no specific goal transform is supplied
        EnterWin(null);
    }

    /// <summary>
    /// Enter win using a specific goal transform as the animation target.
    /// </summary>
    public void EnterWin(Transform goalTarget)
    {
        // Guard against duplicate win calls
        if (current == Phase.Win) return;

        current = Phase.Win;
        StartCoroutine(EnterWinSequence(goalTarget));
    }

    IEnumerator EnterWinSequence(Transform goalTarget)
    {
        // Make sure we have the fly instance
        EnsureFly();

        // Hide gameplay HUDs immediately; Win HUD comes later after the sequence
        if (planHUD) planHUD.SetActive(false);
        if (flyHUD) flyHUD.SetActive(false);
        if (winHUD) winHUD.SetActive(false);

        // Disable gameplay action maps immediately (UI still works later through EventSystem)
        if (playerInput && playerInput.actions != null)
        {
            foreach (var m in playerInput.actions.actionMaps) m.Disable();
        }

        // Disable attitude sensor during the win sequence
        if (AttitudeSensor.current != null && AttitudeSensor.current.enabled)
            InputSystem.DisableDevice(AttitudeSensor.current);

        // Play the immediate "level won" stinger
        SFX(sfxWinLevel);

        // Animate the ship toward the goal, rotate it, and shrink it away
        if (shipHandler != null)
        {
            // Stop all gameplay behavior so the director can animate safely
            shipHandler.PrepareForWinSequence();

            Transform shipT = shipHandler.transform;

            Vector3 startPos = shipT.position;
            Vector3 endPos = goalTarget ? goalTarget.position : shipT.position;

            Vector3 startScale = shipT.localScale;
            Vector3 endScale = Vector3.zero;

            float startAngle = shipT.eulerAngles.z;
            float endAngle = startAngle + winSequenceSpin;

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, winSequenceDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Smooth, slightly nicer-looking motion than plain linear interpolation
                float eased = Mathf.SmoothStep(0f, 1f, t);

                shipT.position = Vector3.Lerp(startPos, endPos, eased);

                Vector2 toGoal = (Vector2)endPos - (Vector2)shipT.position;
                if (toGoal.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.FromToRotation(Vector3.up, toGoal.normalized);
                    shipT.rotation = Quaternion.Slerp(shipT.rotation, targetRot, 10f * Time.deltaTime);
                }

                shipT.localScale = Vector3.Lerp(startScale, endScale, eased);

                yield return null;
            }

            // Clean up the ship so restarting the level/fly phase respawns it fresh
            if (flyGO)
                Destroy(flyGO);

            flyGO = null;
            shipHandler = null;
        }
        else
        {
            // If no ship exists for some reason, still respect the delay
            yield return new WaitForSeconds(winSequenceDuration);
        }

        FinishEnterWin();
    }

    void FinishEnterWin()
    {
        // Show Win HUD now that the sequence is complete
        if (winHUD) winHUD.SetActive(true);

        // Render stars/stats
        if (actions && winHUDscript)
        {
            var (total, probes, launches, par) = actions.Snapshot();
            var stars = actions.ComputeStars();
            winHUDscript.Render(stars, total, par, probes, launches);
        }

        // Play the existing Win HUD entry sound only AFTER the delay/animation
        SFX(sfxEnterWin);

        // Switch to win music now that the panel is appearing
        if (changeMusicOnPhase) Music(musicWin);
    }

    /// <summary>
    /// Enter lose (currently we just restart flight). Kept for future expansion.
    /// </summary>
    public void EnterLose()
    {
        // --- Audio hook ---
        SFX(sfxRestartFly);
        UI_RestartFly();
    }

    // =========================
    // Input map switching
    // =========================

    /// <summary>
    /// Disables every action map, then enables exactly one by name.
    /// </summary>
    void EnableOnlyMap(string mapName)
    {
        if (!playerInput || playerInput.actions == null) return;

        foreach (var m in playerInput.actions.actionMaps) m.Disable();
        playerInput.SwitchCurrentActionMap(mapName);
        playerInput.currentActionMap.Enable();
    }

    // =========================
    // Input relay (hook PlayerInput UnityEvents to these)
    // =========================

    // PLAN map
    public void Plan_AimHold(InputAction.CallbackContext ctx) { if (current == Phase.Plan) planHandler?.OnAimHold(ctx); }
    public void Plan_AimPoint(InputAction.CallbackContext ctx) { if (current == Phase.Plan) planHandler?.OnAimPoint(ctx); }
    public void Plan_SwapProbe(InputAction.CallbackContext ctx) { if (current == Phase.Plan) planHandler?.OnSwapProbe(ctx); }

    // FLY map
    public void Fly_Thrust(InputAction.CallbackContext ctx) { if (current == Phase.Fly) shipHandler?.OnThrust(ctx); }
    public void Fly_Sustained(InputAction.CallbackContext ctx) { if (current == Phase.Fly) shipHandler?.OnSustained(ctx); }
    public void Fly_Burst(InputAction.CallbackContext ctx) { if (current == Phase.Fly) shipHandler?.OnBurst(ctx); }
    public void Fly_Turn(InputAction.CallbackContext ctx) { if (current == Phase.Fly) shipHandler?.OnTurn(ctx); }

    // =========================
    // UI hooks
    // =========================

    /// <summary>
    /// Fly HUD "Calibrate" button.
    /// </summary>
    public void Fly_CalibrateButton()
    {
        if (current != Phase.Fly) return;
        shipHandler?.Calibrate();
        // --- Audio hook ---
        SFX(sfxCalibrate);
    }

    /// <summary>
    /// Fly HUD "Restart" button OR ship lose ? restart just the flight (keep placed anchors).
    /// </summary>
    public void UI_RestartFly()
    {
        Debug.Log("[PD] UI_RestartFly()");

        // --- Audio hook ---
        SFX(sfxRestartFly);

        EnsureFly();

        // Reset ship at the fly spawn point; keep anchors; reset fuel & relock
        var pos = flySpawn ? (Vector2)flySpawn.position : Vector2.zero;
        shipHandler?.RestartAt(pos, resetFuel: true, relockUntilThrust: true);
        shipHandler?.SendMessage("ClearInputState", SendMessageOptions.DontRequireReceiver);

        // Allow counting the next "first launch" for the new attempt
        actions?.RestartFlightGate();

        // Stay in Fly phase and show Fly HUD
        EnableOnlyMap("Fly");
        if (planHUD) planHUD.SetActive(false);
        if (flyHUD) flyHUD.SetActive(true);
        if (winHUD) winHUD.SetActive(false);
    }

    /// <summary>
    /// Plan HUD "Clear Docks" button: remove all spawned anchors so player can re-plan.
    /// </summary>
    public void Plan_ClearAllDocks()
    {
        // Optional: restrict to Plan only
        // if (current != Phase.Plan) return;

        AutoDockNode.ClearAll();

        // Optionally clear any visible prediction line:
        // FindObjectOfType<TrajectoryPredictor>()?.Clear();

        // --- Audio hook ---
        SFX(sfxClearDocks);
    }

    /// <summary>
    /// Win HUD "Retry" button: reset level scoring & anchors, then return to Plan.
    /// </summary>
    public void UI_RestartLevel()
    {
        // Clear placed anchors/docks
        AutoDockNode.ClearAll();

        // Reset scoring for a clean new run
        actions?.ResetForLevel();

        // Hide Win HUD
        if (winHUD) winHUD.SetActive(false);

        // --- Audio hook ---
        SFX(sfxRestartLevel);

        // Reset ship
        UI_RestartFly();

        // Return to Plan phase
        EnterPlan();
    }

    /// <summary>
    /// Win HUD "Menu" button.
    /// </summary>
    public void UI_ReturnToMenu()
    {
        SFX(sfxReturnToMenu);
        SceneManager.LoadScene("MainMenu");
    }

    // =========================
    // Scoring helpers
    // =========================

    /// <summary>
    /// Called by ShipController the first time the player actually launches (per flight).
    /// </summary>
    void OnFirstLaunch()
    {
        actions?.CountFirstLaunchThisFlight();
        // --- Audio hook ---
        SFX(sfxFirstLaunch);
    }

    // =========================
    // (Optional) Sensor warm-up if you later add menu-driven auto-calibration
    // =========================

    IEnumerator CalibrateAfterSensorWarmup()
    {
        yield return null; // 1 frame
        yield return null; // 2 frames (usually enough on device)
        shipHandler?.Calibrate();
    }

    // =========================
    // Audio helpers (safe no-ops if AudioManager isn't present or clips are null)
    // =========================
    void SFX(AudioClip clip)
    {
        if (clip == null) return;
        Debug.Log($"[PD] SFX: {clip.name}");
        AudioManager.I?.PlaySFX(clip);
    }

    void Music(AudioClip clip)
    {
        if (clip == null) return;
        AudioManager.I?.PlayMusic(clip);
    }
}
