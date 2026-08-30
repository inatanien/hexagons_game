// 役割: 川の成長状態を表す型安全なメトリクス。
//       TerrainGrowthEvent<RiverGrowthMetrics> として EventBus に発行され、
//       TerrainClusterProgressRelay が Core の TerrainClusterProgressEvent(River) へ翻訳する。
//
//       ★このメトリクスは「クエスト進捗の観測」専用。
//         魚などの演出は従来どおり RiverClusterEvent（threshold=8）が担当する。
//         進捗の観測と演出の発生条件を分離するために、こちらは閾値なしで毎回発行される。
//
//       ForestGrowthMetrics にある WeightedClusterSize（演出しきい値用の按分重み）は
//       意図的に持たせていない。川の演出は実タイル数で判定しており消費者が存在しないため。
//       必要になった時点でオプション引数として追加できる。

namespace ElfVillage.Tiles
{
    public sealed class RiverGrowthMetrics : ITerrainGrowthMetrics
    {
        /// <summary>配置タイルが属する連結クラスターの枚数（実タイル数）。
        /// クエスト進捗はこちらを使う。複合タイル（景観川）も1枚として数える。</summary>
        public int LargestClusterSize { get; }

        /// <summary>グリッド上に配置された川タイルの総枚数。</summary>
        public int TotalRiverTiles { get; }

        public RiverGrowthMetrics(int largestClusterSize, int totalRiverTiles)
        {
            LargestClusterSize = largestClusterSize;
            TotalRiverTiles    = totalRiverTiles;
        }
    }
}
