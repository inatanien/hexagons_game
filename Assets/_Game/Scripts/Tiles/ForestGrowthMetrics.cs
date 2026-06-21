// 役割: 森の成長状態を表す型安全なメトリクス。
//       TerrainGrowthEvent<ForestGrowthMetrics> として EventBus に発行される。
//       将来的に SpreadScore（広がり度）・ClusterCount など
//       森固有の評価軸をここに追加していく。

namespace ElfVillage.Tiles
{
    public sealed class ForestGrowthMetrics : ITerrainGrowthMetrics
    {
        /// <summary>配置タイルが属する連結クラスターの枚数。</summary>
        public int LargestClusterSize { get; }

        /// <summary>グリッド上に配置された同種タイルの総枚数。</summary>
        public int TotalForestTiles { get; }

        // ── 将来の拡張例 ──────────────────────────────────────────────
        // public float SpreadScore  { get; }  // 広がり度（BBox面積 / タイル数）
        // public int   ClusterCount { get; }  // 孤立クラスターの総数

        public ForestGrowthMetrics(int largestClusterSize, int totalForestTiles)
        {
            LargestClusterSize = largestClusterSize;
            TotalForestTiles   = totalForestTiles;
        }
    }
}
