// 役割: クエストが始まり、盤面のどこを見るかが決まったことを通知するイベント。
//       QuestManagerが開始時に発行する。
//
//       ★受け取り側（Tiles）は、これを「ここから数え直す」合図として使う。
//         クエスト開始前に置かれたタイルを達成の祝いに混ぜないために必要で、
//         「達成イベントの直前に届いたものが対象だろう」という暗黙の対応に頼らずに済む。

namespace ElfVillage.Core
{
    public sealed class QuestFocusStartedEvent
    {
        public QuestFocus Focus { get; }

        public QuestFocusStartedEvent(QuestFocus focus) => Focus = focus;
    }
}
