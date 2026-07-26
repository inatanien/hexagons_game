// 役割: Stage 13「性格（Personality）」の検証。
//       性格差が「乱数のブレ」ではなくProfileの値から決定的に生まれていることを示すため、
//       同じ乱数列をCalm/Curiousへ与えて結果が変わることまで確認する。
//       Unityライフサイクル・Spawner・EventBus購読順に関わる項目はPlayMode側
//       （SpiritPersonalityIntegrationTests）で検証する。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Spirits;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class SpiritPersonalityTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly float[] BadFloats =
            { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, -9999f };

        private static object GetField(object target, string name)
            => target.GetType().GetField(name, Priv).GetValue(target);

        private static void Invoke(object target, string name, params object[] args)
        {
            var m = target.GetType().GetMethod(name, Priv);
            Assert.IsNotNull(m, $"{target.GetType().Name}に{name}が見つかりません");
            m.Invoke(target, args);
        }

        private static HexTile MakeTileAt(Vector3 p)
        {
            var go = new GameObject("TestTile");
            go.transform.position = p;
            return go.AddComponent<HexTile>();
        }

        private static void DestroyTiles(IEnumerable<HexTile> tiles)
        {
            foreach (var t in tiles) if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        private static ForestSpirit MakeSpirit(SpiritPersonalityKind personality, out List<HexTile> home)
        {
            home = new List<HexTile> { MakeTileAt(Vector3.zero), MakeTileAt(new Vector3(1f, 0f, 0f)) };
            var go = new GameObject("TestForestSpirit");
            var spirit = go.AddComponent<ForestSpirit>();
            spirit.Initialize(home, Vector3.zero, 1.5f, 1.5f, 0.5f, personality);
            return spirit;
        }

        private static void Teardown(ForestSpirit spirit, IEnumerable<HexTile> tiles)
        {
            if (spirit != null) Object.DestroyImmediate(spirit.gameObject);
            DestroyTiles(tiles);
        }

        private static SpiritPersonalityProfile Calm    => SpiritPersonalityProfile.For(SpiritPersonalityKind.Calm);
        private static SpiritPersonalityProfile Curious => SpiritPersonalityProfile.For(SpiritPersonalityKind.Curious);

        // ══ 1〜2. enum値の固定（将来のセーブ互換） ═══════════════════════

        [Test]
        public void Enum_Calm_HasFixedValueZero()
            => Assert.AreEqual(0, (int)SpiritPersonalityKind.Calm,
                   "セーブ済みデータとの互換のため、Calmの数値は0から変えてはいけない");

        [Test]
        public void Enum_Curious_HasFixedValueOne()
            => Assert.AreEqual(1, (int)SpiritPersonalityKind.Curious,
                   "セーブ済みデータとの互換のため、Curiousの数値は1から変えてはいけない");

        // ══ 3〜6. Profileの有効性 ════════════════════════════════════════

        private static void AssertProfileIsValid(SpiritPersonalityProfile p, string label)
        {
            foreach (var (name, v) in new (string, float)[]
            {
                ("WanderWeight",      p.WanderWeight),
                ("ObserveWeight",     p.ObserveWeight),
                ("SleepWeight",       p.SleepWeight),
                ("IdleDurationScale", p.IdleDurationScale),
                ("WanderRadiusScale", p.WanderRadiusScale),
                ("HopHeightScale",    p.HopHeightScale),
                ("FamiliarityGain",   p.FamiliarityGain),
                ("MinReactionScale",  p.MinReactionScale),
            })
            {
                Assert.IsTrue(float.IsFinite(v), $"{label}.{name} がNaN/Infinity");
                Assert.GreaterOrEqual(v, 0f,     $"{label}.{name} が負値");
            }

            Assert.Greater(p.WanderWeight + p.ObserveWeight + p.SleepWeight, 0f,
                $"{label} の比重が全て0だと性格として機能しない");
            Assert.Greater(p.IdleDurationScale, 0f, $"{label}.IdleDurationScale は正でなければならない");
            Assert.Greater(p.WanderRadiusScale, 0f, $"{label}.WanderRadiusScale は正でなければならない");
            Assert.LessOrEqual(p.WanderRadiusScale, 1f, $"{label}.WanderRadiusScale はhome範囲を超えてはいけない");
            Assert.Greater(p.HopHeightScale,   0f, $"{label}.HopHeightScale は正でなければならない");
            Assert.Greater(p.FamiliarityGain,  0f, $"{label}.FamiliarityGain は正でなければならない");
            Assert.Greater(p.MinReactionScale, 0f, $"{label}.MinReactionScale は0にしない（完全無反応を作らない）");
            Assert.LessOrEqual(p.MinReactionScale, 1f, $"{label}.MinReactionScale は1以下");
            Assert.GreaterOrEqual(p.HopCount, 1, $"{label}.HopCount は1以上");
            Assert.LessOrEqual(p.HopCount, 20,   $"{label}.HopCount が極端に多い");
        }

        [Test]
        public void Profile_Calm_IsValid() => AssertProfileIsValid(Calm, "Calm");

        [Test]
        public void Profile_Curious_IsValid() => AssertProfileIsValid(Curious, "Curious");

        [Test]
        public void Profile_UnknownKind_FallsBackToCalm()
        {
            var unknown = SpiritPersonalityProfile.For((SpiritPersonalityKind)999);

            AssertProfileIsValid(unknown, "Unknown(999)");
            Assert.AreEqual(Calm.SleepWeight,       unknown.SleepWeight,       0.0001f);
            Assert.AreEqual(Calm.IdleDurationScale, unknown.IdleDurationScale, 0.0001f);
            Assert.AreEqual(Calm.FamiliarityGain,   unknown.FamiliarityGain,   0.0001f);
            Assert.AreEqual(Calm.HopCount,          unknown.HopCount);
        }

        [Test]
        public void Profile_NegativeKind_FallsBackToCalm()
        {
            var unknown = SpiritPersonalityProfile.For((SpiritPersonalityKind)(-5));
            AssertProfileIsValid(unknown, "Unknown(-5)");
            Assert.AreEqual(Calm.SleepWeight, unknown.SleepWeight, 0.0001f);
        }

        [Test]
        public void Profile_DefaultStruct_IsSafeAndNeutral()
        {
            // default(struct) が誤って使われても、致命的な値が下流へ流れないこと。
            // ★比重だけは全て0のままにしてある。これは欠陥ではなく、
            //   DecideNextStateが「全比重0なら既定比重へフォールバック」する契約になっているため。
            //   ここで無理に既定比重を埋めると、フォールバックの経路が二重管理になる。
            var d = default(SpiritPersonalityProfile);

            foreach (var (name, v) in new (string, float)[]
            {
                ("WanderWeight",      d.WanderWeight),
                ("ObserveWeight",     d.ObserveWeight),
                ("SleepWeight",       d.SleepWeight),
                ("IdleDurationScale", d.IdleDurationScale),
                ("WanderRadiusScale", d.WanderRadiusScale),
                ("HopHeightScale",    d.HopHeightScale),
                ("FamiliarityGain",   d.FamiliarityGain),
                ("MinReactionScale",  d.MinReactionScale),
            })
            {
                Assert.IsTrue(float.IsFinite(v), $"default.{name} がNaN/Infinity");
                Assert.GreaterOrEqual(v, 0f,     $"default.{name} が負値");
            }

            Assert.AreEqual(1f, d.IdleDurationScale, 0.0001f, "既定は等倍であるべき");
            Assert.AreEqual(1f, d.WanderRadiusScale, 0.0001f, "既定はhome範囲全体であるべき");
            Assert.AreEqual(1f, d.HopHeightScale,    0.0001f, "既定は等倍であるべき");
            Assert.Greater(d.FamiliarityGain,   0f, "既定でも学習が止まってはいけない");
            Assert.Greater(d.MinReactionScale,  0f, "既定でも完全無反応にしない");
            Assert.GreaterOrEqual(d.HopCount, 1, "既定でも有効な跳ね回数であるべき");

            // 全比重0でも状態選択は既定挙動へ安全に倒れる。
            for (float r = 0f; r <= 1f; r += 0.05f)
                Assert.AreEqual(
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r),
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r,
                        d.WanderWeight, d.ObserveWeight, d.SleepWeight),
                    $"default(struct) random01={r} で既定挙動へ倒れていない");
        }

        // ══ 7〜12. 性格差の向き（説明と数値の一致） ══════════════════════

        [Test]
        public void Curious_MinReactionScale_IsAtLeastCalm()
            => Assert.GreaterOrEqual(Curious.MinReactionScale, Calm.MinReactionScale,
                   "Curiousは慣れきってもCalm以上の反応を残す");

        [Test]
        public void Curious_FamiliarityGain_IsLowerThanCalm()
            => Assert.Less(Curious.FamiliarityGain, Calm.FamiliarityGain,
                   "Curiousは慣れにくい＝gainが低い");

        [Test]
        public void Calm_IdleDurationScale_IsHigherThanCurious()
            => Assert.Greater(Calm.IdleDurationScale, Curious.IdleDurationScale,
                   "Calmは長くじっとしている");

        [Test]
        public void Calm_SleepWeight_IsHigherThanCurious()
            => Assert.Greater(Calm.SleepWeight, Curious.SleepWeight, "Calmはよく眠る");

        [Test]
        public void Curious_WanderWeight_IsHigherThanCalm()
            => Assert.Greater(Curious.WanderWeight, Calm.WanderWeight, "Curiousはよく歩き回る");

        [Test]
        public void Curious_ObserveWeight_IsHigherThanCalm()
            => Assert.Greater(Curious.ObserveWeight, Calm.ObserveWeight, "Curiousはよく木を眺める");

        [Test]
        public void Curious_HopHeightScale_IsHigherThanCalm()
            => Assert.Greater(Curious.HopHeightScale, Calm.HopHeightScale, "Curiousは高く弾む");

        [Test]
        public void Calm_WanderRadiusScale_IsSmallerThanCurious()
            => Assert.Less(Calm.WanderRadiusScale, Curious.WanderRadiusScale,
                   "Calmは中央寄りの狭い範囲で過ごす");

        // ══ 13〜22. 重み付き状態選択 ═════════════════════════════════════

        [Test]
        public void DecideNextState_DefaultSignature_MatchesLegacyThresholds()
        {
            // 従来の閾値（0.50 / 0.85）と完全に一致すること。
            for (float r = 0f; r <= 1f; r += 0.005f)
            {
                var expected = r < 0.50f ? SpiritState.Wander
                             : r < 0.85f ? SpiritState.ObserveTree
                             : SpiritState.Sleep;
                Assert.AreEqual(expected, SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r),
                    $"random01={r} で従来挙動と一致しない");
            }
        }

        [Test]
        public void DecideNextState_DefaultSignature_MatchesDefaultWeights()
        {
            for (float r = 0f; r <= 1f; r += 0.01f)
            {
                Assert.AreEqual(
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r),
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r,
                        SpiritBehaviorMath.DefaultWanderWeight,
                        SpiritBehaviorMath.DefaultObserveWeight,
                        SpiritBehaviorMath.DefaultSleepWeight),
                    $"random01={r} で既定比重版と一致しない");
            }
        }

        [Test]
        public void DecideNextState_NonIdleTransitions_AreUnchangedByWeights()
        {
            // 比重はIdleからの選択にだけ影響し、他の状態からの遷移は変えない。
            var states = new[]
            {
                SpiritState.Wander, SpiritState.ObserveTree,
                SpiritState.Sleep, SpiritState.Stretch, SpiritState.React,
            };

            foreach (var s in states)
                for (float r = 0f; r <= 1f; r += 0.05f)
                    Assert.AreEqual(
                        SpiritBehaviorMath.DecideNextState(s, r),
                        SpiritBehaviorMath.DecideNextState(s, r, 99f, 0.001f, 0f),
                        $"{s} からの遷移が比重で変わってしまった");
        }

        [Test]
        public void DecideNextState_SameInput_IsDeterministic()
        {
            for (float r = 0f; r <= 1f; r += 0.05f)
            {
                var first = SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 0.3f, 0.3f, 0.4f);
                for (int i = 0; i < 5; i++)
                    Assert.AreEqual(first,
                        SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 0.3f, 0.3f, 0.4f));
            }
        }

        [Test]
        public void DecideNextState_WeightsNeedNotSumToOne()
        {
            // 合計10（3:3:4）は合計1（0.3:0.3:0.4）と同じ結果になるはず。
            for (float r = 0f; r <= 1f; r += 0.01f)
                Assert.AreEqual(
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 0.3f, 0.3f, 0.4f),
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 3f, 3f, 4f),
                    $"random01={r} で正規化されていない");
        }

        [Test]
        public void DecideNextState_NegativeWeight_IsTreatedAsZero()
        {
            for (float r = 0f; r <= 1f; r += 0.01f)
            {
                // Sleepを負にすると、Sleepは決して選ばれない。
                var s = SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 1f, 1f, -5f);
                Assert.AreNotEqual(SpiritState.Sleep, s, $"random01={r} で負の重みが選ばれた");

                Assert.AreEqual(
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 1f, 1f, 0f), s,
                    "負の重みは0と同じ扱いであるべき");
            }
        }

        [Test]
        public void DecideNextState_NonFiniteWeight_IsTreatedAsZero()
        {
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                for (float r = 0f; r <= 1f; r += 0.05f)
                {
                    var s = SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 1f, 1f, bad);
                    Assert.AreNotEqual(SpiritState.Sleep, s, $"{bad} の重みが選ばれた");
                    Assert.AreEqual(
                        SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 1f, 1f, 0f), s);
                }
        }

        [Test]
        public void DecideNextState_AllWeightsZero_FallsBackToDefault()
        {
            for (float r = 0f; r <= 1f; r += 0.01f)
                Assert.AreEqual(
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r), // 既定比重版
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 0f, 0f, 0f),
                    $"random01={r} で既定へフォールバックしていない");
        }

        [Test]
        public void DecideNextState_AllWeightsInvalid_FallsBackToDefault()
        {
            foreach (var bad in BadFloats)
                for (float r = 0f; r <= 1f; r += 0.05f)
                    Assert.AreEqual(
                        SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r),
                        SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, bad, bad, bad),
                        $"重み={bad} random01={r} で既定へフォールバックしていない");
        }

        [Test]
        public void DecideNextState_OutOfRangeRandom_IsHandledSafely()
        {
            var defined = new[]
            {
                SpiritState.Idle, SpiritState.Wander, SpiritState.ObserveTree,
                SpiritState.Sleep, SpiritState.Stretch, SpiritState.React,
            };

            foreach (var r in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -42f, 42f })
                CollectionAssert.Contains(defined,
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 0.3f, 0.3f, 0.4f));
        }

        [Test]
        public void DecideNextState_BoundaryRandom_NeverPicksZeroWeightState()
        {
            // r=0 と r=1 の境界で、重み0の遷移先が選ばれないこと。
            foreach (var r in new[] { 0f, 1f })
            {
                Assert.AreNotEqual(SpiritState.Sleep,
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 0.5f, 0.5f, 0f),
                    $"r={r}: 重み0のSleepが選ばれた");
                Assert.AreNotEqual(SpiritState.ObserveTree,
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 0.5f, 0f, 0.5f),
                    $"r={r}: 重み0のObserveTreeが選ばれた");
                Assert.AreNotEqual(SpiritState.Wander,
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 0f, 0.5f, 0.5f),
                    $"r={r}: 重み0のWanderが選ばれた");
            }

            // 1つだけ重みがある場合は必ずそれになる。
            foreach (var r in new[] { 0f, 0.5f, 1f })
                Assert.AreEqual(SpiritState.Wander,
                    SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r, 1f, 0f, 0f));
        }

        [Test]
        public void DecideNextState_SameRandom_CalmAndCuriousDiffer_ForSomeValues()
        {
            // ★性格差が乱数のブレではなくProfileから生まれていることの証明。
            //   同じrandom01を与えて結果が変わる値が実際に存在する。
            int differences = 0;

            for (float r = 0f; r <= 1f; r += 0.01f)
            {
                var calm = SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r,
                    Calm.WanderWeight, Calm.ObserveWeight, Calm.SleepWeight);
                var curious = SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r,
                    Curious.WanderWeight, Curious.ObserveWeight, Curious.SleepWeight);

                if (calm != curious) differences++;
            }

            Assert.Greater(differences, 0, "同じ乱数でCalmとCuriousが必ず同じ状態を選んでいる（性格差が出ていない）");
        }

        /// <summary>0〜1を等間隔にサンプリングして、各状態が選ばれた回数を数える。</summary>
        private static Dictionary<SpiritState, int> SampleIdleTransitions(SpiritPersonalityProfile p, int samples = 1000)
        {
            var counts = new Dictionary<SpiritState, int>
            {
                [SpiritState.Wander]      = 0,
                [SpiritState.ObserveTree] = 0,
                [SpiritState.Sleep]       = 0,
            };

            for (int i = 0; i < samples; i++)
            {
                float r = i / (float)(samples - 1);
                var s = SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r,
                    p.WanderWeight, p.ObserveWeight, p.SleepWeight);
                if (counts.ContainsKey(s)) counts[s]++;
            }
            return counts;
        }

        [Test]
        public void Sampling_Calm_ChoosesSleepMoreOftenThanCurious()
        {
            var calm    = SampleIdleTransitions(Calm);
            var curious = SampleIdleTransitions(Curious);

            Assert.Greater(calm[SpiritState.Sleep], curious[SpiritState.Sleep],
                $"Calm Sleep={calm[SpiritState.Sleep]} / Curious Sleep={curious[SpiritState.Sleep]}");
        }

        [Test]
        public void Sampling_Curious_ChoosesWanderMoreOftenThanCalm()
        {
            var calm    = SampleIdleTransitions(Calm);
            var curious = SampleIdleTransitions(Curious);

            Assert.Greater(curious[SpiritState.Wander], calm[SpiritState.Wander],
                $"Curious Wander={curious[SpiritState.Wander]} / Calm Wander={calm[SpiritState.Wander]}");
        }

        [Test]
        public void Sampling_Curious_ChoosesObserveTreeMoreOftenThanCalm()
        {
            var calm    = SampleIdleTransitions(Calm);
            var curious = SampleIdleTransitions(Curious);

            Assert.Greater(curious[SpiritState.ObserveTree], calm[SpiritState.ObserveTree],
                $"Curious Observe={curious[SpiritState.ObserveTree]} / Calm Observe={calm[SpiritState.ObserveTree]}");
        }

        // ══ 23〜25. 状態時間 ═════════════════════════════════════════════

        [Test]
        public void ComputeStateDuration_ScaleOne_MatchesLegacy()
        {
            foreach (SpiritState s in System.Enum.GetValues(typeof(SpiritState)))
                for (float r = 0f; r <= 1f; r += 0.05f)
                    Assert.AreEqual(
                        SpiritBehaviorMath.ComputeStateDuration(s, r),
                        SpiritBehaviorMath.ComputeStateDuration(s, r, 1f),
                        0.00001f, $"{s} random01={r} で倍率1.0が既存挙動と一致しない");
        }

        [Test]
        public void ComputeStateDuration_CalmIdle_IsLongerThanCurious()
        {
            for (float r = 0f; r <= 1f; r += 0.05f)
            {
                float calm    = SpiritBehaviorMath.ComputeStateDuration(SpiritState.Idle, r, Calm.IdleDurationScale);
                float curious = SpiritBehaviorMath.ComputeStateDuration(SpiritState.Idle, r, Curious.IdleDurationScale);
                Assert.Greater(calm, curious, $"random01={r} でCalmのIdleがCuriousより長くない");
            }
        }

        [Test]
        public void ComputeStateDuration_InvalidScale_IsHandledSafely()
        {
            foreach (var bad in BadFloats.Concat(0f))
                for (float r = 0f; r <= 1f; r += 0.1f)
                {
                    float d = SpiritBehaviorMath.ComputeStateDuration(SpiritState.Idle, r, bad);
                    Assert.IsTrue(float.IsFinite(d), $"倍率{bad}で非有限値");
                    Assert.Greater(d, 0f, $"倍率{bad}で0秒以下の状態になった");
                    Assert.AreEqual(SpiritBehaviorMath.ComputeStateDuration(SpiritState.Idle, r), d, 0.00001f,
                        $"倍率{bad}は1.0扱いであるべき");
                }
        }

        [Test]
        public void ComputeStateDuration_PreservesMinMaxOrdering()
        {
            foreach (var scale in new[] { 0.7f, 1f, 1.4f })
                foreach (SpiritState s in System.Enum.GetValues(typeof(SpiritState)))
                {
                    float min = SpiritBehaviorMath.ComputeStateDuration(s, 0f, scale);
                    float max = SpiritBehaviorMath.ComputeStateDuration(s, 1f, scale);
                    Assert.Less(min, max, $"{s} 倍率{scale} で最小＜最大の関係が壊れた");
                    Assert.Greater(min, 0f, $"{s} 倍率{scale} で0秒以下");
                }
        }

        [Test]
        public void IdleDurationScale_DoesNotLeakToOtherStates()
        {
            // ★性格のIdle倍率が他状態へ漏れていないことを、実際の呼び出し側（EnterState）で確認する。
            //   Calmの倍率1.4が掛かっていれば、Sleep等の時間は規定の最大値を超えてしまう。
            var spirit = MakeSpirit(SpiritPersonalityKind.Calm, out var home);
            try
            {
                var checks = new (SpiritState state, float min, float max)[]
                {
                    (SpiritState.Sleep,       SpiritBehaviorMath.SleepMinDuration,       SpiritBehaviorMath.SleepMaxDuration),
                    (SpiritState.Stretch,     SpiritBehaviorMath.StretchMinDuration,     SpiritBehaviorMath.StretchMaxDuration),
                    (SpiritState.Wander,      SpiritBehaviorMath.WanderMinDuration,      SpiritBehaviorMath.WanderMaxDuration),
                    (SpiritState.ObserveTree, SpiritBehaviorMath.ObserveTreeMinDuration, SpiritBehaviorMath.ObserveTreeMaxDuration),
                };

                foreach (var (state, min, max) in checks)
                    for (int i = 0; i < 40; i++)
                    {
                        Invoke(spirit, "EnterState", state);
                        float d = (float)GetField(spirit, "_stateDuration");
                        Assert.GreaterOrEqual(d, min - 0.0001f, $"{state} の時間が規定より短い（倍率が漏れている）");
                        Assert.LessOrEqual(d,    max + 0.0001f, $"{state} の時間が規定より長い（Idle倍率が漏れている）");
                    }

                // Idleだけは倍率が掛かって規定最大を超えうる。
                bool exceeded = false;
                for (int i = 0; i < 200 && !exceeded; i++)
                {
                    Invoke(spirit, "EnterState", SpiritState.Idle);
                    if ((float)GetField(spirit, "_stateDuration") > SpiritBehaviorMath.IdleMaxDuration + 0.0001f)
                        exceeded = true;
                }
                Assert.IsTrue(exceeded, "CalmのIdle倍率(1.4)が実際に適用されていない");
            }
            finally { Teardown(spirit, home); }
        }

        // ══ 26〜31. Wander範囲とHop ══════════════════════════════════════

        [Test]
        public void WanderTarget_Calm_StaysWithinShrunkHome()
        {
            const float extent = 2f;
            float scale = Calm.WanderRadiusScale;

            for (float rx = 0f; rx <= 1f; rx += 0.05f)
                for (float rz = 0f; rz <= 1f; rz += 0.25f)
                {
                    var t = SpiritBehaviorMath.PickWanderTarget(
                        Vector3.zero, extent * scale, extent * scale, rx, rz);

                    Assert.LessOrEqual(Mathf.Abs(t.x), extent * scale + 0.0001f, "縮小範囲を超えた");
                    Assert.LessOrEqual(Mathf.Abs(t.z), extent * scale + 0.0001f, "縮小範囲を超えた");
                    Assert.Less(Mathf.Abs(t.x), extent, "Calmが元のhome範囲いっぱいまで歩いている");
                }
        }

        [Test]
        public void WanderTarget_Curious_StaysWithinOriginalHome()
        {
            const float extent = 2f;
            float scale = Curious.WanderRadiusScale;

            for (float rx = 0f; rx <= 1f; rx += 0.05f)
                for (float rz = 0f; rz <= 1f; rz += 0.25f)
                {
                    var t = SpiritBehaviorMath.PickWanderTarget(
                        Vector3.zero, extent * scale, extent * scale, rx, rz);

                    Assert.LessOrEqual(Mathf.Abs(t.x), extent + 0.0001f, "Curiousがhome範囲を超えた");
                    Assert.LessOrEqual(Mathf.Abs(t.z), extent + 0.0001f, "Curiousがhome範囲を超えた");
                }
        }

        [Test]
        public void WanderRadiusScale_InvalidValues_AreHandledSafely()
        {
            // Profile側で健全化されるため、不正値を入れても常に (0,1] に収まる。
            foreach (var bad in BadFloats.Concat(0f).Concat(5f))
            {
                var p = new SpiritPersonalityProfile(1f, 1f, 1f, 1f, bad, 1f, 2, 1f, 0.3f);
                Assert.IsTrue(float.IsFinite(p.WanderRadiusScale), $"scale={bad} が非有限");
                Assert.Greater(p.WanderRadiusScale, 0f,   $"scale={bad} が0以下");
                Assert.LessOrEqual(p.WanderRadiusScale, 1f, $"scale={bad} でhome範囲を超えうる");
            }
        }

        [Test]
        public void WanderTarget_AlwaysClampedIntoOriginalHome_ByBeginMove()
        {
            // 最終的な目的地はClampToBoundsで元のhome範囲に収まる（既存の保証の維持）。
            const float extent = 2f;
            foreach (var scale in new[] { 0.6f, 1f })
                for (float rx = 0f; rx <= 1f; rx += 0.1f)
                {
                    var raw = SpiritBehaviorMath.PickWanderTarget(
                        Vector3.zero, extent * scale, extent * scale, rx, 1f - rx);
                    var clamped = SpiritBehaviorMath.ClampToBounds(raw, Vector3.zero, extent, extent);

                    Assert.LessOrEqual(Mathf.Abs(clamped.x), extent + 0.0001f);
                    Assert.LessOrEqual(Mathf.Abs(clamped.z), extent + 0.0001f);
                    Assert.AreEqual(raw.y, clamped.y, 0.0001f, "Y位置がClampで変わってはいけない");
                }
        }

        [Test]
        public void Hop_Calm_IsLowerThanCurious()
        {
            const float baseHeight = 0.038f;
            float calmMax = 0f, curiousMax = 0f;

            for (float p = 0f; p <= 1f; p += 0.001f)
            {
                calmMax = Mathf.Max(calmMax, SpiritBehaviorMath.ComputeHopOffset(
                    p, Calm.HopCount, baseHeight * Calm.HopHeightScale));
                curiousMax = Mathf.Max(curiousMax, SpiritBehaviorMath.ComputeHopOffset(
                    p, Curious.HopCount, baseHeight * Curious.HopHeightScale));
            }

            Assert.Less(calmMax, curiousMax, $"Calm最高点={calmMax:F5} / Curious最高点={curiousMax:F5}");
        }

        [Test]
        public void Hop_BothPersonalities_ReturnToZeroAtStartAndEnd()
        {
            const float baseHeight = 0.038f;

            foreach (var p in new[] { Calm, Curious })
            {
                float h = baseHeight * p.HopHeightScale;
                Assert.AreEqual(0f, SpiritBehaviorMath.ComputeHopOffset(0f, p.HopCount, h), 0.00001f,
                    "移動開始時に浮いている");
                Assert.AreEqual(0f, SpiritBehaviorMath.ComputeHopOffset(1f, p.HopCount, h), 0.00001f,
                    "移動終了時に着地していない");
            }
        }

        [Test]
        public void Hop_DifferentHopCounts_DoNotDriftY()
        {
            // 移動を何度繰り返しても、終了時のオフセットは必ず0（Yが蓄積しない）。
            const float baseHeight = 0.038f;

            foreach (var p in new[] { Calm, Curious })
            {
                float h = baseHeight * p.HopHeightScale;
                float accumulated = 0f;

                for (int move = 0; move < 50; move++)
                    accumulated += SpiritBehaviorMath.ComputeHopOffset(1f, p.HopCount, h);

                Assert.AreEqual(0f, accumulated, 0.00001f,
                    $"HopCount={p.HopCount} で50回移動後にYが{accumulated}ドリフトした");
            }
        }

        [Test]
        public void Hop_NeverExceedsScaledHeight()
        {
            const float baseHeight = 0.038f;

            foreach (var p in new[] { Calm, Curious })
            {
                float h = baseHeight * p.HopHeightScale;
                for (float t = 0f; t <= 1f; t += 0.001f)
                {
                    float y = SpiritBehaviorMath.ComputeHopOffset(t, p.HopCount, h);
                    Assert.GreaterOrEqual(y, 0f, "地面へめり込んだ");
                    Assert.LessOrEqual(y, h + 0.00001f, "跳ねが上限を超えた");
                }
            }
        }

        [Test]
        public void Hop_InvalidProfileValues_AreHandledSafely()
        {
            foreach (var badHeight in BadFloats)
                foreach (var badCount in new[] { -3, 0, 9999 })
                {
                    var p = new SpiritPersonalityProfile(1f, 1f, 1f, 1f, 1f, badHeight, badCount, 1f, 0.3f);
                    Assert.GreaterOrEqual(p.HopCount, 1);
                    Assert.LessOrEqual(p.HopCount, 20);
                    Assert.IsTrue(float.IsFinite(p.HopHeightScale));
                    Assert.Greater(p.HopHeightScale, 0f);

                    for (float t = 0f; t <= 1f; t += 0.1f)
                        Assert.IsTrue(float.IsFinite(
                            SpiritBehaviorMath.ComputeHopOffset(t, p.HopCount, 0.038f * p.HopHeightScale)));
                }
        }

        [Test]
        public void ReactAndObserveHop_DoNotUseWanderHopCount()
        {
            // ObserveTree/ReactのSmallHopは hopCount:1 固定。
            // Curious(HopCount=3)の値を使ってしまうと山が3つになるため、山の数で区別できる。
            const float h = 0.05f;
            int peaks = 0;
            float prev = SpiritBehaviorMath.ComputeHopOffset(0f, 1, h);
            bool rising = false;

            for (float t = 0.001f; t <= 1f; t += 0.001f)
            {
                float y = SpiritBehaviorMath.ComputeHopOffset(t, 1, h);
                if (y > prev) rising = true;
                else if (rising && y < prev) { peaks++; rising = false; }
                prev = y;
            }

            Assert.AreEqual(1, peaks, "その場の小さな跳ねは1回だけであるべき（移動用HopCountが漏れている）");
        }

        // ══ 32〜36. Familiarityとの接続 ══════════════════════════════════

        [Test]
        public void Familiarity_Curious_LearnsSlowerThanCalm()
        {
            const float max = 4f;
            float calm = 0f, curious = 0f;

            for (int i = 0; i < 3; i++)
            {
                calm    = SpiritBehaviorMath.ComputeFamiliarityGain(calm,    Calm.FamiliarityGain,    max);
                curious = SpiritBehaviorMath.ComputeFamiliarityGain(curious, Curious.FamiliarityGain, max);
                Assert.Less(curious, calm,
                    $"{i + 1}回目: Curious={curious:F3} がCalm={calm:F3} 以上になっている");
            }
        }

        [Test]
        public void Familiarity_Curious_KeepsHigherReactionScaleAfterSameStimuli()
        {
            const float max = 4f;
            float calm = 0f, curious = 0f;

            for (int i = 0; i < 3; i++)
            {
                float calmScale    = SpiritBehaviorMath.ComputeReactionScale(calm,    max, Calm.MinReactionScale);
                float curiousScale = SpiritBehaviorMath.ComputeReactionScale(curious, max, Curious.MinReactionScale);

                if (i > 0)
                    Assert.Greater(curiousScale, calmScale,
                        $"{i}回経験後: Curious Scale={curiousScale:F3} がCalm={calmScale:F3} 以下になっている");

                calm    = SpiritBehaviorMath.ComputeFamiliarityGain(calm,    Calm.FamiliarityGain,    max);
                curious = SpiritBehaviorMath.ComputeFamiliarityGain(curious, Curious.FamiliarityGain, max);
            }
        }

        [Test]
        public void Familiarity_AtMaximum_BothPersonalitiesStillReact()
        {
            const float max = 4f;

            float calmScale    = SpiritBehaviorMath.ComputeReactionScale(max, max, Calm.MinReactionScale);
            float curiousScale = SpiritBehaviorMath.ComputeReactionScale(max, max, Curious.MinReactionScale);

            Assert.Greater(calmScale, 0f,    "Calmが完全無反応になっている");
            Assert.Greater(curiousScale, 0f, "Curiousが完全無反応になっている");
            Assert.AreEqual(Calm.MinReactionScale,    calmScale,    0.0001f);
            Assert.AreEqual(Curious.MinReactionScale, curiousScale, 0.0001f);
            Assert.GreaterOrEqual(curiousScale, calmScale,
                "最大まで慣れてもCuriousの反応下限はCalm以上であるべき");
        }

        [Test]
        public void Familiarity_HalfLifeAndMaximum_AreSharedAcrossPersonalities()
        {
            // 性格差はgainとMinReactionScaleだけ。半減期・上限は共通のまま
            // （ForestSpiritのSerializeFieldが1つずつしか無いことで担保される）。
            var f = typeof(ForestSpirit);
            Assert.IsNotNull(f.GetField("_familiarityHalfLife", Priv), "半減期は共通の1フィールドであるべき");
            Assert.IsNotNull(f.GetField("_familiarityMaximum",  Priv), "上限は共通の1フィールドであるべき");
            Assert.IsNull(f.GetField("_familiarityGain",  Priv), "gainはProfileから供給されるべき（重複定義しない）");
            Assert.IsNull(f.GetField("_minReactionScale", Priv), "下限はProfileから供給されるべき（重複定義しない）");
        }

        // ══ 37〜41. 性格決定の決定性 ═════════════════════════════════════

        [Test]
        public void PickPersonality_SameCoordinate_AlwaysSameKind()
        {
            foreach (var (x, z) in new[] { (0f, 0f), (3.5f, -2.25f), (-17.75f, 41.5f), (100f, 100f) })
            {
                var first = SpiritBehaviorMath.PickPersonality(x, z);
                for (int i = 0; i < 20; i++)
                    Assert.AreEqual(first, SpiritBehaviorMath.PickPersonality(x, z),
                        $"({x},{z}) で結果がぶれた");
            }
        }

        [Test]
        public void StableHash_IsDeterministicAndNonNegative()
        {
            for (int a = -50; a <= 50; a += 7)
                for (int b = -50; b <= 50; b += 11)
                {
                    int h = SpiritBehaviorMath.StableHash(a, b);
                    Assert.GreaterOrEqual(h, 0, $"({a},{b}) で負のハッシュ");
                    Assert.AreEqual(h, SpiritBehaviorMath.StableHash(a, b), "同じ入力で値がぶれた");
                }

            // 極値でも例外にならないこと。
            foreach (var a in new[] { int.MinValue, int.MaxValue, 0 })
                Assert.GreaterOrEqual(SpiritBehaviorMath.StableHash(a, a), 0);
        }

        [Test]
        public void PickPersonality_AlwaysReturnsDefinedKind()
        {
            var defined = new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious };

            for (float x = -60f; x <= 60f; x += 1.7f)
                for (float z = -60f; z <= 60f; z += 9.3f)
                    CollectionAssert.Contains(defined, SpiritBehaviorMath.PickPersonality(x, z));
        }

        [Test]
        public void PickPersonality_BothKindsAreReachable()
        {
            int calm = 0, curious = 0;

            for (float x = -30f; x <= 30f; x += 0.9f)
                for (float z = -30f; z <= 30f; z += 1.3f)
                {
                    if (SpiritBehaviorMath.PickPersonality(x, z) == SpiritPersonalityKind.Calm) calm++;
                    else curious++;
                }

            Assert.Greater(calm, 0,    "Calmが1つも生成されない");
            Assert.Greater(curious, 0, "Curiousが1つも生成されない");

            // 極端に偏っていないこと（どちらかが1割未満なら実質1種類しか出ない）。
            int total = calm + curious;
            Assert.Greater(calm    / (float)total, 0.1f, $"Calmへの偏り不足 calm={calm} curious={curious}");
            Assert.Greater(curious / (float)total, 0.1f, $"Curiousへの偏り不足 calm={calm} curious={curious}");
        }

        [Test]
        public void PickPersonality_InvalidCoordinates_AreHandledSafely()
        {
            var defined = new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious };

            foreach (var bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 1e30f, -1e30f })
            {
                CollectionAssert.Contains(defined, SpiritBehaviorMath.PickPersonality(bad, 0f));
                CollectionAssert.Contains(defined, SpiritBehaviorMath.PickPersonality(0f, bad));
                CollectionAssert.Contains(defined, SpiritBehaviorMath.PickPersonality(bad, bad));
            }
        }

        [Test]
        public void RepresentativePosition_IsIndependentOfTileOrder()
        {
            // Spawnerの代表座標選択が列挙順に依存しないこと。
            var positions = new[]
            {
                new Vector3(3f, 0f,  1f),
                new Vector3(-2f, 0f, 5f),
                new Vector3(-2f, 0f, 1f), // 最小X かつ 最小Z ＝ これが代表
                new Vector3(7f, 0f, -4f),
            };

            var forward  = new List<HexTile>();
            var reversed = new List<HexTile>();
            foreach (var p in positions) forward.Add(MakeTileAt(p));
            for (int i = positions.Length - 1; i >= 0; i--) reversed.Add(forward[i]);

            try
            {
                var method = typeof(ForestSpiritSpawner).GetMethod(
                    "TryGetRepresentativePosition", BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(method, "TryGetRepresentativePositionが見つかりません");

                object[] a = { forward,  null };
                object[] b = { reversed, null };
                Assert.IsTrue((bool)method.Invoke(null, a));
                Assert.IsTrue((bool)method.Invoke(null, b));

                var repA = (Vector3)a[1];
                var repB = (Vector3)b[1];

                Assert.AreEqual(repA, repB, "列挙順で代表座標が変わった");
                Assert.AreEqual(new Vector3(-2f, 0f, 1f), repA, "最小X・同値なら最小Zが選ばれていない");

                Assert.AreEqual(
                    SpiritBehaviorMath.PickPersonality(repA.x, repA.z),
                    SpiritBehaviorMath.PickPersonality(repB.x, repB.z),
                    "列挙順で性格が変わった");
            }
            finally { DestroyTiles(forward); }
        }

        [Test]
        public void RepresentativePosition_EmptyOrNullHome_IsHandledSafely()
        {
            var method = typeof(ForestSpiritSpawner).GetMethod(
                "TryGetRepresentativePosition", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            object[] nullArgs  = { null, null };
            object[] emptyArgs = { new List<HexTile>(), null };
            object[] allNull   = { new List<HexTile> { null, null }, null };

            Assert.IsFalse((bool)method.Invoke(null, nullArgs),  "nullのhomeでtrueを返した");
            Assert.IsFalse((bool)method.Invoke(null, emptyArgs), "空のhomeでtrueを返した");
            Assert.IsFalse((bool)method.Invoke(null, allNull),   "全てnullのhomeでtrueを返した");
        }

        [Test]
        public void Personality_DoesNotChangeAfterHomeGrowth()
        {
            var spirit = MakeSpirit(SpiritPersonalityKind.Curious, out var home);
            var grown  = new List<HexTile>();
            try
            {
                Assert.AreEqual(SpiritPersonalityKind.Curious, spirit.Personality);

                // home森が育っても性格は再計算されない。
                grown.AddRange(home);
                grown.Add(MakeTileAt(new Vector3(40f, 0f, 40f)));
                grown.Add(MakeTileAt(new Vector3(41f, 0f, 41f)));

                Assert.IsTrue(spirit.TryFollowForestGrowth(grown, new Vector3(20f, 0f, 20f), 5f, 5f),
                    "home森の成長として認識されなかった");

                Assert.AreEqual(SpiritPersonalityKind.Curious, spirit.Personality,
                    "home成長で性格が変わってしまった");

                var profile = (SpiritPersonalityProfile)GetField(spirit, "_profile");
                Assert.AreEqual(Curious.SleepWeight, profile.SleepWeight, 0.0001f,
                    "home成長でProfileが差し替わった");
            }
            finally
            {
                Teardown(spirit, home);
                foreach (var t in grown) if (t != null) Object.DestroyImmediate(t.gameObject);
            }
        }

        // ══ 43〜48. ForestSpiritへの注入と既存保証 ═══════════════════════

        [Test]
        public void Initialize_KeepsGivenPersonality()
        {
            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
            {
                var spirit = MakeSpirit(kind, out var home);
                try
                {
                    Assert.AreEqual(kind, spirit.Personality);
                    var profile = (SpiritPersonalityProfile)GetField(spirit, "_profile");
                    Assert.AreEqual(SpiritPersonalityProfile.For(kind).SleepWeight, profile.SleepWeight, 0.0001f);
                }
                finally { Teardown(spirit, home); }
            }
        }

        [Test]
        public void Initialize_UnknownPersonality_ResolvesToValidProfile()
        {
            var spirit = MakeSpirit((SpiritPersonalityKind)777, out var home);
            try
            {
                var profile = (SpiritPersonalityProfile)GetField(spirit, "_profile");
                AssertProfileIsValid(profile, "Initialize(unknown)");
                Assert.AreEqual(Calm.SleepWeight, profile.SleepWeight, 0.0001f, "Calmへ倒れていない");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void PersonalityProfile_IsActuallyUsedForWanderRange()
        {
            // Calm(0.6)とCurious(1.0)で、実際に到達しうる最大距離が変わることを
            // 実際の状態遷移（EnterState → BeginMove）を通して確認する。
            float calmMax    = MaxWanderDistanceFromHome(SpiritPersonalityKind.Calm);
            float curiousMax = MaxWanderDistanceFromHome(SpiritPersonalityKind.Curious);

            Assert.Less(calmMax, curiousMax,
                $"Calm最大距離={calmMax:F3} / Curious最大距離={curiousMax:F3}");
            Assert.LessOrEqual(curiousMax, 1.5f * Mathf.Sqrt(2f) + 0.0001f,
                "Curiousがhome範囲を超えた");
        }

        private static float MaxWanderDistanceFromHome(SpiritPersonalityKind kind)
        {
            var spirit = MakeSpirit(kind, out var home);
            try
            {
                float max = 0f;
                for (int i = 0; i < 400; i++)
                {
                    Invoke(spirit, "EnterState", SpiritState.Wander);
                    var target = (Vector3)GetField(spirit, "_moveTo");
                    var d = new Vector2(target.x, target.z); // homeCenterは原点
                    max = Mathf.Max(max, d.magnitude);
                }
                return max;
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void PersonalityRange_AppliesToObserveTreeMovesToo_NotOnlyWander()
        {
            // ★Play Modeでの実測から見つかった問題への回帰テスト。
            //   CalmはめったにWanderしないため、Wanderにだけ範囲倍率を掛けても
            //   ObserveTreeの移動でhome全域まで出てしまい「中央寄りで狭い」が見えなかった。
            //   自発移動の最終目的地そのものへ倍率を掛けることで解決している。
            const float extent = 1.5f;
            float limit = extent * Calm.WanderRadiusScale + 0.0001f;

            var spirit = MakeSpirit(SpiritPersonalityKind.Calm, out var home);
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    Invoke(spirit, "EnterState", SpiritState.ObserveTree);
                    var target = (Vector3)GetField(spirit, "_moveTo");

                    Assert.LessOrEqual(Mathf.Abs(target.x), limit,
                        $"ObserveTreeの目的地がCalmの縮小範囲を超えた: x={target.x}");
                    Assert.LessOrEqual(Mathf.Abs(target.z), limit,
                        $"ObserveTreeの目的地がCalmの縮小範囲を超えた: z={target.z}");
                }
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void PersonalityRange_Curious_StillCoversFullHome()
        {
            const float extent = 1.5f;
            var spirit = MakeSpirit(SpiritPersonalityKind.Curious, out var home);
            try
            {
                float max = 0f;
                for (int i = 0; i < 400; i++)
                {
                    Invoke(spirit, "EnterState", SpiritState.Wander);
                    var target = (Vector3)GetField(spirit, "_moveTo");

                    Assert.LessOrEqual(Mathf.Abs(target.x), extent + 0.0001f, "Curiousがhome範囲を超えた");
                    Assert.LessOrEqual(Mathf.Abs(target.z), extent + 0.0001f, "Curiousがhome範囲を超えた");
                    max = Mathf.Max(max, Mathf.Abs(target.x));
                }

                Assert.Greater(max, extent * Calm.WanderRadiusScale,
                    "Curiousの行動範囲がCalmの縮小範囲を超えていない（差が出ていない）");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void PersonalityProfile_IsActuallyUsedForIdleTransitions()
        {
            // 実際のUpdate経路（EnterState + DecideNextState）で、Calmの方がSleepを多く選ぶ。
            int calmSleep    = CountSleepChoices(SpiritPersonalityKind.Calm);
            int curiousSleep = CountSleepChoices(SpiritPersonalityKind.Curious);

            Assert.Greater(calmSleep, curiousSleep,
                $"Calm Sleep={calmSleep} / Curious Sleep={curiousSleep}");
        }

        private static int CountSleepChoices(SpiritPersonalityKind kind)
        {
            var spirit = MakeSpirit(kind, out var home);
            try
            {
                var profile = (SpiritPersonalityProfile)GetField(spirit, "_profile");
                int sleeps = 0;
                const int samples = 500;

                for (int i = 0; i < samples; i++)
                {
                    float r = i / (float)(samples - 1);
                    if (SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r,
                            profile.WanderWeight, profile.ObserveWeight, profile.SleepWeight)
                        == SpiritState.Sleep) sleeps++;
                }
                return sleeps;
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void Personality_DoesNotChangeStimulusAcceptance()
        {
            // Stage 11の受理条件（home外の森は無視・知覚距離外の花は無視）は性格で変わらない。
            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
            {
                var spirit = MakeSpirit(kind, out var home);
                var far    = new List<HexTile> { MakeTileAt(new Vector3(80f, 0f, 80f)) };
                try
                {
                    var accepts = typeof(ForestSpirit).GetMethod("Accepts", Priv);
                    Assert.IsNotNull(accepts);

                    Assert.IsTrue((bool)accepts.Invoke(spirit, new object[]
                    {
                        new SpiritStimulus(SpiritStimulusKind.ForestGrew, Vector3.zero, home)
                    }), $"{kind}: 自分のhome森の成長が受理されない");

                    Assert.IsFalse((bool)accepts.Invoke(spirit, new object[]
                    {
                        new SpiritStimulus(SpiritStimulusKind.ForestGrew, new Vector3(80f, 0f, 80f), far)
                    }), $"{kind}: home外の森が受理された");

                    Assert.IsFalse((bool)accepts.Invoke(spirit, new object[]
                    {
                        new SpiritStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(80f, 0f, 80f), null)
                    }), $"{kind}: 知覚距離外の花が受理された");

                    Assert.IsTrue((bool)accepts.Invoke(spirit, new object[]
                    {
                        new SpiritStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f), null)
                    }), $"{kind}: 近くの花が受理されない");
                }
                finally { Teardown(spirit, home); DestroyTiles(far); }
            }
        }

        [Test]
        public void Personality_DoesNotChangeSleepAndStretchInterruption()
        {
            // Stage 11の「Sleep/Stretch中は刺激で中断しない」は性格に依存しない純粋関数のまま。
            Assert.IsFalse(SpiritBehaviorMath.CanBeInterruptedByStimulus(SpiritState.Sleep));
            Assert.IsFalse(SpiritBehaviorMath.CanBeInterruptedByStimulus(SpiritState.Stretch));

            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
            {
                var spirit = MakeSpirit(kind, out var home);
                try
                {
                    Invoke(spirit, "EnterState", SpiritState.Sleep);
                    Invoke(spirit, "HandleStimulus",
                        new SpiritStimulus(SpiritStimulusKind.ForestGrew, Vector3.zero, home));

                    Assert.AreEqual(SpiritState.Sleep, spirit.CurrentState,
                        $"{kind}: Sleep中に刺激で中断された");

                    Invoke(spirit, "EnterState", SpiritState.Stretch);
                    Invoke(spirit, "HandleStimulus",
                        new SpiritStimulus(SpiritStimulusKind.ForestGrew, Vector3.zero, home));

                    Assert.AreEqual(SpiritState.Stretch, spirit.CurrentState,
                        $"{kind}: Stretch中に刺激で中断された");
                }
                finally { Teardown(spirit, home); }
            }
        }

        [Test]
        public void Personality_KeepsVisualResetAfterReact()
        {
            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
            {
                var spirit = MakeSpirit(kind, out var home);
                try
                {
                    Invoke(spirit, "EnterState", SpiritState.Idle);
                    Invoke(spirit, "HandleStimulus",
                        new SpiritStimulus(SpiritStimulusKind.ForestGrew, new Vector3(2f, 0f, 0f), home));
                    Assert.AreEqual(SpiritState.React, spirit.CurrentState, $"{kind}: Reactへ入らなかった");

                    // React終了 → Idleへ戻ると表示が完全に戻る。
                    Invoke(spirit, "EnterState", SpiritState.Idle);

                    var visual = (Transform)GetField(spirit, "_bodyRoot");
                    Assert.IsNotNull(visual);
                    Assert.AreEqual(Quaternion.identity, visual.localRotation, $"{kind}: 傾きが残った");
                    Assert.AreEqual(Vector3.one,         visual.localScale,    $"{kind}: 変形が残った");
                    Assert.AreEqual(Vector3.zero,        visual.localPosition, $"{kind}: 跳ねの高さが残った");
                }
                finally { Teardown(spirit, home); }
            }
        }

        [Test]
        public void Personality_ReactUsesFamiliarityScale_NotIdleDurationScale()
        {
            // Reactの長さはFamiliarity由来の_reactScaleだけが調整し、
            // 性格のIdle倍率は掛からない（両者を混ぜない）。
            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
            {
                var spirit = MakeSpirit(kind, out var home);
                try
                {
                    float reactMin = (float)GetField(spirit, "_reactMinDuration");

                    for (int i = 0; i < 60; i++)
                    {
                        Invoke(spirit, "EnterState", SpiritState.React);
                        float d = (float)GetField(spirit, "_stateDuration");

                        Assert.GreaterOrEqual(d, reactMin - 0.0001f, $"{kind}: Reactが最短時間を下回った");
                        Assert.LessOrEqual(d, SpiritBehaviorMath.ReactMaxDuration + 0.0001f,
                            $"{kind}: Reactに性格のIdle倍率が漏れている");
                    }
                }
                finally { Teardown(spirit, home); }
            }
        }

        // ══ 42・49. Spawner経由の決定性（購読順・生成時刺激） ═════════════
        //    ★ForestSpiritSpawner.OnForestGrowはEditModeでも購読順を再現できるが、
        //      Stage 12でEditModeが実際の順序依存を隠した前例があるため、
        //      同じ内容をPlay Modeの実シーンでも確認している（報告に実測値を記載）。

        private static ForestSpiritSpawner MakeSpawner(
            ForestSpiritSpawner.PersonalitySelectionMode mode = ForestSpiritSpawner.PersonalitySelectionMode.DeterministicFromHome,
            SpiritPersonalityKind fixedKind = SpiritPersonalityKind.Calm)
        {
            var go = new GameObject("TestForestSpiritSpawner");
            var spawner = go.AddComponent<ForestSpiritSpawner>();

            typeof(ForestSpiritSpawner).GetField("_personalityMode", Priv).SetValue(spawner, mode);
            typeof(ForestSpiritSpawner).GetField("_fixedPersonality", Priv).SetValue(spawner, fixedKind);

            // 本番の既定は4枚（Stage 15）だが、ここでは性格の挙動を見ているため小さな森でも生成させる。
            typeof(ForestSpiritSpawner).GetField("_minClusterSizeToSpawn", Priv).SetValue(spawner, 1);

            Invoke(spawner, "OnEnable"); // EditModeでは自動発火しないため明示的に購読させる
            return spawner;
        }

        private static void DestroySpawner(ForestSpiritSpawner spawner)
        {
            if (spawner == null) return;
            Invoke(spawner, "OnDisable");
            Object.DestroyImmediate(spawner.gameObject);
        }

        private static void PublishForestGrowth(List<HexTile> tiles)
        {
            var metrics = new ForestGrowthMetrics(largestClusterSize: tiles.Count, totalForestTiles: tiles.Count);
            ElfVillage.Core.EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                terrainType: null, anchor: ElfVillage.HexGrid.HexCoord.Zero,
                affectedTiles: tiles, metrics: metrics));
        }

        private static ForestSpirit GetSpawnedSpirit(ForestSpiritSpawner spawner)
            => (ForestSpirit)typeof(ForestSpiritSpawner).GetField("_spirit", Priv).GetValue(spawner);

        private static List<HexTile> MakeForestAt(Vector3 origin)
            => new List<HexTile>
            {
                MakeTileAt(origin),
                MakeTileAt(origin + new Vector3(1.5f, 0f, 0f)),
                MakeTileAt(origin + new Vector3(0.75f, 0f, 1.3f)),
            };

        [Test]
        public void Spawner_DeterministicMode_SameHome_AlwaysSamePersonality()
        {
            var results = new List<SpiritPersonalityKind>();
            var origin  = new Vector3(6.5f, 0f, -3.25f);

            for (int run = 0; run < 5; run++)
            {
                var spawner = MakeSpawner();
                var forest  = MakeForestAt(origin);
                try
                {
                    PublishForestGrowth(forest);
                    var spirit = GetSpawnedSpirit(spawner);
                    Assert.IsNotNull(spirit, "精霊が生成されなかった");
                    results.Add(spirit.Personality);
                }
                finally { DestroySpawner(spawner); DestroyTiles(forest); }
            }

            foreach (var r in results)
                Assert.AreEqual(results[0], r, "同じhome森なのに性格がぶれた");
        }

        [Test]
        public void Spawner_SubscriptionOrder_DoesNotChangePersonality()
        {
            // ★EventBusの購読順（Spawnerが先／Relayが先）で性格が変わらないこと。
            var origin = new Vector3(-4.5f, 0f, 8.25f);

            SpiritPersonalityKind spawnerFirst = RunWithOrder(spawnerBeforeRelay: true,  origin);
            SpiritPersonalityKind relayFirst   = RunWithOrder(spawnerBeforeRelay: false, origin);

            Assert.AreEqual(spawnerFirst, relayFirst,
                $"購読順で性格が変わった（Spawner先={spawnerFirst} / Relay先={relayFirst}）");
        }

        private static SpiritPersonalityKind RunWithOrder(bool spawnerBeforeRelay, Vector3 origin)
        {
            ForestSpiritSpawner spawner = null;
            SpiritStimulusRelay relay   = null;
            var forest = MakeForestAt(origin);

            try
            {
                if (spawnerBeforeRelay)
                {
                    spawner = MakeSpawner();
                    relay   = MakeRelay();
                }
                else
                {
                    relay   = MakeRelay();
                    spawner = MakeSpawner();
                }

                PublishForestGrowth(forest);
                var spirit = GetSpawnedSpirit(spawner);
                Assert.IsNotNull(spirit, "精霊が生成されなかった");
                return spirit.Personality;
            }
            finally
            {
                DestroySpawner(spawner);
                if (relay != null) { Invoke(relay, "OnDisable"); Object.DestroyImmediate(relay.gameObject); }
                DestroyTiles(forest);
            }
        }

        private static SpiritStimulusRelay MakeRelay()
        {
            var go = new GameObject("TestSpiritStimulusRelay");
            var relay = go.AddComponent<SpiritStimulusRelay>();
            Invoke(relay, "OnEnable");
            return relay;
        }

        [Test]
        public void Spawner_FixedMode_UsesConfiguredPersonality_ThroughNormalInitializePath()
        {
            // ★Fixedでも生成経路（SpawnSpirit → Initialize → 生成時刺激）は同じであること。
            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
            {
                var spawner = MakeSpawner(ForestSpiritSpawner.PersonalitySelectionMode.Fixed, kind);
                var forest  = MakeForestAt(new Vector3(2f, 0f, 2f));
                try
                {
                    PublishForestGrowth(forest);

                    var spirit = GetSpawnedSpirit(spawner);
                    Assert.IsNotNull(spirit, $"{kind}: 精霊が生成されなかった");
                    Assert.AreEqual(kind, spirit.Personality);

                    // Initializeが通っている証拠: home森・Visualが構築されている。
                    Assert.IsNotNull(GetField(spirit, "_bodyRoot"), $"{kind}: Visualが未構築（Initializeを通っていない）");
                    var homeTiles = (List<HexTile>)GetField(spirit, "_homeTiles");
                    Assert.AreEqual(forest.Count, homeTiles.Count, $"{kind}: home森が設定されていない");
                }
                finally { DestroySpawner(spawner); DestroyTiles(forest); }
            }
        }

        [Test]
        public void Spawner_SpawnStimulus_StaysDeterministic_ForBothPersonalities()
        {
            // Stage 12の保証（生成時に必ず1回だけ森の成長を体験する）が性格で変わらないこと。
            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
                foreach (var spawnerFirst in new[] { true, false })
                {
                    ForestSpiritSpawner spawner = null;
                    SpiritStimulusRelay relay   = null;
                    var forest = MakeForestAt(new Vector3(1f, 0f, 1f));

                    try
                    {
                        if (spawnerFirst) { spawner = MakeSpawner(ForestSpiritSpawner.PersonalitySelectionMode.Fixed, kind); relay = MakeRelay(); }
                        else              { relay = MakeRelay(); spawner = MakeSpawner(ForestSpiritSpawner.PersonalitySelectionMode.Fixed, kind); }

                        PublishForestGrowth(forest);

                        var spirit = GetSpawnedSpirit(spawner);
                        Assert.IsNotNull(spirit);

                        var expectedGain = SpiritPersonalityProfile.For(kind).FamiliarityGain;
                        float familiarity = GetFamiliarity(spirit, SpiritStimulusKind.ForestGrew);

                        Assert.AreEqual(expectedGain, familiarity, 0.0001f,
                            $"{kind} spawnerFirst={spawnerFirst}: 生成時刺激が1回だけ記憶されていない（実測 {familiarity:F3}）");

                        Assert.AreEqual(SpiritState.React, spirit.CurrentState,
                            $"{kind} spawnerFirst={spawnerFirst}: 生成時刺激でReactへ入っていない");
                    }
                    finally
                    {
                        DestroySpawner(spawner);
                        if (relay != null) { Invoke(relay, "OnDisable"); Object.DestroyImmediate(relay.gameObject); }
                        DestroyTiles(forest);
                    }
                }
        }

        private static float GetFamiliarity(ForestSpirit spirit, SpiritStimulusKind kind)
        {
            var memory   = GetField(spirit, "_memory");
            var halfLife = (float)GetField(spirit, "_familiarityHalfLife");
            return (float)memory.GetType().GetMethod("GetFamiliarity")
                .Invoke(memory, new object[] { kind, Time.time, halfLife });
        }
    }

    /// <summary>テスト内で配列へ値を足すための小さなヘルパー。</summary>
    internal static class FloatArrayExtensions
    {
        public static float[] Concat(this float[] source, float extra)
        {
            var result = new float[source.Length + 1];
            source.CopyTo(result, 0);
            result[source.Length] = extra;
            return result;
        }
    }
}
