// WinHUDController.cs
using UnityEngine;
using UnityEngine.UI;

public class WinHUDController : MonoBehaviour
{
    [Header("Stars (left -> right)")]
    public Image[] starImages;         // 5 images
    public Sprite starOn;
    public Sprite starOff;

    [Header("Labels (optional)")]
    public Text summaryText;           // "Actions X (par Y) — Probes P, Launches L"

    public void Render(int stars, int totalActions, int par, int probes, int launches)
    {
        if (starImages != null && starOn && starOff)
        {
            for (int i = 0; i < starImages.Length; i++)
                starImages[i].sprite = (i < stars) ? starOn : starOff;
        }

        if (summaryText)
            summaryText.text = $"Actions {totalActions} (par {par}) — Probes {probes}, Launches {launches}";
    }
}
