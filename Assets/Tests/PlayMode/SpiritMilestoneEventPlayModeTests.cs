// 役割: Stage 16「誕生・成長イベントの一回性」を実ライフサイクルで検証する。
//       ★このファイルはイベントの発行タイミングと回数だけを見る。
//         演出・音・通知の中身はPresentation側のテストが担当する。
//       Phase1_v002は開かず、最小Hierarchyを構築する方針を維持する。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Spirits;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class SpiritMilestoneEventPlayModeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _spawned = new();

        private readonly List<ForestSpiritSpawnedEvent>         _births  = new();
        private readonly List<ForestSpiritGrowthCommittedEvent> _growths = new();

        private System.Action<ForestSpiritSpawnedEvent>         _birthHandler;
        private System.Action<ForestSpiritGrowthCommittedEvent> _growthHandler;

        private static void ClearEventBus()
        {
            var f = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "EventBus._handlers が見つかりません");
            ((System.Collections.IDictionary)f.GetValue(null)).Clear();
        }

        [SetUp]
        public void SetUp()
        {
            ClearEventBus();
            GameInteractionStateController.SetState(GameInteractionState.Playing);

            _births.Clear();
            _growths.Clear();

            // Subscribe/Unsubscribeで同じデリゲート実体を使うため変数に保持する。
            _birthHandler  = e => _births.Add(e);
            _growthHandler = e => _growths.Add(e);
            EventBus.Subscribe(_birthHandler);
            EventBus.Subscribe(_growthHandler);
        }

        [TearDown]
        public void TearDown()
        {
            if (_birthHandler  != null) EventBus.Unsubscribe(_birthHandler);
            if (_growthHandler != null) EventBus.Unsubscribe(_growthHandler);

            GameInteractionStateController.SetState(GameInteractionState.Playing);
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearEventBus();
        }

        // ── ヘルパー ──────────────────────────────────────────────────

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        private List<HexTile> MakeForest(string name, Vector3 origin, int count)
        {
            var root = Track(new GameObject(name));
            var tiles = new List<HexTile>();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject(name + "_Tile" + i);
                go.transform.SetParent(root.transform, true);
                var tile = go.AddComponent<HexTile>();
                tile.Initialize(new HexCoord(i, -i), 1f);
                go.transform.position = origin + new Vector3(i * 1.5f, 0f, (i % 2) * 0.866f);
                tiles.Add(tile);
            }
            return tiles;
        }

        private ForestSpiritSpawner MakeSpiritsSystem(bool relayFirst = false)
        {
            var go = Track(new GameObject("Spirits"));
            ForestSpiritSpawner spawner;
            if (relayFirst)
            {
                go.AddComponent<SpiritStimulusRelay>();
                spawner = go.AddComponent<ForestSpiritSpawner>();
            }
            else
            {
                spawner = go.AddComponent<ForestSpiritSpawner>();
                go.AddComponent<SpiritStimulusRelay>();
            }
            typeof(ForestSpiritSpawner).GetField("_minClusterSizeToSpawn", Priv).SetValue(spawner, 4);
            return spawner;
        }

        private static void PublishForestGrowth(IReadOnlyList<HexTile> tiles)
            => EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                   null, HexCoord.Zero, tiles, new ForestGrowthMetrics(tiles.Count, tiles.Count)));

        private static ForestSpirit SpiritOf(ForestSpiritSpawner s) => s.GetComponentInChildren<ForestSpirit>(true);
        private static object GetField(object t, string n) => t.GetType().GetField(n, Priv).GetValue(t);

        private static void PublishFlowerNear(ForestSpirit spirit)
            => EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(
                   SpiritStimulusKind.FlowerBloomed, spirit.transform.position + new Vector3(0.5f, 0f, 0f), null)));

        private static IEnumerator WaitUntil(System.Func<bool> cond, float timeout, string msg)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!cond())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, msg);
                yield return null;
            }
        }

        // ══ 誕生イベントの一回性 ════════════════════════════════════════

        [UnityTest]
        public IEnumerator BirthEvent_IsPublishedExactlyOnce_WhenClusterReachesMinimum()
        {
            var spawner = MakeSpiritsSystem();
            yield return null;

            // 3枚までは生まれないので誕生イベントも出ない。
            for (int size = 1; size <= 3; size++)
            {
                PublishForestGrowth(MakeForest("F" + size, new Vector3(size * 20f, 0f, 0f), size));
                yield return null;
                Assert.AreEqual(0, _births.Count, $"クラスタ{size}枚で誕生イベントが発行された");
            }

            var forest = MakeForest("Home", Vector3.zero, 4);
            PublishForestGrowth(forest);
            yield return null;

            Assert.AreEqual(1, _births.Count, "4枚到達で誕生イベントがちょうど1回発行されるべき");

            var spirit = SpiritOf(spawner);
            Assert.IsNotNull(spirit);
            Assert.AreEqual(spirit.Personality, _births[0].Personality, "payloadの性格が一致しない");
            Assert.AreEqual(SpiritGrowthStage.Sprout, _births[0].Stage, "生まれた段階はSproutのはず");
            Assert.AreEqual(spirit.transform.position, _births[0].WorldPosition, "payloadの位置が一致しない");
        }

        [UnityTest]
        public IEnumerator BirthEvent_IsNotRepublished_WhenForestKeepsGrowing()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;
            Assert.AreEqual(1, _births.Count);

            for (int extra = 0; extra < 4; extra++)
            {
                var grown = new List<HexTile>(forest);
                grown.AddRange(MakeForest("Ext" + extra, new Vector3(-3f - extra * 1.5f, 0f, 0f), 2));
                PublishForestGrowth(grown);
                yield return null;

                Assert.AreEqual(1, _births.Count, $"{extra + 1}回目の成長で誕生イベントが再発行された");
            }
        }

        [UnityTest]
        public IEnumerator BirthEvent_IsOnce_RegardlessOfSubscriptionOrder()
        {
            var counts = new List<int>();

            foreach (var relayFirst in new[] { false, true })
            {
                MakeSpiritsSystem(relayFirst);
                var forest = MakeForest("Home", Vector3.zero, 4);
                yield return null;

                PublishForestGrowth(forest);
                yield return null;

                counts.Add(_births.Count);

                TearDown();
                SetUp();
            }

            Assert.AreEqual(1, counts[0], "Spawner先: 誕生イベントが1回でない");
            Assert.AreEqual(1, counts[1], "Relay先: 誕生イベントが1回でない");
        }

        // ══ 成長イベントの一回性 ════════════════════════════════════════

        private IEnumerator SpawnAndSettle(float tFluff, float tBloom, System.Action<ForestSpirit> onReady)
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.IsNotNull(spirit);

            typeof(ForestSpirit).GetField("_growthThresholdFluff", Priv).SetValue(spirit, tFluff);
            typeof(ForestSpirit).GetField("_growthThresholdBloom", Priv).SetValue(spirit, tBloom);

            yield return WaitUntil(() => spirit.CurrentState == SpiritState.Idle, 10f, "Idleへ戻らなかった");
            onReady(spirit);
        }

        [UnityTest]
        public IEnumerator GrowthEvent_IsPublishedOnce_PerStage()
        {
            ForestSpirit spirit = null;
            yield return SpawnAndSettle(2f, 99f, s => spirit = s);

            Assert.AreEqual(0, _growths.Count, "まだ成長していないのにイベントが出ている");

            PublishFlowerNear(spirit);   // 累積体験 1→2 でFluffへ
            yield return null;

            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f, "成長しなかった");

            Assert.AreEqual(1, _growths.Count, "成長イベントがちょうど1回発行されるべき");
            Assert.AreEqual(SpiritGrowthStage.Sprout, _growths[0].PreviousStage);
            Assert.AreEqual(SpiritGrowthStage.Fluff,  _growths[0].NewStage);
            Assert.AreEqual(spirit.Personality, _growths[0].Personality);

            // 演出が終わってもう一度Idleを経ても、再発行されない。
            yield return WaitUntil(() => !(bool)GetField(spirit, "_growthFlourishActive"), 5f, "演出が終わらない");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.AreEqual(1, _growths.Count, "同じ段階で成長イベントが再発行された");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator GrowthEvent_MultiplePending_FiresOncePerIdleVisit()
        {
            ForestSpirit spirit = null;
            // 閾値を同値にして1回の刺激でSprout→Bloomまで予約させる。
            yield return SpawnAndSettle(2f, 2f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;

            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f,
                "最初のIdleでFluffへ進まなかった");

            Assert.AreEqual(1, _growths.Count, "1回のIdle滞在で2回発行された");
            Assert.AreEqual(SpiritGrowthStage.Fluff, _growths[0].NewStage);

            // 同じIdle滞在中は2段階目のイベントが出ない。
            while (spirit.CurrentState == SpiritState.Idle)
            {
                Assert.AreEqual(1, _growths.Count, "同じIdle滞在中に2段階目のイベントが出た");
                yield return null;
            }

            // 次にIdleへ入ったとき、残りの段階が1回だけ発行される。
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Bloom, 45f,
                "次のIdleでBloomへ進まなかった");

            Assert.AreEqual(2, _growths.Count, "Bloom到達で通算2回になっているべき");
            Assert.AreEqual(SpiritGrowthStage.Fluff, _growths[1].PreviousStage);
            Assert.AreEqual(SpiritGrowthStage.Bloom, _growths[1].NewStage);
        }

        [UnityTest]
        public IEnumerator GrowthEvent_NotPublished_WhenInterruptedBeforeMidpoint()
        {
            ForestSpirit spirit = null;
            yield return SpawnAndSettle(2f, 99f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;

            yield return WaitUntil(() => (bool)GetField(spirit, "_growthFlourishActive"), 30f,
                "成長演出が始まらなかった");

            Assert.IsFalse((bool)GetField(spirit, "_growthAppliedThisFlourish"),
                "既に頂点を越えていた（テストが成立していない）");
            Assert.AreEqual(0, _growths.Count, "頂点前なのに成長イベントが発行された");

            // 頂点前にReactで割り込む
            PublishFlowerNear(spirit);
            yield return null;

            Assert.AreEqual(SpiritState.React, spirit.CurrentState, "Reactへ割り込めなかった");
            Assert.AreEqual(0, _growths.Count, "頂点前の中断で成長イベントが発行された");
            Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage, "段階が確定してしまった");

            // やり直して最終的に1回だけ発行される。
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f,
                "中断後に再演出されなかった");
            Assert.AreEqual(1, _growths.Count, "再演出後の発行が1回でない");
        }

        [UnityTest]
        public IEnumerator GrowthEvent_NotPublished_WhileSettingsIsOpen()
        {
            ForestSpirit spirit = null;
            yield return SpawnAndSettle(2f, 99f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;

            yield return WaitUntil(() => (bool)GetField(spirit, "_growthFlourishActive"), 30f,
                "成長演出が始まらなかった");

            GameInteractionStateController.SetState(GameInteractionState.Settings);
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(0, _growths.Count, "Settings中に成長イベントが発行された");

            GameInteractionStateController.SetState(GameInteractionState.Playing);
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 15f,
                "解除後に成長しなかった");

            Assert.AreEqual(1, _growths.Count, "解除後の発行が1回でない");
        }
    }
}
