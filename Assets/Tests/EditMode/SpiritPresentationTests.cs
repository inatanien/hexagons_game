// 役割: Stage 16「誕生・成長演出」の純粋計算を検証する。
//       演出の一回性・停止・VFXの実挙動はPlayMode側で検証する。

using System.Reflection;
using NUnit.Framework;
using ElfVillage.Spirits;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class SpiritPresentationTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private static ForestSpiritPresentation MakePresentation()
            => new GameObject("TestPresentation").AddComponent<ForestSpiritPresentation>();

        // ══ Progress01 ══════════════════════════════════════════════════

        [Test]
        public void Progress01_NormalRange()
        {
            Assert.AreEqual(0f,   ForestSpiritPresentation.Progress01(0f,   1.2f), 0.0001f);
            Assert.AreEqual(0.5f, ForestSpiritPresentation.Progress01(0.6f, 1.2f), 0.0001f);
            Assert.AreEqual(1f,   ForestSpiritPresentation.Progress01(1.2f, 1.2f), 0.0001f);
            Assert.AreEqual(1f,   ForestSpiritPresentation.Progress01(5f,   1.2f), 0.0001f, "上限を超えない");
        }

        [Test]
        public void Progress01_InvalidInputs_AreHandledSafely()
        {
            foreach (var bad in new[] { float.NaN, float.NegativeInfinity, -1f })
                Assert.AreEqual(0f, ForestSpiritPresentation.Progress01(bad, 1.2f), 0.0001f,
                    $"経過={bad} で0にならない");

            foreach (var badDuration in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
                Assert.AreEqual(1f, ForestSpiritPresentation.Progress01(0.5f, badDuration), 0.0001f,
                    $"長さ={badDuration} は即完了扱いにすべき");
        }

        [Test]
        public void Progress01_IsMonotonic()
        {
            float prev = -1f;
            for (float e = 0f; e <= 1.5f; e += 0.05f)
            {
                float p = ForestSpiritPresentation.Progress01(e, 1.2f);
                Assert.GreaterOrEqual(p, prev, $"経過={e} で進行率が戻った");
                prev = p;
            }
        }

        // ══ ComputeBirthScale ═══════════════════════════════════════════

        [Test]
        public void BirthScale_StartsSmall_AndEndsAtOne()
        {
            Assert.AreEqual(0.15f, ForestSpiritPresentation.ComputeBirthScale(0f, 0.15f), 0.0001f,
                "生まれた瞬間は小さいはず");
            Assert.AreEqual(1f, ForestSpiritPresentation.ComputeBirthScale(1f, 0.15f), 0.0001f,
                "演出終了時は必ず等倍へ戻る（大きさが残らない）");
        }

        [Test]
        public void BirthScale_IsNeverZeroOrNegative()
        {
            // 0スケールは法線が壊れて描画が乱れるため、どんな入力でも正の値を返すこと。
            foreach (var start in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 0.15f, 2f })
                for (float p = 0f; p <= 1f; p += 0.02f)
                {
                    float s = ForestSpiritPresentation.ComputeBirthScale(p, start);
                    Assert.IsTrue(float.IsFinite(s), $"start={start} p={p} で非有限値");
                    Assert.Greater(s, 0f, $"start={start} p={p} で0以下になった");
                }
        }

        [Test]
        public void BirthScale_GrowsOverall()
        {
            // 途中で軽く行き過ぎる設計なので厳密な単調増加ではないが、
            // 前半から後半にかけて確実に大きくなること。
            float early = ForestSpiritPresentation.ComputeBirthScale(0.1f, 0.15f);
            float mid   = ForestSpiritPresentation.ComputeBirthScale(0.5f, 0.15f);
            float late  = ForestSpiritPresentation.ComputeBirthScale(0.9f, 0.15f);

            Assert.Less(early, mid,  "序盤より中盤が大きいはず");
            Assert.Less(mid,   late, "中盤より終盤が大きいはず");
        }

        [Test]
        public void BirthScale_StaysWithinReasonableBounds()
        {
            for (float p = 0f; p <= 1f; p += 0.01f)
            {
                float s = ForestSpiritPresentation.ComputeBirthScale(p, 0.15f);
                Assert.LessOrEqual(s, 1.15f, $"p={p} で膨らみすぎ（ゴム的に見える）");
            }
        }

        [Test]
        public void BirthScale_InvalidProgress_IsHandledSafely()
        {
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -3f, 9f })
            {
                float s = ForestSpiritPresentation.ComputeBirthScale(bad, 0.15f);
                Assert.IsTrue(float.IsFinite(s) && s > 0f, $"progress={bad} で不正な値: {s}");
            }
        }

        [Test]
        public void BirthScale_IsDeterministic()
        {
            for (float p = 0f; p <= 1f; p += 0.07f)
            {
                float a = ForestSpiritPresentation.ComputeBirthScale(p, 0.15f);
                for (int i = 0; i < 3; i++)
                    Assert.AreEqual(a, ForestSpiritPresentation.ComputeBirthScale(p, 0.15f), 0f);
            }
        }

        // ══ 段階ごとの演出選択 ══════════════════════════════════════════

        [Test]
        public void LightColor_BloomDiffersFromOtherStages()
        {
            var p = MakePresentation();
            try
            {
                var sprout = p.LightColorFor(SpiritGrowthStage.Sprout);
                var fluff  = p.LightColorFor(SpiritGrowthStage.Fluff);
                var bloom  = p.LightColorFor(SpiritGrowthStage.Bloom);

                Assert.AreEqual(sprout, fluff, "Bloom以外は同じ色でよい");
                Assert.AreNotEqual(fluff, bloom, "Bloomだけは色を変えて華やかさを出す");
            }
            finally { Object.DestroyImmediate(p.gameObject); }
        }

        [Test]
        public void LightColor_UnknownStage_IsHandledSafely()
        {
            var p = MakePresentation();
            try
            {
                var below = p.LightColorFor((SpiritGrowthStage)(-9));
                var above = p.LightColorFor((SpiritGrowthStage)999);

                Assert.AreEqual(p.LightColorFor(SpiritGrowthStage.Fluff), below, "負の段階は通常色へ");
                Assert.AreEqual(p.LightColorFor(SpiritGrowthStage.Bloom), above, "Bloom超えはBloom色へ");
            }
            finally { Object.DestroyImmediate(p.gameObject); }
        }

        [Test]
        public void Se_IsNullWhenUnassigned_AndNeverThrows()
        {
            // SE素材はStage 16では未設定のまま。null でも安全に扱えること。
            var p = MakePresentation();
            try
            {
                Assert.IsNull(p.SeFor(SpiritGrowthStage.Fluff));
                Assert.IsNull(p.SeFor(SpiritGrowthStage.Bloom));
                Assert.IsNull(p.SeFor((SpiritGrowthStage)999));
            }
            finally { Object.DestroyImmediate(p.gameObject); }
        }

        // ══ 誕生演出の進行 ══════════════════════════════════════════════

        [Test]
        public void BirthMultiplier_IsOne_WhenNotPlaying()
        {
            var p = MakePresentation();
            try
            {
                Assert.IsFalse(p.IsPlayingBirth);
                Assert.AreEqual(1f, p.BirthScaleMultiplier, 0.0001f,
                    "演出していないときは何も変えない");
            }
            finally { Object.DestroyImmediate(p.gameObject); }
        }

        [Test]
        public void BirthMultiplier_ReturnsToOne_AfterAdvancingPastDuration()
        {
            var p = MakePresentation();
            try
            {
                typeof(ForestSpiritPresentation).GetMethod("BeginBirth", Priv)
                    .Invoke(p, new object[] { Vector3.zero });   // 誕生位置（この検証では使わない）
                Assert.IsTrue(p.IsPlayingBirth);
                Assert.Less(p.BirthScaleMultiplier, 1f, "開始直後は小さいはず");

                var advance = typeof(ForestSpiritPresentation).GetMethod("Advance", Priv);
                advance.Invoke(p, new object[] { 5f });   // 十分に進める

                Assert.IsFalse(p.IsPlayingBirth, "演出が終わっていない");
                Assert.AreEqual(1f, p.BirthScaleMultiplier, 0.0001f, "終了後は等倍へ戻る");
            }
            finally { Object.DestroyImmediate(p.gameObject); }
        }

        [Test]
        public void Advance_IgnoresInvalidDelta()
        {
            var p = MakePresentation();
            try
            {
                typeof(ForestSpiritPresentation).GetMethod("BeginBirth", Priv)
                    .Invoke(p, new object[] { Vector3.zero });   // 誕生位置（この検証では使わない）
                float before = p.BirthScaleMultiplier;

                var advance = typeof(ForestSpiritPresentation).GetMethod("Advance", Priv);
                foreach (var bad in new object[] { float.NaN, float.PositiveInfinity, -1f, 0f })
                    advance.Invoke(p, new[] { bad });

                Assert.AreEqual(before, p.BirthScaleMultiplier, 0.0001f,
                    "不正なdeltaで演出が進んでしまった");
                Assert.IsTrue(p.IsPlayingBirth, "不正なdeltaで演出が終了してしまった");
            }
            finally { Object.DestroyImmediate(p.gameObject); }
        }
    }
}
