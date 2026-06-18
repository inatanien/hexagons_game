// 役割: 隣接タイルとのエッジ一致を検証する純粋ロジッククラス。
//       MonoBehaviour に依存せず単体テスト可能。

using System.Collections.Generic;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public static class EdgeMatcher
    {
        /// <summary>
        /// 指定セルに tileType を rotation で置いたとき、
        /// 既存の隣接タイルすべてと辺が一致するか返す。
        /// 隣接に配置済みタイルが1つもない場合は true（制約なし）。
        /// </summary>
        public static bool IsPlaceable(
            HexCoord coord,
            TileType tileType,
            int rotation,
            IReadOnlyDictionary<HexCoord, HexTile> grid)
        {
            var candidate = new TileData(coord, tileType, rotation);
            bool hasPlacedNeighbor = false;

            for (int dir = 0; dir < 6; dir++)
            {
                HexCoord neighborCoord = coord.Neighbor(dir);
                if (!grid.TryGetValue(neighborCoord, out HexTile neighborTile)) continue;
                if (!neighborTile.IsPlaced) continue;

                hasPlacedNeighbor = true;
                if (!candidate.CanConnect(neighborTile.Data, dir))
                    return false;
            }

            // 隣接に配置済みがなければ最初の1枚として自由に置ける
            return true;
        }

        /// <summary>
        /// 既に配置済みのタイルが1枚以上あるか返す。
        /// グリッドが空のうちはどこにでも置けるようにするための補助。
        /// </summary>
        public static bool HasAnyPlaced(IReadOnlyDictionary<HexCoord, HexTile> grid)
        {
            foreach (var tile in grid.Values)
                if (tile.IsPlaced) return true;
            return false;
        }
    }
}
