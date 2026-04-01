using System;

namespace YG
{
    [Serializable]
    public class BakerySavedItemData
    {
        public int level;
        public float posX;
        public float posY;
        public float rotationZ;
    }

    public partial class SavesYG
    {
        public bool bakeryHasSave;
        public int bakeryScore;
        public int bakeryCoins;
        public int bakeryHighestLevelReached;
        public int bakeryBestScore;
        public int bakeryNextSpawnLevel;
        public int bakeryRemoveCharges = 3;
        public int bakeryShuffleCharges = 2;
        public int bakeryUpgradeCharges = 2;
        public int bakeryDiscoveredCount;
        public int[] bakeryInventoryCounts = new int[9];
        public bool[] bakeryDiscoveredLevels = new bool[9];
        public BakerySavedItemData[] bakeryItems = Array.Empty<BakerySavedItemData>();
    }
}
