// 役割: クエストの達成条件の種別。
//       「どのイベントを観測して進捗を数えるか」だけを表し、
//       クエストごとの個別ルールは持たない。
//       questIdごとのif分岐を増やさずにクエストを追加できるようにするための軸。

namespace ElfVillage.Quest
{
    public enum QuestConditionKind
    {
        /// <summary>連結クラスターの規模がtargetCountに達する。
        /// TerrainClusterProgressEvent（Core）を観測する。
        /// 「現在の状態」なので加算せず、到達した最大値を保持する。</summary>
        ClusterSize = 0,

        /// <summary>該当カテゴリのタイルをtargetCount枚置く。
        /// TileCategoryPlacedEvent（Core）を数える。</summary>
        TilePlacedCount = 1,

        /// <summary>eventKeyで指定した出来事がtargetCount回起きる（橋・シナジーなど）。
        /// WorldEventOccurredEvent（Core）を数える。</summary>
        EventOccurrence = 2,

        // ★新しい種別は必ず末尾へ追加すること。
        //   途中へ挿入すると既存アセットのkindがずれる。
    }
}
