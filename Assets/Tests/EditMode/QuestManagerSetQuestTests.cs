// 役割: QuestManager.SetQuest（外部からクエストを差し替えるAPI、Stage A）を固定する。
//       QuestSequenceRunnerがこのAPIだけでクエストを順に出せるようにするための前提を守る。
//
//       ★無効なクエストを渡しても、現在のクエストを壊さない。
//         Sequenceに設定ミスが1つあるだけで進行中のクエストまで止まっては困るため、
//         「先に候補を検証し、成功するときだけ切り替える」ことをここで固定する。
//
//       ★1つのクエストにつきQuestStartedEventは1回だけ。
//         SetQuestとStartのどちらが先に走っても変わらないことを両方向で確認する
//         （コンポーネントのStart実行順に依存させないため）。
//
//       注意: EditModeではAddComponent直後にOnEnable/OnDisableが自動発火しないため、
//       リフレクションで明示的に呼び出す（既存テストと同じ手法）。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Quest;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class QuestManagerSetQuestTests
    {
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

        private QuestDefinition MakeQuest(string title, QuestCondition condition)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(q);
            q.title     = title;
            q.condition = condition;
            return q;
        }

        private QuestDefinition MakeClusterQuest(TerrainClusterCategory category, int targetCount)
            => MakeQuest("クラスタークエスト",
                new QuestCondition(QuestConditionKind.ClusterSize, category, targetCount));

        private QuestDefinition MakeOccurrenceQuest(string eventKey, int targetCount)
            => MakeQuest("出来事クエスト", new QuestCondition(eventKey, targetCount));

        /// <summary>_activeQuest未設定のQuestManager（Sequence運用の起動時と同じ状態）。</summary>
        private QuestManager MakeIdleManager()
        {
            var go = new GameObject("TestQuestManager");
            _created.Add(go);
            var manager = go.AddComponent<QuestManager>();
            InvokeLifecycle(manager, "OnEnable");
            InvokeLifecycle(manager, "Start");
            return manager;
        }

        /// <summary>Inspectorへクエストを割り当てた単体運用のQuestManager。</summary>
        private QuestManager MakeAssignedManager(QuestDefinition quest, bool runStart = true)
        {
            var go = new GameObject("TestQuestManager");
            _created.Add(go);
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            InvokeLifecycle(manager, "OnEnable");
            if (runStart) InvokeLifecycle(manager, "Start");
            return manager;
        }

        private static void Teardown(QuestManager manager) => InvokeLifecycle(manager, "OnDisable");

        private sealed class Recorder : System.IDisposable
        {
            public readonly List<QuestDefinition> Started   = new();
            public readonly List<int>             Progress  = new();
            public readonly List<QuestDefinition> Completed = new();

            private readonly System.Action<QuestStartedEvent>         _onStarted;
            private readonly System.Action<QuestProgressChangedEvent> _onProgress;
            private readonly System.Action<QuestCompletedEvent>       _onCompleted;

            public Recorder()
            {
                _onStarted   = e => Started.Add(e.Quest);
                _onProgress  = e => Progress.Add(e.CurrentCount);
                _onCompleted = e => Completed.Add(e.Quest);
                EventBus.Subscribe(_onStarted);
                EventBus.Subscribe(_onProgress);
                EventBus.Subscribe(_onCompleted);
            }

            public void Dispose()
            {
                EventBus.Unsubscribe(_onStarted);
                EventBus.Unsubscribe(_onProgress);
                EventBus.Unsubscribe(_onCompleted);
            }
        }

        // ── 1. 基本: 差し替えて開始できる ───────────────────────────────

        [Test]
        public void SetQuest_StartsQuestAndPublishesStartedOnce()
        {
            var manager = MakeIdleManager();
            var quest   = MakeClusterQuest(TerrainClusterCategory.Forest, 5);

            using (var r = new Recorder())
            {
                Assert.IsTrue(manager.SetQuest(quest), "有効なクエストならtrueを返すはず");

                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { quest }, r.Started, "QuestStartedEventは1回だけのはず");
                CollectionAssert.AreEqual(new[] { 5 },     r.Progress);
                CollectionAssert.AreEqual(new[] { quest }, r.Completed);
            }
        }

        // ── 2. 条件種別が変わっても購読が張り替わる ─────────────────────

        [Test]
        public void SetQuest_SwitchesSubscriptionToNewKind()
        {
            var cluster    = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var occurrence = MakeOccurrenceQuest(WorldEventKeys.Bridge, 1);
            var manager    = MakeAssignedManager(cluster);

            using (var r = new Recorder())
            {
                Assert.IsTrue(manager.SetQuest(occurrence));

                // 旧クエストのイベントはもう届かない
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                Assert.AreEqual(0, r.Completed.Count, "差し替え後に旧条件のイベントで進んではいけない");

                // 新クエストのイベントで進む
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { occurrence }, r.Completed);
            }
        }

        // ── 3. 進捗と完了状態がリセットされる ───────────────────────────

        [Test]
        public void SetQuest_ResetsProgressAndCompletion()
        {
            var first   = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var second  = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeAssignedManager(first);

            using (var r = new Recorder())
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                Assert.AreEqual(1, r.Completed.Count, "1本目が達成しているはず");

                Assert.IsTrue(manager.SetQuest(second));
                r.Progress.Clear();

                // 達成済みフラグが残っていると2本目が一切進まない
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 3));
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { 3 }, r.Progress, "2本目は0からやり直すはず");
            }
        }

        // ── 4〜5. 無効な候補で現在のクエストを壊さない ──────────────────

        [Test]
        public void SetQuest_WithNull_ReturnsFalseAndKeepsCurrentQuest()
        {
            var current = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeAssignedManager(current);

            using (var r = new Recorder())
            {
                Assert.IsFalse(manager.SetQuest(null), "nullではfalseを返すはず");

                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { current }, r.Completed,
                    "nullを渡しても現在のクエストは動き続けるはず");
                // Recorderは既にStart済みの状態から記録を始めているので、
                // ここで数えるのは「SetQuest(null)以降に増えた分」
                Assert.AreEqual(0, r.Started.Count, "nullを渡してQuestStartedEventが発行されてはいけない");
            }
        }

        [Test]
        public void SetQuest_WithInvalidQuest_ReturnsFalseAndKeepsCurrentQuest()
        {
            var current = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeAssignedManager(current);

            var invalids = new[]
            {
                MakeQuest("condition未設定", null),
                MakeQuest("targetCount不正",
                    new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.Forest, 0)),
                MakeQuest("eventKey未設定", new QuestCondition("   ", 1)),
            };

            using (var r = new Recorder())
            {
                foreach (var invalid in invalids)
                    Assert.IsFalse(manager.SetQuest(invalid), $"{invalid.title} ではfalseを返すはず");

                Assert.AreEqual(0, r.Started.Count, "無効な候補でQuestStartedEventを出してはいけない");

                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { current }, r.Completed,
                    "無効な候補を渡しても現在のクエストは壊れないはず");
            }
        }

        // ── 6〜7. StartとSetQuestの実行順に依存しない ───────────────────

        [Test]
        public void SetQuestBeforeStart_DoesNotPublishStartedTwice()
        {
            var quest = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            // Inspector割り当てあり・Startはまだ呼ばない（Runnerが先に走ったケース）
            var manager = MakeAssignedManager(quest, runStart: false);

            using (var r = new Recorder())
            {
                Assert.IsTrue(manager.SetQuest(quest));
                InvokeLifecycle(manager, "Start");
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { quest }, r.Started,
                    "SetQuestが先でも、後から呼ばれるStartが同じクエストを再開始してはいけない");
            }
        }

        [Test]
        public void SetQuestAfterStart_WithSameQuest_DoesNotPublishStartedTwice()
        {
            var quest   = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeAssignedManager(quest);   // Startまで実行済み

            using (var r = new Recorder())
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 3));

                Assert.IsTrue(manager.SetQuest(quest), "同じクエストの再設定は成功扱いのはず");

                Assert.AreEqual(0, r.Started.Count,
                    "Startが先でも、同じクエストのSetQuestで再開始してはいけない");
                CollectionAssert.AreEqual(new[] { 3 }, r.Progress,
                    "同じクエストの再設定で進捗が巻き戻ってはいけない");

                Teardown(manager);
            }
        }

        // ── 7b. 別のQuestへは正しく切り替わる（Sequence運用の本筋） ─────

        [Test]
        public void SetQuest_WithDifferentQuest_PublishesStartedForEach()
        {
            var manager = MakeIdleManager();
            var a = MakeQuest("1本目", new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.Forest, 5));
            var b = MakeQuest("2本目", new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.River, 3));

            using (var r = new Recorder())
            {
                Assert.IsTrue(manager.SetQuest(a));
                Assert.IsTrue(manager.SetQuest(b));
                InvokeLifecycle(manager, "Start");   // 後からStartが走っても増えない
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { a, b }, r.Started,
                    "別のQuestへ切り替えたときは Started(A) → Started(B) の2回だけ発行されるはず");
            }
        }

        // ── 8. 未設定のまま開始しても購読しない（Sequence運用の起動時） ──

        [Test]
        public void IdleManager_DoesNotSubscribeUntilSetQuest()
        {
            var manager = MakeIdleManager();

            using (var r = new Recorder())
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 9));
                EventBus.Publish(new TileCategoryPlacedEvent(TerrainClusterCategory.Field));
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));

                Assert.AreEqual(0, r.Started.Count,  "未設定ではQuestStartedEventを出さないはず");
                Assert.AreEqual(0, r.Progress.Count, "未設定では何も購読していないはず");

                Teardown(manager);
            }
        }

        // ── 9. SetQuest後のOnDisableで新しい種別の購読が残らない ────────

        [Test]
        public void OnDisableAfterSetQuest_UnsubscribesNewKind()
        {
            var cluster    = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var occurrence = MakeOccurrenceQuest(WorldEventKeys.Bridge, 1);
            var manager    = MakeAssignedManager(cluster);

            Assert.IsTrue(manager.SetQuest(occurrence));
            Teardown(manager);

            using (var r = new Recorder())
            {
                EventBus.Publish(new WorldEventOccurredEvent(WorldEventKeys.Bridge));
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));

                Assert.AreEqual(0, r.Progress.Count,  "OnDisable後は新旧どちらの種別も購読していないはず");
                Assert.AreEqual(0, r.Completed.Count);
            }
        }

        // ── 10. 差し替えを繰り返しても二重購読しない ────────────────────

        [Test]
        public void RepeatedSetQuest_DoesNotDoubleSubscribe()
        {
            var manager = MakeIdleManager();
            var a = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var b = MakeClusterQuest(TerrainClusterCategory.Forest, 5);
            var c = MakeClusterQuest(TerrainClusterCategory.Forest, 5);

            using (var r = new Recorder())
            {
                manager.SetQuest(a);
                manager.SetQuest(b);
                manager.SetQuest(c);

                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 3));
                Teardown(manager);

                CollectionAssert.AreEqual(new[] { 3 }, r.Progress,
                    "差し替えを繰り返しても購読は常に1つだけのはず");
            }
        }
    }
}
