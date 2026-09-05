// 役割: 川タイルの連結クラスターを評価し、5枚ごとの節目で RiverBridgeEvent を発行する。
//       TilePlacedEvent を購読し、川タイルが配置されるたびに BFS でクラスターを計算。
//       クラスターサイズがちょうど5の倍数（5, 10, 15…）に達した瞬間だけイベントを発行する
//       ことで、成長し続けるクラスターに5枚ごと1本ずつ橋が追加される。
//
//       ★「隣にある」ではなく「川として繋がっている」を数える。
//         川タイル同士が隣り合っていても、互いにField辺を向け合っていれば水は繋がらない。
//         見た目が繋がっていない川を1本の川として数えると、
//         離れた水たまりが集まっただけで橋が架かってしまう。
//         判定は EdgeMatcher へ委譲する（溝を開くかどうかを決めているのと同じ関数）。
//
//       ★橋を架ける先は「架けられるタイル」から選ぶ。
//         節目に到達した1枚が曲がりだった場合でも、同じ川の中の直線か緩カーブへ架ける。
//         ここで発行を見送ると、橋を待っているクエストがそのぶん足踏みする。
//
//       ★川かどうかは TileType.HasCategory(River) で判定する。
//         以前はSceneのTileType[]へ登録されたアセット参照の一致で判定しており、
//         川タイルを増やすたびに登録漏れで橋が架からなくなる作りだった
//         （景観川6種の追加時に実際に発生）。RiverFlowSystem / RiverGrowthEvaluator と
//         同じ判定へ揃えてある。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class RiverBridgeEvaluator : MonoBehaviour
    {
        [SerializeField] private HexGridManager _gridManager;

        [Header("橋を架ける連結枚数の間隔")]
        [SerializeField] private int _interval = 5;

        // 同じタイルへ2本目を架けないための記録。
        // ★架ける場所を決めるのはこのクラスなので、記録もここに持つ。
        //   BridgeSystem は受け取った場所へ描くだけ、という役割分担を保つため。
        private readonly HashSet<HexCoord> _bridgedCoords = new HashSet<HexCoord>();

        private void OnEnable()  => EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
        private void OnDisable() => EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (!IsRiverType(evt.TileType)) return;
            if (_interval <= 0) return;

            var cluster = FindCluster(evt.Coord);
            if (cluster.Count % _interval != 0) return;

            if (!TryPickBridgeTile(cluster, evt.Coord, out HexTile bridgeTile)) return;

            _bridgedCoords.Add(bridgeTile.Data.coord);
            EventBus.Publish(new RiverBridgeEvent(bridgeTile, cluster, cluster.Count));
        }

        /// <summary>
        /// このクラスターの中から橋を架ける1枚を選ぶ。
        /// 置いたばかりのタイルに架けられるならそれを使い、無理なら近いものから探す。
        /// クラスター内に架けられるタイルが1枚も無いときだけ false。
        /// </summary>
        private bool TryPickBridgeTile(List<HexTile> cluster, HexCoord placedCoord, out HexTile picked)
        {
            picked = null;
            int bestDistance = int.MaxValue;

            foreach (var tile in cluster)
            {
                HexCoord coord = tile.Data.coord;
                if (_bridgedCoords.Contains(coord)) continue;
                if (!RiverChannelLayout.CanHostBridge(tile.Data.tileType, coord.q, coord.r, coord.s)) continue;

                int distance = placedCoord.DistanceTo(coord);
                if (distance > bestDistance) continue;

                // ★同じ距離なら座標の小さいほうへ決める。
                //   クラスターを走る順に任せると、同じ盤面でも架かる場所が変わってしまう。
                if (distance == bestDistance && !IsEarlier(coord, picked.Data.coord)) continue;

                picked       = tile;
                bestDistance = distance;
            }

            return picked != null;
        }

        private static bool IsEarlier(HexCoord a, HexCoord b)
            => a.q != b.q ? a.q < b.q : a.r < b.r;

        // ── BFS: 置いたタイルから、川として繋がっているタイルを辿る ──
        // RiverGrowthEvaluator と同じ辿り方（別イベント・別閾値のため専用に保持）

        private List<HexTile> FindCluster(HexCoord startCoord)
        {
            var result  = new List<HexTile>();
            var visited = new HashSet<HexCoord>();
            var queue   = new Queue<HexCoord>();

            visited.Add(startCoord);
            queue.Enqueue(startCoord);

            while (queue.Count > 0)
            {
                var coord = queue.Dequeue();
                if (!_gridManager.TryGetTile(coord, out var tile)) continue;
                if (!tile.IsPlaced || !IsRiverType(tile.Data.tileType)) continue;

                result.Add(tile);

                for (int dir = 0; dir < 6; dir++)
                {
                    // 隣にあるだけでは辿らない。水路が実際に繋がっている辺だけを渡る
                    if (!IsRiverEdgeConnected(tile, coord, dir)) continue;

                    var next = coord.Neighbor(dir);
                    if (visited.Add(next))
                        queue.Enqueue(next);
                }
            }

            return result;
        }

        /// <summary>
        /// dir方向の隣と、川として繋がっているか。
        /// ★HexGridManager が溝を開くかどうかを決めているのと同じ関数を通す。
        /// </summary>
        private bool IsRiverEdgeConnected(HexTile from, HexCoord coord, int dir)
            => _gridManager.TryGetTile(coord.Neighbor(dir), out var neighbor)
               && EdgeMatcher.AreConnectedAs(from, neighbor, dir, TileCategory.River);

        /// <summary>
        /// 川タイルか。接続判定・デッキ抽選と同じ TileType.HasCategory(River) を使う。
        /// ★見た目だけの landDecoration はカテゴリへ参加しないため、ここへは影響しない。
        /// </summary>
        private static bool IsRiverType(TileType type)
            => type != null && type.HasCategory(TileCategory.River);
    }
}
