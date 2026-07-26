// 役割: 森の精霊が新しく生まれたことを伝えるイベント（Stage 16）。
//
//       ★ForestSpirit参照をpayloadへ入れない
//         購読側（演出・通知・音）が必要とするのは「どこで・どんな子が生まれたか」だけ。
//         MonoBehaviour参照を配ると、破棄済みオブジェクトへのアクセス、購読側での寿命管理、
//         テストでの後始末がすべて難しくなる。値だけを渡すことで、
//         イベントが飛んだ後に精霊が破棄されても購読側は安全に扱える。
//
//       ★「Spawned（今生まれた）」という名前の意味
//         将来セーブ・ロードを実装したとき、復元された精霊に対してこのイベントを
//         発行してはいけない（誕生演出が再生されてしまうため）。
//         名前でその意図を明示している。

using UnityEngine;

namespace ElfVillage.Spirits
{
    public sealed class ForestSpiritSpawnedEvent
    {
        /// <summary>生まれた位置（演出とVFXの発生点）。</summary>
        public Vector3 WorldPosition { get; }

        public SpiritPersonalityKind Personality { get; }

        /// <summary>生まれた時点の成長段階（通常はSprout）。</summary>
        public SpiritGrowthStage Stage { get; }

        public ForestSpiritSpawnedEvent(Vector3 worldPosition, SpiritPersonalityKind personality,
                                         SpiritGrowthStage stage)
        {
            WorldPosition = worldPosition;
            Personality   = personality;
            Stage         = stage;
        }
    }
}
