using UnityEngine;

public class GoalTrigger : MonoBehaviour
{
    [SerializeField] PhaseDirector director;  // drag your PhaseDirector here in Inspector

    void Awake()
    {
        if (!director) director = FindFirstObjectByType<PhaseDirector>();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        // Only ships can finish
        if (!other.GetComponent<ShipController>() &&
            !other.GetComponentInParent<ShipController>()) return;

        director?.EnterWin();
    }
}
