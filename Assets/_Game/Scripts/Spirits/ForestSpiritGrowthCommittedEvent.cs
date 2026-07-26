// 役割: 森の精霊の成長段階が「確定した瞬間」を伝えるイベント（Stage 16）。
//
//       ★発行タイミングは成長演出の頂点（midpoint commit）のちょうど1回だけ
//         Stage 14で、段階の確定と見た目の適用は UpdateGrowthFlourish の p>=0.5 という
//         同一地点で行われ、_growthAppliedThisFlourish が二重適用を防いでいる。
//         このイベントも同じ地点・同じガードの内側で発行するため、
//           ・頂点前に中断された場合は発行されない（段階も未確定のまま）
//           ・頂点後に中断されても再発行されない
//           ・1段階につき1回、1回のIdle滞在につき1回
//         というStage 14の保証がそのまま引き継がれる。
//
//       ForestSpirit参照は持たない（ForestSpiritSpawnedEventと同じ理由）。

using UnityEngine;

namespace ElfVillage.Spirits
{
    public sealed class ForestSpiritGrowthCommittedEvent
    {
        /// <summary>成長した位置（演出とVFXの発生点）。</summary>
        public Vector3 WorldPosition { get; }

        public SpiritGrowthStage PreviousStage { get; }
        public SpiritGrowthStage NewStage      { get; }
        public SpiritPersonalityKind Personality { get; }

        public ForestSpiritGrowthCommittedEvent(Vector3 worldPosition,
                                                 SpiritGrowthStage previousStage,
                                                 SpiritGrowthStage newStage,
                                                 SpiritPersonalityKind personality)
        {
            WorldPosition = worldPosition;
            PreviousStage = previousStage;
            NewStage      = newStage;
            Personality   = personality;
        }
    }
}
