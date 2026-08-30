// 役割: クエスト達成を「世界の見た目で祝う」ための合図。
//       QuestManagerがQuestCompletedEventに続けて発行する。
//
//       ★Focusを一緒に運ぶのが要点。
//         受け取り側は「どのタイル集合を祝えばよいか」をこの情報から選ぶので、
//         「達成の直前に届いたイベントのタイルだろう」という暗黙の推測に依存しない。
//       ★タイルへの参照は運ばない。Quest層は盤面を知らないままにしておく。

namespace ElfVillage.Core
{
    public sealed class QuestCelebrationEvent
    {
        public QuestFocus Focus { get; }

        /// <summary>達成したクエストの表示名（ログ・将来の演出文言用）。</summary>
        public string QuestTitle { get; }

        public QuestCelebrationEvent(QuestFocus focus, string questTitle)
        {
            Focus      = focus;
            QuestTitle = questTitle ?? string.Empty;
        }
    }
}
