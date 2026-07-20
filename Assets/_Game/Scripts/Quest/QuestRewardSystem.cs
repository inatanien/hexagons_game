// 役割: クエスト達成（QuestCompletedEvent）を「世界の変化」へ繋ぐ最小限の報酬システム。
//       QuestManagerを直接参照せず、EventBus経由のQuestCompletedEventだけを購読する。
//       Stage 4時点ではrewardId="forest_unlock_birds"のみ対応し、実際の鳥の生成等はまだ行わない。
//       未対応のrewardId（空文字含む）は何もしない。
//       同じrewardIdは一度だけ解放する。QuestManager側でQuestCompletedEventは1クエストにつき
//       1回だけ発行される設計だが、複数クエストが同じrewardIdを指す場合や将来の変更に備えて、
//       このシステム自身でも解放済みrewardIdを記憶し、二重解放（RewardUnlockedEventの重複発行）
//       を防ぐ。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Quest
{
    public class QuestRewardSystem : MonoBehaviour
    {
        private readonly HashSet<string> _unlockedRewardIds = new HashSet<string>();

        private void OnEnable()
        {
            EventBus.Subscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<QuestCompletedEvent>(OnQuestCompleted);
        }

        private void OnQuestCompleted(QuestCompletedEvent evt)
        {
            string rewardId = evt.Quest.rewardId;
            if (string.IsNullOrEmpty(rewardId)) return;
            if (_unlockedRewardIds.Contains(rewardId)) return;

            switch (rewardId)
            {
                case "forest_unlock_birds":
                    Debug.Log("Reward Unlocked: Birds");
                    break;

                default:
                    return; // Stage 4では未対応のrewardId
            }

            _unlockedRewardIds.Add(rewardId);
            EventBus.Publish(new RewardUnlockedEvent(rewardId));
        }
    }
}
