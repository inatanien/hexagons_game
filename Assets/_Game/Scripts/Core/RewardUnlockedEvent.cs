// 役割: 報酬（rewardId）が解放されたことを通知するイベント。精霊・鳥・音楽・天候など、
//       今後さまざまなシステムがQuestやQuestManagerを一切知らずに、このイベントだけを
//       購読して「世界の変化」を実装できるようにする。

namespace ElfVillage.Core
{
    public sealed class RewardUnlockedEvent
    {
        public string RewardId { get; }

        public RewardUnlockedEvent(string rewardId) => RewardId = rewardId;
    }
}
