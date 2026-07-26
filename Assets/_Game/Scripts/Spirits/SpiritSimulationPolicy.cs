// 役割: 精霊のシミュレーションを進めてよい操作状態かを判定する純粋関数（Stage 15）。
//
//       ★「ゲーム全体を止めるゲート」にはしない
//         このプロジェクトは Time.timeScale を一切使っておらず、PauseMenu / Settings は
//         入力だけを止めている（HexGridManager はタイル配置を、CameraController は
//         Settings 中のカメラ操作を止める）。既存の Critter 系（Butterfly / Firefly /
//         RewardBird 等）は操作状態を参照せず動き続ける。
//         そこへ全体停止の仕組みを持ち込むと影響範囲が読めないため、
//         Stage 15 では「精霊だけをどう扱うか」に限定した判定にしている。
//
//       ★静的な時計を持たない
//         停止可能な時刻は ForestSpirit が個体ごとに保持する。
//         ここに静的な時計を置くと「誰が進めるのか」が曖昧になり、
//         複数体になったときに時間が倍速で進む危険がある。

using ElfVillage.Core;

namespace ElfVillage.Spirits
{
    public static class SpiritSimulationPolicy
    {
        /// <summary>
        /// この操作状態で精霊のシミュレーションを進めてよいか。
        ///   Playing   … 進める
        ///   PauseMenu … 進める（背景で世界が息づいている方が心地よく、既存Critterとも揃う）
        ///   Settings  … 止める（ゲーム全体を触っている最中なので世界も止める）
        /// 未知の状態は安全側（止める）に倒す。新しい操作状態が追加されたとき、
        /// 精霊が意図せず動き続けるより、止まっている方が気づきやすく実害も小さいため。
        /// </summary>
        public static bool ShouldSimulate(GameInteractionState state)
        {
            switch (state)
            {
                case GameInteractionState.Playing:   return true;
                case GameInteractionState.PauseMenu: return true;
                case GameInteractionState.Settings:  return false;
                default:                              return false;
            }
        }
    }
}
