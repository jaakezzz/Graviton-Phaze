using UnityEngine;

[CreateAssetMenu(menuName = "Graviton/Level Config")]
public class LevelConfig : ScriptableObject
{
    [Min(0)] public int minActionsForFiveStars = 2; // “par” for this level
}
