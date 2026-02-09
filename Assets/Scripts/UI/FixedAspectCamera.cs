using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FixedAspectCamera : MonoBehaviour
{
    [Tooltip("width / height, e.g. 9f/19f")]
    public float targetAspect = 9f / 19f;

    Camera cam;
    int lastW, lastH;

    void Awake() { cam = GetComponent<Camera>(); }
    void OnEnable() { Apply(); }
    void OnDisable() { if (cam) cam.rect = new Rect(0, 0, 1, 1); } // restore

    void Update()
    {
        // Re-apply when the Game View/device size changes
        if (Screen.width != lastW || Screen.height != lastH)
            Apply();
    }

    void OnPreCull() { Apply(); } // ensures it wins over pipeline changes

    void Apply()
    {
        lastW = Screen.width; lastH = Screen.height;

        float windowAspect = (float)Screen.width / Screen.height;
        if (windowAspect > targetAspect)
        {
            // Wider than target: pillarbox
            float viewportWidth = targetAspect / windowAspect;
            float x = (1f - viewportWidth) * 0.5f;
            cam.rect = new Rect(x, 0f, viewportWidth, 1f);
        }
        else
        {
            // Taller than target: letterbox
            float viewportHeight = windowAspect / targetAspect;
            float y = (1f - viewportHeight) * 0.5f;
            cam.rect = new Rect(0f, y, 1f, viewportHeight);
        }

        cam.ResetAspect(); // make Camera.aspect match the pixel rect
    }
}
