// 役割: 一連のクエスト（QuestSequenceDefinition）を最後までやり遂げたことを通知するイベント。
//       QuestSequenceRunnerが1つのSequenceにつき1回だけ発行する。
//
//       ★追加するSequence用イベントはこれだけにしてある。
//         開始は1本目のQuestStartedEventで分かり、「何問目か」を表示するUIもまだ無いため、
//         QuestSequenceStartedEvent / QuestSequenceProgressEventは必要になってから足す。

namespace ElfVillage.Quest
{
    public sealed class QuestSequenceCompletedEvent
    {
        public QuestSequenceDefinition Sequence { get; }

        public QuestSequenceCompletedEvent(QuestSequenceDefinition sequence) => Sequence = sequence;
    }
}
