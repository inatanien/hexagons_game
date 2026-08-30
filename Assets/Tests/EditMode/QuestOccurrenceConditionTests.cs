// 役割: 加算型のクエスト条件（TilePlacedCount / EventOccurrence）と、
//       条件種別ごとの購読の分離を固定する（Step 3）。
//
//       ★ClusterSizeは「現在の状態の観測」で加算しない。
//         TilePlacedCount / EventOccurrenceは「出来事の回数」なので1イベントにつき+1。
//         どちらもクランプ・通知・完了1回はStep 2のReportProgressへ合流する。
//
//       ★条件種別ごとに必要なイベントだけを購読することを、3種別すべてについて固定する。
//         ここが崩れると、例えば森クエストがタイル配置のたびに進んでしまう。
//
//       注意: EditModeではAddComponent直後にOnEnable/OnDisableが自動発火しないため、
//       リフレクションで明示的に呼び出す（既存テストと同じ手法）。

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Quest;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class QuestOccurrenceConditionTests
    {
        private const string QuestFolder = "Assets/_Game/ScriptableObjects/QuestData/";
        private const string ScenePath   = "Assets/Scenes/Phase1_v002.unity";

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
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

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}に{fieldName}フィールドが見つかりません");
            field.SetValue(target, value);
        }

        private QuestDefinition MakeQuest(QuestCondition condition)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(q);
            q.title     = "テストクエスト";
            q.condition = condition;
            return q;
        }

        private QuestManager MakeManager(QuestDefinition quest, bool runLifecycle = true)
        {
            var go      = new GameObject("TestQuestManager");
            _created.Add(go);
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            if (runLifecycle)
            {
                InvokeLifecycle(manager, "OnEnable");
                InvokeLifecycle(manager, "Start");
            }
            return manager;
        }

        private static void Teardown(QuestManager manager) => InvokeLifecycle(manager, "OnDisable");

        /// <summary>進捗・達成の通知を数えるスコープ。</summary>
        private sealed class Counter : System.IDisposable
        {
            public int Progressed;
            public int Completed;
            public readonly List<int> Values = new();

            private readonly System.Action<QuestProgressChangedEvent> _onProgress;
            private readonly System.Action<QuestCompletedEvent>       _onCompleted;

            public Counter()
            {
                _onProgress  = e => { Progressed++; Values.Add(e.CurrentCount); };
                _onCompleted = _ => Completed++;
                EventBus.Subscribe(_onProgress);
                EventBus.Subscribe(_onCompleted);
            }

            public void Dispose()
            {
                EventBus.Unsubscribe(_onProgress);
                EventBus.Unsubscribe(_onCompleted);
            }
        }

        // ── TilePlacedCount ────────────────────────────────────────────

        [Test]
        public void TilePlacedCount_CompletesOnSecondMatchingTile()
        {
            var manager = MakeManager(MakeQuest(
                new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 2)));

            using (var c = new Counter())
            {
                EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Forest));  // 対象外
                EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Field));
                Assert.AreEqual(0, c.Completed, "1枚目では達成しないはず");

                EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Field));
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { 1, 2 }, c.Values, "対象カテゴリだけを1枚ずつ数えるはず");
                Assert.AreEqual(1, c.Completed);
            }
        }

        [Test]
        public void TilePlacedCount_IsIncremental_NotObservedValue()
        {
            var manager = MakeManager(MakeQuest(
                new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 5)));

            using (var c = new Counter())
            {
                for (int i = 0; i < 3; i++)
                    EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Field));
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { 1, 2, 3 }, c.Values, "出来事は1回につき+1のはず");
                Assert.AreEqual(0, c.Completed);
            }
        }

        // ── EventOccurrence ────────────────────────────────────────────

        [Test]
        public void EventOccurrence_Bridge_Completes()
        {
            var manager = MakeManager(MakeQuest(new QuestCondition(WorldEventKeys.Bridge, 1)));

            using (var c = new Counter())
            {
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                Teardown(manager);

                Assert.AreEqual(1, c.Completed, "橋の出来事1回で達成するはず");
            }
        }

        [Test]
        public void EventOccurrence_Synergy_MatchesOnlyItsOwnKey()
        {
            var manager = MakeManager(MakeQuest(new QuestCondition("synergy:ForestRiver", 1)));

            using (var c = new Counter())
            {
                EventBus.Publish(new WorldEventOccurredEvent("synergy:ForestFlower"));
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                Assert.AreEqual(0, c.Progressed, "別の出来事では進まないはず");

                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Synergy("ForestRiver")));
                Teardown(manager);

                Assert.AreEqual(1, c.Completed);
            }
        }

        [Test]
        public void EventOccurrence_KeyComparison_IgnoresCaseAndPadding()
        {
            var manager = MakeManager(MakeQuest(new QuestCondition("  Synergy:ForestRiver ", 1)));

            using (var c = new Counter())
            {
                EventBus.Publish(new WorldEventOccurredEvent("synergy:forestriver"));
                Teardown(manager);

                Assert.AreEqual(1, c.Completed,
                    "手入力の綴りの大小差・前後の空白でクエストが進まなくならないはず");
            }
        }

        [Test]
        public void EventOccurrence_BlankKey_DoesNotStartOrSubscribe()
        {
            foreach (string key in new[] { null, "", "   " })
            {
                var manager = MakeManager(MakeQuest(new QuestCondition(key, 1)), runLifecycle: false);

                bool started = false;
                System.Action<QuestStartedEvent> onStarted = _ => started = true;
                EventBus.Subscribe(onStarted);
                using (var c = new Counter())
                {
                    InvokeLifecycle(manager, "OnEnable");
                    InvokeLifecycle(manager, "Start");
                    EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                    Teardown(manager);

                    Assert.IsFalse(started,        $"eventKey=\"{key}\" では開始しないはず");
                    Assert.AreEqual(0, c.Progressed, $"eventKey=\"{key}\" では購読もしないはず");
                }
                EventBus.Unsubscribe(onStarted);
            }
        }

        [Test]
        public void AfterCompletion_AdditionalOccurrences_PublishNothing()
        {
            var manager = MakeManager(MakeQuest(new QuestCondition(WorldEventKeys.Bridge, 1)));

            using (var c = new Counter())
            {
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                Teardown(manager);

                Assert.AreEqual(1, c.Progressed, "達成後は進捗を再発行しないはず");
                Assert.AreEqual(1, c.Completed,  "達成後は完了を再発行しないはず");
            }
        }

        // ── 条件種別ごとの購読の分離 ────────────────────────────────────

        [Test]
        public void ClusterSizeQuest_DoesNotSubscribeToPlacementOrOccurrence()
        {
            var manager = MakeManager(MakeQuest(
                new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.Forest, 5)));

            using (var c = new Counter())
            {
                EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Forest));
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                Teardown(manager);

                Assert.AreEqual(0, c.Progressed, "ClusterSizeクエストは配置・出来事イベントに反応しないはず");
            }
        }

        [Test]
        public void TilePlacedCountQuest_DoesNotSubscribeToClusterOrOccurrence()
        {
            var manager = MakeManager(MakeQuest(
                new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 2)));

            using (var c = new Counter())
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Field, 5));
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                Teardown(manager);

                Assert.AreEqual(0, c.Progressed, "TilePlacedCountクエストはクラスター・出来事イベントに反応しないはず");
            }
        }

        [Test]
        public void EventOccurrenceQuest_DoesNotSubscribeToClusterOrPlacement()
        {
            var manager = MakeManager(MakeQuest(new QuestCondition(WorldEventKeys.Bridge, 1)));

            using (var c = new Counter())
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 9));
                EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Field));
                Teardown(manager);

                Assert.AreEqual(0, c.Progressed, "EventOccurrenceクエストはクラスター・配置イベントに反応しないはず");
            }
        }

        [Test]
        public void RepeatedEnableDisable_DoesNotDoubleSubscribe_ForEveryKind()
        {
            var cases = new (QuestCondition condition, System.Action publish)[]
            {
                (new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.Forest, 5),
                    () => EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 3))),
                (new QuestCondition(QuestConditionKind.TilePlacedCount, TerrainClusterCategory.Field, 5),
                    () => EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Field))),
                (new QuestCondition(WorldEventKeys.Bridge, 5),
                    () => EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge))),
            };

            foreach (var (condition, publish) in cases)
            {
                var manager = MakeManager(MakeQuest(condition), runLifecycle: false);

                using (var c = new Counter())
                {
                    InvokeLifecycle(manager, "OnEnable");
                    InvokeLifecycle(manager, "OnDisable");
                    InvokeLifecycle(manager, "OnEnable");

                    publish();
                    Teardown(manager);

                    Assert.AreEqual(1, c.Progressed,
                        $"{condition.kind} でEnable/Disableを繰り返しても通知は1回だけのはず（二重購読の検出）");
                }
            }
        }

        // ── 実アセット・シーン設定の固定 ────────────────────────────────

        [Test]
        public void Asset_FieldPlaced2_HasExpectedCondition()
        {
            var quest = UnityEditor.AssetDatabase.LoadAssetAtPath<QuestDefinition>(QuestFolder + "Quest_FieldPlaced2.asset");

            Assert.IsNotNull(quest, "Quest_FieldPlaced2.assetが見つかりません");
            Assert.AreEqual(QuestConditionKind.TilePlacedCount, quest.condition.kind);
            Assert.AreEqual(TerrainClusterCategory.Field,       quest.condition.category);
            Assert.AreEqual(2,                                  quest.condition.targetCount);
            Assert.IsTrue(string.IsNullOrWhiteSpace(quest.rewardId), "報酬なしクエストのはず");
        }

        [Test]
        public void Asset_Bridge1_HasExpectedCondition()
        {
            var quest = UnityEditor.AssetDatabase.LoadAssetAtPath<QuestDefinition>(QuestFolder + "Quest_Bridge1.asset");

            Assert.IsNotNull(quest, "Quest_Bridge1.assetが見つかりません");
            Assert.AreEqual(QuestConditionKind.EventOccurrence, quest.condition.kind);
            Assert.AreEqual(WorldEventKeys.Bridge,              quest.condition.eventKey);
            Assert.AreEqual(1,                                  quest.condition.targetCount);
            Assert.IsTrue(string.IsNullOrWhiteSpace(quest.rewardId), "報酬なしクエストのはず");
        }

        [Test]
        public void Asset_ForestRiverSynergy1_HasExpectedCondition()
        {
            var quest = UnityEditor.AssetDatabase.LoadAssetAtPath<QuestDefinition>(QuestFolder + "Quest_ForestRiverSynergy1.asset");

            Assert.IsNotNull(quest, "Quest_ForestRiverSynergy1.assetが見つかりません");
            Assert.AreEqual(QuestConditionKind.EventOccurrence,      quest.condition.kind);
            Assert.AreEqual(WorldEventKeys.Synergy("ForestRiver"),   quest.condition.eventKey,
                "シーンのSynergyEvaluator（SynergyId=ForestRiver）が出すキーと一致していなければならない");
            Assert.AreEqual(1,                                       quest.condition.targetCount);
            Assert.IsTrue(string.IsNullOrWhiteSpace(quest.rewardId), "報酬なしクエストのはず");
        }

        /// <summary>
        /// Phase1_v002にWorldEventRelayがちょうど1個配置されていることを固定するための、
        /// 軽量なScene設定テスト。
        /// 0個なら翻訳が起きずクエストが進まず、2個なら出来事が二重に数えられるため、
        /// どちらもここで検出する。
        ///
        /// ★シーンを開くとエディタの状態（開いているシーン・未保存の変更）を壊してしまうため、
        ///   .unityのテキストからスクリプト参照の出現数を数える方式にしている。
        ///   Unityのシリアライズ形式（MonoBehaviourのm_Scriptがスクリプトのguidを持つこと）に
        ///   依存しているので、将来Unityの保存形式が変わったらこのテストを見直すこと。
        ///   コンポーネントの設定内容までは検証しない（WorldEventRelayはSerializeFieldを持たない）。
        /// </summary>
        [Test]
        public void Scene_Phase1v002_HasExactlyOneWorldEventRelay()
        {
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(
                "Assets/_Game/Scripts/Tiles/WorldEventRelay.cs");
            Assert.IsFalse(string.IsNullOrEmpty(guid), "WorldEventRelay.csが見つかりません");

            string sceneText = File.ReadAllText(
                Path.Combine(Application.dataPath, "..", ScenePath));

            int count = 0;
            int index = sceneText.IndexOf(guid, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = sceneText.IndexOf(guid, index + guid.Length, System.StringComparison.Ordinal);
            }

            Assert.AreEqual(1, count, $"{ScenePath} にWorldEventRelayはちょうど1つ配置されているはず");
        }
    }
}
