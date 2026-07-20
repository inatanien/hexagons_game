// 役割: クエストが達成されたことを通知するイベント（1クエストにつき1回だけ発行される）。
//       将来の報酬システム・精霊システム等は、このイベントを購読するだけで拡張できる。

namespace ElfVillage.Quest
{
    public sealed class QuestCompletedEvent
    {
        public QuestDefinition Quest { get; }

        public QuestCompletedEvent(QuestDefinition quest) => Quest = quest;
    }
}
