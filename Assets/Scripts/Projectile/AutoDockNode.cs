using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public struct ComboSprite
{
    [Tooltip("Exact combo key, e.g. S, R, J, V, SR, SJ, SRJV")]
    public string key;
    [Tooltip("Sprite to use for this combination.")]
    public Sprite sprite;
}

public class AutoDockNode : MonoBehaviour
{
    // Global registry so PhaseDirector/UI can clear all docks quickly.
    public static readonly HashSet<AutoDockNode> All = new();

    [Header("Docking")]
    [Tooltip("Distance within which a probe snap-docks to this node.")]
    public float snapRadius = 0.5f;

    [Header("Spawn Parent (optional)")]
    [Tooltip("If set, spawned anchors will be parented under this transform.")]
    public Transform anchorParent;

    [Header("On Dock ? Spawn Prefabs (optional)")]
    [Tooltip("Prefab to spawn when a Stabilizer probe docks.")]
    public GaussStabilizer2D stabilizerPrefab;
    [Tooltip("Prefab to spawn when a Repulsor probe docks. (Set Strength S < 0 in this prefab by default.)")]
    public GravityWell2D repulsorPrefab;
    [Tooltip("Prefab to spawn when a Jetstream probe docks.")]
    public UniformPatch2D jetstreamPrefab;
    [Tooltip("Prefab to spawn when a Vortex probe docks.")]
    public VortexField2D vortexPrefab;

    // --- Jetstream arrow overlay ---
    [Header("Jetstream Arrow (optional)")]
    [Tooltip("SpriteRenderer for the arrow overlay that shows jetstream direction (child object).")]
    [SerializeField] SpriteRenderer jetArrow;

    [Tooltip("Hide the arrow if the direction magnitude is below this.")]
    [SerializeField] float arrowHideThreshold = 0.001f;

    [Tooltip("Fallback direction if no override/prefab info is available (up = 0?).")]
    [SerializeField] Vector2 fallbackArrowDir = Vector2.up;

    [Header("Jetstream Arrow Tinting")]
    [Tooltip("Color gradient based on jetstream magnitude. Left edge is 0 force, right edge is maxExpectedJetstreamMag.")]
    [SerializeField] Gradient jetArrowGradient;

    [Tooltip("The magnitude that represents the far right of the color gradient (e.g., maximum thrust).")]
    [SerializeField] float maxExpectedJetstreamMag = 10f;

    // -----------------------------
    // Acceptance toggles per type
    // -----------------------------
    [Header("Acceptance")]
    public bool acceptStabilizer = true;
    public bool acceptRepulsor = true;
    public bool acceptJetstream = true;
    public bool acceptVortex = true;

    // -----------------------------
    // Optional parameter overrides per type
    // Leave 'apply' unchecked to use prefab defaults.
    // -----------------------------
    [System.Serializable]
    public class StabilizerOverrides
    {
        public bool apply = false;
        [Tooltip("Depth U0 of the Gaussian potential.")]
        public float U0 = 5f;
        [Tooltip("Radius R (controls falloff).")]
        public float R = 1.5f;
        [Tooltip("Optional accel clamp near center (0 = no clamp).")]
        public float accelClamp = 0f;
    }

    [System.Serializable]
    public class RepulsorOverrides
    {
        public bool apply = false;
        [Tooltip("Strength S (use negative for repulsion).")]
        public float S = -6f;
        [Tooltip("Soft core epsilon to avoid singularity.")]
        public float epsilon = 0.1f;
    }

    [System.Serializable]
    public class JetstreamOverrides
    {
        public bool apply = false;
        [Tooltip("Uniform acceleration vector inside the patch.")]
        public Vector2 E = new Vector2(0f, 3f);
        [Tooltip("Patch radius (for circular region checks).")]
        public float radius = 2f;
        [Tooltip("Smoothed entry radius.")]
        public bool smoothEdges = true;
        [Tooltip("Smoothing factor.")]
        public float R = 2f;
    }

    [System.Serializable]
    public class VortexOverrides
    {
        public bool apply = false;
        [Tooltip("turn rate scale (rad/s). Bigger = tighter curve")]
        public float Omega = 2f;
        [Tooltip("Gaussian radius of influence")]
        public float radius = 1f;
        [Tooltip("accel clamp for comfort")]
        public float aMax = 3f;
        [Tooltip("direction")]
        public bool clockwise = true;
    }

    [Header("Overrides (optional)")]
    public StabilizerOverrides stabilizer = new();
    public RepulsorOverrides repulsor = new();
    public JetstreamOverrides jetstream = new();
    public VortexOverrides vortex = new();

    // --- Audio (optional) ---
    [Header("Audio (optional)")]
    [SerializeField] AudioClip sfxDockAccept;      // played only on successful dock


    // Track the currently spawned anchor so we can clear/replace it.
    [HideInInspector] public GameObject activeAnchorGO;

    // Convenience: true if a probe has already occupied this dock.
    public bool IsOccupied => activeAnchorGO != null;

    // =============================
    // VISUALS: single combo sprite
    // =============================
    [Header("Visuals (combo sprite)")]
    [Tooltip("Renderer whose sprite will be replaced based on the accepted probe combination.")]
    [SerializeField] SpriteRenderer targetRenderer;

    [Tooltip("Provide one entry for each combination you have a sprite for (S,R,J,V, SR, SJ, ..., SRJV).")]
    [SerializeField] ComboSprite[] comboTable;

    [Tooltip("Warn in console if a matching combination sprite is not found.")]
    [SerializeField] bool warnIfMissing = true;

    // --------- Occupied-state sprites (one per probe type) ---------
    [Header("Visuals (occupied sprites)")]
    [Tooltip("Sprite to show when this dock is occupied by a Stabilizer probe.")]
    [SerializeField] Sprite occupiedStabilizerSprite;
    [Tooltip("Sprite to show when this dock is occupied by a Repulsor probe.")]
    [SerializeField] Sprite occupiedRepulsorSprite;
    [Tooltip("Sprite to show when this dock is occupied by a Jetstream probe.")]
    [SerializeField] Sprite occupiedJetstreamSprite;
    [Tooltip("Sprite to show when this dock is occupied by a Vortex probe.")]
    [SerializeField] Sprite occupiedVortexSprite;

    // Fast lookup at runtime
    Dictionary<string, Sprite> _comboLookup;

    // Track which probe type last docked (to decide arrow visibility & occupied sprite)
    ProbeType? _lastDockedType = null;

    void Awake()
    {
        if (!targetRenderer) targetRenderer = GetComponent<SpriteRenderer>();
        TryResolveJetArrow();
        BuildComboLookup();
        RefreshDockVisuals();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!targetRenderer) targetRenderer = GetComponent<SpriteRenderer>();
        TryResolveJetArrow();
        BuildComboLookup();
        // Update immediately in editor when toggles change
        if (!Application.isPlaying) RefreshDockVisuals();

        // Update arrow immediately in editor when you tweak Acceptance or overrides.
        if (isActiveAndEnabled)
            UpdateJetstreamArrowVisual();
    }
#endif

    void OnEnable()
    {
        All.Add(this);
        RefreshDockVisuals();
        UpdateJetstreamArrowVisual();
    }
    void OnDisable() => All.Remove(this);

    // Returns true if this dock accepts a given probe type.
    public bool Accepts(ProbeType type) => type switch
    {
        ProbeType.Stabilizer => acceptStabilizer,
        ProbeType.Repulsor => acceptRepulsor,
        ProbeType.Jetstream => acceptJetstream,
        ProbeType.Vortex => acceptVortex,
        _ => false
    };

    /// <summary>
    /// Spawns the anchor for the given probe type if this dock accepts it and is not occupied.
    /// Applies optional per-node parameter overrides if enabled.
    /// </summary>
    public void SpawnAnchorFor(ProbeType type)
    {
        if (!Accepts(type))
        {
            Debug.Log($"AutoDockNode '{name}': Rejected probe {type} (not accepted by this dock).");
            return;
        }

        // ?? Do not replace an existing anchor; require a manual ClearAnchor() first.
        if (IsOccupied)
        {
            Debug.Log($"AutoDockNode '{name}': already occupied. Ignoring new {type}.");
            return;
        }

        switch (type)
        {
            case ProbeType.Stabilizer:
                if (stabilizerPrefab)
                {
                    var comp = Instantiate(
                        stabilizerPrefab, transform.position, Quaternion.identity,
                        anchorParent ? anchorParent : null
                    );
                    activeAnchorGO = comp.gameObject;

                    // Apply optional overrides
                    if (stabilizer.apply)
                    {
                        // These field names assume your GaussStabilizer2D exposes them publicly.
                        comp.U0 = stabilizer.U0;
                        comp.R = stabilizer.R;
                        if (HasField(comp, "aMax")) comp.aMax = stabilizer.accelClamp; // if your class names it aMax
                    }
                }
                break;

            case ProbeType.Repulsor:
                if (repulsorPrefab)
                {
                    var comp = Instantiate(
                        repulsorPrefab, transform.position, Quaternion.identity,
                        anchorParent ? anchorParent : null
                    );
                    activeAnchorGO = comp.gameObject;

                    if (repulsor.apply)
                    {
                        comp.S = repulsor.S;         // negative for repulsion
                        comp.eps = repulsor.epsilon;   // assuming 'eps' field exists
                    }
                }
                break;

            case ProbeType.Jetstream:
                if (jetstreamPrefab)
                {
                    var comp = Instantiate(
                        jetstreamPrefab, transform.position, Quaternion.identity,
                        anchorParent ? anchorParent : null
                    );
                    activeAnchorGO = comp.gameObject;

                    if (jetstream.apply)
                    {
                        comp.E = jetstream.E;       // uniform accel inside
                        comp.radius = jetstream.radius;  // circular patch radius
                        comp.smoothEdges = jetstream.smoothEdges;
                        comp.R = jetstream.R;
                    }
                }
                break;

            case ProbeType.Vortex:
                if (vortexPrefab)
                {
                    var comp = Instantiate(
                        vortexPrefab, transform.position, Quaternion.identity,
                        anchorParent ? anchorParent : null
                    );
                    activeAnchorGO = comp.gameObject;

                    if (vortex.apply)
                    {
                        comp.omega = vortex.Omega;      // turn rate scale (rad/s)
                        comp.R = vortex.radius;     // gaussian radius
                        comp.aMax = vortex.aMax;       // accel clamp
                        comp.clockwise = vortex.clockwise;  // direction
                    }
                }
                break;
        }

        // If spawn succeeded, set occupied visuals and remember type
        if (activeAnchorGO != null)
        {
            _lastDockedType = type;
            SetOccupiedVisual(type);     // switch to the correct occupied sprite + manage arrow
            SFX(sfxDockAccept);          // only plays on a successful dock
        }
        UpdateJetstreamArrowVisual();

        // NOTE: field scripts should self-register with FieldManager in OnEnable.
        // If they don't, fetch FieldManager.Instance and register here.
    }

    /// <summary>
    /// Destroys the currently spawned anchor (if any) and frees the dock for re-use.
    /// </summary>
    public void ClearAnchor()
    {
        if (!activeAnchorGO) return;

        // If your field sources don't auto-unregister in OnDisable/OnDestroy,
        // you could explicitly call FieldManager.Instance?.Unregister(...) here
        // before destroying the object.
        Destroy(activeAnchorGO);
        activeAnchorGO = null;

        // Reset occupied state
        _lastDockedType = null;

        // Anchor gone ? fall back to override/prefab/fallback
        RefreshDockVisuals();
        UpdateJetstreamArrowVisual();
    }

    /// <summary>
    /// Clears anchors on every dock node in the scene.
    /// </summary>
    public static void ClearAll()
    {
        foreach (var n in All)
            if (n) n.ClearAnchor();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, snapRadius);
    }

    // Small helper to avoid compile errors if a given field doesn't exist on your component.
    static bool HasField(object obj, string fieldName)
    {
        if (obj == null) return false;
        var t = obj.GetType();
        return t.GetField(fieldName) != null || t.GetProperty(fieldName) != null;
    }

    // ===== visuals helpers =====

    void TryResolveJetArrow()
    {
        if (jetArrow) return;

        var renderers = GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in renderers)
        {
            // Skip the main dock renderer; use the first different sprite renderer as arrow
            if (sr != null && sr != targetRenderer)
            {
                jetArrow = sr;
                break;
            }
        }
    }

    void BuildComboLookup()
    {
        if (_comboLookup == null) _comboLookup = new Dictionary<string, Sprite>();
        _comboLookup.Clear();
        if (comboTable == null) return;

        foreach (var e in comboTable)
        {
            if (string.IsNullOrWhiteSpace(e.key)) continue;

            string normalized = NormalizeComboKey(e.key);
            _comboLookup[normalized] = e.sprite; // later entries override earlier ones if duplicated
        }
    }

    string CurrentComboKey()
    {
        string key = "";

        if (acceptStabilizer) key += "S";
        if (acceptRepulsor) key += "R";
        if (acceptJetstream) key += "J";
        if (acceptVortex) key += "V";

        return key;
    }

    string NormalizeComboKey(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        bool s = raw.Contains("S");
        bool r = raw.Contains("R");
        bool j = raw.Contains("J");
        bool v = raw.Contains("V");

        string key = "";
        if (s) key += "S";
        if (r) key += "R";
        if (j) key += "J";
        if (v) key += "V";

        return key;
    }

    public void RefreshDockVisuals()
    {
        if (!targetRenderer) return;

        // If we?re occupied, show the occupied sprite for the docked type
        if (IsOccupied && _lastDockedType.HasValue)
        {
            switch (_lastDockedType.Value)
            {
                case ProbeType.Stabilizer:
                    if (occupiedStabilizerSprite) targetRenderer.sprite = occupiedStabilizerSprite;
                    break;
                case ProbeType.Repulsor:
                    if (occupiedRepulsorSprite) targetRenderer.sprite = occupiedRepulsorSprite;
                    break;
                case ProbeType.Jetstream:
                    if (occupiedJetstreamSprite) targetRenderer.sprite = occupiedJetstreamSprite;
                    break;
                case ProbeType.Vortex:
                    if (occupiedVortexSprite) targetRenderer.sprite = occupiedVortexSprite;
                    break;
            }
            return;
        }

        // Not occupied ? revert to base combo sprite
        string key = CurrentComboKey();
        if (string.IsNullOrEmpty(key))
        {
            targetRenderer.sprite = null; // or keep previous if you prefer
            return;
        }

        if (_comboLookup != null && _comboLookup.TryGetValue(key, out var sp) && sp != null)
        {
            targetRenderer.sprite = sp;
        }
        else
        {
            if (warnIfMissing)
                Debug.LogWarning($"AutoDockNode '{name}': No combo sprite assigned for key '{key}'.");
            targetRenderer.sprite = null; // ensure we don't leave a stale sprite
        }
    }

    // switch to occupied sprite and manage arrow visibility according to the docked type.
    void SetOccupiedVisual(ProbeType type)
    {
        // swap sprite
        _lastDockedType = type;
        RefreshDockVisuals();

        // arrow behavior: hide for non-Jetstream; show+rotate for Jetstream
        if (type != ProbeType.Jetstream)
        {
            if (jetArrow && jetArrow.gameObject.activeSelf)
                jetArrow.gameObject.SetActive(false);
        }
        else
        {
            UpdateJetstreamArrowVisual();
        }
    }

    // Computes the intended jetstream direction and rotates/shows the arrow.
    // Assumes arrow sprite points up (+Y) at 0?; we rotate from Vector2.up to dir.
    void UpdateJetstreamArrowVisual()
    {
        if (jetArrow == null)
            return;

        // If occupied by a NON-Jetstream probe, always hide arrow.
        if (IsOccupied && _lastDockedType.HasValue && _lastDockedType.Value != ProbeType.Jetstream)
        {
            if (jetArrow.gameObject.activeSelf) jetArrow.gameObject.SetActive(false);
            return;
        }

        // If this dock doesn't accept Jetstream, hide arrow.
        if (!acceptJetstream)
        {
            if (jetArrow.gameObject.activeSelf) jetArrow.gameObject.SetActive(false);
            return;
        }

        // Decide which direction to show:
        // Priority:
        //  1) If a UniformPatch2D anchor is already spawned here, use its E.
        //  2) Else if per-node overrides are enabled, use override E.
        //  3) Else if the jetstream prefab exists, use its prefab E.
        //  4) Else fallback.
        Vector2 dir = fallbackArrowDir;

        if (activeAnchorGO != null)
        {
            var up = activeAnchorGO.GetComponent<UniformPatch2D>();
            if (up) dir = up.E;
        }
        else if (jetstream.apply)
        {
            dir = jetstream.E;
        }
        else if (jetstreamPrefab != null)
        {
            dir = jetstreamPrefab.E;
        }

        // Show/hide based on magnitude
        float mag = dir.magnitude;
        if (mag < arrowHideThreshold)
        {
            if (jetArrow.gameObject.activeSelf) jetArrow.gameObject.SetActive(false);
            return;
        }

        if (!jetArrow.gameObject.activeSelf) jetArrow.gameObject.SetActive(true);

        // Rotate arrow so that its "up" faces dir.
        // We can safely divide by 'mag' here because we just checked it against the hide threshold.
        float ang = Vector2.SignedAngle(Vector2.up, dir / mag);
        jetArrow.transform.localRotation = Quaternion.Euler(0f, 0f, ang);

        // Apply Color Tint
        if (jetArrowGradient != null && maxExpectedJetstreamMag > 0f)
        {
            // Normalize the magnitude between 0 and 1 based on your expected maximum
            float t = Mathf.Clamp01(mag / maxExpectedJetstreamMag);
            jetArrow.color = jetArrowGradient.Evaluate(t);
        }
    }

    // Safe audio helper: uses AudioManager if present, otherwise a one-shot 2D/3D at this node.
    void SFX(AudioClip clip)
    {
        if (!clip) return;
        if (AudioManager.I != null) AudioManager.I.PlaySFX(clip);
    }
}