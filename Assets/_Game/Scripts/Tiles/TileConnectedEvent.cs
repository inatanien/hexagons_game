// 役割: 同種タイルが隣接接続したときに EventBus 経由で配信されるイベントデータ。
//       接続イベントを受け取るすべてのシステム（ビジュアル・サウンド・クエスト・精霊など）
//       が EventBus.Subscribe<TileConnectedEvent> で購読する。
//       TileConnectionFX はその購読者のひとつに過ぎない。

using System.Collections.Generic;

namespace ElfVillage.Tiles
{
    /// <summary>
    /// タイル配置後、同種タイルへの接続が1件以上発生したときに発火する。
    /// PlacedTile に接続した全辺の情報を Edges リストで提供する。
    /// </summary>
    public sealed class TileConnectedEvent
    {
        public HexTile                       PlacedTile { get; }
        public TileType                      TileType   { get; }
        public IReadOnlyList<ConnectionEdge> Edges      { get; }

        public TileConnectedEvent(HexTile placed, TileType type, List<ConnectionEdge> edges)
        {
            PlacedTile = placed;
            TileType   = type;
            Edges      = edges;
        }
    }

    /// <summary>接続した1辺の情報（方向 + 隣接タイル）。</summary>
    public readonly struct ConnectionEdge
    {
        /// <summary>配置タイルから見た方向インデックス（0〜5）。</summary>
        public readonly int     Direction;
        public readonly HexTile Neighbor;

        public ConnectionEdge(int direction, HexTile neighbor)
        {
            Direction = direction;
            Neighbor  = neighbor;
        }
    }
}
