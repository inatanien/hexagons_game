// 役割: Playing / PauseMenu / Settings の現在状態を一元管理する静的クラス。
//       EventBus と同じく状態を持たないシステム間の共有点として Core に置き、
//       Tiles（HexGridManager）・UI（PauseMenuController等）の両方から直接参照できるようにする。

namespace ElfVillage.Core
{
    public static class GameInteractionStateController
    {
        public static GameInteractionState Current { get; private set; } = GameInteractionState.Playing;

        public static void SetState(GameInteractionState next)
        {
            if (Current == next) return;
            var previous = Current;
            Current = next;
            EventBus.Publish(new GameInteractionStateChangedEvent(previous, next));
        }
    }
}
