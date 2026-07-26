// 役割: SpiritBehaviorMath（精霊の行動計算・純粋関数）の単体テスト。
//       これらの関数はUnityEngine.Randomを使わず、乱数は引数として受け取る設計のため、
//       実行順・乱数状態に依存せず決定論的に検証できる。
//       ForestSpiritSpawner/ForestSpirit側の生成・home固定も、既存テストと同じ
//       リフレクションによるライフサイクル呼び出しで検証する。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Spirits;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class SpiritBehaviorMathTests
    {
        private static readonly SpiritState[] AllStates =
        {
            SpiritState.Idle, SpiritState.Wander, SpiritState.ObserveTree, SpiritState.Sleep, SpiritState.Stretch,
        };

        // ── 1. DecideNextStateが定義済みの状態だけを返す ─────────────────

        [Test]
        public void DecideNextState_AlwaysReturnsDefinedState()
        {
            foreach (var state in AllStates)
            {
                for (int i = 0; i <= 100; i++)
                {
                    var next = SpiritBehaviorMath.DecideNextState(state, i / 100f);
                    CollectionAssert.Contains(AllStates, next,
                        $"{state} から未定義の状態 {next} へ遷移した");
                }
            }
        }

        // ── Stage 10-1. SleepからのみStretchへ遷移する ──────────────────────

        [Test]
        public void DecideNextState_SleepAlwaysGoesToStretch()
        {
            for (int i = 0; i <= 100; i++)
                Assert.AreEqual(SpiritState.Stretch,
                    SpiritBehaviorMath.DecideNextState(SpiritState.Sleep, i / 100f),
                    "Sleepの後は必ずStretch（伸び）を挟むはず");
        }

        [Test]
        public void DecideNextState_StretchIsOnlyReachableFromSleep()
        {
            for (int i = 0; i <= 100; i++)
            {
                float r = i / 100f;
                Assert.AreNotEqual(SpiritState.Stretch, SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r));
                Assert.AreNotEqual(SpiritState.Stretch, SpiritBehaviorMath.DecideNextState(SpiritState.Wander, r));
                Assert.AreNotEqual(SpiritState.Stretch, SpiritBehaviorMath.DecideNextState(SpiritState.ObserveTree, r));
                Assert.AreNotEqual(SpiritState.Stretch, SpiritBehaviorMath.DecideNextState(SpiritState.Stretch, r));
            }
        }

        // ── Stage 10-2. Stretchは必ずIdleへ遷移する ────────────────────────

        [Test]
        public void DecideNextState_StretchAlwaysReturnsToIdle()
        {
            for (int i = 0; i <= 100; i++)
                Assert.AreEqual(SpiritState.Idle,
                    SpiritBehaviorMath.DecideNextState(SpiritState.Stretch, i / 100f),
                    "Stretchの後は必ずIdleへ戻るはず");
        }

        [Test]
        public void DecideNextState_SleepIsOnlyReachableFromIdle()
        {
            for (int i = 0; i <= 100; i++)
            {
                float r = i / 100f;
                Assert.AreNotEqual(SpiritState.Sleep, SpiritBehaviorMath.DecideNextState(SpiritState.Wander, r));
                Assert.AreNotEqual(SpiritState.Sleep, SpiritBehaviorMath.DecideNextState(SpiritState.ObserveTree, r));
                Assert.AreNotEqual(SpiritState.Sleep, SpiritBehaviorMath.DecideNextState(SpiritState.Sleep, r));
                Assert.AreNotEqual(SpiritState.Sleep, SpiritBehaviorMath.DecideNextState(SpiritState.Stretch, r));
            }
        }

        // ── 2. 同じ入力から同じ状態が返る ────────────────────────────────

        [Test]
        public void DecideNextState_IsDeterministic()
        {
            foreach (var state in AllStates)
            {
                for (int i = 0; i <= 20; i++)
                {
                    float r = i / 20f;
                    var a = SpiritBehaviorMath.DecideNextState(state, r);
                    var b = SpiritBehaviorMath.DecideNextState(state, r);
                    Assert.AreEqual(a, b, "同じ入力なら必ず同じ結果になるはず");
                }
            }
        }

        // ── 3. 境界値0.0と1.0付近でも正常に遷移する ──────────────────────

        [Test]
        public void DecideNextState_BoundaryRandomValues_AreHandled()
        {
            foreach (var state in AllStates)
            {
                foreach (var r in new[] { 0f, 0.0001f, 0.4999f, 0.5f, 0.9999f, 1f })
                {
                    var next = SpiritBehaviorMath.DecideNextState(state, r);
                    CollectionAssert.Contains(AllStates, next, $"state={state}, r={r}");
                }
            }
        }

        // ── 10. 未知・不正な入力を安全に処理する ────────────────────────

        [Test]
        public void DecideNextState_InvalidInputs_AreHandledSafely()
        {
            foreach (var r in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -5f, 99f })
            {
                var next = SpiritBehaviorMath.DecideNextState(SpiritState.Idle, r);
                CollectionAssert.Contains(AllStates, next, $"r={r} で未定義状態が返った");
            }

            // 未定義のenum値を渡しても例外を投げずIdleへ倒す
            var undefined = (SpiritState)999;
            Assert.AreEqual(SpiritState.Idle, SpiritBehaviorMath.DecideNextState(undefined, 0.5f));
        }

        [Test]
        public void ComputeStateDuration_IsPositiveAndWithinRange()
        {
            foreach (var state in AllStates)
            {
                foreach (var r in new[] { 0f, 0.5f, 1f, float.NaN, -3f })
                {
                    float d = SpiritBehaviorMath.ComputeStateDuration(state, r);
                    Assert.Greater(d, 0f, $"{state} の継続時間は正の値であるべき (r={r})");
                    Assert.IsTrue(float.IsFinite(d));
                }
            }
        }

        // ── 4. Wander目的地が必ずhome範囲内に収まる ──────────────────────

        [Test]
        public void PickWanderTarget_AlwaysWithinBounds()
        {
            var center = new Vector3(5f, 1f, -3f);
            const float ex = 2.5f, ez = 1.5f;

            for (int i = 0; i <= 20; i++)
            {
                for (int j = 0; j <= 20; j++)
                {
                    var t = SpiritBehaviorMath.PickWanderTarget(center, ex, ez, i / 20f, j / 20f);
                    Assert.LessOrEqual(Mathf.Abs(t.x - center.x), ex + 0.0001f, "X方向がhome範囲を超えた");
                    Assert.LessOrEqual(Mathf.Abs(t.z - center.z), ez + 0.0001f, "Z方向がhome範囲を超えた");
                    Assert.AreEqual(center.y, t.y, 0.0001f, "Yは中心のまま維持されるはず");
                }
            }
        }

        // ── 5. 極小のhome範囲でもNaNや範囲外座標を返さない ───────────────

        [Test]
        public void PickWanderTarget_TinyOrInvalidBounds_StaysSafe()
        {
            var center = new Vector3(1f, 0f, 2f);

            foreach (var extent in new[] { 0f, 0.0001f, -1f, float.NaN, float.PositiveInfinity })
            {
                foreach (var r in new[] { 0f, 0.5f, 1f })
                {
                    var t = SpiritBehaviorMath.PickWanderTarget(center, extent, extent, r, r);
                    Assert.IsTrue(float.IsFinite(t.x) && float.IsFinite(t.y) && float.IsFinite(t.z),
                        $"extent={extent} でNaN/Infinityが返った: {t}");

                    // 不正・極小な範囲では中心付近から動かないこと
                    float safeExtent = (float.IsFinite(extent) && extent > 0f) ? extent : 0f;
                    Assert.LessOrEqual(Mathf.Abs(t.x - center.x), safeExtent + 0.0001f);
                    Assert.LessOrEqual(Mathf.Abs(t.z - center.z), safeExtent + 0.0001f);
                }
            }
        }

        // ── 6. ClampToBoundsがX/Zを正しく制限する ────────────────────────

        [Test]
        public void ClampToBounds_LimitsXAndZ_ButKeepsY()
        {
            var center = new Vector3(0f, 5f, 0f);
            const float ex = 2f, ez = 3f;

            var far = new Vector3(100f, 42f, -100f);
            var clamped = SpiritBehaviorMath.ClampToBounds(far, center, ex, ez);

            Assert.AreEqual(2f,  clamped.x, 0.0001f, "Xは中心+extentXへ制限されるはず");
            Assert.AreEqual(-3f, clamped.z, 0.0001f, "Zは中心-extentZへ制限されるはず");
            Assert.AreEqual(42f, clamped.y, 0.0001f, "Yは制限せずそのまま通すはず");
        }

        [Test]
        public void ClampToBounds_InsidePoint_IsUnchanged()
        {
            var center = new Vector3(0f, 0f, 0f);
            var inside = new Vector3(0.5f, 1f, -0.5f);
            var clamped = SpiritBehaviorMath.ClampToBounds(inside, center, 2f, 2f);

            Assert.AreEqual(inside.x, clamped.x, 0.0001f);
            Assert.AreEqual(inside.z, clamped.z, 0.0001f);
        }

        [Test]
        public void ClampToBounds_InvalidInputs_AreFinite()
        {
            var result = SpiritBehaviorMath.ClampToBounds(
                new Vector3(float.NaN, float.PositiveInfinity, float.NaN),
                new Vector3(1f, 1f, 1f), float.NaN, -2f);

            Assert.IsTrue(float.IsFinite(result.x) && float.IsFinite(result.y) && float.IsFinite(result.z),
                "不正入力でもNaN/Infinityを返さないはず: " + result);
        }

        // ── 7. Idleの揺れが指定振幅を超えない ────────────────────────────

        [Test]
        public void ComputeIdleSway_NeverExceedsAmplitude()
        {
            const float amplitude = 0.06f;
            for (float t = 0f; t < 50f; t += 0.13f)
            {
                float sway = SpiritBehaviorMath.ComputeIdleSway(t, 1.2f, amplitude);
                Assert.LessOrEqual(Mathf.Abs(sway), amplitude + 0.0001f, $"t={t} で振幅を超えた");
            }
        }

        [Test]
        public void ComputeIdleSway_InvalidInputs_AreFinite()
        {
            foreach (var bad in new[] { float.NaN, float.PositiveInfinity })
            {
                float sway = SpiritBehaviorMath.ComputeIdleSway(bad, bad, bad);
                Assert.IsTrue(float.IsFinite(sway), "不正入力でも有限値を返すはず");
            }
        }

        // ── 8・9. 移動補間値が0〜1で、時間経過に対して単調増加する ──────────

        [Test]
        public void ComputeMoveProgress_StaysWithinZeroToOne()
        {
            const float duration = 4f;
            for (float e = -2f; e <= 8f; e += 0.17f)
            {
                float p = SpiritBehaviorMath.ComputeMoveProgress(e, duration);
                Assert.GreaterOrEqual(p, 0f, $"elapsed={e}");
                Assert.LessOrEqual(p, 1f, $"elapsed={e}");
            }
        }

        [Test]
        public void ComputeMoveProgress_IsMonotonicallyIncreasing()
        {
            const float duration = 4f;
            float previous = -1f;
            for (float e = 0f; e <= duration; e += 0.05f)
            {
                float p = SpiritBehaviorMath.ComputeMoveProgress(e, duration);
                Assert.GreaterOrEqual(p, previous - 0.0001f, $"elapsed={e} で進行度が逆行した");
                previous = p;
            }
            Assert.AreEqual(1f, SpiritBehaviorMath.ComputeMoveProgress(duration, duration), 0.0001f,
                "継続時間ちょうどで進行度は1になるはず");
        }

        [Test]
        public void ComputeMoveProgress_InvalidDuration_IsHandled()
        {
            Assert.AreEqual(1f, SpiritBehaviorMath.ComputeMoveProgress(1f, 0f), 0.0001f,
                "継続時間0は即到着扱い（0除算しない）");
            Assert.IsTrue(float.IsFinite(SpiritBehaviorMath.ComputeMoveProgress(float.NaN, float.NaN)));
        }

        // ══ Stage 10: Stretch ═══════════════════════════════════════════

        // ── 3. Stretch補間の開始と終了で元のスケールになる ────────────────

        [Test]
        public void ComputeStretchScale_StartAndEnd_AreIdentity()
        {
            foreach (var p in new[] { 0f, 1f })
            {
                var s = SpiritBehaviorMath.ComputeStretchScale(p);
                Assert.AreEqual(1f, s.x, 0.0001f, $"progress={p} でXが元に戻っていない");
                Assert.AreEqual(1f, s.y, 0.0001f, $"progress={p} でYが元に戻っていない");
                Assert.AreEqual(1f, s.z, 0.0001f, $"progress={p} でZが元に戻っていない");
            }
        }

        [Test]
        public void ComputeStretchScale_FirstHalfStretchesVertically_SecondHalfWidens()
        {
            var early = SpiritBehaviorMath.ComputeStretchScale(0.25f);
            Assert.Greater(early.y, 1f, "前半は縦へ伸びるはず");
            Assert.Less(early.x, 1f, "前半は横が縮むはず");

            var late = SpiritBehaviorMath.ComputeStretchScale(0.75f);
            Assert.Less(late.y, 1f, "後半は縦が戻り気味になるはず");
            Assert.Greater(late.x, 1f, "後半は横へふわっと広がるはず");
        }

        [Test]
        public void ComputeStretchScale_IsContinuous_NoSuddenPop()
        {
            // 回帰テスト: 以前は中間(p=0.5)で符号を反転させていたため、
            // 縦スケールが 1.11 → 0.88 へ瞬間的に飛び「カクッ」と見える不具合があった。
            // 隣接するprogress間でスケールが大きく跳ばないことを保証する。
            const float step = 0.01f;
            var prev = SpiritBehaviorMath.ComputeStretchScale(0f);
            for (float p = step; p <= 1f; p += step)
            {
                var cur = SpiritBehaviorMath.ComputeStretchScale(p);
                Assert.LessOrEqual(Mathf.Abs(cur.y - prev.y), 0.02f,
                    $"progress={p} 付近で縦スケールが不連続に飛んでいる");
                Assert.LessOrEqual(Mathf.Abs(cur.x - prev.x), 0.02f,
                    $"progress={p} 付近で横スケールが不連続に飛んでいる");
                prev = cur;
            }
        }

        [Test]
        public void ComputeStretchScale_PassesThroughNeutralAtMidpoint()
        {
            var mid = SpiritBehaviorMath.ComputeStretchScale(0.5f);
            Assert.AreEqual(1f, mid.x, 0.0001f, "中間では一度等倍を通過するはず");
            Assert.AreEqual(1f, mid.y, 0.0001f, "中間では一度等倍を通過するはず");
        }

        [Test]
        public void ComputeStretchScale_DeformationStaysSmall()
        {
            // ゴム・液体的に見えないよう、変形量が過大にならないことを保証する。
            for (float p = 0f; p <= 1f; p += 0.02f)
            {
                var s = SpiritBehaviorMath.ComputeStretchScale(p);
                Assert.LessOrEqual(Mathf.Abs(s.y - 1f), 0.20f, $"progress={p} で縦の変形が大きすぎる");
                Assert.LessOrEqual(Mathf.Abs(s.x - 1f), 0.20f, $"progress={p} で横の変形が大きすぎる");
            }
        }

        // ── 4・5. 有限値を返す／不正なprogressを安全にClampする ─────────────

        [Test]
        public void ComputeStretchScale_InvalidProgress_IsClampedAndFinite()
        {
            foreach (var p in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -3f, 7f })
            {
                var s = SpiritBehaviorMath.ComputeStretchScale(p);
                Assert.IsTrue(float.IsFinite(s.x) && float.IsFinite(s.y) && float.IsFinite(s.z),
                    $"progress={p} で非有限値が返った: {s}");
            }

            // 範囲外は0/1へClampされるので、結果は等倍になる
            Assert.AreEqual(1f, SpiritBehaviorMath.ComputeStretchScale(-3f).y, 0.0001f);
            Assert.AreEqual(1f, SpiritBehaviorMath.ComputeStretchScale(7f).y,  0.0001f);
        }

        [Test]
        public void ComputeStretchScale_InvalidIntensity_IsFinite()
        {
            foreach (var k in new[] { float.NaN, float.PositiveInfinity, -0.5f })
            {
                var s = SpiritBehaviorMath.ComputeStretchScale(0.3f, k);
                Assert.IsTrue(float.IsFinite(s.x) && float.IsFinite(s.y) && float.IsFinite(s.z));
            }
        }

        // ══ Stage 10: Hop ═══════════════════════════════════════════════

        // ── 6. progress 0と1でオフセット0 ───────────────────────────────

        [Test]
        public void ComputeHopOffset_StartAndEnd_AreZero()
        {
            foreach (var count in new[] { 1, 2, 3, 5 })
            {
                Assert.AreEqual(0f, SpiritBehaviorMath.ComputeHopOffset(0f, count, 0.05f), 0.0001f,
                    $"hopCount={count} でprogress=0が接地していない");
                Assert.AreEqual(0f, SpiritBehaviorMath.ComputeHopOffset(1f, count, 0.05f), 0.0001f,
                    $"hopCount={count} でprogress=1が接地していない");
            }
        }

        // ── 7・8. 中間で0以上、hopHeightを超えない ──────────────────────

        [Test]
        public void ComputeHopOffset_StaysWithinZeroAndHopHeight()
        {
            const float height = 0.05f;
            bool sawPositive = false;

            for (float p = 0f; p <= 1f; p += 0.005f)
            {
                float o = SpiritBehaviorMath.ComputeHopOffset(p, 2, height);
                Assert.GreaterOrEqual(o, 0f, $"progress={p} で負のオフセット");
                Assert.LessOrEqual(o, height + 0.0001f, $"progress={p} でhopHeightを超えた");
                if (o > 0.001f) sawPositive = true;
            }
            Assert.IsTrue(sawPositive, "中間のprogressで実際に跳ねている（0より大きい）はず");
        }

        // ── 9・10. 全範囲で有限値／不正なhopCount・hopHeightでも安全 ────────

        [Test]
        public void ComputeHopOffset_InvalidInputs_AreHandledSafely()
        {
            foreach (var count in new[] { -5, 0, 1000 })
            {
                for (float p = 0f; p <= 1f; p += 0.1f)
                {
                    float o = SpiritBehaviorMath.ComputeHopOffset(p, count, 0.05f);
                    Assert.IsTrue(float.IsFinite(o), $"hopCount={count}, progress={p} で非有限値");
                    Assert.GreaterOrEqual(o, 0f);
                    Assert.LessOrEqual(o, 0.05f + 0.0001f);
                }
            }

            foreach (var h in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                float o = SpiritBehaviorMath.ComputeHopOffset(0.5f, 2, h);
                Assert.IsTrue(float.IsFinite(o), $"hopHeight={h} で非有限値");
                Assert.GreaterOrEqual(o, 0f);
            }

            foreach (var p in new[] { float.NaN, -2f, 5f })
            {
                float o = SpiritBehaviorMath.ComputeHopOffset(p, 2, 0.05f);
                Assert.IsTrue(float.IsFinite(o), $"progress={p} で非有限値");
            }
        }

        // ── 11. 同じ入力から同じ結果を返す ──────────────────────────────

        [Test]
        public void ComputeHopOffset_IsDeterministic()
        {
            for (float p = 0f; p <= 1f; p += 0.07f)
            {
                float a = SpiritBehaviorMath.ComputeHopOffset(p, 3, 0.04f);
                float b = SpiritBehaviorMath.ComputeHopOffset(p, 3, 0.04f);
                Assert.AreEqual(a, b, 0f, $"progress={p} で結果が一致しない");
            }
        }

        // ══ Stage 10: ObserveTreeリアクション ════════════════════════════

        // ── 13. リアクション選択が定義済みの種類だけを返す ────────────────

        [Test]
        public void PickObserveReaction_ReturnsOnlyDefinedKinds()
        {
            var defined = new[]
            {
                SpiritReactionKind.TiltHead,
                SpiritReactionKind.SmallHop,
            };

            for (int i = 0; i <= 100; i++)
                CollectionAssert.Contains(defined, SpiritBehaviorMath.PickObserveReaction(i / 100f));
        }

        [Test]
        public void PickObserveReaction_BothKindsAreReachable()
        {
            Assert.AreEqual(SpiritReactionKind.TiltHead, SpiritBehaviorMath.PickObserveReaction(0.1f));
            Assert.AreEqual(SpiritReactionKind.SmallHop, SpiritBehaviorMath.PickObserveReaction(0.9f));
        }

        // ── 14. 不正な乱数入力を安全に処理する ──────────────────────────

        [Test]
        public void PickObserveReaction_InvalidRandom_IsHandledSafely()
        {
            var defined = new[]
            {
                SpiritReactionKind.TiltHead,
                SpiritReactionKind.SmallHop,
            };

            foreach (var r in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -9f, 42f })
                CollectionAssert.Contains(defined, SpiritBehaviorMath.PickObserveReaction(r));
        }

        // ── 15. リアクション終了時に傾きが元へ戻る ──────────────────────

        [Test]
        public void ComputeTiltAngle_StartAndEnd_AreZero()
        {
            Assert.AreEqual(0f, SpiritBehaviorMath.ComputeTiltAngle(0f, 16f), 0.0001f, "開始時は傾いていないはず");
            Assert.AreEqual(0f, SpiritBehaviorMath.ComputeTiltAngle(1f, 16f), 0.0001f, "終了時は傾きが残らないはず");
        }

        [Test]
        public void ComputeTiltAngle_NeverExceedsMaxAngle_AndIsFinite()
        {
            const float max = 16f;
            for (float p = 0f; p <= 1f; p += 0.01f)
            {
                float a = SpiritBehaviorMath.ComputeTiltAngle(p, max);
                Assert.LessOrEqual(Mathf.Abs(a), max + 0.0001f, $"progress={p} で最大角を超えた");
                Assert.IsTrue(float.IsFinite(a));
            }

            foreach (var bad in new[] { float.NaN, float.PositiveInfinity })
                Assert.IsTrue(float.IsFinite(SpiritBehaviorMath.ComputeTiltAngle(bad, bad)));
        }

        // ══ ForestSpiritSpawner / ForestSpirit ══════════════════════════

        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません");
            method.Invoke(c, null);
        }

        private static ForestSpiritSpawner MakeSpawner()
        {
            var go = new GameObject("TestForestSpiritSpawner");
            var spawner = go.AddComponent<ForestSpiritSpawner>();
            SetMinClusterSizeToOne(spawner);
            InvokeLifecycle(spawner, "OnEnable");
            return spawner;
        }

        /// <summary>
        /// 本番の既定値は4枚（Stage 15）だが、このファイルのテストは
        /// 生成条件ではなく生成後の挙動を見ているため、旧来どおり小さな森でも生成させる。
        /// 生成条件そのものはSpiritIntegrationTestsで検証する。
        /// </summary>
        private static void SetMinClusterSizeToOne(ForestSpiritSpawner spawner)
            => typeof(ForestSpiritSpawner)
                .GetField("_minClusterSizeToSpawn", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(spawner, 1);

        private static HexTile MakeTileAt(Vector3 position)
        {
            var go = new GameObject("TestForestTile");
            go.transform.position = position;
            return go.AddComponent<HexTile>();
        }

        private static void PublishForestGrowth(List<HexTile> tiles)
        {
            var metrics = new ForestGrowthMetrics(largestClusterSize: tiles.Count, totalForestTiles: tiles.Count);
            EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                terrainType: null, anchor: HexCoord.Zero, affectedTiles: tiles, metrics: metrics));
        }

        private static Vector3 GetHomeCenter(ForestSpirit spirit)
            => (Vector3)typeof(ForestSpirit)
                .GetField("_homeCenter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(spirit);

        private static void DestroyTiles(params List<HexTile>[] groups)
        {
            foreach (var g in groups)
                foreach (var t in g)
                    if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        // ── Stage 10-12・15. リアクションは最大1回／状態終了で表示が元へ戻る ──
        //    ForestSpiritのUpdateはEditModeで自動実行されないため、内部状態を直接進めて検証する。

        private static ForestSpirit MakeSpirit(out List<HexTile> tiles,
                                                SpiritPersonalityKind personality = SpiritPersonalityKind.Calm)
        {
            tiles = new List<HexTile> { MakeTileAt(Vector3.zero), MakeTileAt(new Vector3(1f, 0f, 0f)) };
            var go = new GameObject("TestForestSpirit");
            var spirit = go.AddComponent<ForestSpirit>();
            spirit.Initialize(tiles, Vector3.zero, 1.5f, 1.5f, 0.5f, personality);
            return spirit;
        }

        private static object GetField(object target, string name)
            => typeof(ForestSpirit).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private static void SetField(object target, string name, object value)
            => typeof(ForestSpirit).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private static void Invoke(object target, string name, params object[] args)
            => typeof(ForestSpirit).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

        [Test]
        public void ObserveReaction_RunsAtMostOncePerObserveTree()
        {
            var spirit = MakeSpirit(out var tiles);
            try
            {
                Invoke(spirit, "EnterState", SpiritState.ObserveTree);
                SetField(spirit, "_isMoving", false);
                SetField(spirit, "_stateDuration", 10f);

                // リアクションが1回走り切るまで時間を進める
                for (float t = 0f; t <= 6f; t += 0.1f)
                {
                    SetField(spirit, "_stateElapsed", t);
                    Invoke(spirit, "ApplyObserveReaction");
                }
                Assert.IsTrue((bool)GetField(spirit, "_reactionFinished"),
                    "1回のリアクションが完了しているはず");

                // 完了後にさらに時間を進めても、再生されない（何度も連続実行しない）
                var bodyRoot = (Transform)GetField(spirit, "_bodyRoot");
                for (float t = 6f; t <= 10f; t += 0.1f)
                {
                    SetField(spirit, "_stateElapsed", t);
                    Invoke(spirit, "ApplyObserveReaction");
                    Assert.AreEqual(Quaternion.identity, bodyRoot.localRotation,
                        "リアクション完了後は再度傾かないはず");
                    Assert.AreEqual(Vector3.zero, bodyRoot.localPosition,
                        "リアクション完了後は再度跳ねないはず");
                }
            }
            finally
            {
                Object.DestroyImmediate(spirit.gameObject);
                DestroyTiles(tiles);
            }
        }

        [Test]
        public void EnteringNewState_ResetsRotationScaleAndOffset()
        {
            var spirit = MakeSpirit(out var tiles);
            try
            {
                var bodyRoot = (Transform)GetField(spirit, "_bodyRoot");

                // 演出で変形した状態を人工的に作る
                bodyRoot.localRotation = Quaternion.Euler(0f, 0f, 20f);
                bodyRoot.localScale    = new Vector3(1.2f, 0.8f, 1.2f);
                bodyRoot.localPosition = new Vector3(0f, 0.05f, 0f);

                Invoke(spirit, "EnterState", SpiritState.Idle);

                Assert.AreEqual(Quaternion.identity, bodyRoot.localRotation, "状態遷移で傾きが残ってはいけない");
                Assert.AreEqual(Vector3.one,        bodyRoot.localScale,    "状態遷移で変形が残ってはいけない");
                Assert.AreEqual(Vector3.zero,       bodyRoot.localPosition, "状態遷移でYオフセットが残ってはいけない");
            }
            finally
            {
                Object.DestroyImmediate(spirit.gameObject);
                DestroyTiles(tiles);
            }
        }

        [Test]
        public void StretchPose_AtEndOfState_ReturnsToIdentityScale()
        {
            var spirit = MakeSpirit(out var tiles);
            try
            {
                var bodyRoot = (Transform)GetField(spirit, "_bodyRoot");
                Invoke(spirit, "EnterState", SpiritState.Stretch);
                SetField(spirit, "_stateDuration", 1.2f);

                // 途中では変形している
                SetField(spirit, "_stateElapsed", 0.3f);
                Invoke(spirit, "ApplyStretchPose");
                Assert.AreNotEqual(Vector3.one, bodyRoot.localScale, "Stretch中は変形しているはず");

                // 終了時には等倍へ戻る
                SetField(spirit, "_stateElapsed", 1.2f);
                Invoke(spirit, "ApplyStretchPose");
                Assert.AreEqual(1f, bodyRoot.localScale.x, 0.0001f);
                Assert.AreEqual(1f, bodyRoot.localScale.y, 0.0001f);
                Assert.AreEqual(1f, bodyRoot.localScale.z, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(spirit.gameObject);
                DestroyTiles(tiles);
            }
        }

        // ── 11. 最初の森イベントで1体だけ生成される ─────────────────────

        [Test]
        public void FirstForestEvent_SpawnsExactlyOneSpirit()
        {
            var spawner = MakeSpawner();
            var forestA = new List<HexTile> { MakeTileAt(new Vector3(10f, 0f, 10f)), MakeTileAt(new Vector3(11f, 0f, 10f)) };
            try
            {
                PublishForestGrowth(forestA);
                Assert.AreEqual(1, spawner.GetComponentsInChildren<ForestSpirit>(true).Length,
                    "最初の森イベントで精霊が1体だけ生成されるはず");
            }
            finally
            {
                InvokeLifecycle(spawner, "OnDisable");
                Object.DestroyImmediate(spawner.gameObject);
                DestroyTiles(forestA);
            }
        }

        // ── 12. 同一イベントの再送で重複生成されない ─────────────────────

        [Test]
        public void ResendingSameEvent_DoesNotSpawnDuplicate()
        {
            var spawner = MakeSpawner();
            var forestA = new List<HexTile> { MakeTileAt(new Vector3(10f, 0f, 10f)) };
            try
            {
                PublishForestGrowth(forestA);
                PublishForestGrowth(forestA);
                PublishForestGrowth(forestA);

                Assert.AreEqual(1, spawner.GetComponentsInChildren<ForestSpirit>(true).Length,
                    "同じイベントを再送しても精霊は増えないはず");
            }
            finally
            {
                InvokeLifecycle(spawner, "OnDisable");
                Object.DestroyImmediate(spawner.gameObject);
                DestroyTiles(forestA);
            }
        }

        // ── 13. 遠方の別クラスターで既存精霊のhomeが変わらない・2体目も出ない ──

        [Test]
        public void DistantForestEvent_DoesNotMoveOrDuplicateSpirit()
        {
            var spawner = MakeSpawner();
            var forestA = new List<HexTile> { MakeTileAt(new Vector3(10f, 0f, 10f)), MakeTileAt(new Vector3(11f, 0f, 10f)) };
            var forestB = new List<HexTile> { MakeTileAt(new Vector3(-40f, 0f, -40f)), MakeTileAt(new Vector3(-41f, 0f, -40f)) };
            try
            {
                PublishForestGrowth(forestA);
                var spirit = spawner.GetComponentsInChildren<ForestSpirit>(true)[0];
                var before = GetHomeCenter(spirit);

                PublishForestGrowth(forestB);

                Assert.AreEqual(before, GetHomeCenter(spirit),
                    "遠方の別クラスターでは既存精霊のhome中心は変わらないはず");
                Assert.AreEqual(1, spawner.GetComponentsInChildren<ForestSpirit>(true).Length,
                    "別クラスターのイベントで2体目を生成しないはず");
            }
            finally
            {
                InvokeLifecycle(spawner, "OnDisable");
                Object.DestroyImmediate(spawner.gameObject);
                DestroyTiles(forestA, forestB);
            }
        }

        // ── 14. home森と重なる成長イベントでは範囲を更新できる ───────────

        [Test]
        public void OwnForestGrowth_UpdatesHomeBounds()
        {
            var spawner = MakeSpawner();
            var tileA1 = MakeTileAt(new Vector3(10f, 0f, 10f));
            var tileA2 = MakeTileAt(new Vector3(11f, 0f, 10f));
            var tileA3 = MakeTileAt(new Vector3(24f, 0f, 10f)); // 森Aが東へ大きく伸びる
            var forestA = new List<HexTile> { tileA1, tileA2 };
            try
            {
                PublishForestGrowth(forestA);
                var spirit = spawner.GetComponentsInChildren<ForestSpirit>(true)[0];
                var before = GetHomeCenter(spirit);

                PublishForestGrowth(new List<HexTile> { tileA1, tileA2, tileA3 });

                Assert.AreNotEqual(before, GetHomeCenter(spirit),
                    "自分のhome森が育った場合は範囲・中心が更新されるはず");
            }
            finally
            {
                InvokeLifecycle(spawner, "OnDisable");
                Object.DestroyImmediate(spawner.gameObject);
                DestroyTiles(new List<HexTile> { tileA1, tileA2, tileA3 });
            }
        }

        // ── 15. OnDisable後にEventBus購読が残らない ──────────────────────

        [Test]
        public void AfterOnDisable_NoEventBusSubscriptionRemains()
        {
            var spawner = MakeSpawner();
            var forestA = new List<HexTile> { MakeTileAt(new Vector3(10f, 0f, 10f)) };
            try
            {
                InvokeLifecycle(spawner, "OnDisable");

                var handlersField = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
                var handlers = (System.Collections.IDictionary)handlersField.GetValue(null);

                int remaining = 0;
                if (handlers.Contains(typeof(TerrainGrowthEvent<ForestGrowthMetrics>)))
                {
                    var del = (System.Delegate)handlers[typeof(TerrainGrowthEvent<ForestGrowthMetrics>)];
                    foreach (var d in del.GetInvocationList())
                        if (System.Object.ReferenceEquals(d.Target, spawner)) remaining++;
                }
                Assert.AreEqual(0, remaining, "OnDisable後にこのSpawnerの購読が残ってはいけない");

                // 購読解除後のイベントでは生成もされない
                PublishForestGrowth(forestA);
                Assert.AreEqual(0, spawner.GetComponentsInChildren<ForestSpirit>(true).Length);
            }
            finally
            {
                Object.DestroyImmediate(spawner.gameObject);
                DestroyTiles(forestA);
            }
        }
    }
}
