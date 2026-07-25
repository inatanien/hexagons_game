// 役割: 森の成長状態を表す型安全なメトリクス。
//       TerrainGrowthEvent<ForestGrowthMetrics> として EventBus に発行される。
//       将来的に SpreadScore（広がり度）・ClusterCount など
//       森固有の評価軸をここに追加していく。

namespace ElfVillage.Tiles
{
    public sealed class ForestGrowthMetrics : ITerrainGrowthMetrics
    {
        /// <summary>配置タイルが属する連結クラスターの枚数（実タイル数）。
        /// クエスト進捗・進行判定はこちらを使う。複合タイルも1枚として数える。</summary>
        public int LargestClusterSize { get; }

        /// <summary>グリッド上に配置された同種タイルの総枚数。</summary>
        public int TotalForestTiles { get; }

        /// <summary>
        /// 演出の発生しきい値専用の重み付きクラスターサイズ（Stage 8）。
        /// 複合タイルはareaWeightで按分されるため、1枚が全カテゴリ合計で1.0だけ寄与する
        /// （例: 森0.7＋花0.3の複合タイルは森として0.7）。単一属性タイルは1.0で挙動不変。
        /// ★クエスト進捗には使わないこと（LargestClusterSizeを使う）。
        /// </summary>
        public float WeightedClusterSize { get; }

        // ── 将来の拡張例 ──────────────────────────────────────────────
        // public float SpreadScore  { get; }  // 広がり度（BBox面積 / タイル数）
        // public int   ClusterCount { get; }  // 孤立クラスターの総数

        /// <param name="weightedClusterSize">負値（既定）の場合はlargestClusterSizeと同じ値になる。
        /// 重みを持たない呼び出し側（既存テスト等）が従来どおりの挙動を保てるようにするため。</param>
        public ForestGrowthMetrics(int largestClusterSize, int totalForestTiles, float weightedClusterSize = -1f)
        {
            LargestClusterSize  = largestClusterSize;
            TotalForestTiles    = totalForestTiles;
            WeightedClusterSize = weightedClusterSize < 0f ? largestClusterSize : weightedClusterSize;
        }
    }
}
