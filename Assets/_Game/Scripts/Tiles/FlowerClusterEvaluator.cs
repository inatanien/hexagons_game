// 役割: 花畑タイルの連結クラスターを評価し FlowerClusterEvent を発行する。
//       TilePlacedEvent を購読し、花畑タイルが配置されるたびに BFS でクラスターを計算。
//       閾値（デフォルト3枚）以上で連結していればイベントを発行する。
//       クラスター判定は TileType.HasEffectCategory(Field) を使う（Session 12）。
//       これにより legacy 単一タイル（TileType_Field）と、Field要素を持つ複合タイル
//       （TileType_ForestFlower等。Field要素がvisualOnlyでも対象になる）が
//       同じFlowerクラスターとして繋がる。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class FlowerClusterEvaluator : MonoBehaviour
    {
        [SerializeField] private HexGridManager _gridManager;

        [Header("花びらが舞うまでの最小連結枚数")]
        [SerializeField] private int _threshold = 3;

        private void OnEnable()  => EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
        private void OnDisable() => EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (!IsFlowerType(evt.TileType)) return;

            var cluster = FindCluster(evt.Coord);
            if (cluster.Count < _threshold) return;

            EventBus.Publish(new FlowerClusterEvent(cluster));
        }

        // ── BFS: 配置タイルから花畑種別すべてを対象に連結クラスターを取得 ──

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
                if (!tile.IsPlaced || !IsFlowerType(tile.Data.tileType)) continue;

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

        private static bool IsFlowerType(TileType type)
            => type != null && type.HasEffectCategory(TileCategory.Field);
    }
}
