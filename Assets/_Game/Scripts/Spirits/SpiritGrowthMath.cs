// 役割: 成長に関する計算を、副作用のない純粋関数として提供する（Stage 14）。
//       ★SpiritBehaviorMathへ足さず別クラスにしている理由
//         SpiritBehaviorMathは既に500行を超えており、CLAUDE.mdの「200行を超えたら分割を検討」
//         を大きく上回っている。これ以上肥大させないため、成長は独立した責務として分ける。
//         （SpiritBehaviorMath自体の分割はStage 14では行わない。）
//
//       Unity Objectを一切参照せず、副作用もアロケーションも起こさない。
//       不正入力（NaN・Infinity・負値・逆転した閾値・未知の段階）でも例外を投げず、
//       必ず安全な値へ丸めてから計算する。

using UnityEngine;

namespace ElfVillage.Spirits
{
    /// <summary>
    /// 段階ごとの見た目パラメータ。structなので戻り値でアロケーションが起きない。
    /// ★ここで表現するのは綿毛だけ。体色・Body・目・当たり判定・移動範囲には一切関与しない。
    /// </summary>
    public readonly struct SpiritGrowthVisual
    {
        public readonly int   FluffLayers;
        public readonly float FluffScale;

        public SpiritGrowthVisual(int fluffLayers, float fluffScale)
        {
            FluffLayers = fluffLayers;
            FluffScale  = fluffScale;
        }
    }

    public static class SpiritGrowthMath
    {
        /// <summary>累積経験の上限。float精度が壊れる領域へ入らないための保護であり、ゲーム的な上限ではない。</summary>
        public const float MaxLifetimeExperience = 1000000f;

        /// <summary>最終段階(Bloom)の毛玉数。ForestSpiritはこの数だけ事前生成する。</summary>
        public const int MaxFluffLayers = 9;

        // ── 累積経験の健全化 ────────────────────────────────────────

        /// <summary>
        /// 累積経験を安全な範囲へ丸める。
        /// NaN→0 / 負値→0 / -Infinity→0 / +Infinity→上限 / 上限超えの有限値→上限。
        /// </summary>
        public static float ClampExperience(float value)
        {
            // NaNは比較が常にfalseになるため、必ず最初に弾く。
            if (float.IsNaN(value)) return 0f;
            if (float.IsPositiveInfinity(value)) return MaxLifetimeExperience;

            // 負値と-Infinityをまとめてここで0にする。
            if (value <= 0f) return 0f;

            return value > MaxLifetimeExperience ? MaxLifetimeExperience : value;
        }

        // ── 段階の判定 ──────────────────────────────────────────────

        /// <summary>
        /// 累積経験から成長段階を求める。
        /// 経験が増えても段階が下がらない（単調非減少）ことを保証する。
        /// 閾値が逆転・重複していても例外を出さず、内部で補正して単調性を保つ。
        /// </summary>
        public static SpiritGrowthStage ComputeGrowthStage(
            float lifetimeExperience, float thresholdFluff, float thresholdBloom)
        {
            float exp = ClampExperience(lifetimeExperience);

            float tFluff = SafeThreshold(thresholdFluff);
            float tBloom = SafeThreshold(thresholdBloom);

            // 上位段階の閾値が下位より小さいと「経験が増えると段階が下がる」区間ができてしまう。
            // 下位以上へ引き上げることで、逆転・重複した設定でも単調性が壊れない。
            if (tBloom < tFluff) tBloom = tFluff;

            if (exp >= tBloom) return SpiritGrowthStage.Bloom;
            if (exp >= tFluff) return SpiritGrowthStage.Fluff;
            return SpiritGrowthStage.Sprout;
        }

        /// <summary>
        /// 閾値の健全化。負値は0（＝最初から到達済み）、
        /// NaNと+Infinityは「到達不能」として扱う（段階が勝手に上がるより安全なため）。
        /// </summary>
        private static float SafeThreshold(float threshold)
        {
            if (float.IsNaN(threshold)) return float.PositiveInfinity;
            return threshold < 0f ? 0f : threshold;
        }

        // ── 段階の健全化と見た目 ────────────────────────────────────

        /// <summary>未知の段階を有効範囲へ丸める。負値はSprout、Bloomを超える値はBloom。</summary>
        public static SpiritGrowthStage ClampStage(SpiritGrowthStage stage)
        {
            if ((int)stage <= (int)SpiritGrowthStage.Sprout) return SpiritGrowthStage.Sprout;
            if ((int)stage >= (int)SpiritGrowthStage.Bloom)  return SpiritGrowthStage.Bloom;
            return stage;
        }

        /// <summary>
        /// 段階ごとの綿毛の見た目。未知の段階は ClampStage で安全に丸めてから解決する。
        /// FluffLayersは必ず1以上 MaxFluffLayers以下、FluffScaleは必ず正の有限値。
        /// </summary>
        public static SpiritGrowthVisual ComputeGrowthVisual(SpiritGrowthStage stage)
        {
            switch (ClampStage(stage))
            {
                case SpiritGrowthStage.Bloom:
                    return new SpiritGrowthVisual(9, 1.20f);

                case SpiritGrowthStage.Fluff:
                    // Stage 13までの見た目と一致する（成長の途中段階が従来の姿）。
                    return new SpiritGrowthVisual(6, 1.00f);

                case SpiritGrowthStage.Sprout:
                default:
                    return new SpiritGrowthVisual(4, 0.85f);
            }
        }

        // ── 成長演出の判定 ──────────────────────────────────────────

        /// <summary>
        /// 成長演出を予約すべきか。段階が上がるときだけtrue。
        /// 同じ段階・後退では予約しない（経験が減ることは無いが、念のため後退を明示的に拒否する）。
        /// </summary>
        public static bool ShouldQueueGrowthVisual(SpiritGrowthStage previous, SpiritGrowthStage next)
            => (int)ClampStage(next) > (int)ClampStage(previous);

        /// <summary>
        /// 今回の演出で進む段階を返す。
        /// ★一度に複数段階は進めない。プレイヤーが各段階を必ず目にできるようにするため、
        ///   残りは呼び出し側のpendingに残り、次の機会に改めて演出される。
        /// pendingがcurrent以下なら何も進めない。Bloomを超えない。
        /// </summary>
        public static SpiritGrowthStage ResolveGrowthTransition(
            SpiritGrowthStage current, SpiritGrowthStage pending)
        {
            var c = ClampStage(current);
            var p = ClampStage(pending);

            if ((int)p <= (int)c) return c;

            // pはClampStageでBloom以下が保証されているため、Minを取ればBloomを超えない。
            return (SpiritGrowthStage)Mathf.Min((int)c + 1, (int)p);
        }
    }
}
