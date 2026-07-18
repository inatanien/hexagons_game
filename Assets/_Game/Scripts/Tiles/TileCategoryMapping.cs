// 役割: EdgeTypeとTileCategoryを橋渡しする一方向マッピング。

namespace ElfVillage.Tiles
{
    public static class TileCategoryMapping
    {
        /// <summary>EdgeTypeに対応するTileCategoryを返す。対応がない場合（Noneなど）はnull。</summary>
        public static TileCategory? FromEdgeType(EdgeType edge)
        {
            switch (edge)
            {
                case EdgeType.Forest: return TileCategory.Forest;
                case EdgeType.Field:  return TileCategory.Field;
                case EdgeType.River:  return TileCategory.River;
                case EdgeType.Road:   return TileCategory.Road;
                default:              return null;
            }
        }
    }
}
