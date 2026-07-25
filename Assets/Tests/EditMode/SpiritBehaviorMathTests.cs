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
            SpiritState.Idle, SpiritState.Wander, SpiritState.ObserveTree, SpiritState.Sleep,
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

        [Test]
        public void DecideNextState_SleepAlwaysReturnsToIdle()
        {
            for (int i = 0; i <= 100; i++)
                Assert.AreEqual(SpiritState.Idle,
                    SpiritBehaviorMath.DecideNextState(SpiritState.Sleep, i / 100f),
                    "Sleepからは必ずIdleへ戻るはず（いきなり動き出さない）");
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
            InvokeLifecycle(spawner, "OnEnable");
            return spawner;
        }

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
