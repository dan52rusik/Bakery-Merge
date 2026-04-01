using UnityEngine;

public static class BakeryMergeBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureGameExists()
    {
        if (Object.FindFirstObjectByType<BakeryMergeGame>() != null)
        {
            return;
        }

        var root = new GameObject("Bakery Merge");
        root.AddComponent<BakeryMergeGame>();
    }
}
