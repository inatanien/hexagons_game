// 役割: タイルが配置されたことを通知するイベント。
//       TileConnectedEvent（同種接続時のみ発火）とは異なり、
//       孤立配置を含む全てのタイル配置で発行される。
//       地形成長評価システム（ForestGrowthEvaluator 等）の起点として使用する。

using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public sealed class TilePlacedEvent
    {
        public HexTile  Tile     { get; }
        public TileType TileType { get; }
        public HexCoord Coord    { get; }

        public TilePlacedEvent(HexTile tile, TileType tileType, HexCoord coord)
        {
            Tile     = tile;
            TileType = tileType;
            Coord    = coord;
        }
    }
}
