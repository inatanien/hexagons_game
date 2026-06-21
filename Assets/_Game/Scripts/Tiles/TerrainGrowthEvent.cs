// 役割: 地形の成長を通知するジェネリックイベント。
//       TMetrics に地形ごとの型安全なメトリクスを指定する。
//       EventBus は TerrainGrowthEvent<ForestGrowthMetrics> と
//       TerrainGrowthEvent<RiverGrowthMetrics> を別の型として扱うため、
//       地形ごとに独立した購読チェーンが維持される。
//       新しい地形を追加するときはメトリクスクラスを1つ追加するだけでよい。

using System.Collections.Generic;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public sealed class TerrainGrowthEvent<TMetrics>
        where TMetrics : ITerrainGrowthMetrics
    {
        /// <summary>成長した地形のタイルタイプ。</summary>
        public TileType TerrainType { get; }

        /// <summary>今回配置されたタイルの座標（イベントの起点）。</summary>
        public HexCoord Anchor { get; }

        /// <summary>
        /// 成長に関係する全タイル（接続クラスター全体）。
        /// 風・鳥・精霊など演出のスポーン範囲として使用する。
        /// </summary>
        public IReadOnlyList<HexTile> AffectedTiles { get; }

        /// <summary>地形ごとの型安全なメトリクス。</summary>
        public TMetrics Metrics { get; }

        public TerrainGrowthEvent(TileType terrainType, HexCoord anchor,
                                   IReadOnlyList<HexTile> affectedTiles, TMetrics metrics)
        {
            TerrainType   = terrainType;
            Anchor        = anchor;
            AffectedTiles = affectedTiles;
            Metrics       = metrics;
        }
    }
}
