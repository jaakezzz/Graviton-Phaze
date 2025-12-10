using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class FuelGauge : MonoBehaviour
{
    [SerializeField] ShipController ship;   // leave empty; it will auto-find
    [SerializeField] Image fill;            // assign the FuelFill Image (Type=Filled)
    [SerializeField] Text label;           // optional % text
    [SerializeField] Gradient color;        // optional green?yellow?red
    [SerializeField] float uiSmoothing = 8f;

    float target01, shown01;
    bool subscribed;

    void OnEnable()
    {
        TryBind();                          // first attempt
        if (!subscribed) StartCoroutine(WaitAndBind()); // ship may spawn a frame later
    }

    void OnDisable()
    {
        if (subscribed && ship != null)
            ship.onFuelChanged.RemoveListener(OnFuel);
        subscribed = false;
    }

    IEnumerator WaitAndBind()
    {
        // keep looking for a ShipController until found (spawns during EnterFly)
        while (!subscribed)
        {
            if (!ship) ship = FindAnyObjectByType<ShipController>();
            TryBind();
            if (!subscribed) yield return null;
        }
    }

    void TryBind()
    {
        if (subscribed) return;
        if (!ship) return;
        ship.onFuelChanged.AddListener(OnFuel);
        subscribed = true;

        // snap to current value immediately
        OnFuel(ship.Fuel, ship.FuelMax);
        shown01 = target01;
        ApplyUI();
    }

    void Update()
    {
        // smooth the UI towards target
        shown01 = Mathf.Lerp(shown01, target01, 1f - Mathf.Exp(-uiSmoothing * Time.deltaTime));
        ApplyUI();
    }

    void OnFuel(float current, float max)
    {
        target01 = max > 0 ? Mathf.Clamp01(current / max) : 0f;
        if (label) label.text = Mathf.RoundToInt(target01 * 100f) + "%";
    }

    void ApplyUI()
    {
        if (fill)
        {
            fill.fillAmount = shown01;
            if (color != null) fill.color = color.Evaluate(shown01);
        }
    }
}
