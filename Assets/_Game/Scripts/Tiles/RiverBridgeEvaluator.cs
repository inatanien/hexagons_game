// 役割: 川タイルの連結クラスターを評価し、5枚ごとの節目で RiverBridgeEvent を発行する。
//       TilePlacedEvent を購読し、川タイルが配置されるたびに BFS でクラスターを計算。
//       クラスターサイズがちょうど5の倍数（5, 10, 15…）に達した瞬間だけイベントを発行する
//       ことで、成長し続けるクラスターに5枚ごと1本ずつ橋が追加される。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class RiverBridgeEvaluator : MonoBehaviour
    {
        [SerializeField] private HexGridManager _gridManager;

        [Header("川として扱うタイルタイプ（複数可）")]
        [SerializeField] private TileType[] _riverTileTypes;

        [Header("橋を架ける連結枚数の間隔")]
        [SerializeField] private int _interval = 5;

        private void OnEnable()  => EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
        private void OnDisable() => EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (!IsRiverType(evt.TileType)) return;
            if (_interval <= 0) return;

            var cluster = FindCluster(evt.Coord);
            if (cluster.Count % _interval != 0) return;

            if (!_gridManager.TryGetTile(evt.Coord, out var placedTile)) return;

            EventBus.Publish(new RiverBridgeEvent(placedTile, cluster.Count));
        }

        // ── BFS: 配置タイルから川種別すべてを対象に連結クラスターを取得 ──
        // RiverGrowthEvaluator と同一のロジック（別イベント・別閾値のため専用に保持）

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
                    var next = coord.Neighbor(dir);
                    if (visited.Add(next))
                        queue.Enqueue(next);
                }
            }

            return result;
        }

        private bool IsRiverType(TileType type)
        {
            if (_riverTileTypes == null) return false;
            foreach (var rt in _riverTileTypes)
                if (rt == type) return true;
            return false;
        }
    }
}
