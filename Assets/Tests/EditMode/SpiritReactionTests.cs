// 役割: Stage 11「世界へのリアクション」の検証。
//       刺激の受理条件・優先度・割り込み規則は純粋関数として分離してあるため決定論的に検証でき、
//       React状態への遷移や表示リセットは既存と同じリフレクションによるライフサイクル呼び出しで確認する。

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
    public class SpiritReactionTests
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

        private static HexTile MakeTileAt(Vector3 position)
        {
            var go = new GameObject("TestTile");
            go.transform.position = position;
            return go.AddComponent<HexTile>();
        }

        private static void DestroyTiles(IEnumerable<HexTile> tiles)
        {
            foreach (var t in tiles) if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        /// <summary>home森を(0,0,0)付近に持つ精霊を作る。</summary>
        private static ForestSpirit MakeSpirit(out List<HexTile> homeTiles,
                                                SpiritPersonalityKind personality = SpiritPersonalityKind.Calm)
        {
            homeTiles = new List<HexTile> { MakeTileAt(Vector3.zero), MakeTileAt(new Vector3(1f, 0f, 0f)) };
            var go = new GameObject("TestForestSpirit");
            var spirit = go.AddComponent<ForestSpirit>();
            spirit.Initialize(homeTiles, Vector3.zero, 1.5f, 1.5f, 0.5f, personality);
            Invoke(spirit, "OnEnable"); // 刺激の購読を開始する
            return spirit;
        }

        private static void Teardown(ForestSpirit spirit, IEnumerable<HexTile> tiles)
        {
            Invoke(spirit, "OnDisable");
            Object.DestroyImmediate(spirit.gameObject);
            DestroyTiles(tiles);
        }

        private static void PublishStimulus(SpiritStimulusKind kind, Vector3 pos, IReadOnlyList<HexTile> tiles = null)
            => EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(kind, pos, tiles)));

        // ══ 1〜3. Stimulusの表現 ═════════════════════════════════════════

        [Test]
        public void Stimulus_RepresentsForestGrewAndFlowerBloomed()
        {
            var tiles = new List<HexTile> { MakeTileAt(Vector3.zero) };
            try
            {
                var forest = new SpiritStimulus(SpiritStimulusKind.ForestGrew, new Vector3(1f, 0f, 2f), tiles);
                Assert.AreEqual(SpiritStimulusKind.ForestGrew, forest.Kind);
                Assert.AreEqual(new Vector3(1f, 0f, 2f), forest.WorldPosition);
                Assert.AreSame(tiles, forest.RelatedTiles);

                var flower = new SpiritStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(-3f, 0f, 4f));
                Assert.AreEqual(SpiritStimulusKind.FlowerBloomed, flower.Kind);
                Assert.IsNull(flower.RelatedTiles, "関係タイルは省略できるはず");
            }
            finally { DestroyTiles(tiles); }
        }

        [Test]
        public void UnknownStimulusKind_HasZeroPriority_AndIsIgnored()
        {
            var unknown = (SpiritStimulusKind)999;
            Assert.AreEqual(0, SpiritBehaviorMath.GetStimulusPriority(unknown), "未知の刺激は優先度0のはず");
            Assert.IsFalse(SpiritBehaviorMath.ShouldInterrupt(0, SpiritBehaviorMath.GetStimulusPriority(unknown)),
                "未知の刺激では割り込まないはず");
        }

        [Test]
        public void InvalidPosition_IsRejectedByPerception()
        {
            foreach (var bad in new[]
            {
                new Vector3(float.NaN, 0f, 0f),
                new Vector3(0f, 0f, float.PositiveInfinity),
                new Vector3(float.NegativeInfinity, float.NaN, 0f),
            })
            {
                Assert.IsFalse(SpiritBehaviorMath.IsWithinPerception(Vector3.zero, bad, 5f),
                    $"不正な位置 {bad} は拒否されるはず");
                Assert.IsFalse(SpiritBehaviorMath.IsWithinPerception(bad, Vector3.zero, 5f));
            }
        }

        // ══ 4〜8. 優先度と割り込み ═══════════════════════════════════════

        [Test]
        public void StimulusPriorities_MatchSpecification()
        {
            Assert.AreEqual(1, SpiritBehaviorMath.GetStimulusPriority(SpiritStimulusKind.ForestGrew));
            Assert.AreEqual(1, SpiritBehaviorMath.GetStimulusPriority(SpiritStimulusKind.FlowerBloomed));
        }

        [Test]
        public void ShouldInterrupt_OnlyWhenIncomingIsHigher()
        {
            Assert.IsTrue(SpiritBehaviorMath.ShouldInterrupt(0, 1), "通常状態(0)へは優先度1が割り込めるはず");
            Assert.IsTrue(SpiritBehaviorMath.ShouldInterrupt(1, 2));
            Assert.IsFalse(SpiritBehaviorMath.ShouldInterrupt(1, 1), "同優先度では割り込まないはず");
            Assert.IsFalse(SpiritBehaviorMath.ShouldInterrupt(2, 1), "低い優先度では割り込まないはず");
            Assert.IsFalse(SpiritBehaviorMath.ShouldInterrupt(0, 0), "優先度0の刺激は割り込まないはず");
        }

        [Test]
        public void ShouldInterrupt_IsDeterministic()
        {
            for (int c = 0; c <= 3; c++)
                for (int i = 0; i <= 3; i++)
                    Assert.AreEqual(SpiritBehaviorMath.ShouldInterrupt(c, i),
                                    SpiritBehaviorMath.ShouldInterrupt(c, i),
                                    $"current={c}, incoming={i} で結果が揺れる");
        }

        [Test]
        public void SleepAndStretch_CannotBeInterrupted()
        {
            Assert.IsFalse(SpiritBehaviorMath.CanBeInterruptedByStimulus(SpiritState.Sleep));
            Assert.IsFalse(SpiritBehaviorMath.CanBeInterruptedByStimulus(SpiritState.Stretch));
            Assert.IsTrue(SpiritBehaviorMath.CanBeInterruptedByStimulus(SpiritState.Idle));
            Assert.IsTrue(SpiritBehaviorMath.CanBeInterruptedByStimulus(SpiritState.Wander));
            Assert.IsTrue(SpiritBehaviorMath.CanBeInterruptedByStimulus(SpiritState.ObserveTree));
        }

        // ══ 9〜14. 受理条件 ══════════════════════════════════════════════

        [Test]
        public void ForestGrew_RelatedToHome_IsAccepted()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                PublishStimulus(SpiritStimulusKind.ForestGrew, new Vector3(0.5f, 0f, 0f), home);
                Assert.AreEqual(SpiritState.React, spirit.CurrentState, "home森の成長は受理してReactへ入るはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void ForestGrew_FromDistantCluster_IsIgnored()
        {
            var spirit = MakeSpirit(out var home);
            var far = new List<HexTile> { MakeTileAt(new Vector3(-40f, 0f, -40f)) };
            try
            {
                PublishStimulus(SpiritStimulusKind.ForestGrew, new Vector3(-40f, 0f, -40f), far);
                Assert.AreEqual(SpiritState.Idle, spirit.CurrentState, "別クラスターの成長は無視するはず");
            }
            finally { Teardown(spirit, home); DestroyTiles(far); }
        }

        [Test]
        public void FlowerBloomed_WithinPerception_IsAccepted()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(3f, 0f, 0f));
                Assert.AreEqual(SpiritState.React, spirit.CurrentState, "知覚距離内の開花は受理するはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void FlowerBloomed_BeyondPerception_IsIgnored()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(500f, 0f, 500f));
                Assert.AreEqual(SpiritState.Idle, spirit.CurrentState, "知覚距離外の開花は無視するはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void Perception_UsesHorizontalDistance_NotAffectedByHeight()
        {
            // 水平では近いがYが大きく離れている場合でも知覚できること（Y差で不自然に無視されない）
            Assert.IsTrue(SpiritBehaviorMath.IsWithinPerception(
                new Vector3(0f, 0.35f, 0f), new Vector3(1f, 50f, 0f), 5f),
                "Y差が大きくても水平距離が近ければ知覚するはず");

            // 逆に水平で遠ければ、Yが同じでも知覚しない
            Assert.IsFalse(SpiritBehaviorMath.IsWithinPerception(
                new Vector3(0f, 0f, 0f), new Vector3(50f, 0f, 0f), 5f));
        }

        [Test]
        public void Perception_InvalidRadius_IsRejected()
        {
            foreach (var r in new[] { 0f, -3f, float.NaN, float.PositiveInfinity })
                Assert.IsFalse(SpiritBehaviorMath.IsWithinPerception(Vector3.zero, new Vector3(1f, 0f, 0f), r),
                    $"不正な半径 {r} では知覚しないはず");
        }

        // ══ 15〜23. 状態遷移 ═════════════════════════════════════════════

        [Test]
        public void CanEnterReact_FromIdleWanderAndObserveTree()
        {
            foreach (var from in new[] { SpiritState.Idle, SpiritState.Wander, SpiritState.ObserveTree })
            {
                var spirit = MakeSpirit(out var home);
                try
                {
                    Invoke(spirit, "EnterState", from);
                    Assert.AreEqual(from, spirit.CurrentState, "前提: 指定状態に入っていること");

                    PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                    Assert.AreEqual(SpiritState.React, spirit.CurrentState, $"{from} からReactへ遷移できるはず");
                }
                finally { Teardown(spirit, home); }
            }
        }

        [Test]
        public void SleepAndStretch_AreNotInterruptedByStimulus()
        {
            foreach (var from in new[] { SpiritState.Sleep, SpiritState.Stretch })
            {
                var spirit = MakeSpirit(out var home);
                try
                {
                    Invoke(spirit, "EnterState", from);
                    PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(1f, 0f, 0f));
                    Assert.AreEqual(from, spirit.CurrentState, $"{from} は刺激で中断されないはず");
                }
                finally { Teardown(spirit, home); }
            }
        }

        [Test]
        public void React_AlwaysReturnsToIdle()
        {
            for (int i = 0; i <= 100; i++)
                Assert.AreEqual(SpiritState.Idle,
                    SpiritBehaviorMath.DecideNextState(SpiritState.React, i / 100f),
                    "Reactの後は必ずIdleへ戻るはず");
        }

        [Test]
        public void LeavingReact_ClearsPriority()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                Assert.AreEqual(1, (int)GetField(spirit, "_currentPriority"), "React中は優先度が保持されるはず");

                Invoke(spirit, "EnterState", SpiritState.Idle);
                Assert.AreEqual(0, (int)GetField(spirit, "_currentPriority"), "React終了時に優先度がクリアされるはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void SamePriorityStimulus_DoesNotRestartReact()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                Assert.AreEqual(SpiritState.React, spirit.CurrentState);

                SetField(spirit, "_stateElapsed", 0.7f);
                PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(2.5f, 0f, 0f));

                Assert.AreEqual(0.7f, (float)GetField(spirit, "_stateElapsed"), 0.0001f,
                    "同優先度の刺激ではReactが最初からやり直されないはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void InterruptingWander_DiscardsOldDestination()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                Invoke(spirit, "EnterState", SpiritState.Wander);
                Assert.IsTrue((bool)GetField(spirit, "_isMoving"), "前提: Wanderで移動中であること");

                PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));

                Assert.AreEqual(SpiritState.React, spirit.CurrentState);
                Assert.IsFalse((bool)GetField(spirit, "_isMoving"), "React開始で水平移動が停止するはず");

                // React後はIdleへ。古い目的地へ移動を再開しない。
                Invoke(spirit, "EnterState", SpiritState.Idle);
                Assert.IsFalse((bool)GetField(spirit, "_isMoving"), "Idleでは移動しないはず（古い目的地へ戻らない）");
            }
            finally { Teardown(spirit, home); }
        }

        // ══ 24〜27. 表示 ═════════════════════════════════════════════════

        [Test]
        public void ForestGrew_PlaysSmallHopOnly()
        {
            Assert.AreEqual(SpiritReactionKind.SmallHop,
                SpiritBehaviorMath.PickReactionFor(SpiritStimulusKind.ForestGrew));

            var spirit = MakeSpirit(out var home);
            try
            {
                PublishStimulus(SpiritStimulusKind.ForestGrew, new Vector3(0.5f, 0f, 0f), home);
                SetField(spirit, "_stateDuration", 1.5f);
                SetField(spirit, "_stateElapsed", 0.75f);
                Invoke(spirit, "ApplyReactPose");

                var body = (Transform)GetField(spirit, "_bodyRoot");
                Assert.Greater(body.localPosition.y, 0f, "SmallHopでは上下オフセットが出るはず");
                Assert.AreEqual(Quaternion.identity, body.localRotation, "SmallHopでは首を傾げないはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void FlowerBloomed_PlaysTiltHeadOnly()
        {
            Assert.AreEqual(SpiritReactionKind.TiltHead,
                SpiritBehaviorMath.PickReactionFor(SpiritStimulusKind.FlowerBloomed));

            var spirit = MakeSpirit(out var home);
            try
            {
                PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                SetField(spirit, "_stateDuration", 1.5f);
                SetField(spirit, "_stateElapsed", 0.75f);
                Invoke(spirit, "ApplyReactPose");

                var body = (Transform)GetField(spirit, "_bodyRoot");
                Assert.AreNotEqual(Quaternion.identity, body.localRotation, "TiltHeadでは首が傾くはず");
                Assert.AreEqual(0f, body.localPosition.y, 0.0001f, "TiltHeadでは跳ねないはず");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void LeavingReact_ResetsVisualPose()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                PublishStimulus(SpiritStimulusKind.FlowerBloomed, new Vector3(2f, 0f, 0f));
                SetField(spirit, "_stateDuration", 1.5f);
                SetField(spirit, "_stateElapsed", 0.75f);
                Invoke(spirit, "ApplyReactPose");

                var body = (Transform)GetField(spirit, "_bodyRoot");
                Assert.AreNotEqual(Quaternion.identity, body.localRotation, "前提: React中は変形していること");

                Invoke(spirit, "EnterState", SpiritState.Idle);

                Assert.AreEqual(Quaternion.identity, body.localRotation, "React終了で傾きが残ってはいけない");
                Assert.AreEqual(Vector3.one,        body.localScale,    "React終了で変形が残ってはいけない");
                Assert.AreEqual(Vector3.zero,       body.localPosition, "React終了でYオフセットが残ってはいけない");
            }
            finally { Teardown(spirit, home); }
        }

        [Test]
        public void StimulusAtSamePosition_DoesNotProduceInvalidRotation()
        {
            var spirit = MakeSpirit(out var home);
            try
            {
                var before = spirit.transform.rotation;
                // 精霊とまったく同じ位置で刺激が起きたケース
                PublishStimulus(SpiritStimulusKind.FlowerBloomed, spirit.transform.position);

                var after = spirit.transform.rotation;
                Assert.IsFalse(float.IsNaN(after.x) || float.IsNaN(after.y)
                            || float.IsNaN(after.z) || float.IsNaN(after.w),
                    "同一位置の刺激で不正な回転(NaN)を作ってはいけない");
                Assert.AreEqual(before, after, "向きを決められない場合は回転を変えないはず");
            }
            finally { Teardown(spirit, home); }
        }

        // ══ 28〜31. Relay ════════════════════════════════════════════════

        private static SpiritStimulusRelay MakeRelay()
        {
            var go = new GameObject("TestSpiritStimulusRelay");
            var relay = go.AddComponent<SpiritStimulusRelay>();
            Invoke(relay, "OnEnable");
            return relay;
        }

        private static int CountSubscribers<T>(object target)
        {
            var handlers = (System.Collections.IDictionary)
                typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            if (!handlers.Contains(typeof(T))) return 0;

            int n = 0;
            foreach (var d in ((System.Delegate)handlers[typeof(T)]).GetInvocationList())
                if (ReferenceEquals(d.Target, target)) n++;
            return n;
        }

        [Test]
        public void Relay_SubscribesOnEnable_AndUnsubscribesOnDisable()
        {
            var relay = MakeRelay();
            try
            {
                Assert.AreEqual(1, CountSubscribers<TerrainGrowthEvent<ForestGrowthMetrics>>(relay),
                    "OnEnableで森イベントを購読するはず");
                Assert.AreEqual(1, CountSubscribers<FlowerClusterEvent>(relay),
                    "OnEnableで花イベントを購読するはず");

                Invoke(relay, "OnDisable");

                Assert.AreEqual(0, CountSubscribers<TerrainGrowthEvent<ForestGrowthMetrics>>(relay),
                    "OnDisableで購読解除されるはず");
                Assert.AreEqual(0, CountSubscribers<FlowerClusterEvent>(relay),
                    "OnDisableで購読解除されるはず");
            }
            finally { Object.DestroyImmediate(relay.gameObject); }
        }

        [Test]
        public void Relay_DoesNotDoubleSubscribe_WhenEnabledTwice()
        {
            var relay = MakeRelay();
            try
            {
                Invoke(relay, "OnEnable"); // 二重呼び出し
                Assert.AreEqual(1, CountSubscribers<TerrainGrowthEvent<ForestGrowthMetrics>>(relay),
                    "同一インスタンスで重複購読してはいけない");
                Assert.AreEqual(1, CountSubscribers<FlowerClusterEvent>(relay));
            }
            finally { Invoke(relay, "OnDisable"); Object.DestroyImmediate(relay.gameObject); }
        }

        [Test]
        public void Relay_TranslatesFlowerClusterEvent_IntoStimulus()
        {
            var relay = MakeRelay();
            var tiles = new List<HexTile> { MakeTileAt(new Vector3(2f, 0f, 4f)), MakeTileAt(new Vector3(4f, 0f, 4f)) };
            try
            {
                SpiritStimulusEvent received = null;
                System.Action<SpiritStimulusEvent> handler = e => received = e;
                EventBus.Subscribe(handler);
                try
                {
                    EventBus.Publish(new FlowerClusterEvent(tiles));

                    Assert.IsNotNull(received, "花イベントが刺激へ翻訳されるはず");
                    Assert.AreEqual(SpiritStimulusKind.FlowerBloomed, received.Stimulus.Kind);
                    Assert.AreEqual(3f, received.Stimulus.WorldPosition.x, 0.0001f, "タイル群の重心が使われるはず");
                    Assert.AreEqual(4f, received.Stimulus.WorldPosition.z, 0.0001f);
                }
                finally { EventBus.Unsubscribe(handler); }
            }
            finally { Invoke(relay, "OnDisable"); Object.DestroyImmediate(relay.gameObject); DestroyTiles(tiles); }
        }

        [Test]
        public void Relay_AfterDisable_PublishesNothing()
        {
            var relay = MakeRelay();
            var tiles = new List<HexTile> { MakeTileAt(Vector3.zero) };
            Invoke(relay, "OnDisable");
            try
            {
                int count = 0;
                System.Action<SpiritStimulusEvent> handler = e => count++;
                EventBus.Subscribe(handler);
                try
                {
                    EventBus.Publish(new FlowerClusterEvent(tiles));
                    var metrics = new ForestGrowthMetrics(tiles.Count, tiles.Count);
                    EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                        null, HexCoord.Zero, tiles, metrics));

                    Assert.AreEqual(0, count, "無効化後は刺激を発行しないはず");
                }
                finally { EventBus.Unsubscribe(handler); }
            }
            finally { Object.DestroyImmediate(relay.gameObject); DestroyTiles(tiles); }
        }

        [Test]
        public void Relay_EmptyTileList_PublishesNothing()
        {
            var relay = MakeRelay();
            try
            {
                int count = 0;
                System.Action<SpiritStimulusEvent> handler = e => count++;
                EventBus.Subscribe(handler);
                try
                {
                    EventBus.Publish(new FlowerClusterEvent(new List<HexTile>()));
                    Assert.AreEqual(0, count, "有効なタイルが無ければ刺激を発行しないはず");
                }
                finally { EventBus.Unsubscribe(handler); }
            }
            finally { Invoke(relay, "OnDisable"); Object.DestroyImmediate(relay.gameObject); }
        }
    }
}
