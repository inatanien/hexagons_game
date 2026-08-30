// 役割: 1つのクエストの内容を定義するScriptableObject。
//       進捗判定・達成判定はQuestManagerが行い、このアセットはデータのみを保持する。
//       達成条件はQuestCondition（1クエスト1条件）へ集約してあり、
//       クエストを増やしてもコード側にquestIdごとの分岐は生まれない。

using UnityEngine;

namespace ElfVillage.Quest
{
    [CreateAssetMenu(fileName = "Quest_", menuName = "ElfVillage/QuestDefinition")]
    public class QuestDefinition : ScriptableObject
    {
        [Tooltip("UI表示用のタイトル（例: \"森を育てよう\"）")]
        public string title;

        [Tooltip("UI表示用の説明文（任意）")]
        [TextArea]
        public string description;

        [Tooltip("達成条件。1クエストにつき1条件（複数条件のANDは対象外）")]
        public QuestCondition condition = new QuestCondition();

        [Tooltip("達成時にQuestRewardSystemが発行する報酬ID（例: \"forest_unlock_birds\"）。" +
                  "空の場合は報酬なしクエストとして扱う。")]
        public string rewardId;

        /// <summary>
        /// 達成に必要な数。UI等がQuestConditionの内部構造を知らずに参照するための糖衣。
        /// conditionが未設定のときは0を返す（QuestManagerがそのクエストを開始しないため、
        /// UIがこの値で進捗を描くことはない）。
        /// </summary>
        public int TargetCount => condition != null ? condition.targetCount : 0;
    }
}
