using UnityEngine;

[CreateAssetMenu(menuName = "Graviton/Level Database")]
public class LevelDatabase : ScriptableObject
{
    public LevelConfig[] levels;
}