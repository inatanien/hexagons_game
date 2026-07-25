// 役割: 既存の世界イベントを、精霊向けのSpiritStimulusEventへ翻訳して再発行する中継役。
//       Stage 1のTerrainClusterProgressRelayと同じ「詳細イベント → 汎用イベント」の翻訳層。
//
//       ★このクラスは精霊の参照・個体一覧・現在状態を一切持たない。
//         精霊の生成や管理も行わない（それはForestSpiritSpawnerの責務）。
//         各精霊が自分でSpiritStimulusEventを購読し、自分に関係する刺激かを判断する。
//
//       Stage 11で購読するのは以下の2つだけ。鳥・昼夜は必要になったStageで追加する。
//         ・TerrainGrowthEvent<ForestGrowthMetrics>  → ForestGrew
//         ・FlowerClusterEvent                        → FlowerBloomed
//
//       どちらのイベントも位置情報を持たないため、関係タイル群の重心をワールド位置として使う
//       （FlowerClusterEventはTilesのみ、TerrainGrowthEventのAnchorはHexCoordであり
//         SpiritsはHexGridを参照していないため、いずれにせよタイル座標から求める）。
//
//       ForestGrewの翻訳はForestSpiritSpawnerのhome更新とは独立している。
//       Spawnerは「home範囲の更新」、この中継は「刺激の通知」と役割が分かれており、
//       同じ判定を二重に行ってはいない（home一致判定は受け取った精霊側が1箇所で行う）。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;
using ElfVillage.Tiles;

namespace ElfVillage.Spirits
{
    public class SpiritStimulusRelay : MonoBehaviour
    {
        private bool _subscribed;

        private void OnEnable()
        {
            if (_subscribed) return; // 同一インスタンスによる重複購読を防ぐ
            EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
            EventBus.Subscribe<FlowerClusterEvent>(OnFlowerCluster);
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
            EventBus.Unsubscribe<FlowerClusterEvent>(OnFlowerCluster);
            _subscribed = false;
        }

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
            => Publish(SpiritStimulusKind.ForestGrew, evt.AffectedTiles);

        private void OnFlowerCluster(FlowerClusterEvent evt)
            => Publish(SpiritStimulusKind.FlowerBloomed, evt.Tiles);

        private static void Publish(SpiritStimulusKind kind, IReadOnlyList<HexTile> tiles)
        {
            if (!TryGetCenter(tiles, out Vector3 center)) return;
            EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(kind, center, tiles)));
        }

        /// <summary>タイル群の重心を求める。有効なタイルが1枚も無ければfalse（刺激を発行しない）。</summary>
        private static bool TryGetCenter(IReadOnlyList<HexTile> tiles, out Vector3 center)
        {
            center = Vector3.zero;
            if (tiles == null || tiles.Count == 0) return false;

            var sum = Vector3.zero;
            int count = 0;
            foreach (var t in tiles)
            {
                if (t == null) continue;
                sum += t.transform.position;
                count++;
            }
            if (count == 0) return false;

            center = sum / count;
            return true;
        }
    }
}
