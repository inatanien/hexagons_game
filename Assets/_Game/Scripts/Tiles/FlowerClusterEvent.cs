// 役割: 花畑タイルが閾値以上連結したときに発行されるイベント。
//       FlowerClusterEvaluator が発行し FlowerPetalSystem が受け取る。

using System.Collections.Generic;

namespace ElfVillage.Tiles
{
    public sealed class FlowerClusterEvent
    {
        public IReadOnlyList<HexTile> Tiles { get; }

        public FlowerClusterEvent(IReadOnlyList<HexTile> tiles) => Tiles = tiles;
    }
}
