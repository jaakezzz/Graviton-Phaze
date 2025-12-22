using UnityEngine;

public static class GyroCalibrationService
{
    static bool hasBaseline;
    static Quaternion baseline;

    public static bool HasBaseline => hasBaseline;

    public static void SetBaseline(Quaternion q)
    {
        baseline = q;
        hasBaseline = true;
    }

    public static bool TryGetBaseline(out Quaternion q)
    {
        q = baseline;
        return hasBaseline;
    }

    public static void ClearBaseline()
    {
        hasBaseline = false;
    }
}
