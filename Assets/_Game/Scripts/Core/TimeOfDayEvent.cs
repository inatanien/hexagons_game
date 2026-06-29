// 役割: 時間帯が切り替わるタイミングで発行されるイベント。
//       他システムが昼夜に応じた挙動を取るために購読する。

namespace ElfVillage.Core
{
    public sealed class TimeOfDayEvent
    {
        public enum Phase { Morning, Afternoon, Evening, Night }

        public Phase Current { get; }

        public TimeOfDayEvent(Phase current) => Current = current;
    }
}
