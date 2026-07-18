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

        /// <summary>
        /// 指定セルに tileType を置いたとき、6方向それぞれについて
        /// 配置済みの隣接タイルが同じ tileCategory を持つかを返す（配置プレビューの演出用）。
        /// </summary>
        public static bool[] GetSynergyEdges(
            HexCoord coord,
            TileType tileType,
            IReadOnlyDictionary<HexCoord, HexTile> grid)
        {
            var result = new bool[6];
            if (tileType == null) return result;

            for (int dir = 0; dir < 6; dir++)
            {
                if (!grid.TryGetValue(coord.Neighbor(dir), out HexTile neighborTile)) continue;
                if (!neighborTile.IsPlaced) continue;
                result[dir] = SameCategory(tileType, neighborTile.Data.tileType);
            }
            return result;
        }

        // ── 方向ベースの接続判定API（TileType.elements対応の下準備） ──────────
        // まだ本番処理（HexGridManager等）からは呼ばれない。次セッションでの
        // 接続判定の一元化に備え、重複ロジックを避けるための土台として用意する。

        private const int DirectionCount = 6;

        /// <summary>指定方向の反対方向を返す（0〜5に正規化）。</summary>
        public static int GetOppositeDirection(int direction)
        {
            int normalized = ((direction % DirectionCount) + DirectionCount) % DirectionCount;
            return (normalized + DirectionCount / 2) % DirectionCount;
        }

        /// <summary>
        /// 指定方向のEdgeTypeを安全に取得する。tileTypeがnull、edgesが未設定、
        /// directionが範囲外の場合はfalseを返す（例外を投げない）。
        /// </summary>
        public static bool TryGetEdgeType(TileType tileType, int direction, out EdgeType edgeType)
        {
            edgeType = EdgeType.None;
            if (tileType == null) return false;
            if (tileType.edges == null) return false;
            if (direction < 0 || direction >= DirectionCount) return false;
            if (direction >= tileType.edges.Length) return false;

            edgeType = tileType.edges[direction];
            return true;
        }

        /// <summary>
        /// sourceのsourceDirection側の辺と、neighborの反対方向側の辺が
        /// 同じ非None EdgeTypeで一致しているかを判定する。None同士は一致として扱わない。
        /// </summary>
        public static bool AreEdgesCompatible(TileType source, int sourceDirection, TileType neighbor)
        {
            if (!TryGetEdgeType(source, sourceDirection, out EdgeType sourceEdge)) return false;
            if (sourceEdge == EdgeType.None) return false;

            int neighborDirection = GetOppositeDirection(sourceDirection);
            if (!TryGetEdgeType(neighbor, neighborDirection, out EdgeType neighborEdge)) return false;

            return sourceEdge == neighborEdge;
        }

        /// <summary>
        /// sourceDirection方向で辺が一致する場合、その辺が属するTileCategoryを返す。
        /// 辺が一致しない場合、またはEdgeTypeに対応するTileCategoryが存在しない
        /// （Villageのように辺を持たないカテゴリ）場合はfalseを返す。
        /// TileType.elements/variantの設定有無に関わらず、edgesのみを情報源として判定する。
        /// </summary>
        public static bool TryGetConnectedCategory(
            TileType source, int sourceDirection, TileType neighbor, out TileCategory category)
        {
            category = default;
            if (!AreEdgesCompatible(source, sourceDirection, neighbor)) return false;
            if (!TryGetEdgeType(source, sourceDirection, out EdgeType matchedEdge)) return false;

            var mapped = TileCategoryMapping.FromEdgeType(matchedEdge);
            if (!mapped.HasValue) return false;

            category = mapped.Value;
            return true;
        }
    }
}
