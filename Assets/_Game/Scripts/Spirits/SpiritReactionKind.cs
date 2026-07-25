// 役割: 精霊が見せる小さなリアクションの種類。
//       ObserveTree中のサブ演出（Stage 10）と、外部刺激へのReact（Stage 11）の
//       両方で共通して使う。同じ意味の列挙を2つ持たないよう1つに統一している。

namespace ElfVillage.Spirits
{
    public enum SpiritReactionKind
    {
        /// <summary>少し首を傾げる。</summary>
        TiltHead = 0,

        /// <summary>その場で小さく1回跳ねる。</summary>
        SmallHop = 1,
    }
}
