// 役割: 森タイルの成長を評価し TerrainGrowthEvent<ForestGrowthMetrics> を発行する。
//       TilePlacedEvent を購読し、森タイルが配置されるたびに BFS でクラスターを計算する。
//       川・街など他地形の評価は同じ構造でそれぞれのエバリュエーターを追加する。
//       クラスター判定は TileType.HasEffectCategory(Forest) を使う（Session 12）。
//       これにより legacy 単一タイル（TileType_Forest）と、Forest要素を持つ複合タイル
//       （TileType_ForestFlower等。Forest要素がvisualOnlyでなければ）が同じForestクラスター
//       として繋がる。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class ForestGrowthEvaluator : MonoBehaviour
    {
        [SerializeField] private HexGridManager _gridManager;

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

            var cluster = FindCluster(evt.Coord);
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

        // ── BFS: 配置タイルからForestカテゴリを持つタイルを全走査 ────────────────────

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
                if (!tile.IsPlaced || !IsForestType(tile.Data.tileType)) continue;

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

        private static bool IsForestType(TileType type)
            => type != null && type.HasEffectCategory(TileCategory.Forest);
    }
}
