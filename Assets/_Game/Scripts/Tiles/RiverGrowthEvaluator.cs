// 役割: 川タイルの連結クラスターを評価し、2系統のイベントを発行する。
//       TilePlacedEvent を購読し、川タイルが配置されるたびに BFS でクラスターを計算する。
//
//       1) TerrainGrowthEvent<RiverGrowthMetrics> ... クエスト進捗の観測用。閾値なしで毎回発行。
//       2) RiverClusterEvent                      ... 魚などの演出用。閾値（既定8枚）以上のときだけ発行。
//
//       ★2系統に分けているのは、「クエストが観測する進捗」と「演出が発生する閾値」を
//         完全に分離するため。閾値を1つにすると、川3枚のクエストを作った途端に
//         魚が3枚で湧いてしまう（逆に魚に合わせると3枚のクエストが作れない）。
//         森は ForestGrowthEvaluator が同じ形で閾値なしの成長イベントを出しており、それと対称。
//
//       ★川かどうかは TileType.HasCategory(River) で判定する。
//         以前はSceneのTileType[]へ登録されたアセット参照の一致で判定しており、
//         川タイルを増やすたびに登録漏れで魚が出なくなる作りだった
//         （景観川6種の追加時に実際に発生）。RiverFlowSystem / RiverBridgeEvaluator と
//         同じ判定へ揃えてある。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class RiverGrowthEvaluator : MonoBehaviour
    {
        [SerializeField] private HexGridManager _gridManager;

        [Header("魚が出現するまでの最小連結枚数")]
        [SerializeField] private int _threshold = 8;

        // 配置済み川タイルの総数（TilePlacedEventによるインクリメント管理）。
        // ★この値はイベントの累計であって盤面の実測ではない。
        //   Save/Load・タイル削除・盤面の再構築を導入すると、
        //   加算値と実際の盤面がずれる可能性がある。
        //   その時点で「盤面から再計算する」方式へ置き換えること
        //   （ForestGrowthEvaluator._totalForestTiles も同じ制約を抱えている）。
        //   クエスト進捗が読むのは LargestClusterSize（毎回BFSで実測）なので、
        //   ずれの影響を受けるのは TotalRiverTiles を使う将来の機能だけ。
        private int _totalRiverTiles;

        private void OnEnable()  => EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
        private void OnDisable() => EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (!IsRiverType(evt.TileType)) return;

            _totalRiverTiles++;

            var cluster = FindCluster(evt.Coord);

            // ── 1) クエスト進捗の観測用。閾値と無関係に毎回発行する ──
            EventBus.Publish(new TerrainGrowthEvent<RiverGrowthMetrics>(
                terrainType:   evt.TileType,
                anchor:        evt.Coord,
                affectedTiles: cluster,
                metrics:       new RiverGrowthMetrics(cluster.Count, _totalRiverTiles)
            ));

            // ── 2) 魚などの演出用。ここから下は従来どおりの挙動 ──
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

        /// <summary>
        /// 川タイルか。接続判定・デッキ抽選と同じ TileType.HasCategory(River) を使う。
        /// ★見た目だけの landDecoration はカテゴリへ参加しないため、ここへは影響しない。
        /// </summary>
        private static bool IsRiverType(TileType type)
            => type != null && type.HasCategory(TileCategory.River);
    }
}
