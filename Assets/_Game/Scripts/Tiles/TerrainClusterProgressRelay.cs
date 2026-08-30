// 役割: Tiles側の詳細な成長イベント（TerrainGrowthEvent<ForestGrowthMetrics>等）を、
//       CoreのTerrainClusterProgressEventへ変換して再発行する中継コンポーネント。
//       Quest等、Coreのみに依存したいシステムが、Tiles固有の型を一切知らずに
//       クラスター進捗を購読できるようにするための翻訳役。
//       特定のシステム（WorldBreathSystem等）へは一切依存しない、完全に独立したコンポーネント。
//       Forest（TerrainGrowthEvent<ForestGrowthMetrics>）とRiver（同<RiverGrowthMetrics>）を中継する。
//       どちらも地形ごとのエバリュエーターが閾値なしで毎回発行するため、
//       0→1→2のような常時進捗としてクエストが観測できる。
//       ★購読と解除は必ず対にすること。片方だけ足すと、シーン遷移のたびに
//         購読が残って進捗が多重通知される。
//       FlowerClusterEventの中継は未実装（FlowerClusterEvaluatorが閾値未満では発行しないため、
//       0→1→2のような常時進捗通知には使えない。花クエストを追加する段階でVFX用イベントと
//       常時進捗通知の責務を改めて検討する）。

using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class TerrainClusterProgressRelay : MonoBehaviour
    {
        private void OnEnable()
        {
            EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
            EventBus.Subscribe<TerrainGrowthEvent<RiverGrowthMetrics>>(OnRiverGrow);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
            EventBus.Unsubscribe<TerrainGrowthEvent<RiverGrowthMetrics>>(OnRiverGrow);
        }

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            EventBus.Publish(new TerrainClusterProgressEvent(
                TerrainClusterCategory.Forest, evt.Metrics.LargestClusterSize));
        }

        // クエスト進捗は実タイル数で数える。魚の発生条件（RiverClusterEvent／threshold=8）とは
        // 別系統なので、ここには閾値を持ち込まない
        private void OnRiverGrow(TerrainGrowthEvent<RiverGrowthMetrics> evt)
        {
            EventBus.Publish(new TerrainClusterProgressEvent(
                TerrainClusterCategory.River, evt.Metrics.LargestClusterSize));
        }
    }
}
