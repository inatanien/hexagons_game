// 役割: 川タイルの連結クラスターを評価し RiverClusterEvent を発行する。
//       TilePlacedEvent を購読し、川タイルが配置されるたびに BFS でクラスターを計算。
//       閾値（デフォルト8枚）以上で連結していればイベントを発行する。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class RiverGrowthEvaluator : MonoBehaviour
    {
        [SerializeField] private HexGridManager _gridManager;

        [Header("川として扱うタイルタイプ（複数可）")]
        [SerializeField] private TileType[] _riverTileTypes;

        [Header("魚が出現するまでの最小連結枚数")]
        [SerializeField] private int _threshold = 8;

        private void OnEnable()  => EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
        private void OnDisable() => EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (!IsRiverType(evt.TileType)) return;

            var cluster = FindCluster(evt.Coord);
            if (cluster.Count < _threshold) return;

            EventBus.Publish(new RiverClusterEvent(cluster));
        }

        // ── BFS: 配置タイルから川種別すべてを対象に連結クラスターを取得 ──

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
