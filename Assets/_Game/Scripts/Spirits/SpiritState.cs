// 役割: 精霊の行動状態。Stage 9のプロトタイプではこの4状態のみを扱う。

namespace ElfVillage.Spirits
{
    public enum SpiritState
    {
        /// <summary>その場で小さく揺れながら待機する。</summary>
        Idle = 0,

        /// <summary>home森の範囲内で目的地を選び、ゆっくり移動する。</summary>
        Wander = 1,

        /// <summary>home森のタイル付近へ移動し、一定時間そのタイル（＝木々）を眺める。</summary>
        ObserveTree = 2,

        /// <summary>
        /// その場で丸くなって動きを抑える休眠状態。
        /// ★Stage 9時点では「夜になったから眠る」ではなく、昼夜条件に依存しない
        ///   暫定的な休眠として実装している。将来TimeOfDayEventを導入する際に
        ///   「夜の睡眠」へ接続できるよう、状態名と遷移だけ先に用意してある。
        /// </summary>
        Sleep = 3,

        /// <summary>
        /// 起床直後の「伸び」。Sleepからのみ入り、Idleへのみ戻る短い状態（Stage 10）。
        /// 水平移動は行わず、Visualルートのスケール変形だけで表現する。
        /// </summary>
        Stretch = 4,

        /// <summary>
        /// 世界からの刺激（森の成長・花の開花など）への反応（Stage 11）。
        /// Idle / Wander / ObserveTree からのみ入り、Idleへのみ戻る。
        /// Sleep / Stretch は外部刺激で中断されない。
        /// 水平移動は行わず、刺激の方向を向いて小さなリアクションを1回見せるだけ。
        /// </summary>
        React = 5,
    }
}
