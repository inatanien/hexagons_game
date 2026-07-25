// 役割: 精霊向けの刺激をEventBusへ流すための内部イベント（Spiritsアセンブリ内で完結）。
//       SpiritStimulusRelayが発行し、各精霊が自分で購読して「自分に関係する刺激か」を判断する。
//       Relayが精霊を検索したり直接呼び出したりしないため、
//       精霊の生成・管理の責務はRelayへ漏れない。

namespace ElfVillage.Spirits
{
    public sealed class SpiritStimulusEvent
    {
        public SpiritStimulus Stimulus { get; }

        public SpiritStimulusEvent(SpiritStimulus stimulus) => Stimulus = stimulus;
    }
}
