// SensorBootstrap.cs
using UnityEngine;
using UnityEngine.InputSystem;

public class SensorBootstrap : MonoBehaviour
{
    void Awake()
    {
        var s = AttitudeSensor.current;
        if (s != null && !s.enabled)
            InputSystem.EnableDevice(s); // keep it on from menu onward
    }
}
