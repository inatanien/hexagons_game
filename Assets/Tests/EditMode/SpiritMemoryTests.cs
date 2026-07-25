// 役割: Stage 12「記憶（見慣れ度）」の検証。
//       減衰・加算・反応強度は純粋関数として分離してあるため決定論的に検証でき、
//       ForestSpirit側の統合（加算前の値でScaleを算出する順序、受理しなかった刺激で慣れない等）は
//       既存と同じリフレクションによるライフサイクル呼び出しで確認する。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Spirits;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class SpiritMemoryTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

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

        private static ForestSpirit MakeSpirit(out List<HexTile> home)
        {
            home = new List<HexTile> { MakeTileAt(Vector3.zero), MakeTileAt(new Vector3(1f, 0f, 0f)) };
            var go = new GameObject("TestForestSpirit");
            var spirit = go.AddComponent<ForestSpirit>();
            spirit.Initialize(home, Vector3.zero, 1.5f, 1.5f, 0.5f);
            Invoke(spirit, "OnEnable");
            return spirit;
        }

        private static void Teardown(ForestSpirit spirit, IEnumerable<HexTile> tiles)
        {
            Invoke(spirit, "OnDisable");
            Object.DestroyImmediate(spirit.gameObject);
            DestroyTiles(tiles);
        }

        private static void Publish(SpiritStimulusKind kind, Vector3 pos, IReadOnlyList<HexTile> tiles = null)
            => EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(kind, pos, tiles)));

        private static float GetFamiliarity(ForestSpirit spirit, SpiritStimulusKind kind)
        {
            var memory   = GetField(spirit, "_memory");
            var halfLife = (float)GetField(spirit, "_familiarityHalfLife");
            return (float)memory.GetType()
                .GetMethod("GetFamiliarity")
                .Invoke(memory, new object[] { kind, Time.time, halfLife });
        }

        private static float GetReactScale(ForestSpirit spirit) => (float)GetField(spirit, "_reactScale");

        // ══ 1〜9. 減衰 ═══════════════════════════════════════════════════

        [Test]
        public void Decay_ZeroElapsed_KeepsValue()
        {
            Assert.AreEqual(3f, SpiritBehaviorMath.ComputeDecayedFamiliarity(3f, 0f, 60f), 0.0001f);
        }

        [Test]
        public void Decay_AfterOneHalfLife_IsAboutHalf()
        {
            Assert.AreEqual(2f, SpiritBehaviorMath.ComputeDecayedFamiliarity(4f, 60f, 60f), 0.001f);
            Assert.AreEqual(1f, SpiritBehaviorMath.ComputeDecayedFamiliarity(4f, 120f, 60f), 0.001f,
                "半減期2回ぶんで1/4になるはず");
        }

        [Test]
        public void Decay_IsMonotonicallyNonIncreasing()
        {
            float prev = float.MaxValue;
            for (float e = 0f; e <= 300f; e += 5f)
            {
                float v = SpiritBehaviorMath.ComputeDecayedFamiliarity(4f, e, 60f);
                Assert.LessOrEqual(v, prev + 0.0001f, $"elapsed={e} で値が増えた");
                prev = v;
            }
        }

        [Test]
        public void Decay_NeverReturnsNegative_AndNeverExceedsInput()
        {
            for (float e = 0f; e <= 600f; e += 13f)
            {
                float v = SpiritBehaviorMath.ComputeDecayedFamiliarity(4f, e, 60f);
                Assert.GreaterOrEqual(v, 0f);
                Assert.LessOrEqual(v, 4f + 0.0001f, "減衰で元の値を超えてはいけない");
            }
        }

        [Test]
        public void Decay_NegativeElapsed_DoesNotIncreaseValue()
        {
            Assert.AreEqual(3f, SpiritBehaviorMath.ComputeDecayedFamiliarity(3f, -50f, 60f), 0.0001f,
                "負の経過時間で記憶が増えてはいけない");
        }

        [Test]
        public void Decay_InvalidHalfLife_IsHandledSafely()
        {
            foreach (var hl in new[] { 0f, -10f, float.NaN, float.PositiveInfinity })
            {
                float v = SpiritBehaviorMath.ComputeDecayedFamiliarity(3f, 30f, hl);
                Assert.IsTrue(float.IsFinite(v), $"halfLife={hl} で非有限値");
                Assert.GreaterOrEqual(v, 0f);
            }
        }

        [Test]
        public void Decay_InvalidInputs_AreFinite()
        {
            foreach (var c in new[] { float.NaN, float.PositiveInfinity, -5f })
            {
                float v = SpiritBehaviorMath.ComputeDecayedFamiliarity(c, 30f, 60f);
                Assert.IsTrue(float.IsFinite(v));
                Assert.GreaterOrEqual(v, 0f);
            }
            Assert.IsTrue(float.IsFinite(SpiritBehaviorMath.ComputeDecayedFamiliarity(3f, float.NaN, 60f)));
        }

        [Test]
        public void Decay_IsDeterministic()
        {
            for (float e = 0f; e <= 200f; e += 17f)
                Assert.AreEqual(SpiritBehaviorMath.ComputeDecayedFamiliarity(4f, e, 60f),
                                SpiritBehaviorMath.ComputeDecayedFamiliarity(4f, e, 60f), 0f);
        }

        // ══ 10〜14. 加算 ═════════════════════════════════════════════════

        [Test]
        public void Gain_AddsNormally()
        {
            Assert.AreEqual(2f, SpiritBehaviorMath.ComputeFamiliarityGain(1f, 1f, 4f), 0.0001f);
        }

        [Test]
        public void Gain_NeverExceedsMaximum()
        {
            Assert.AreEqual(4f, SpiritBehaviorMath.ComputeFamiliarityGain(3.5f, 10f, 4f), 0.0001f);
            Assert.AreEqual(4f, SpiritBehaviorMath.ComputeFamiliarityGain(99f, 1f, 4f), 0.0001f);
        }

        [Test]
        public void Gain_NegativeGain_DoesNotReduceMemory()
        {
            Assert.AreEqual(2f, SpiritBehaviorMath.ComputeFamiliarityGain(2f, -5f, 4f), 0.0001f,
                "負のgainで記憶が減ってはいけない");
        }

        [Test]
        public void Gain_InvalidInputs_AreHandledSafely()
        {
            foreach (var c in new[] { float.NaN, float.PositiveInfinity, -3f })
            {
                float v = SpiritBehaviorMath.ComputeFamiliarityGain(c, 1f, 4f);
                Assert.IsTrue(float.IsFinite(v));
                Assert.GreaterOrEqual(v, 0f);
                Assert.LessOrEqual(v, 4f + 0.0001f);
            }
            foreach (var max in new[] { 0f, -1f, float.NaN })
                Assert.IsTrue(float.IsFinite(SpiritBehaviorMath.ComputeFamiliarityGain(1f, 1f, max)));
        }

        [Test]
        public void Gain_IsDeterministic()
        {
            Assert.AreEqual(SpiritBehaviorMath.ComputeFamiliarityGain(1.3f, 0.7f, 4f),
                            SpiritBehaviorMath.ComputeFamiliarityGain(1.3f, 0.7f, 4f), 0f);
        }

        // ══ 15〜21. ReactionScale ════════════════════════════════════════

        [Test]
        public void Scale_ZeroFamiliarity_IsMaximum()
        {
            Assert.AreEqual(1f, SpiritBehaviorMath.ComputeReactionScale(0f, 4f, 0.25f), 0.0001f);
        }

        [Test]
        public void Scale_HighFamiliarity_ApproachesMinimum()
        {
            Assert.AreEqual(0.25f, SpiritBehaviorMath.ComputeReactionScale(4f, 4f, 0.25f), 0.0001f);
            Assert.AreEqual(0.25f, SpiritBehaviorMath.ComputeReactionScale(99f, 4f, 0.25f), 0.0001f,
                "上限を超えた見慣れ度でも最小値で頭打ちになるはず");
        }

        [Test]
        public void Scale_IsMonotonicallyNonIncreasing()
        {
            float prev = float.MaxValue;
            for (float f = 0f; f <= 4f; f += 0.05f)
            {
                float s = SpiritBehaviorMath.ComputeReactionScale(f, 4f, 0.25f);
                Assert.LessOrEqual(s, prev + 0.0001f, $"familiarity={f} で反応が大きくなった");
                prev = s;
            }
        }

        [Test]
        public void Scale_StaysWithinMinimumAndOne()
        {
            for (float f = -5f; f <= 10f; f += 0.3f)
            {
                float s = SpiritBehaviorMath.ComputeReactionScale(f, 4f, 0.25f);
                Assert.GreaterOrEqual(s, 0.25f - 0.0001f, $"familiarity={f} で最小値を下回った");
                Assert.LessOrEqual(s, 1f + 0.0001f, $"familiarity={f} で1を超えた");
            }
        }

        [Test]
        public void Scale_NeverZero_SoReactionAlwaysRemains()
        {
            for (float f = 0f; f <= 20f; f += 0.5f)
                Assert.Greater(SpiritBehaviorMath.ComputeReactionScale(f, 4f, 0.25f), 0f,
                    "見慣れても完全に0にはならないはず");

            // minimumScaleが不正でも0にはしない
            foreach (var min in new[] { 0f, -1f, float.NaN })
                Assert.Greater(SpiritBehaviorMath.ComputeReactionScale(4f, 4f, min), 0f);
        }

        [Test]
        public void Scale_InvalidInputs_AreHandledSafely()
        {
            foreach (var f in new[] { float.NaN, float.PositiveInfinity, -9f })
            {
                float s = SpiritBehaviorMath.ComputeReactionScale(f, 4f, 0.25f);
                Assert.IsTrue(float.IsFinite(s));
                Assert.GreaterOrEqual(s, 0.25f - 0.0001f);
                Assert.LessOrEqual(s, 1f + 0.0001f);
            }
            foreach (var max in new[] { 0f, -2f, float.NaN })
                Assert.IsTrue(float.IsFinite(SpiritBehaviorMath.ComputeReactionScale(1f, max, 0.25f)));
        }

        [Test]
        public void Scale_IsDeterministic()
        {
            Assert.AreEqual(SpiritBehaviorMath.ComputeReactionScale(1.7f, 4f, 0.25f),
                            SpiritBehaviorMath.ComputeReactionScale(1.7f, 4f, 0.25f), 0f);
        }

        // ══ 22〜32. 統合 ═════════════════════════════════════════════════

        [Test]
        public void FirstStimulus_ProducesNearMaximumReaction()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Assert.AreEqual(0f, GetFamiliarity(spirit, SpiritStimulusKind.FlowerBloomed), 0.0001f,
                    "前提: まだ何も覚えていないこと");

                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));

                Assert.AreEqual(1f, GetReactScale(spirit), 0.0001f, "初回は最大の反応になるはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void ScaleUsesFamiliarityBeforeThisExperience()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));

                // 初回のScaleは1.0（加算前=0から算出）。加算後の見慣れ度は1になっている。
                Assert.AreEqual(1f, GetReactScale(spirit), 0.0001f,
                    "今回ぶんを加算してからScaleを計算してはいけない");
                Assert.AreEqual(1f, GetFamiliarity(spirit, SpiritStimulusKind.FlowerBloomed), 0.01f,
                    "体験後は見慣れ度が加算されているはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void RepeatedStimulus_ShrinksReaction()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                var scales = new List<float>();
                for (int i = 0; i < 4; i++)
                {
                    // 同優先度の連続刺激は拒否されるため、毎回Idleへ戻してから与える
                    Invoke(spirit, "EnterState", SpiritState.Idle);
                    Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                    scales.Add(GetReactScale(spirit));
                }

                for (int i = 1; i < scales.Count; i++)
                    Assert.Less(scales[i], scales[i - 1] + 0.0001f,
                        $"{i + 1}回目の反応が前回より大きくなっている: {string.Join(", ", scales)}");

                Assert.Less(scales[scales.Count - 1], scales[0],
                    "繰り返すほど反応が小さくなるはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void ForestAndFlowerFamiliarity_AreTrackedSeparately()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                // 花だけを何度も体験する
                for (int i = 0; i < 3; i++)
                {
                    Invoke(spirit, "EnterState", SpiritState.Idle);
                    Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                }

                Assert.Greater(GetFamiliarity(spirit, SpiritStimulusKind.FlowerBloomed), 1f,
                    "花には見慣れているはず");
                Assert.AreEqual(0f, GetFamiliarity(spirit, SpiritStimulusKind.ForestGrew), 0.0001f,
                    "森の見慣れ度は影響を受けないはず");

                // 森は初体験なので最大の反応になる
                Invoke(spirit, "EnterState", SpiritState.Idle);
                Publish(SpiritStimulusKind.ForestGrew, Vector3.zero, home);
                Assert.AreEqual(1f, GetReactScale(spirit), 0.0001f,
                    "花に慣れていても森への初回反応は最大のはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void AfterTimePasses_ReactionGrowsBackViaDecay()
        {
            // 実時間を待たずに、減衰の純粋関数で「時間経過後の反応」を確認する。
            const float halfLife = 60f;
            float familiarAfterFour = 4f;

            float scaleNow   = SpiritBehaviorMath.ComputeReactionScale(familiarAfterFour, 4f, 0.25f);
            float decayed    = SpiritBehaviorMath.ComputeDecayedFamiliarity(familiarAfterFour, halfLife * 2f, halfLife);
            float scaleLater = SpiritBehaviorMath.ComputeReactionScale(decayed, 4f, 0.25f);

            Assert.Greater(scaleLater, scaleNow, "時間が経てば反応が再び大きくなるはず");
        }

        [Test]
        public void RejectedStimuli_DoNotBuildFamiliarity()
        {
            var spirit = MakeSpirit(out var home);
            var far = new List<HexTile> { MakeTileAt(new Vector3(-40f, 0f, -40f)) };
            try
            {
                // home外の森
                Publish(SpiritStimulusKind.ForestGrew, new Vector3(-40f, 0f, -40f), far);
                Assert.AreEqual(0f, GetFamiliarity(spirit, SpiritStimulusKind.ForestGrew), 0.0001f,
                    "home外の森では慣れないはず");

                // 知覚距離外の花
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(500f, 0f, 500f));
                Assert.AreEqual(0f, GetFamiliarity(spirit, SpiritStimulusKind.FlowerBloomed), 0.0001f,
                    "知覚距離外の花では慣れないはず");

                // 不正な位置
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(float.NaN, 0f, 0f));
                Assert.AreEqual(0f, GetFamiliarity(spirit, SpiritStimulusKind.FlowerBloomed), 0.0001f,
                    "不正な位置の刺激では慣れないはず");
            }
            finally { Teardown(spirit, home); DestroyTiles(far); }
        }

        [Test]
        public void StimuliRejectedDuringSleepOrStretch_DoNotBuildFamiliarity()
        {
            foreach (var state in new[] { SpiritState.Sleep, SpiritState.Stretch })
            {
                var spirit = MakeSpirit(out var home);
                try
                {
                    Invoke(spirit, "EnterState", state);
                    Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));

                    Assert.AreEqual(state, spirit.CurrentState, "前提: 中断されていないこと");
                    Assert.AreEqual(0f, GetFamiliarity(spirit, SpiritStimulusKind.FlowerBloomed), 0.0001f,
                        $"{state} 中に拒否した刺激では慣れないはず");
                }
                finally { Teardown(spirit, home); }
            }
        }

        [Test]
        public void SamePriorityStimulusDuringReact_DoesNotBuildFamiliarity()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                float after1 = GetFamiliarity(spirit, SpiritStimulusKind.FlowerBloomed);
                Assert.AreEqual(SpiritState.React, spirit.CurrentState);

                // React中に同優先度の刺激（拒否される）
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2.5f, 0f, 0f));

                Assert.AreEqual(after1, GetFamiliarity(spirit, SpiritStimulusKind.FlowerBloomed), 0.01f,
                    "React中に拒否された刺激では慣れが増えないはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void EvenWhenVeryFamiliar_SpiritStillEntersReact()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    Invoke(spirit, "EnterState", SpiritState.Idle);
                    Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                }

                Assert.AreEqual(SpiritState.React, spirit.CurrentState,
                    "十分見慣れてもReactへは入る（完全無視しない）はず");
                Assert.Greater(GetReactScale(spirit), 0f, "反応の強さは0にならないはず");
                Assert.Less(GetReactScale(spirit), 0.5f, "十分見慣れたら反応は小さくなっているはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void ReactDuration_ShrinksButNeverBelowMinimum()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                float firstDuration = (float)GetField(spirit, "_stateDuration");

                for (int i = 0; i < 8; i++)
                {
                    Invoke(spirit, "EnterState", SpiritState.Idle);
                    Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                }
                float lastDuration = (float)GetField(spirit, "_stateDuration");
                float minDuration  = (float)GetField(spirit, "_reactMinDuration");

                Assert.Less(lastDuration, firstDuration, "見慣れるとReactは短くなるはず");
                Assert.GreaterOrEqual(lastDuration, minDuration - 0.0001f,
                    "最短時間を下回って視認不能になってはいけない");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void LeavingReact_StillResetsVisualPose_WhenFamiliar()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    Invoke(spirit, "EnterState", SpiritState.Idle);
                    Publish(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                }

                SetField(spirit, "_stateElapsed", 0.4f);
                Invoke(spirit, "ApplyReactPose");
                Invoke(spirit, "EnterState", SpiritState.Idle);

                var body = (Transform)GetField(spirit, "_bodyRoot");
                Assert.AreEqual(Quaternion.identity, body.localRotation);
                Assert.AreEqual(Vector3.one,        body.localScale);
                Assert.AreEqual(Vector3.zero,       body.localPosition);
            }
            finally { Teardown(spirit, home); }
        }

        // ══ SpiritMemory 自体の安全性 ════════════════════════════════════

        [Test]
        public void Memory_UnknownKind_IsHandledSafely()
        {
            var memory = new SpiritMemory();
            var unknown = (SpiritStimulusKind)999;

            Assert.AreEqual(0f, memory.GetFamiliarity(unknown, 0f, 60f), 0.0001f);
            Assert.DoesNotThrow(() => memory.Reinforce(unknown, 0f, 60f, 1f, 4f),
                "未知の刺激種類でも例外を投げないはず");
            Assert.AreEqual(0f, memory.GetFamiliarity(unknown, 0f, 60f), 0.0001f);
        }

        // ── 保存スロットの固定対応（enum値へ依存しないこと） ──────────────

        private static float[] GetSlots(SpiritMemory memory)
            => (float[])typeof(SpiritMemory)
                .GetField("_familiarity", Priv).GetValue(memory);

        [Test]
        public void Memory_ForestGrew_UsesFixedSlotZero()
        {
            var memory = new SpiritMemory();
            memory.Reinforce(SpiritStimulusKind.ForestGrew, 0f, 60f, 1f, 4f);

            var slots = GetSlots(memory);
            Assert.AreEqual(1f, slots[0], 0.0001f, "ForestGrewは固定でスロット0を使うはず");
            Assert.AreEqual(0f, slots[1], 0.0001f, "他のスロットへ書き込んではいけない");
        }

        [Test]
        public void Memory_FlowerBloomed_UsesFixedSlotOne()
        {
            var memory = new SpiritMemory();
            memory.Reinforce(SpiritStimulusKind.FlowerBloomed, 0f, 60f, 1f, 4f);

            var slots = GetSlots(memory);
            Assert.AreEqual(0f, slots[0], 0.0001f, "他のスロットへ書き込んではいけない");
            Assert.AreEqual(1f, slots[1], 0.0001f, "FlowerBloomedは固定でスロット1を使うはず");
        }

        [Test]
        public void Memory_SlotMapping_DoesNotDependOnEnumNumericValue()
        {
            // enumの数値をそのままインデックスに使っていないことを、
            // 「未知の大きな値では一切書き込まれない」ことで確認する。
            // 単純キャスト実装なら範囲外アクセスや別スロットの汚染が起こり得る。
            var memory = new SpiritMemory();
            memory.Reinforce((SpiritStimulusKind)7,   0f, 60f, 1f, 4f);
            memory.Reinforce((SpiritStimulusKind)(-3), 0f, 60f, 1f, 4f);

            var slots = GetSlots(memory);
            foreach (var v in slots)
                Assert.AreEqual(0f, v, 0.0001f, "未知のenum値で既存スロットが汚染されてはいけない");
        }

        [Test]
        public void Memory_TwoKinds_DoNotShareSlot()
        {
            var memory = new SpiritMemory();
            memory.Reinforce(SpiritStimulusKind.ForestGrew,    0f, 60f, 2f, 4f);
            memory.Reinforce(SpiritStimulusKind.FlowerBloomed, 0f, 60f, 1f, 4f);

            Assert.AreEqual(2f, memory.GetFamiliarity(SpiritStimulusKind.ForestGrew,    0f, 60f), 0.0001f);
            Assert.AreEqual(1f, memory.GetFamiliarity(SpiritStimulusKind.FlowerBloomed, 0f, 60f), 0.0001f);
        }

        // ── 生成時の森刺激が購読順に依存しないこと ────────────────────────

        private static ForestSpiritSpawner MakeSpawner(bool relayFirst, out SpiritStimulusRelay relay)
        {
            var go = new GameObject("TestSystems");
            ForestSpiritSpawner spawner;
            if (relayFirst)
            {
                relay   = go.AddComponent<SpiritStimulusRelay>();
                spawner = go.AddComponent<ForestSpiritSpawner>();
                Invoke(relay,   "OnEnable");
                Invoke(spawner, "OnEnable");
            }
            else
            {
                spawner = go.AddComponent<ForestSpiritSpawner>();
                relay   = go.AddComponent<SpiritStimulusRelay>();
                Invoke(spawner, "OnEnable");
                Invoke(relay,   "OnEnable");
            }
            return spawner;
        }

        [Test]
        public void SpawningForestGrowth_IsAlwaysRemembered_RegardlessOfSubscriptionOrder()
        {
            // Spawner→Relay / Relay→Spawner のどちらの購読順でも、
            // 生成した森の成長が最初の体験として必ず記憶されること（決定的にA）。
            foreach (var relayFirst in new[] { false, true })
            {
                var spawner = MakeSpawner(relayFirst, out var relay);
                var tiles = new List<HexTile> { MakeTileAt(Vector3.zero), MakeTileAt(new Vector3(1f, 0f, 0f)) };
                try
                {
                    EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                        null, ElfVillage.HexGrid.HexCoord.Zero, tiles,
                        new ForestGrowthMetrics(tiles.Count, tiles.Count)));

                    var spirits = spawner.GetComponentsInChildren<ForestSpirit>(true);
                    Assert.AreEqual(1, spirits.Length, "精霊は1体だけ生成されるはず");

                    float fam = GetFamiliarity(spirits[0], SpiritStimulusKind.ForestGrew);
                    Assert.AreEqual(1f, fam, 0.01f,
                        $"relayFirst={relayFirst} で生成時の森が記憶されていない（購読順に依存している）");
                }
                finally
                {
                    Invoke(relay, "OnDisable");
                    Invoke(spawner, "OnDisable");
                    foreach (var sp in spawner.GetComponentsInChildren<ForestSpirit>(true))
                        Invoke(sp, "OnDisable");
                    Object.DestroyImmediate(spawner.gameObject);
                    DestroyTiles(tiles);
                }
            }
        }

        [Test]
        public void SpawningForestGrowth_IsNotDoubleCounted()
        {
            // 直接渡し＋Relay経由の二重到達でも、React中の同優先度として弾かれるため1回ぶんだけ記憶される。
            var spawner = MakeSpawner(relayFirst: false, out var relay);
            var tiles = new List<HexTile> { MakeTileAt(Vector3.zero), MakeTileAt(new Vector3(1f, 0f, 0f)) };
            try
            {
                EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                    null, ElfVillage.HexGrid.HexCoord.Zero, tiles,
                    new ForestGrowthMetrics(tiles.Count, tiles.Count)));

                var spirit = spawner.GetComponentsInChildren<ForestSpirit>(true)[0];
                Assert.AreEqual(1f, GetFamiliarity(spirit, SpiritStimulusKind.ForestGrew), 0.01f,
                    "生成時の刺激が二重に記憶されてはいけない");
            }
            finally
            {
                Invoke(relay, "OnDisable");
                Invoke(spawner, "OnDisable");
                foreach (var sp in spawner.GetComponentsInChildren<ForestSpirit>(true))
                    Invoke(sp, "OnDisable");
                Object.DestroyImmediate(spawner.gameObject);
                DestroyTiles(tiles);
            }
        }

        [Test]
        public void Memory_DecaysOverTime()
        {
            var memory = new SpiritMemory();
            memory.Reinforce(SpiritStimulusKind.FlowerBloomed, now: 0f, halfLifeSeconds: 60f, gain: 4f, maximum: 4f);

            Assert.AreEqual(4f, memory.GetFamiliarity(SpiritStimulusKind.FlowerBloomed, 0f, 60f), 0.0001f);
            Assert.AreEqual(2f, memory.GetFamiliarity(SpiritStimulusKind.FlowerBloomed, 60f, 60f), 0.001f,
                "半減期ぶん経過で半分になるはず");
        }

        [Test]
        public void Memory_ReinforceAfterDecay_BuildsOnDecayedValue()
        {
            var memory = new SpiritMemory();
            memory.Reinforce(SpiritStimulusKind.ForestGrew, now: 0f, halfLifeSeconds: 60f, gain: 4f, maximum: 4f);

            // 半減期ぶん経ってから再体験 → 2.0まで薄れた上に1.0加算される
            memory.Reinforce(SpiritStimulusKind.ForestGrew, now: 60f, halfLifeSeconds: 60f, gain: 1f, maximum: 4f);

            Assert.AreEqual(3f, memory.GetFamiliarity(SpiritStimulusKind.ForestGrew, 60f, 60f), 0.01f,
                "薄れた値へ加算されるはず（2.0 + 1.0）");
        }
    }
}
