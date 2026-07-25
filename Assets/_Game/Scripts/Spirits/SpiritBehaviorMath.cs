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
        public const float StretchMinDuration     = 1.0f;
        public const float StretchMaxDuration     = 1.5f;
        public const float ReactMinDuration       = 1.0f;
        public const float ReactMaxDuration       = 2.0f;

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
                    // 目覚めたら必ず伸びをしてからIdleへ戻る（いきなり動き出さない）。
                    return SpiritState.Stretch;

                case SpiritState.Stretch:
                    // 伸びの後は必ずIdleへ。StretchへはSleepからしか入らない一方通行。
                    return SpiritState.Idle;

                case SpiritState.React:
                    // 反応し終えたら必ずIdleへ戻る（中断前のWanderへは復帰しない）。
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
                case SpiritState.Stretch:     return Mathf.Lerp(StretchMinDuration,     StretchMaxDuration,     r);
                case SpiritState.React:       return Mathf.Lerp(ReactMinDuration,       ReactMaxDuration,       r);
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

        // ══ Stage 10: 演出用の計算 ═══════════════════════════════════════

        /// <summary>
        /// 起床時の「伸び」のスケール倍率（Visualルートへ乗算する）。
        /// 前半は縦に伸びて横が縮み、後半は横へふわっと広がってから元へ戻る。
        /// progress 0 と 1 では必ず (1,1,1) を返すため、途中終了しても変形が残らない。
        /// ゴム・液体的に見えないよう変形量は控えめ（既定で最大±約12%）。
        /// </summary>
        public static Vector3 ComputeStretchScale(float progress, float intensity = 0.12f)
        {
            float p = Safe01(progress);
            float k = Mathf.Abs(SafeFinite(intensity));

            // sin(2πp) は 0 → +1 → 0 → -1 → 0 と連続に変化する。
            // 前半(0〜0.5)は正で縦に伸び横が縮み、後半(0.5〜1)は負に転じて横へ広がる。
            // 中間で符号を切り替える実装だとスケールが不連続に飛んで「カクッ」と見えるため、
            // 1本の連続した波で表現している。p=0,0.5,1 では必ず0になり変形が残らない。
            float wave = Mathf.Sin(p * Mathf.PI * 2f);

            float vertical   = 1f + k * wave;
            float horizontal = 1f - k * wave * 0.6f; // 横は縦より控えめに反応させる

            return new Vector3(horizontal, vertical, horizontal);
        }

        /// <summary>
        /// 移動中の跳ねによるY方向オフセット。
        /// progress 0 と 1 で必ず0を返すため、状態が終わるたびにY座標が蓄積しない。
        /// 戻り値は必ず 0 以上 hopHeight 以下。
        /// </summary>
        public static float ComputeHopOffset(float progress, int hopCount, float hopHeight)
        {
            float p = Safe01(progress);
            float h = SafeFinite(hopHeight);
            if (h <= 0f) return 0f;

            // 不正なhopCountは安全な既定値へ倒す（0以下・極端に大きい値を弾く）。
            int count = (hopCount >= 1 && hopCount <= 20) ? hopCount : 2;

            // |sin| をcount回繰り返すことで、count回の跳ねになる。
            // p=0,1 では sin(0)=sin(count*PI)=0 なので必ず接地する。
            float wave = Mathf.Abs(Mathf.Sin(p * Mathf.PI * count));

            // 移動の始点・終点付近では跳ねを弱め、helper的に自然な立ち上がりにする。
            float envelope = Mathf.Sin(p * Mathf.PI);

            float offset = h * wave * envelope;
            return Mathf.Clamp(offset, 0f, h);
        }

        /// <summary>
        /// ObserveTree中に1回だけ行う小さなリアクションの種類を選ぶ。
        /// random01は0〜1の正規化乱数（不正値は安全に丸める）。定義済みの種類しか返さない。まずは50%ずつ。
        /// </summary>
        public static SpiritReactionKind PickObserveReaction(float random01)
            => Safe01(random01) < 0.5f ? SpiritReactionKind.TiltHead : SpiritReactionKind.SmallHop;

        /// <summary>
        /// リアクションの進行度（0〜1）に対する首の傾き角（度）。
        /// 開始・終了で必ず0になるため、状態終了時に傾きが残らない。
        /// </summary>
        public static float ComputeTiltAngle(float progress, float maxAngleDeg)
        {
            float p = Safe01(progress);
            float a = SafeFinite(maxAngleDeg);
            return Mathf.Sin(p * Mathf.PI) * a;
        }

        // ══ Stage 11: 世界からの刺激への反応 ═════════════════════════════

        /// <summary>
        /// 刺激の優先度。刺激データ側には持たせず、ここへ一元化する
        /// （呼び出し側が勝手な優先度を注入できないようにするため）。
        /// 未知の種類は0を返し、割り込みが起きないようにする。
        /// </summary>
        public static int GetStimulusPriority(SpiritStimulusKind kind)
        {
            switch (kind)
            {
                case SpiritStimulusKind.ForestGrew:    return 1;
                case SpiritStimulusKind.FlowerBloomed: return 1;
                default:                                return 0; // 未知の刺激は無視する
            }
        }

        /// <summary>
        /// 割り込んでよいか。より高い優先度のときだけ割り込める。
        /// 同じ優先度では割り込まないため、同種の刺激が連続してもReactが再開始されない。
        /// 反応していない通常状態のcurrentPriorityは0として渡す。
        /// </summary>
        public static bool ShouldInterrupt(int currentPriority, int incomingPriority)
            => incomingPriority > 0 && incomingPriority > currentPriority;

        /// <summary>
        /// 外部刺激で中断してよい状態か。SleepとStretchは中断しない
        /// （眠っている最中や伸びの途中に反応すると不自然なため）。
        /// </summary>
        public static bool CanBeInterruptedByStimulus(SpiritState state)
            => state == SpiritState.Idle
            || state == SpiritState.Wander
            || state == SpiritState.ObserveTree
            || state == SpiritState.React;

        /// <summary>
        /// 刺激を知覚できる距離内か。水平（X/Z）距離で判定し、Y差の影響を受けない
        /// （精霊は少し浮いており、タイルは地面にあるため、Yを含めると不自然に無視されてしまう）。
        /// NaN・Infinityを含む座標や不正な半径はfalse（＝知覚しない）で安全に拒否する。
        /// </summary>
        public static bool IsWithinPerception(Vector3 spiritPosition, Vector3 stimulusPosition, float radius)
        {
            if (!IsFinite(spiritPosition) || !IsFinite(stimulusPosition)) return false;
            if (!float.IsFinite(radius) || radius <= 0f) return false;

            float dx = stimulusPosition.x - spiritPosition.x;
            float dz = stimulusPosition.z - spiritPosition.z;
            return (dx * dx + dz * dz) <= radius * radius;
        }

        /// <summary>
        /// 刺激の種類に対応するリアクション。Stage 11では固定対応にして、
        /// 見た目から刺激の意味が読み取れるようにする
        /// （将来Personalityで確率的に変える余地は、この関数を差し替えるだけで残る）。
        /// </summary>
        public static SpiritReactionKind PickReactionFor(SpiritStimulusKind kind)
        {
            switch (kind)
            {
                case SpiritStimulusKind.ForestGrew:    return SpiritReactionKind.SmallHop;
                case SpiritStimulusKind.FlowerBloomed: return SpiritReactionKind.TiltHead;
                default:                                return SpiritReactionKind.TiltHead;
            }
        }

        private static bool IsFinite(Vector3 v)
            => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        // ══ Stage 12: 記憶（見慣れ度）═══════════════════════════════════

        /// <summary>
        /// 見慣れ度の指数減衰。halfLifeSeconds経過するごとに半分になる。
        /// 経過0なら値は変わらず、経過が増えるほど単調非増加。負値は返さない。
        /// 不正な入力（NaN・Infinity・負の経過時間・0以下の半減期）は安全に処理する。
        /// </summary>
        public static float ComputeDecayedFamiliarity(float current, float elapsedSeconds, float halfLifeSeconds)
        {
            float c = SafeFinite(current);
            if (c <= 0f) return 0f;

            float e = SafeFinite(elapsedSeconds);
            if (e <= 0f) return c; // 経過0以下（負の経過含む）では減衰させない

            // 半減期が不正なら減衰させない（記憶が突然消えるより据え置きの方が安全）
            if (!float.IsFinite(halfLifeSeconds) || halfLifeSeconds <= 0f) return c;

            float decayed = c * Mathf.Pow(0.5f, e / halfLifeSeconds);
            return float.IsFinite(decayed) && decayed > 0f ? decayed : 0f;
        }

        /// <summary>
        /// 体験1回ぶんの加算。上限を超えず、負値も返さない。
        /// 負のgainでは記憶が減らない（0として扱う）。
        /// </summary>
        public static float ComputeFamiliarityGain(float current, float gain, float maximum)
        {
            float max = SafeFinite(maximum);
            if (max <= 0f) return 0f;

            float c = Mathf.Clamp(SafeFinite(current), 0f, max);
            float g = SafeFinite(gain);
            if (g < 0f) g = 0f; // 負のgainで記憶を減らさない

            return Mathf.Clamp(c + g, 0f, max);
        }

        /// <summary>
        /// 見慣れ度からリアクションの強さ（0より大きく1以下）を求める。
        /// 見慣れていないほど1に近く、見慣れるほどminimumScaleに近づく（単調非増加）。
        /// 完全に0にはしない＝見慣れても小さな反応は必ず残る（Stage 12の方針）。
        /// </summary>
        public static float ComputeReactionScale(float familiarity, float maximumFamiliarity, float minimumScale)
        {
            // minimumScaleは0より大きい値に丸める（完全無視を作らないため）
            float min = SafeFinite(minimumScale);
            if (!(min > 0f)) min = 0.01f;
            if (min > 1f) min = 1f;

            float max = SafeFinite(maximumFamiliarity);
            if (max <= 0f) return 1f; // 上限が無効なら「まだ見慣れていない」扱い

            float f = Mathf.Clamp(SafeFinite(familiarity), 0f, max);
            float t = f / max; // 0（未経験）〜1（完全に見慣れた）

            return Mathf.Lerp(1f, min, t);
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
