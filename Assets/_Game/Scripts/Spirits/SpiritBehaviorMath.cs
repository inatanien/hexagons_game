// 役割: 精霊の行動に関する計算を、副作用のない純粋関数として提供する。
//       ★UnityEngine.Randomをここでは一切使わない。乱数は呼び出し側（ForestSpirit）が
//         生成し、正規化済みの値（0〜1）として渡す。これにより同じ入力なら必ず同じ結果になり、
//         EditModeテストが実行順・乱数状態に依存しなくなる（Stage 9の要件）。
//       不正入力（NaN・Infinity・範囲外・負の長さ）を受けても例外を投げず、
//       必ず安全な値へ丸めてから計算する。

using UnityEngine;

namespace ElfVillage.Spirits
{
    public static class SpiritBehaviorMath
    {
        // ── 各状態の継続時間（秒） ─────────────────────────────────────
        // 最低継続時間を全状態に設けることで、状態が高速に切り替わらないようにする。
        public const float IdleMinDuration        = 2f;
        public const float IdleMaxDuration        = 4f;
        public const float WanderMinDuration      = 3f;
        public const float WanderMaxDuration      = 5f;
        public const float ObserveTreeMinDuration = 3f;
        public const float ObserveTreeMaxDuration = 6f;
        public const float SleepMinDuration       = 6f;
        public const float SleepMaxDuration       = 10f;

        /// <summary>
        /// 次の状態を決める。random01は0〜1の正規化乱数（範囲外・NaNは安全に丸める）。
        /// 定義済みのSpiritStateしか返さない。Sleepへは Idle からのみ入り、Sleepからは Idle へのみ戻る。
        /// </summary>
        public static SpiritState DecideNextState(SpiritState current, float random01)
        {
            float r = Safe01(random01);

            switch (current)
            {
                case SpiritState.Idle:
                    if (r < 0.50f) return SpiritState.Wander;
                    if (r < 0.85f) return SpiritState.ObserveTree;
                    return SpiritState.Sleep;

                case SpiritState.Wander:
                    return r < 0.60f ? SpiritState.Idle : SpiritState.ObserveTree;

                case SpiritState.ObserveTree:
                    return r < 0.70f ? SpiritState.Idle : SpiritState.Wander;

                case SpiritState.Sleep:
                    // 目覚めたら必ずIdleへ戻る（いきなり動き出さない）。
                    return SpiritState.Idle;

                default:
                    // 未定義・不正な状態が渡された場合の安全な既定値。
                    return SpiritState.Idle;
            }
        }

        /// <summary>状態ごとの継続時間。random01で最小〜最大の間を線形に選ぶ。</summary>
        public static float ComputeStateDuration(SpiritState state, float random01)
        {
            float r = Safe01(random01);

            switch (state)
            {
                case SpiritState.Idle:        return Mathf.Lerp(IdleMinDuration,        IdleMaxDuration,        r);
                case SpiritState.Wander:      return Mathf.Lerp(WanderMinDuration,      WanderMaxDuration,      r);
                case SpiritState.ObserveTree: return Mathf.Lerp(ObserveTreeMinDuration, ObserveTreeMaxDuration, r);
                case SpiritState.Sleep:       return Mathf.Lerp(SleepMinDuration,       SleepMaxDuration,       r);
                default:                      return Mathf.Lerp(IdleMinDuration,        IdleMaxDuration,        r);
            }
        }

        /// <summary>
        /// home森の範囲内から目的地を選ぶ。randX01/randZ01は0〜1の正規化乱数。
        /// 戻り値は必ず中心±extentの矩形内に収まる（extentが0や不正値でも中心を返すだけで範囲外にならない）。
        /// Y座標は中心のYをそのまま使う（地面の高さは呼び出し側が決める）。
        /// </summary>
        public static Vector3 PickWanderTarget(Vector3 center, float extentX, float extentZ, float randX01, float randZ01)
        {
            var c  = SafeVector(center);
            float ex = SafeExtent(extentX);
            float ez = SafeExtent(extentZ);

            // 0〜1 を -1〜+1 へ写して中心からのオフセットにする。
            float ox = (Safe01(randX01) * 2f - 1f) * ex;
            float oz = (Safe01(randZ01) * 2f - 1f) * ez;

            return new Vector3(c.x + ox, c.y, c.z + oz);
        }

        /// <summary>
        /// 座標をhome範囲（中心±extent）へ制限する。X/Zのみ制限し、Yはそのまま通す。
        /// </summary>
        public static Vector3 ClampToBounds(Vector3 position, Vector3 center, float extentX, float extentZ)
        {
            var p  = SafeVector(position);
            var c  = SafeVector(center);
            float ex = SafeExtent(extentX);
            float ez = SafeExtent(extentZ);

            float x = Mathf.Clamp(p.x, c.x - ex, c.x + ex);
            float z = Mathf.Clamp(p.z, c.z - ez, c.z + ez);
            return new Vector3(x, p.y, z);
        }

        /// <summary>
        /// Idle時の上下の揺れ量。戻り値の絶対値は必ずamplitude以下に収まる。
        /// </summary>
        public static float ComputeIdleSway(float time, float phase, float amplitude)
        {
            float t = SafeFinite(time);
            float p = SafeFinite(phase);
            float a = Mathf.Abs(SafeFinite(amplitude));
            return Mathf.Sin(t + p) * a;
        }

        /// <summary>
        /// 移動の進行度（0〜1）。時間経過に対して単調増加し、範囲外へ出ない。
        /// 到着時にワープしないよう、開始・終了が滑らかになるイージングを掛けている。
        /// </summary>
        public static float ComputeMoveProgress(float elapsed, float duration)
        {
            float e = SafeFinite(elapsed);
            float d = SafeFinite(duration);

            if (e <= 0f) return 0f;
            if (d <= 0f) return 1f; // 継続時間が0以下なら即到着扱い（0除算を避ける）

            float t = Mathf.Clamp01(e / d);
            return t * t * (3f - 2f * t); // smoothstep（[0,1]で単調増加）
        }

        // ── 入力の安全化 ──────────────────────────────────────────────

        /// <summary>0〜1へ丸める。NaN・Infinityは0として扱う。</summary>
        private static float Safe01(float value)
            => float.IsNaN(value) ? 0f : Mathf.Clamp01(value);

        /// <summary>有限値へ丸める。NaN・Infinityは0として扱う。</summary>
        private static float SafeFinite(float value)
            => float.IsFinite(value) ? value : 0f;

        /// <summary>範囲の半幅は必ず0以上の有限値にする（負値・NaNは0＝その場に留まる）。</summary>
        private static float SafeExtent(float value)
            => float.IsFinite(value) && value > 0f ? value : 0f;

        private static Vector3 SafeVector(Vector3 v)
            => new Vector3(SafeFinite(v.x), SafeFinite(v.y), SafeFinite(v.z));
    }
}
