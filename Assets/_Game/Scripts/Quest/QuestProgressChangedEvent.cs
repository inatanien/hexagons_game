// 役割: アクティブなクエストの進捗が変化したことを通知するイベント。
//       CurrentCountは0〜Quest.targetCountへクランプ済みの値。

namespace ElfVillage.Quest
{
    public sealed class QuestProgressChangedEvent
    {
        public QuestDefinition Quest        { get; }
        public int             CurrentCount { get; }

        public QuestProgressChangedEvent(QuestDefinition quest, int currentCount)
        {
            Quest        = quest;
            CurrentCount = currentCount;
        }
    }
}
