// 役割: 操作状態に応じて「演出や通知の時間を進めてよいか」を判定する純粋関数（Stage 16）。
//
//       ★なぜCoreに置くか
//         同じ判定を必要とする購読者が2つある。
//           ・Spirits … 精霊のシミュレーション（SpiritSimulationPolicy）
//           ・UI      … マイルストーン通知の表示時間（WorldNoticeUI）
//         UIからSpiritsを参照するとasmdefの依存方向（Tiles + Quest ← UI）が崩れるため、
//         共通の判定はEventBusやGameInteractionStateと同じCoreへ置く。
//         各レイヤーは自分が既に参照しているCoreだけを見ればよく、asmdefは一切変わらない。
//
//       ★このプロジェクトはTime.timeScaleを使っていない
//         PauseMenu / Settings は入力だけを止めており、世界のシミュレーションは
//         止まらない。そのため「止めたい側」がこの判定を見て自分で止める必要がある。

namespace ElfVillage.Core
{
    public static class InteractionTimePolicy
    {
        /// <summary>
        /// この操作状態で、演出・通知・シミュレーションの時間を進めてよいか。
        ///   Playing   … 進める
        ///   PauseMenu … 進める（背景で世界が息づいている方が心地よい）
        ///   Settings  … 止める（ゲーム全体を触っている最中なので世界も止める）
        /// 未知の状態は安全側（止める）へ倒す。新しい操作状態が追加されたとき、
        /// 意図せず進み続けるより、止まっている方が気づきやすく実害も小さいため。
        /// </summary>
        public static bool ShouldAdvanceTime(GameInteractionState state)
        {
            switch (state)
            {
                case GameInteractionState.Playing:   return true;
                case GameInteractionState.PauseMenu: return true;
                case GameInteractionState.Settings:  return false;
                default:                              return false;
            }
        }

        /// <summary>現在の操作状態で時間を進めてよいか（呼び出し側の定型文を減らすための糖衣）。</summary>
        public static bool ShouldAdvanceNow()
            => ShouldAdvanceTime(GameInteractionStateController.Current);
    }
}
