// 役割: 異なる地形タイルの辺隣接と枚数条件を評価し TerrainSynergyEvent を発行する。
//       TilePlacedEvent を購読し、置かれたタイルの6近傍に異種タイルがあれば
//       BFS でそれぞれのクラスターサイズを計算して条件を判定する。
//       新しいシナジー（森×村 など）はこのコンポーネントを追加して設定するだけで追加できる。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class SynergyEvaluator : MonoBehaviour
    {
        [SerializeField] private HexGridManager _gridManager;

        [Header("シナジー識別子")]
        [SerializeField] private string _synergyId = "ForestRiver";

        [Header("地形 A（例：森）")]
        [SerializeField] private TileType[] _typesA;
        [SerializeField] private int        _minCountA = 5;

        [Header("地形 B（例：川）")]
        [SerializeField] private TileType[] _typesB;
        [SerializeField] private int        _minCountB = 5;

        private readonly HashSet<HexCoord> _coordsA = new();
        private readonly HashSet<HexCoord> _coordsB = new();

        private void OnEnable()  => EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
        private void OnDisable() => EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            bool isA = IsType(evt.TileType, _typesA);
            bool isB = IsType(evt.TileType, _typesB);
            if (!isA && !isB) return;

            if (isA) _coordsA.Add(evt.Coord);
            if (isB) _coordsB.Add(evt.Coord);

            // 今置いたタイルの6近傍に異種タイルがあるか確認
            var neighborSet = isA ? _coordsB : _coordsA;

            for (int dir = 0; dir < 6; dir++)
            {
                var nb = evt.Coord.Neighbor(dir);
                if (!neighborSet.Contains(nb)) continue;

                // 辺共有を発見 → BFS で隣接クラスターのサイズを確認
                var ownCoords   = isA ? _coordsA : _coordsB;
                var otherCoords = isA ? _coordsB : _coordsA;
                int minOwn      = isA ? _minCountA : _minCountB;
                int minOther    = isA ? _minCountB : _minCountA;

                var ownCluster   = BfsCluster(evt.Coord, ownCoords);
                var otherCluster = BfsCluster(nb, otherCoords);

                if (ownCluster.Count < minOwn || otherCluster.Count < minOther) continue;

                // 条件達成 → タイル群を取得してイベント発行
                var clusterA = isA ? ownCluster : otherCluster;
                var clusterB = isA ? otherCluster : ownCluster;

                EventBus.Publish(new TerrainSynergyEvent(
                    _synergyId,
                    CoordsToTiles(clusterA),
                    CoordsToTiles(clusterB)
                ));
                return; // 1回の配置で複数回発行しない
            }
        }

        // ── BFS：指定座標から同一セット内で連結するクラスターを取得 ──

        private List<HexCoord> BfsCluster(HexCoord start, HashSet<HexCoord> validCoords)
        {
            var result  = new List<HexCoord>();
            var visited = new HashSet<HexCoord>();
            var queue   = new Queue<HexCoord>();

            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var coord = queue.Dequeue();
                if (!validCoords.Contains(coord)) continue;
                result.Add(coord);

                for (int d = 0; d < 6; d++)
                {
                    var next = coord.Neighbor(d);
                    if (visited.Add(next))
                        queue.Enqueue(next);
                }
            }
            return result;
        }

        private List<HexTile> CoordsToTiles(List<HexCoord> coords)
        {
            var tiles = new List<HexTile>(coords.Count);
            foreach (var c in coords)
                if (_gridManager.TryGetTile(c, out var tile))
                    tiles.Add(tile);
            return tiles;
        }

        private static bool IsType(TileType type, TileType[] types)
        {
            if (types == null) return false;
            foreach (var t in types)
                if (t == type) return true;
            return false;
        }
    }
}
