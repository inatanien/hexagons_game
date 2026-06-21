// 役割: 森タイルの成長を評価し TerrainGrowthEvent<ForestGrowthMetrics> を発行する。
//       TilePlacedEvent を購読し、森タイルが配置されるたびに BFS でクラスターを計算する。
//       川・街など他地形の評価は同じ構造でそれぞれのエバリュエーターを追加する。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class ForestGrowthEvaluator : MonoBehaviour
    {
        [SerializeField] private HexGridManager _gridManager;

        [Header("森として扱うタイルタイプ（複数可）")]
        [SerializeField] private TileType[] _forestTileTypes;

        // 配置済み森タイルの総数（インクリメント管理）
        // TODO: Save/Load または TileRemoval 実装時は
        //       インクリメント管理ではなく再計算へ変更する
        private int _totalForestTiles;

        private void OnEnable()  => EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
        private void OnDisable() => EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (!IsForestType(evt.TileType)) return;

            _totalForestTiles++;

            var cluster = FindCluster(evt.Coord, evt.TileType);
            var metrics = new ForestGrowthMetrics(
                largestClusterSize: cluster.Count,
                totalForestTiles:   _totalForestTiles
            );

            EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                terrainType:   evt.TileType,
                anchor:        evt.Coord,
                affectedTiles: cluster,
                metrics:       metrics
            ));
        }

        // ── BFS: 配置タイルから同種タイルを全走査 ────────────────────

        private List<HexTile> FindCluster(HexCoord startCoord, TileType targetType)
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
                if (!tile.IsPlaced || tile.Data.tileType != targetType) continue;

                result.Add(tile);

                for (int dir = 0; dir < 6; dir++)
                {
                    var next = coord.Neighbor(dir);
                    if (visited.Add(next))   // Add は追加できたとき true を返す
                        queue.Enqueue(next);
                }
            }

            return result;
        }

        private bool IsForestType(TileType type)
        {
            if (_forestTileTypes == null) return false;
            foreach (var ft in _forestTileTypes)
                if (ft == type) return true;
            return false;
        }
    }
}
