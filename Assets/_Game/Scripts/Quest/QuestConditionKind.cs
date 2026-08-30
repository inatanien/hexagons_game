// 役割: クエストの達成条件の種別。
//       「どのイベントを観測して進捗を数えるか」だけを表し、
//       クエストごとの個別ルールは持たない。
//       questIdごとのif分岐を増やさずにクエストを追加できるようにするための軸。

namespace ElfVillage.Quest
{
    public enum QuestConditionKind
    {
        /// <summary>連結クラスターの規模がtargetCountに達する。
        /// TerrainClusterProgressEvent（Core）を観測する。</summary>
        ClusterSize = 0,

        // 追加予定（Step 3）:
        //   TilePlacedCount = 1   該当タイルをN枚置く
        //   EventOccurrence = 2   特定の出来事がN回起きる（橋・シナジー）
        // ★必ず末尾へ追加すること。途中へ挿入すると既存アセットのkindがずれる。
    }
}
