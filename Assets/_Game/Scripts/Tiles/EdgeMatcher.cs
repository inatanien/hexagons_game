// 役割: 隣接タイルとのエッジ一致を検証する純粋ロジッククラス。
//       MonoBehaviour に依存せず単体テスト可能。

using System.Collections.Generic;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public static class EdgeMatcher
    {
        /// <summary>
        /// 指定セルにタイルを置けるか返す。
        /// ルール: 最初の1枚は自由配置、2枚目以降は配置済みタイルへの隣接が必須。
        /// エッジマッチングは配置を妨げない（将来のスコアリングで活用予定）。
        /// </summary>
        public static bool IsPlaceable(
            HexCoord coord,
            TileType tileType,
            int rotation,
            IReadOnlyDictionary<HexCoord, HexTile> grid)
        {
            if (!HasAnyPlaced(grid)) return true;

            for (int dir = 0; dir < 6; dir++)
            {
                if (grid.TryGetValue(coord.Neighbor(dir), out HexTile neighbor) && neighbor.IsPlaced)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 指定セルに tileType を rotation で置いたとき、隣接タイルすべてと辺が一致するか返す。
        /// 配置ブロックではなく、スコアリング・視覚ヒント用。
        /// </summary>
        public static bool IsEdgeCompatible(
            HexCoord coord,
            TileType tileType,
            int rotation,
            IReadOnlyDictionary<HexCoord, HexTile> grid)
        {
            var candidate = new TileData(coord, tileType, rotation);
            for (int dir = 0; dir < 6; dir++)
            {
                if (!grid.TryGetValue(coord.Neighbor(dir), out HexTile neighborTile)) continue;
                if (!neighborTile.IsPlaced) continue;
                // 同一カテゴリのタイル同士は辺の種別によらず常に互換
                if (SameCategory(tileType, neighborTile.Data.tileType)) continue;
                if (!candidate.CanConnect(neighborTile.Data, dir)) return false;
            }
            return true;
        }

        /// <summary>
        /// 両タイルが空でない同一 tileCategory を持つか判定する。
        /// </summary>
        public static bool SameCategory(TileType a, TileType b)
        {
            if (a == null || b == null) return false;
            if (string.IsNullOrEmpty(a.tileCategory)) return false;
            return a.tileCategory == b.tileCategory;
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
