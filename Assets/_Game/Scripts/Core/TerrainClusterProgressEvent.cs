// 役割: 地形クラスターの成長進捗をシステム間へ通知する汎用イベント。
//       Tiles側の詳細イベント（TerrainGrowthEvent<T>等）をTerrainClusterProgressRelayが
//       翻訳して発行する。Quest等、Coreのみに依存したいシステムはこちらを購読する。

namespace ElfVillage.Core
{
    public sealed class TerrainClusterProgressEvent
    {
        public TerrainClusterCategory Category    { get; }
        public int                    ClusterSize { get; }

        public TerrainClusterProgressEvent(TerrainClusterCategory category, int clusterSize)
        {
            Category    = category;
            ClusterSize = clusterSize;
        }
    }
}
