// 役割: 1つのクエストの内容を定義するScriptableObject。
//       進捗判定・達成判定はQuestManagerが行い、このアセットはデータのみを保持する。

using UnityEngine;
using ElfVillage.Core;

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

        [Tooltip("進捗判定の対象カテゴリ")]
        public TerrainClusterCategory targetCategory;

        [Tooltip("達成に必要なクラスターサイズ。0以下は不正値として扱い、QuestManagerは" +
                  "このクエストを開始しない（警告ログを出す）")]
        public int targetCount = 5;

        [Tooltip("達成時にQuestRewardSystemが解釈する報酬ID（例: \"forest_unlock_birds\"）。" +
                  "空文字の場合は報酬なし。")]
        public string rewardId;
    }
}
