// 役割: クエスト達成（QuestCompletedEvent）を「世界の変化」へ繋ぐ最小限の報酬システム。
//       QuestManagerを直接参照せず、EventBus経由のQuestCompletedEventだけを購読する。
//
//       ★rewardIdの内容は一切解釈しない。どの報酬に反応するかは受信側の責務
//         （鳥ならBirdRewardSpawnerが自分で判断する）。
//         以前はここでrewardIdをswitchし、未知のIDを黙って捨てていた。
//         そのため新しいクエストを作るたびにcaseの追加を忘れると
//         「達成したのに何も起きない」という原因の分かりにくい不具合になっていた。
//
//       rewardIdが空（null / 空文字 / 空白のみ）のクエストは「報酬なしクエスト」として扱い、
//       RewardUnlockedEventを発行しない。達成の手応えだけで完結するクエストがあってよい。
//       QuestCompletedEvent自体は報酬の有無に関わらず発行されるので、UIの達成演出は変わらない。
//
//       同じ報酬は一度だけ解放する。QuestManager側でQuestCompletedEventは1クエストにつき
//       1回だけ発行される設計だが、複数クエストが同じrewardIdを指す場合や将来の変更に備えて、
//       このシステム自身でも解放済みrewardIdを記憶し、二重解放を防ぐ。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Quest
{
    public class QuestRewardSystem : MonoBehaviour
    {
        /// <summary>解放済みの報酬ID。前後の空白を落とした正規化済みの文字列だけを入れる。</summary>
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
            if (evt.Quest == null) return;

            string rewardId = evt.Quest.rewardId;

            // 報酬なしクエスト。空白だけのIDも同じ扱いにする
            // （SOへ誤ってスペースが入っただけで「報酬あり」と判定させない）
            if (string.IsNullOrWhiteSpace(rewardId)) return;

            // rewardIdは表示文字列ではなく識別子なので、前後の空白は意味を持たない。
            // 記録も発行も正規化後の文字列だけを使う。
            // 正規化前を使うと "birds" と "birds " が別IDとして二重に解放され、
            // 受信側の完全一致にも失敗する（SOの入力ミスが原因の分かりにくい不具合になる）
            rewardId = rewardId.Trim();

            // Addは追加できたときだけtrueを返す。記録と重複判定を1文にして、
            // 「発行したのに記録し忘れる」経路が生まれないようにする
            if (!_unlockedRewardIds.Add(rewardId)) return;

            EventBus.Publish(new RewardUnlockedEvent(rewardId));
        }
    }
}
