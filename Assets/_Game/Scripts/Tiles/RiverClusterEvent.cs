// 役割: 川タイルが閾値枚数以上で連結したことを通知するイベント。
//       RiverGrowthEvaluator が発行し、FishSystem など演出システムが購読する。

using System.Collections.Generic;

namespace ElfVillage.Tiles
{
    public sealed class RiverClusterEvent
    {
        /// <summary>連結した川タイルの一覧。</summary>
        public IReadOnlyList<HexTile> Tiles { get; }

        /// <summary>クラスターサイズ（Tiles.Count の糖衣）。</summary>
        public int ClusterSize => Tiles.Count;

        public RiverClusterEvent(IReadOnlyList<HexTile> tiles)
        {
            Tiles = tiles;
        }
    }
}
