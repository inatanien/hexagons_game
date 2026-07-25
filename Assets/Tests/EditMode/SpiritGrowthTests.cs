// 役割: Stage 14「成長（Growth）」の検証。
//       成長段階の導出・見た目・段階遷移は純粋関数として分離してあるため決定論的に検証でき、
//       累積体験（LifetimeExperience）の加算条件はForestSpirit側の受理経路を通して確認する。
//       ライフサイクル・Visual更新・演出の保留と中断はPlayMode側で検証する。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Spirits;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class SpiritGrowthTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly float[] BadFloats =
            { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, -9999f };

        private static object GetField(object target, string name)
            => target.GetType().GetField(name, Priv).GetValue(target);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, Priv).SetValue(target, value);

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

        private static ForestSpirit MakeSpirit(out List<HexTile> home,
                                                SpiritPersonalityKind personality = SpiritPersonalityKind.Calm)
        {
            home = new List<HexTile> { MakeTileAt(Vector3.zero), MakeTileAt(new Vector3(1f, 0f, 0f)) };
            var go = new GameObject("TestForestSpirit");
            var spirit = go.AddComponent<ForestSpirit>();
            spirit.Initialize(home, Vector3.zero, 1.5f, 1.5f, 0.5f, personality);
            Invoke(spirit, "OnEnable");
            return spirit;
        }

        private static void Teardown(ForestSpirit spirit, IEnumerable<HexTile> tiles)
        {
            if (spirit != null)
            {
                Invoke(spirit, "OnDisable");
                Object.DestroyImmediate(spirit.gameObject);
            }
            DestroyTiles(tiles);
        }

        private static SpiritMemory MemoryOf(ForestSpirit spirit) => (SpiritMemory)GetField(spirit, "_memory");
        private static float ExperienceOf(ForestSpirit spirit) => MemoryOf(spirit).GetLifetimeExperience();

        private static void Publish(SpiritStimulusKind kind, Vector3 pos, IReadOnlyList<HexTile> tiles = null)
            => EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(kind, pos, tiles)));

        // ══ LifetimeExperience の基本 ═══════════════════════════════════

        [Test]
        public void Experience_NewMemory_StartsAtZero()
            => Assert.AreEqual(0f, new SpiritMemory().GetLifetimeExperience(), 0.0001f);

        [Test]
        public void Experience_NewSpirit_StartsAtZeroAndSprout()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Assert.AreEqual(0f, ExperienceOf(spirit), 0.0001f);
                Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage,
                    "新規個体はSproutから始まるはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void Experience_OneReinforce_IncreasesByExactlyOne()
        {
            var memory = new SpiritMemory();
            memory.Reinforce(SpiritStimulusKind.ForestGrew, 0f, 60f, gain: 1f, maximum: 4f);
            Assert.AreEqual(1f, memory.GetLifetimeExperience(), 0.0001f);

            memory.Reinforce(SpiritStimulusKind.FlowerBloomed, 0f, 60f, gain: 1f, maximum: 4f);
            Assert.AreEqual(2f, memory.GetLifetimeExperience(), 0.0001f, "刺激種類が違っても合算されるはず");
        }

        [Test]
        public void Experience_IsIndependentOfFamiliarityGain()
        {
            // ★FamiliarityGain（慣れやすさ）は成長速度へ影響しない。
            var slow = new SpiritMemory();
            var fast = new SpiritMemory();

            for (int i = 0; i < 5; i++)
            {
                slow.Reinforce(SpiritStimulusKind.ForestGrew, i, 60f, gain: 0.6f, maximum: 4f);
                fast.Reinforce(SpiritStimulusKind.ForestGrew, i, 60f, gain: 1.5f, maximum: 4f);
            }

            Assert.AreEqual(5f, slow.GetLifetimeExperience(), 0.0001f);
            Assert.AreEqual(5f, fast.GetLifetimeExperience(), 0.0001f);
            Assert.AreEqual(slow.GetLifetimeExperience(), fast.GetLifetimeExperience(), 0.0001f,
                "gainが違っても累積体験は同じであるべき");
        }

        [Test]
        public void Experience_UnknownStimulusKind_DoesNotIncrease()
        {
            var memory = new SpiritMemory();
            memory.Reinforce((SpiritStimulusKind)999, 0f, 60f, gain: 1f, maximum: 4f);
            Assert.AreEqual(0f, memory.GetLifetimeExperience(), 0.0001f);
        }

        [Test]
        public void Experience_FamiliarityDecay_DoesNotReduceExperience()
        {
            var memory = new SpiritMemory();
            memory.Reinforce(SpiritStimulusKind.FlowerBloomed, now: 0f, halfLifeSeconds: 60f, gain: 4f, maximum: 4f);

            float before = memory.GetLifetimeExperience();
            Assert.AreEqual(1f, before, 0.0001f);

            // 半減期10回ぶん経過させてもFamiliarityは薄れるが、累積体験は変わらない。
            float decayed = memory.GetFamiliarity(SpiritStimulusKind.FlowerBloomed, 600f, 60f);
            Assert.Less(decayed, 0.01f, "Familiarityは減衰しているはず");
            Assert.AreEqual(before, memory.GetLifetimeExperience(), 0.0001f,
                "Familiarityの減衰で累積体験が減ってはいけない");
        }

        [Test]
        public void Experience_StopsAtMaximum()
        {
            var memory = new SpiritMemory();
            SetField(memory, "_lifetimeExperience", SpiritGrowthMath.MaxLifetimeExperience);

            for (int i = 0; i < 10; i++)
                memory.Reinforce(SpiritStimulusKind.ForestGrew, i, 60f, gain: 1f, maximum: 4f);

            Assert.AreEqual(SpiritGrowthMath.MaxLifetimeExperience, memory.GetLifetimeExperience(), 0.001f,
                "上限を超えて増えてはいけない");
        }

        [Test]
        public void Experience_CorruptedStoredValue_IsSanitizedOnRead()
        {
            foreach (var bad in BadFloats)
            {
                var memory = new SpiritMemory();
                SetField(memory, "_lifetimeExperience", bad);

                float v = memory.GetLifetimeExperience();
                Assert.IsTrue(float.IsFinite(v), $"{bad} で非有限値が返った");
                Assert.GreaterOrEqual(v, 0f, $"{bad} で負値が返った");
                Assert.LessOrEqual(v, SpiritGrowthMath.MaxLifetimeExperience, $"{bad} で上限を超えた");
            }
        }

        [Test]
        public void Experience_PositiveInfinityStored_SaturatesToMaximum()
        {
            var memory = new SpiritMemory();
            SetField(memory, "_lifetimeExperience", float.PositiveInfinity);
            Assert.AreEqual(SpiritGrowthMath.MaxLifetimeExperience, memory.GetLifetimeExperience(), 0.001f);
        }

        // ══ ClampExperience ═════════════════════════════════════════════

        [Test]
        public void ClampExperience_HandlesAllInvalidInputs()
        {
            Assert.AreEqual(0f, SpiritGrowthMath.ClampExperience(float.NaN), 0.0001f, "NaN→0");
            Assert.AreEqual(0f, SpiritGrowthMath.ClampExperience(float.NegativeInfinity), 0.0001f, "-Inf→0");
            Assert.AreEqual(0f, SpiritGrowthMath.ClampExperience(-5f), 0.0001f, "負値→0");
            Assert.AreEqual(SpiritGrowthMath.MaxLifetimeExperience,
                SpiritGrowthMath.ClampExperience(float.PositiveInfinity), 0.001f, "+Inf→上限");
            Assert.AreEqual(SpiritGrowthMath.MaxLifetimeExperience,
                SpiritGrowthMath.ClampExperience(SpiritGrowthMath.MaxLifetimeExperience * 10f), 0.001f, "上限超え→上限");
            Assert.AreEqual(12.5f, SpiritGrowthMath.ClampExperience(12.5f), 0.0001f, "正常値はそのまま");
            Assert.AreEqual(0f, SpiritGrowthMath.ClampExperience(0f), 0.0001f);
        }

        // ══ ComputeGrowthStage ══════════════════════════════════════════

        private const float TFluff = 8f;
        private const float TBloom = 20f;

        private static SpiritGrowthStage StageOf(float exp)
            => SpiritGrowthMath.ComputeGrowthStage(exp, TFluff, TBloom);

        [Test]
        public void Stage_SproutBoundary()
        {
            Assert.AreEqual(SpiritGrowthStage.Sprout, StageOf(0f));
            Assert.AreEqual(SpiritGrowthStage.Sprout, StageOf(TFluff - 0.01f), "閾値直前はSprout");
            Assert.AreEqual(SpiritGrowthStage.Fluff,  StageOf(TFluff),         "閾値と一致でFluff");
            Assert.AreEqual(SpiritGrowthStage.Fluff,  StageOf(TFluff + 0.01f), "閾値直後はFluff");
        }

        [Test]
        public void Stage_BloomBoundary()
        {
            Assert.AreEqual(SpiritGrowthStage.Fluff, StageOf(TBloom - 0.01f), "閾値直前はFluff");
            Assert.AreEqual(SpiritGrowthStage.Bloom, StageOf(TBloom),         "閾値と一致でBloom");
            Assert.AreEqual(SpiritGrowthStage.Bloom, StageOf(TBloom + 0.01f), "閾値直後はBloom");
            Assert.AreEqual(SpiritGrowthStage.Bloom, StageOf(999999f),        "最大付近でもBloom止まり");
        }

        [Test]
        public void Stage_NeverRegressesAsExperienceGrows()
        {
            int previous = -1;
            for (float exp = 0f; exp <= 40f; exp += 0.1f)
            {
                int current = (int)StageOf(exp);
                Assert.GreaterOrEqual(current, previous, $"経験={exp} で段階が後退した");
                previous = current;
            }
        }

        [Test]
        public void Stage_InvalidExperience_IsSprout()
        {
            foreach (var bad in new[] { float.NaN, float.NegativeInfinity, -50f })
                Assert.AreEqual(SpiritGrowthStage.Sprout, StageOf(bad), $"{bad} でSprout以外になった");

            // +Infinityは上限へ飽和するため、閾値を超えてBloomになる（安全な挙動）。
            Assert.AreEqual(SpiritGrowthStage.Bloom, StageOf(float.PositiveInfinity));
        }

        [Test]
        public void Stage_InvertedThresholds_StayMonotonic()
        {
            // Bloomの閾値がFluffより小さい設定でも、経験が増えて段階が下がってはいけない。
            int previous = -1;
            for (float exp = 0f; exp <= 40f; exp += 0.1f)
            {
                int current = (int)SpiritGrowthMath.ComputeGrowthStage(exp, thresholdFluff: 20f, thresholdBloom: 5f);
                Assert.GreaterOrEqual(current, previous, $"経験={exp} で段階が後退した（逆転閾値）");
                previous = current;
            }
        }

        [Test]
        public void Stage_DuplicateThresholds_AreHandledSafely()
        {
            Assert.AreEqual(SpiritGrowthStage.Sprout,
                SpiritGrowthMath.ComputeGrowthStage(7f, 8f, 8f));
            // 閾値が同じなら中間段階は存在せず、一気にBloomになる（単調性は保たれる）。
            Assert.AreEqual(SpiritGrowthStage.Bloom,
                SpiritGrowthMath.ComputeGrowthStage(8f, 8f, 8f));

            int previous = -1;
            for (float exp = 0f; exp <= 20f; exp += 0.1f)
            {
                int current = (int)SpiritGrowthMath.ComputeGrowthStage(exp, 8f, 8f);
                Assert.GreaterOrEqual(current, previous);
                previous = current;
            }
        }

        [Test]
        public void Stage_NonFiniteThresholds_AreUnreachable()
        {
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity })
            {
                Assert.AreEqual(SpiritGrowthStage.Sprout,
                    SpiritGrowthMath.ComputeGrowthStage(999999f, bad, bad),
                    $"到達不能な閾値({bad})なのに段階が上がった");

                Assert.AreEqual(SpiritGrowthStage.Fluff,
                    SpiritGrowthMath.ComputeGrowthStage(999999f, 8f, bad),
                    $"Bloomの閾値が到達不能({bad})なのにBloomになった");
            }
        }

        [Test]
        public void Stage_NegativeThresholds_AreTreatedAsAlreadyReached()
        {
            Assert.AreEqual(SpiritGrowthStage.Bloom,
                SpiritGrowthMath.ComputeGrowthStage(0f, -5f, -1f),
                "負の閾値は0扱いで最初から到達済みになるはず");
        }

        [Test]
        public void Stage_IsDeterministic()
        {
            for (float exp = 0f; exp <= 30f; exp += 0.7f)
            {
                var first = StageOf(exp);
                for (int i = 0; i < 5; i++) Assert.AreEqual(first, StageOf(exp));
            }
        }

        // ══ ComputeGrowthVisual ═════════════════════════════════════════

        [Test]
        public void Visual_Sprout()
        {
            var v = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Sprout);
            Assert.AreEqual(4, v.FluffLayers);
            Assert.AreEqual(0.85f, v.FluffScale, 0.0001f);
        }

        [Test]
        public void Visual_Fluff()
        {
            var v = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Fluff);
            Assert.AreEqual(6, v.FluffLayers);
            Assert.AreEqual(1.00f, v.FluffScale, 0.0001f);
        }

        [Test]
        public void Visual_Bloom()
        {
            var v = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Bloom);
            Assert.AreEqual(9, v.FluffLayers);
            Assert.AreEqual(1.20f, v.FluffScale, 0.0001f);
        }

        [Test]
        public void Visual_IncreasesMonotonicallyWithStage()
        {
            var s = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Sprout);
            var f = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Fluff);
            var b = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Bloom);

            Assert.Less(s.FluffLayers, f.FluffLayers);
            Assert.Less(f.FluffLayers, b.FluffLayers);
            Assert.Less(s.FluffScale,  f.FluffScale);
            Assert.Less(f.FluffScale,  b.FluffScale);
        }

        [Test]
        public void Visual_NeverExceedsPreallocatedFluffCount()
        {
            // BuildVisualが確保する数を超えると、表示できない毛玉が要求されてしまう。
            foreach (SpiritGrowthStage stage in System.Enum.GetValues(typeof(SpiritGrowthStage)))
            {
                var v = SpiritGrowthMath.ComputeGrowthVisual(stage);
                Assert.LessOrEqual(v.FluffLayers, SpiritGrowthMath.MaxFluffLayers,
                    $"{stage} が事前確保数({SpiritGrowthMath.MaxFluffLayers})を超えている");
                Assert.GreaterOrEqual(v.FluffLayers, 1, $"{stage} の毛玉が0個以下");
                Assert.IsTrue(float.IsFinite(v.FluffScale) && v.FluffScale > 0f, $"{stage} のScaleが不正");
            }
        }

        [Test]
        public void Visual_UnknownStage_IsClamped()
        {
            var below = SpiritGrowthMath.ComputeGrowthVisual((SpiritGrowthStage)(-7));
            var sprout = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Sprout);
            Assert.AreEqual(sprout.FluffLayers, below.FluffLayers, "負の段階はSproutへ倒れるはず");
            Assert.AreEqual(sprout.FluffScale,  below.FluffScale, 0.0001f);

            var above = SpiritGrowthMath.ComputeGrowthVisual((SpiritGrowthStage)999);
            var bloom = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Bloom);
            Assert.AreEqual(bloom.FluffLayers, above.FluffLayers, "Bloom超えはBloomへ倒れるはず");
            Assert.AreEqual(bloom.FluffScale,  above.FluffScale, 0.0001f);
        }

        [Test]
        public void ClampStage_HandlesOutOfRange()
        {
            Assert.AreEqual(SpiritGrowthStage.Sprout, SpiritGrowthMath.ClampStage((SpiritGrowthStage)(-99)));
            Assert.AreEqual(SpiritGrowthStage.Bloom,  SpiritGrowthMath.ClampStage((SpiritGrowthStage)99));
            Assert.AreEqual(SpiritGrowthStage.Fluff,  SpiritGrowthMath.ClampStage(SpiritGrowthStage.Fluff));
        }

        // ══ ShouldQueueGrowthVisual / ResolveGrowthTransition ═══════════

        [Test]
        public void ShouldQueue_OnlyWhenStageIncreases()
        {
            Assert.IsTrue(SpiritGrowthMath.ShouldQueueGrowthVisual(SpiritGrowthStage.Sprout, SpiritGrowthStage.Fluff));
            Assert.IsTrue(SpiritGrowthMath.ShouldQueueGrowthVisual(SpiritGrowthStage.Sprout, SpiritGrowthStage.Bloom));
            Assert.IsTrue(SpiritGrowthMath.ShouldQueueGrowthVisual(SpiritGrowthStage.Fluff,  SpiritGrowthStage.Bloom));

            Assert.IsFalse(SpiritGrowthMath.ShouldQueueGrowthVisual(SpiritGrowthStage.Fluff, SpiritGrowthStage.Fluff),
                "同じ段階では予約しない");
            Assert.IsFalse(SpiritGrowthMath.ShouldQueueGrowthVisual(SpiritGrowthStage.Bloom, SpiritGrowthStage.Sprout),
                "後退では予約しない");
        }

        [Test]
        public void ShouldQueue_UnknownStages_AreClamped()
        {
            Assert.IsFalse(SpiritGrowthMath.ShouldQueueGrowthVisual(SpiritGrowthStage.Bloom, (SpiritGrowthStage)999),
                "Bloom超えはBloom扱いなので予約しない");
            Assert.IsTrue(SpiritGrowthMath.ShouldQueueGrowthVisual((SpiritGrowthStage)(-5), SpiritGrowthStage.Fluff));
        }

        [Test]
        public void Resolve_AdvancesExactlyOneStage()
        {
            Assert.AreEqual(SpiritGrowthStage.Fluff,
                SpiritGrowthMath.ResolveGrowthTransition(SpiritGrowthStage.Sprout, SpiritGrowthStage.Bloom),
                "2段階跨ぎでも1段階だけ進むはず");

            Assert.AreEqual(SpiritGrowthStage.Fluff,
                SpiritGrowthMath.ResolveGrowthTransition(SpiritGrowthStage.Sprout, SpiritGrowthStage.Fluff));

            Assert.AreEqual(SpiritGrowthStage.Bloom,
                SpiritGrowthMath.ResolveGrowthTransition(SpiritGrowthStage.Fluff, SpiritGrowthStage.Bloom));
        }

        [Test]
        public void Resolve_DoesNotRegressOrExceedBloom()
        {
            Assert.AreEqual(SpiritGrowthStage.Bloom,
                SpiritGrowthMath.ResolveGrowthTransition(SpiritGrowthStage.Bloom, SpiritGrowthStage.Sprout),
                "pendingが下でも後退しない");

            Assert.AreEqual(SpiritGrowthStage.Fluff,
                SpiritGrowthMath.ResolveGrowthTransition(SpiritGrowthStage.Fluff, SpiritGrowthStage.Fluff),
                "同じならそのまま");

            Assert.AreEqual(SpiritGrowthStage.Bloom,
                SpiritGrowthMath.ResolveGrowthTransition(SpiritGrowthStage.Bloom, (SpiritGrowthStage)999),
                "Bloomを超えない");
        }

        [Test]
        public void Resolve_RepeatedCalls_ReachPendingOneStepAtATime()
        {
            var current = SpiritGrowthStage.Sprout;
            const SpiritGrowthStage pending = SpiritGrowthStage.Bloom;

            current = SpiritGrowthMath.ResolveGrowthTransition(current, pending);
            Assert.AreEqual(SpiritGrowthStage.Fluff, current, "1回目でFluff");

            current = SpiritGrowthMath.ResolveGrowthTransition(current, pending);
            Assert.AreEqual(SpiritGrowthStage.Bloom, current, "2回目でBloom");

            current = SpiritGrowthMath.ResolveGrowthTransition(current, pending);
            Assert.AreEqual(SpiritGrowthStage.Bloom, current, "3回目以降は変わらない");
        }

        // ══ 受理経路との統合（拒否された刺激で増えないこと）═══════════════

        [Test]
        public void Experience_AcceptedStimulus_Increases()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Assert.AreEqual(0f, ExperienceOf(spirit), 0.0001f);

                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));
                Assert.AreEqual(1f, ExperienceOf(spirit), 0.0001f, "近くの花は受理されて経験が増えるはず");

                Invoke(spirit, "EnterState", SpiritState.Idle);
                Publish(SpiritStimulusKind.ForestGrew, Vector3.zero, home);
                Assert.AreEqual(2f, ExperienceOf(spirit), 0.0001f, "自分のhome森の成長も受理されるはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void Experience_RejectedStimuli_DoNotIncrease()
        {
            var spirit = MakeSpirit(out var home);
            var far    = new List<HexTile> { MakeTileAt(new Vector3(80f, 0f, 80f)) };
            try
            {
                Invoke(spirit, "EnterState", SpiritState.Idle);

                // home外の森
                Publish(SpiritStimulusKind.ForestGrew, new Vector3(80f, 0f, 80f), far);
                Assert.AreEqual(0f, ExperienceOf(spirit), 0.0001f, "home外の森で増えた");

                // 知覚距離外の花
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(80f, 0f, 80f));
                Assert.AreEqual(0f, ExperienceOf(spirit), 0.0001f, "遠方の花で増えた");

                // 未知の刺激種類
                Publish((SpiritStimulusKind)999, new Vector3(1f, 0f, 0f));
                Assert.AreEqual(0f, ExperienceOf(spirit), 0.0001f, "未知の刺激で増えた");

                // Sleep中
                Invoke(spirit, "EnterState", SpiritState.Sleep);
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));
                Assert.AreEqual(0f, ExperienceOf(spirit), 0.0001f, "Sleep中に増えた");

                // Stretch中
                Invoke(spirit, "EnterState", SpiritState.Stretch);
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));
                Assert.AreEqual(0f, ExperienceOf(spirit), 0.0001f, "Stretch中に増えた");
            }
            finally { Teardown(spirit, home); DestroyTiles(far); }
        }

        [Test]
        public void Experience_SamePriorityDuringReact_IsRejected()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Invoke(spirit, "EnterState", SpiritState.Idle);
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));
                Assert.AreEqual(SpiritState.React, spirit.CurrentState);
                Assert.AreEqual(1f, ExperienceOf(spirit), 0.0001f);

                // React中の同優先度は拒否される（Stage 11）→ 経験も増えない。
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));
                Assert.AreEqual(1f, ExperienceOf(spirit), 0.0001f,
                    "React中の同優先度で経験が増えてしまった");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void Experience_CalmAndCurious_GrowAtSameRate()
        {
            var calm    = MakeSpirit(out var calmHome,    SpiritPersonalityKind.Calm);
            var curious = MakeSpirit(out var curiousHome, SpiritPersonalityKind.Curious);
            try
            {
                // 両方のhomeを含む位置で刺激を出すのではなく、それぞれ個別に同じ回数与える。
                for (int i = 0; i < 4; i++)
                {
                    Invoke(calm,    "EnterState", SpiritState.Idle);
                    Invoke(curious, "EnterState", SpiritState.Idle);
                    Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));
                }

                Assert.AreEqual(4f, ExperienceOf(calm),    0.0001f);
                Assert.AreEqual(4f, ExperienceOf(curious), 0.0001f);
                Assert.AreEqual(ExperienceOf(calm), ExperienceOf(curious), 0.0001f,
                    "性格によって成長速度が変わってはいけない（Stage 14の方針）");
            }
            finally
            {
                Teardown(calm, calmHome);
                Teardown(curious, curiousHome);
            }
        }

        // ══ Stage 12の保証（加算前FamiliarityでScaleを算出）の維持 ════════

        [Test]
        public void ReactionScale_StillUsesFamiliarityBeforeThisExperience()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));

                Assert.AreEqual(1f, (float)GetField(spirit, "_reactScale"), 0.0001f,
                    "初回の反応は最大のはず（成長の追加で順序が壊れていないか）");
                Assert.AreEqual(1f, ExperienceOf(spirit), 0.0001f,
                    "同じ受理で累積体験は1増えているはず");
            }
            finally { Teardown(spirit, home); }
        }

        // ══ 予約（pending）の挙動 ═══════════════════════════════════════

        [Test]
        public void Pending_HoldsHighestReachedStage()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                // 閾値を下げて、少ない刺激で2段階跨ぐようにする（production側にテストモードは足さない）。
                SetField(spirit, "_growthThresholdFluff", 1f);
                SetField(spirit, "_growthThresholdBloom", 2f);

                Invoke(spirit, "EnterState", SpiritState.Idle);
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));
                Invoke(spirit, "EnterState", SpiritState.Idle);
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));

                Assert.AreEqual(2f, ExperienceOf(spirit), 0.0001f);
                Assert.AreEqual(SpiritGrowthStage.Bloom, (SpiritGrowthStage)GetField(spirit, "_pendingGrowthStage"),
                    "pendingは最終到達段階(Bloom)を保持するはず");

                // 予約しただけでは段階も見た目も変わらない。
                Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage,
                    "刺激受理の時点で段階が確定してはいけない（安全なIdleでのみ演出する）");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void Pending_DoesNotRegressWhenStageAlreadyHigher()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                SetField(spirit, "_growthStage",        SpiritGrowthStage.Bloom);
                SetField(spirit, "_pendingGrowthStage", SpiritGrowthStage.Bloom);
                SetField(spirit, "_growthThresholdFluff", 1f);
                SetField(spirit, "_growthThresholdBloom", 2f);

                Invoke(spirit, "EnterState", SpiritState.Idle);
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));

                Assert.AreEqual(SpiritGrowthStage.Bloom, (SpiritGrowthStage)GetField(spirit, "_pendingGrowthStage"),
                    "既に最高段階なのにpendingが下がった");
            }
            finally { Teardown(spirit, home); }
        }
    }
}
