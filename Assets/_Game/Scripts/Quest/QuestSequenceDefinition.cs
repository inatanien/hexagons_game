// 役割: 複数のクエストを出す順番だけを保持するScriptableObject。
//       「次はどれか」を知っているのはこのアセットとQuestSequenceRunnerだけで、
//       QuestDefinition側にnextQuestのような繋がりは持たせない
//       （同じクエストを別の並びで使い回せなくなるため）。
//
//       今回は直列のみ。分岐・ランダム・並列・選択式は対象外で、
//       必要になった時点で別の仕組みとして設計する。

using UnityEngine;

namespace ElfVillage.Quest
{
    [CreateAssetMenu(fileName = "QuestSequence_", menuName = "ElfVillage/QuestSequenceDefinition")]
    public class QuestSequenceDefinition : ScriptableObject
    {
        [Tooltip("この順番に1本ずつ出題する。空欄や不正なクエストは飛ばして次へ進む")]
        public QuestDefinition[] quests;
    }
}
