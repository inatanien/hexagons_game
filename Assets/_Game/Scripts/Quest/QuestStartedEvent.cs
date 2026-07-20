// 役割: クエストが開始されたことを通知するイベント。UIはQuestManagerを直接参照せず、
//       このイベントとQuestDefinition（表示用データ）だけで表示を組み立てられる。

namespace ElfVillage.Quest
{
    public sealed class QuestStartedEvent
    {
        public QuestDefinition Quest { get; }

        public QuestStartedEvent(QuestDefinition quest) => Quest = quest;
    }
}
