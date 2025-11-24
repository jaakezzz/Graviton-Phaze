using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class TrajectoryPredictor : MonoBehaviour
{
    public FieldManager fields;

    [Header("Style")]
    public int maxPoints = 120;
    public float pointStep = 0.02f;   // seconds between points
    public float fadeAfter = 2.0f;    // seconds of path to show (for straight-line feel)

    [Header("Physics plug (later)")]
    public bool usePhysics = true;   // set true once you have AccelAt()
    public float gravityY = 0f;       // quick test if you want a constant accel
    public float speedFloor = 0.05f;  // stop if too slow

    LineRenderer lr;

    void Awake()
    {
        lr = GetComponent<LineRenderer>();
        lr.positionCount = 0;
    }

    public void Show()
    {
        if (lr.positionCount == 0) lr.positionCount = 1;
    }

    public void Clear()
    {
        lr.positionCount = 0;
    }

    public void Draw(Vector2 origin, Vector2 v0)
    {
        if (v0.sqrMagnitude < speedFloor * speedFloor) { Clear(); return; }

        if (lr == null) lr = GetComponent<LineRenderer>();

        Vector3 pos = new Vector3(origin.x, origin.y, 0f);
        Vector2 v = v0;

        lr.positionCount = 0;
        float t = 0f;
        for (int i = 0; i < maxPoints; i++)
        {
            // write current point
            if (lr.positionCount == i) lr.positionCount = i + 1;
            lr.SetPosition(i, pos);

            // advance
            float dt = pointStep;
            t += dt;

            Vector2 a = Vector2.zero;

            if (usePhysics && fields != null)
            {
                a = fields.AccelAt(new Vector2(pos.x, pos.y), v, t);
            }

            v += a * dt;
            pos += new Vector3(v.x, v.y, 0f) * dt;

            if (t > fadeAfter) break; // short preview
        }
    }
}
