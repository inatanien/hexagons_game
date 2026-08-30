// 役割: Tiles固有のイベントを、Questから利用できるCoreのイベントへ翻訳して再発行する中継コンポーネント。
//       TerrainClusterProgressRelay（成長の進捗を翻訳する）と同じ考え方で、
//       こちらは「出来事」と「タイル配置」を翻訳する。
//
//       翻訳規則:
//         RiverBridgeEvent      → WorldEventOccurredEvent(WorldEventKeys.Bridge)
//         TerrainSynergyEvent   → WorldEventOccurredEvent(WorldEventKeys.Synergy(SynergyId))
//         TilePlacedEvent       → TileCategoryPlacedEvent（タイルが持つカテゴリごとに1回）
//
//       ★盤面は一切見ない。HexGridManagerへの参照も持たない。
//         森・川の判定やクラスター計算は既存の評価システムの責務であり、
//         ここは受け取った結果の語彙を変えるだけ。
//       ★購読と解除は必ず対にすること。片方だけ足すと購読が残って多重通知になる。

using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class WorldEventRelay : MonoBehaviour
    {
        private void OnEnable()
        {
            EventBus.Subscribe<RiverBridgeEvent>(OnBridge);
            EventBus.Subscribe<TerrainSynergyEvent>(OnSynergy);
            EventBus.Subscribe<TilePlacedEvent>(OnTilePlaced);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<RiverBridgeEvent>(OnBridge);
            EventBus.Unsubscribe<TerrainSynergyEvent>(OnSynergy);
            EventBus.Unsubscribe<TilePlacedEvent>(OnTilePlaced);
        }

        private void OnBridge(RiverBridgeEvent evt)
        {
            EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
        }

        private void OnSynergy(TerrainSynergyEvent evt)
        {
            string key = WorldEventKeys.Synergy(evt.SynergyId);
            // SynergyIdはInspectorへ手入力された文字列。未入力のまま "synergy:" だけの
            // 無意味なキーを流すと、どのシナジークエストにも一致しないイベントが増えるだけなので翻訳しない
            if (string.IsNullOrEmpty(key)) return;

            EventBus.Publish(new WorldEventOccurredEvent(key));
        }

        private void OnTilePlaced(TilePlacedEvent evt)
        {
            if (evt.TileType == null) return;

            // ★情報源はGetEffectiveCategories（＝HasCategoryと同じ、ゲームプレイ用の判定）。
            //   visualOnly要素とlandDecorationは含まれないため、
            //   見た目だけの花や木が「畑タイルを置いた」として数えられることはない。
            //   同じカテゴリの要素が複数あってもこのメソッドが重複排除するので、
            //   1枚のタイルにつき同じカテゴリのイベントは1回だけになる。
            foreach (var category in evt.TileType.GetEffectiveCategories())
            {
                if (!TryToCoreCategory(category, out var coreCategory)) continue;
                EventBus.Publish(new TileCategoryPlacedEvent(coreCategory));
            }
        }

        /// <summary>
        /// TilesのTileCategoryをCoreのTerrainClusterCategoryへ変換する。
        /// 対応表をTiles側へ置くのは、CoreがTileCategoryを知ってはいけないため。
        /// Road / Villageは現在Core側に対応する値がないので翻訳しない。
        /// </summary>
        private static bool TryToCoreCategory(TileCategory category, out TerrainClusterCategory result)
        {
            switch (category)
            {
                case TileCategory.Forest: result = TerrainClusterCategory.Forest; return true;
                case TileCategory.Field:  result = TerrainClusterCategory.Field;  return true;
                case TileCategory.River:  result = TerrainClusterCategory.River;  return true;
                default:                  result = default;                       return false;
            }
        }
    }
}
