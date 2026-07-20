// 役割: Tiles側の詳細な成長イベント（TerrainGrowthEvent<ForestGrowthMetrics>等）を、
//       CoreのTerrainClusterProgressEventへ変換して再発行する中継コンポーネント。
//       Quest等、Coreのみに依存したいシステムが、Tiles固有の型を一切知らずに
//       クラスター進捗を購読できるようにするための翻訳役。
//       特定のシステム（WorldBreathSystem等）へは一切依存しない、完全に独立したコンポーネント。
//       Stage 1ではForest（TerrainGrowthEvent<ForestGrowthMetrics>）のみを中継する。
//       FlowerClusterEventの中継は未実装（FlowerClusterEvaluatorが閾値未満では発行しないため、
//       0→1→2のような常時進捗通知には使えない。花クエストを追加する段階でVFX用イベントと
//       常時進捗通知の責務を改めて検討する）。

using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class TerrainClusterProgressRelay : MonoBehaviour
    {
        private void OnEnable()  => EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
        private void OnDisable() => EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            EventBus.Publish(new TerrainClusterProgressEvent(
                TerrainClusterCategory.Forest, evt.Metrics.LargestClusterSize));
        }
    }
}
