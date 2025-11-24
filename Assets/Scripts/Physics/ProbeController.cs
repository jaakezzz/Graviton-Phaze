using UnityEngine;

public class ProbeController : MonoBehaviour
{
    public Vector2 Vel;
    public ProbeType Type;
    public FieldManager Fields;

    [Header("Fail checks")]
    public float vEps = 0.05f;
    public float stationaryTimeout = 0.6f;
    public float spawnGrace = 0.15f;

    float stationaryTimer, age;
    Camera cam;

    public void Init(Vector2 pos, Vector2 v0, ProbeType type, FieldManager fields)
    {
        transform.position = pos;
        Vel = v0;
        Type = type;
        Fields = fields;
    }

    void Awake() { cam = Camera.main; }

    bool TryDock()
    {
        Vector2 p = transform.position;

        foreach (var node in AutoDockNode.All)
        {
            if (!node) continue;
            float r = node.snapRadius;
            if (((Vector2)node.transform.position - p).sqrMagnitude <= r * r)
            {
                // Snap & zero
                transform.position = node.transform.position;
                Vel = Vector2.zero;

                // Spawn the correct anchor for this probe type
                node.SpawnAnchorFor(Type);

                Destroy(gameObject); // probe consumed
                return true;
            }
        }
        return false;
    }


    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        age += dt;

        Vector2 pos = transform.position;
        Vector2 a = (Fields != null) ? Fields.AccelAt(pos, Vel, Time.time) : Vector2.zero;

        // Semi-implicit Euler
        Vel += a * dt;
        pos += Vel * dt;
        transform.position = pos;

        if (TryDock()) return;

        // Out-of-bounds (simple camera box with margin)
        if (IsOutOfBounds(pos)) { Destroy(gameObject); return; }

        // Stationary self-destruct
        if (age > spawnGrace)
        {
            if (Vel.sqrMagnitude <= vEps * vEps)
            {
                stationaryTimer += dt;
                if (stationaryTimer >= stationaryTimeout) { Destroy(gameObject); return; }
            }
            else stationaryTimer = 0f;
        }
    }

    bool IsOutOfBounds(Vector2 p)
    {
        if (!cam || !cam.orthographic) return false;
        float margin = 2f;
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector2 c = cam.transform.position;
        return p.x < c.x - halfW - margin || p.x > c.x + halfW + margin
            || p.y < c.y - halfH - margin || p.y > c.y + halfH + margin;
    }
}
