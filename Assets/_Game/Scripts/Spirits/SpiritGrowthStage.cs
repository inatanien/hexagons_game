// 役割: 精霊の成長段階（Stage 14）。
//       「世界の出来事を見届けた累積体験」が一定量に達するごとに1つ進む。
//       段階が表すのは綿毛の豊かさだけで、行動・能力・体色は一切変えない。
//
//       ★enum値を明示する理由
//         GrowthStage自体はセーブ対象ではない（LifetimeExperienceから導出する）が、
//         段階の大小比較と ResolveGrowthTransition の +1 演算が数値順に依存している。
//         宣言順が入れ替わると「成長が後退しない」保証が静かに壊れるため、値を固定する。
//         既存値は将来変更しないこと。段階を増やす場合は末尾へ足す。

namespace ElfVillage.Spirits
{
    public enum SpiritGrowthStage
    {
        /// <summary>生まれたて。綿毛が薄く、まだ芯が見える。</summary>
        Sprout = 0,

        /// <summary>綿毛がしっかりしてきた（Stage 13までの見た目に相当）。</summary>
        Fluff = 1,

        /// <summary>綿毛が満ちている。</summary>
        Bloom = 2,
    }
}
