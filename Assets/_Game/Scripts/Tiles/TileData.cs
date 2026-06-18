// 役割: グリッド上に置かれたタイルの状態を表す構造体。
//       HexCoord・TileType・回転数を保持する純粋データ。

using System;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    [Serializable]
    public struct TileData
    {
        public HexCoord coord;
        public TileType tileType;
        public int rotation; // 0〜5（60°単位）

        public TileData(HexCoord coord, TileType tileType, int rotation = 0)
        {
            this.coord = coord;
            this.tileType = tileType;
            this.rotation = ((rotation % 6) + 6) % 6;
        }

        /// <summary>回転を加味した方向 direction の辺の種類を返す。</summary>
        public EdgeType GetEdge(int direction)
        {
            if (tileType == null) return EdgeType.None;
            return tileType.GetEdge(direction - rotation);
        }

        /// <summary>隣接タイルとの辺が接続できるか判定する。</summary>
        public bool CanConnect(TileData neighbor, int directionToNeighbor)
        {
            EdgeType myEdge = GetEdge(directionToNeighbor);
            if (myEdge == EdgeType.None) return true;
            EdgeType theirEdge = neighbor.GetEdge(directionToNeighbor + 3); // 反対方向
            return myEdge == theirEdge;
        }
    }
}
