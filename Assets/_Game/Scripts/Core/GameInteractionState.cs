// 役割: ゲーム全体の入力・UI状態を表す列挙型と、状態変化を通知するイベント。

namespace ElfVillage.Core
{
    public enum GameInteractionState
    {
        Playing,
        PauseMenu,
        Settings,
    }

    public sealed class GameInteractionStateChangedEvent
    {
        public GameInteractionState Previous { get; }
        public GameInteractionState Current  { get; }

        public GameInteractionStateChangedEvent(GameInteractionState previous, GameInteractionState current)
        {
            Previous = previous;
            Current  = current;
        }
    }
}
