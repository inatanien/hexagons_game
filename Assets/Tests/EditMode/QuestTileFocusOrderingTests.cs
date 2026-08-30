// 役割: 祝う対象の解決が「購読順」に左右されないことを、実際のイベント経路で固定する。
//
//       ★EventBusは同期発行だが、同じイベントを購読している者どうしの順番は保証されない。
//         ここでは意地悪な順番、つまり Relay を Tracker より先に購読させた状態で試す。
//         この順だと
//           TilePlacedEvent → Relay → QuestManager → 達成 → Celebration
//         まで走ったあとに Tracker の TilePlacedEvent が届くので、
//         その場で解決していたら「達成を決めた最後の1枚」が抜け落ちる。
//         Trackerが解決をLateUpdateまで遅らせていれば、順番に関係なく正しい集合になる。
//
//       ★Script Execution Orderや購読順の調整では直さない。
//         それは順序への依存を別の形で残すだけなので、テストもその前提を置かない。
//
//       注意: EditModeではAddComponent直後にOnEnable/OnDisableが自動発火せず、
//       LateUpdateも回らないため、リフレクションで明示的に呼び出す。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Quest;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class QuestTileFocusOrderingTests
    {
        private readonly List<Object>    _created = new();
        private readonly List<Component> _enabled = new();

        [TearDown]
        public void TearDown()
        {
            // ★EventBusはstaticなので、購読を残したままにすると後続のテストへ漏れる。
            //   このテストで有効化したものは、有効化した逆順で必ず解除する
            for (int i = _enabled.Count - 1; i >= 0; i--)
                if (_enabled[i] != null) InvokeLifecycle(_enabled[i], "OnDisable");
            _enabled.Clear();

            foreach (var o in _created)
                if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ── ヘルパー ────────────────────────────────────────────────

        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません（リフレクション対象名の変更を確認してください）");
            method.Invoke(c, null);
        }

        private static readonly Dictionary<TileCategory, EdgeType> s_categoryToEdge = new()
        {
            { TileCategory.Forest, EdgeType.Forest },
            { TileCategory.Field,  EdgeType.Field  },
            { TileCategory.River,  EdgeType.River  },
        };

        private TileType MakeTileType(TileCategory category)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            _created.Add(t);

            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category = category;
            _created.Add(v);

            t.elements = new[] { new TileElement { variant = v, areaWeight = 1f, visualOnly = false } };
            if (s_categoryToEdge.TryGetValue(category, out var edgeType))
                for (int d = 0; d < 6; d++) t.edges[d] = edgeType;
            return t;
        }

        private HexTile MakeTile(int q, int r)
        {
            var go = new GameObject($"TestTile_{q}_{r}");
            _created.Add(go);
            var tile = go.AddComponent<HexTile>();
            tile.Initialize(new HexCoord(q, r), 1f);
            return tile;
        }

        private QuestDefinition MakeQuest(QuestCondition condition)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(q);
            q.title     = "順番テスト用クエスト";
            q.condition = condition;
            return q;
        }

        /// <summary>
        /// 本番と同じ経路を組む。★Relay類をTrackerより先に購読させるのが要点で、
        /// 「Trackerが先に処理されるはず」という前提が無いことを確かめるための順番。
        /// </summary>
        private (QuestManager manager, QuestTileFocusTracker tracker) MakeRig()
        {
            var go = new GameObject("TestQuestRig");
            _created.Add(go);

            var worldRelay   = go.AddComponent<WorldEventRelay>();
            var clusterRelay = go.AddComponent<TerrainClusterProgressRelay>();
            var manager      = go.AddComponent<QuestManager>();
            var tracker      = go.AddComponent<QuestTileFocusTracker>();

            foreach (var component in new Component[] { worldRelay, clusterRelay, manager, tracker })
            {
                InvokeLifecycle(component, "OnEnable");
                _enabled.Add(component);
            }

            return (manager, tracker);
        }

        private sealed class Resolved : System.IDisposable
        {
            public readonly List<IReadOnlyList<HexTile>> Results = new();

            private readonly System.Action<QuestTileSelectionResolvedEvent> _handler;

            public Resolved()
            {
                _handler = e => Results.Add(e.Tiles);
                EventBus.Subscribe(_handler);
            }

            public IReadOnlyList<HexTile> Last => Results[Results.Count - 1];

            public void Dispose() => EventBus.Unsubscribe(_handler);
        }

        private QuestSequenceDefinition MakeSequence(params QuestDefinition[] quests)
        {
            var s = ScriptableObject.CreateInstance<QuestSequenceDefinition>();
            _created.Add(s);
            s.name   = "OrderingTestSequence";
            s.quests = quests;
            return s;
        }

        /// <summary>Sequence進行役を後付けする。切り替え待ちを0にすると同じチェーンで次が始まる。</summary>
        private QuestSequenceRunner AttachRunner(QuestManager manager, QuestSequenceDefinition sequence, float nextQuestDelay)
        {
            var runner = manager.gameObject.AddComponent<QuestSequenceRunner>();
            SetPrivateField(runner, "_sequence", sequence);
            SetPrivateField(runner, "_questManager", manager);
            SetPrivateField(runner, "_nextQuestDelay", nextQuestDelay);

            InvokeLifecycle(runner, "OnEnable");
            _enabled.Add(runner);
            return runner;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}に{fieldName}フィールドが見つかりません");
            field.SetValue(target, value);
        }

        /// <summary>そのフレームの同期イベント処理が終わった状態まで進める。</summary>
        private static void EndOfFrame(QuestTileFocusTracker tracker)
            => InvokeLifecycle(tracker, "LateUpdate");

        // ── 1. TilePlacedCount ─────────────────────────────────────────

        [Test]
        public void TilePlacedCount_RelaySubscribedFirst_StillIncludesTheDecidingTile()
        {
            var rig       = MakeRig();
            var fieldType = MakeTileType(TileCategory.Field);
            var first     = MakeTile(0, 0);
            var second    = MakeTile(1, 0);

            using (var r = new Resolved())
            {
                rig.manager.SetQuest(MakeQuest(
                    new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 2)));

                EventBus.Publish(new TilePlacedEvent(first,  fieldType, first.Data.coord));
                EventBus.Publish(new TilePlacedEvent(second, fieldType, second.Data.coord));
                EndOfFrame(rig.tracker);


                Assert.AreEqual(1, r.Results.Count, "解決は1回だけのはず");
                CollectionAssert.AreEquivalent(new[] { first, second }, r.Last,
                    "達成を決めた2枚目も対象に入るはず（Relayが先に処理されても抜け落ちない）");
            }
        }

        // ── 2. ClusterSize ─────────────────────────────────────────────

        [Test]
        public void ClusterSize_RelaySubscribedFirst_StillSelectsWholeCluster()
        {
            var rig     = MakeRig();
            var cluster = new[] { MakeTile(0, 0), MakeTile(1, 0), MakeTile(2, 0), MakeTile(3, 0), MakeTile(4, 0) };

            using (var r = new Resolved())
            {
                rig.manager.SetQuest(MakeQuest(
                    new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.Forest, 5)));

                EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                    null, HexCoord.Zero, cluster, new ForestGrowthMetrics(cluster.Length, cluster.Length)));
                EndOfFrame(rig.tracker);


                CollectionAssert.AreEquivalent(cluster, r.Last,
                    "達成させた最新クラスター全体が選ばれるはず");
            }
        }

        // ── 3. Bridge ──────────────────────────────────────────────────

        [Test]
        public void Bridge_RelaySubscribedFirst_StillSelectsWholeRiverGroup()
        {
            var rig     = MakeRig();
            var cluster = new[] { MakeTile(0, 0), MakeTile(1, 0), MakeTile(2, 0), MakeTile(3, 0), MakeTile(4, 0) };

            using (var r = new Resolved())
            {
                rig.manager.SetQuest(MakeQuest(new QuestCondition(WorldEventKeys.Bridge, 1)));

                EventBus.Publish(new RiverBridgeEvent(cluster[4], cluster, cluster.Length));
                EndOfFrame(rig.tracker);


                CollectionAssert.AreEquivalent(cluster, r.Last,
                    "RiverBridgeEvent.Tiles全体が選ばれるはず");
            }
        }

        // ── 4. Synergy ─────────────────────────────────────────────────

        [Test]
        public void Synergy_RelaySubscribedFirst_StillSelectsBothSides()
        {
            var rig    = MakeRig();
            var forest = new List<HexTile> { MakeTile(0, 0), MakeTile(1, 0) };
            var river  = new List<HexTile> { MakeTile(0, 1), MakeTile(1, 1), MakeTile(2, 1) };

            using (var r = new Resolved())
            {
                rig.manager.SetQuest(MakeQuest(
                    new QuestCondition(WorldEventKeys.Synergy("ForestRiver"), 1)));

                EventBus.Publish(new TerrainSynergyEvent("ForestRiver", forest, river));
                EndOfFrame(rig.tracker);


                var expected = new List<HexTile>(forest);
                expected.AddRange(river);
                CollectionAssert.AreEquivalent(expected, r.Last, "TilesA + TilesB が選ばれるはず");
            }
        }

        // ── 4b. 切り替え待ち0秒で次クエストが同じチェーンで始まっても正しい ──
        // Sequenceの_nextQuestDelayが0だと、達成から次クエスト開始までが
        // 元のTilePlacedEventと同じ同期チェーンの中で進む。
        // その途中で蓄積を消してしまうと、達成を決めた最後の1枚ごと失われる。

        [Test]
        public void InstantQuestSwitch_StillCelebratesAllTilesOfTheFinishedQuest()
        {
            var rig       = MakeRig();
            var fieldType = MakeTileType(TileCategory.Field);
            var first     = MakeTile(0, 0);
            var second    = MakeTile(1, 0);
            var third     = MakeTile(2, 0);

            var questA = MakeQuest(new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 2));
            var questB = MakeQuest(new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 1));
            var runner = AttachRunner(rig.manager, MakeSequence(questA, questB), nextQuestDelay: 0f);

            using (var r = new Resolved())
            {
                InvokeLifecycle(runner, "Start");   // 1本目が始まる
                EndOfFrame(rig.tracker);

                EventBus.Publish(new TilePlacedEvent(first,  fieldType, first.Data.coord));
                // ★この1枚で達成し、同じチェーンの中で2本目が始まる（delay 0）
                EventBus.Publish(new TilePlacedEvent(second, fieldType, second.Data.coord));
                EndOfFrame(rig.tracker);

                Assert.AreEqual(1, r.Results.Count, "1本目の祝いは1回だけ発行されるはず");
                CollectionAssert.AreEquivalent(new[] { first, second }, r.Last,
                    "切り替えが即時でも、達成を決めた2枚目まで対象に入るはず");

                // 2本目の祝いには1本目のタイルが混ざらない
                EventBus.Publish(new TilePlacedEvent(third, fieldType, third.Data.coord));
                EndOfFrame(rig.tracker);

                Assert.AreEqual(2, r.Results.Count);
                CollectionAssert.AreEquivalent(new[] { third }, r.Last,
                    "2本目は自分の1枚だけを祝うはず（前のクエストのタイルは持ち越さない）");
            }
        }

        // ── 5. 解決待ちのままOnDisableされたら発行しない ────────────────

        [Test]
        public void PendingCelebration_DiscardedOnDisable()
        {
            var rig       = MakeRig();
            var fieldType = MakeTileType(TileCategory.Field);
            var tile      = MakeTile(0, 0);

            using (var r = new Resolved())
            {
                rig.manager.SetQuest(MakeQuest(
                    new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 1)));

                EventBus.Publish(new TilePlacedEvent(tile, fieldType, tile.Data.coord));
                // ここでまだ解決していない（LateUpdate前）
                Assert.AreEqual(0, r.Results.Count, "この時点では解決していないはず");

                InvokeLifecycle(rig.tracker, "OnDisable");
                EndOfFrame(rig.tracker);

                Assert.AreEqual(0, r.Results.Count, "無効化されたら解決待ちは捨てるはず");
            }
        }

        // ── 6. 解決待ち中に次のクエストが始まっても、前のFocusで解決する ─

        [Test]
        public void PendingCelebration_IsResolvedWithItsOwnFocus_EvenIfNextQuestStarts()
        {
            var rig       = MakeRig();
            var fieldType = MakeTileType(TileCategory.Field);
            var first     = MakeTile(0, 0);
            var second    = MakeTile(1, 0);

            using (var r = new Resolved())
            {
                rig.manager.SetQuest(MakeQuest(
                    new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 2)));

                EventBus.Publish(new TilePlacedEvent(first,  fieldType, first.Data.coord));
                EventBus.Publish(new TilePlacedEvent(second, fieldType, second.Data.coord));
                Assert.AreEqual(0, r.Results.Count, "まだ解決していないはず");

                // 解決待ちのまま次のクエストが始まった（Sequenceの切り替えが同じフレームで起きた場合）
                rig.manager.SetQuest(MakeQuest(
                    new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.River, 3)));

                Assert.AreEqual(0, r.Results.Count,
                    "次のクエストが始まっても、まだ同期チェーンの途中なので解決しないはず");

                EndOfFrame(rig.tracker);

                Assert.AreEqual(1, r.Results.Count, "フレーム末で解決されるはず");
                CollectionAssert.AreEquivalent(new[] { first, second }, r.Last,
                    "前のクエストの祝いは、前のクエストのFocusで解決されるはず");

                // 二重解決しない
                EndOfFrame(rig.tracker);

                Assert.AreEqual(1, r.Results.Count, "同じ祝いを二度解決してはいけない");
            }
        }
    }
}
