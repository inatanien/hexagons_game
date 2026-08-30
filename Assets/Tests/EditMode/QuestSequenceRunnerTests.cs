// 役割: QuestSequenceRunner（クエストを順番に出す進行役、Stage B）の単体テスト。
//
//       ★RunnerはQuestManagerの内部状態を触らず、SetQuest()とQuestCompletedEventだけで進む。
//         ここでもRunnerの内部フィールドを書き換えず、公開された経路（イベント発行）だけで検証する。
//
//       ★待ち時間の挙動（遅延・二重予約の防止・OnDisableでの取り消し）はコルーチンが必要なため、
//         PlayModeのQuestSequenceRunnerPlayModeTestsで確認する。
//         こちらは待ち時間0（即切り替え）にして、順番・スキップ・完了判定を固定する。
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
    public class QuestSequenceRunnerTests
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

        private QuestDefinition MakeClusterQuest(string title, TerrainClusterCategory category, int targetCount)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(q);
            q.title     = title;
            q.condition = new QuestCondition(QuestConditionKind.ClusterSize, category, targetCount);
            return q;
        }

        private QuestDefinition MakeInvalidQuest(string title)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            _created.Add(q);
            q.title     = title;
            q.condition = null;   // QuestManagerが弾く
            return q;
        }

        private QuestSequenceDefinition MakeSequence(params QuestDefinition[] quests)
        {
            var s = ScriptableObject.CreateInstance<QuestSequenceDefinition>();
            _created.Add(s);
            s.name   = "TestSequence";
            s.quests = quests;
            return s;
        }

        /// <summary>QuestManagerとRunnerを同じGameObjectへ載せ、実際の起動順（OnEnable→Start）で動かす。</summary>
        private (QuestManager manager, QuestSequenceRunner runner) MakeRig(QuestSequenceDefinition sequence)
        {
            var go = new GameObject("TestQuestRig");
            _created.Add(go);

            var manager = go.AddComponent<QuestManager>();
            var runner  = go.AddComponent<QuestSequenceRunner>();
            SetPrivateField(runner, "_sequence", sequence);
            SetPrivateField(runner, "_questManager", manager);
            // 待ち時間はPlayModeで検証する。ここでは即切り替えにして順番だけを見る
            SetPrivateField(runner, "_nextQuestDelay", 0f);

            InvokeLifecycle(manager, "OnEnable");
            InvokeLifecycle(runner,  "OnEnable");
            InvokeLifecycle(manager, "Start");
            InvokeLifecycle(runner,  "Start");
            return (manager, runner);
        }

        private static void Teardown(QuestManager manager, QuestSequenceRunner runner)
        {
            InvokeLifecycle(runner,  "OnDisable");
            InvokeLifecycle(manager, "OnDisable");
        }

        private sealed class Recorder : System.IDisposable
        {
            public readonly List<QuestDefinition>         Started           = new();
            public readonly List<QuestSequenceDefinition> SequenceCompleted = new();

            private readonly System.Action<QuestStartedEvent>           _onStarted;
            private readonly System.Action<QuestSequenceCompletedEvent> _onSequence;

            public Recorder()
            {
                _onStarted  = e => Started.Add(e.Quest);
                _onSequence = e => SequenceCompleted.Add(e.Sequence);
                EventBus.Subscribe(_onStarted);
                EventBus.Subscribe(_onSequence);
            }

            public void Dispose()
            {
                EventBus.Unsubscribe(_onStarted);
                EventBus.Unsubscribe(_onSequence);
            }
        }

        /// <summary>森クエストを達成させる（ClusterSize条件を満たすイベントを流す）。</summary>
        private static void CompleteForest(int size) =>
            EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, size));

        private static void CompleteRiver(int size) =>
            EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.River, size));

        // ── 1. 1本目を開始する ──────────────────────────────────────────

        [Test]
        public void Start_BeginsFirstQuest()
        {
            var first  = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var second = MakeClusterQuest("2本目", TerrainClusterCategory.River, 3);

            using (var r = new Recorder())
            {
                var rig = MakeRig(MakeSequence(first, second));
                Teardown(rig.manager, rig.runner);

                CollectionAssert.AreEqual(new[] { first }, r.Started, "Startでは1本目だけが開始されるはず");
            }
        }

        // ── 2. 達成すると次へ進む ───────────────────────────────────────

        [Test]
        public void CompletingQuest_StartsNextQuest()
        {
            var first  = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var second = MakeClusterQuest("2本目", TerrainClusterCategory.River, 3);

            using (var r = new Recorder())
            {
                var rig = MakeRig(MakeSequence(first, second));

                CompleteForest(5);
                Teardown(rig.manager, rig.runner);

                CollectionAssert.AreEqual(new[] { first, second }, r.Started, "達成したら次のクエストが始まるはず");
                Assert.AreEqual(0, r.SequenceCompleted.Count, "まだ途中なのでSequence完了は出ないはず");
            }
        }

        // ── 3. 最後まで達成するとSequence完了が1回だけ出る ──────────────

        [Test]
        public void CompletingLastQuest_PublishesSequenceCompletedOnce()
        {
            var first  = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var second = MakeClusterQuest("2本目", TerrainClusterCategory.River, 3);
            var seq    = MakeSequence(first, second);

            using (var r = new Recorder())
            {
                var rig = MakeRig(seq);

                CompleteForest(5);
                CompleteRiver(3);
                Teardown(rig.manager, rig.runner);

                CollectionAssert.AreEqual(new[] { seq }, r.SequenceCompleted, "Sequence完了は1回だけのはず");
            }
        }

        // ── 4. 完走後に達成イベントが来ても再進行しない ─────────────────

        [Test]
        public void AfterSequenceFinished_FurtherCompletionsDoNothing()
        {
            var only = MakeClusterQuest("唯一", TerrainClusterCategory.Forest, 5);
            var seq  = MakeSequence(only);

            using (var r = new Recorder())
            {
                var rig = MakeRig(seq);

                CompleteForest(5);
                // 完走後にもう一度同じ達成イベントを流す
                EventBus.Publish(new QuestCompletedEvent(only));
                EventBus.Publish(new QuestCompletedEvent(only));
                Teardown(rig.manager, rig.runner);

                CollectionAssert.AreEqual(new[] { only }, r.Started,        "完走後に新しいクエストを始めてはいけない");
                CollectionAssert.AreEqual(new[] { seq },  r.SequenceCompleted, "Sequence完了を再発行してはいけない");
            }
        }

        // ── 5〜6. 空欄・無効なクエストは飛ばす ──────────────────────────

        [Test]
        public void NullEntry_IsSkipped()
        {
            var valid = MakeClusterQuest("有効", TerrainClusterCategory.Forest, 5);

            using (var r = new Recorder())
            {
                var rig = MakeRig(MakeSequence(null, valid));
                Teardown(rig.manager, rig.runner);

                CollectionAssert.AreEqual(new[] { valid }, r.Started, "空欄は飛ばして次を開始するはず");
            }
        }

        [Test]
        public void InvalidQuest_IsSkipped_AndCurrentQuestKeepsWorking()
        {
            var first   = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var invalid = MakeInvalidQuest("condition未設定");
            var third   = MakeClusterQuest("3本目", TerrainClusterCategory.River, 3);

            using (var r = new Recorder())
            {
                var rig = MakeRig(MakeSequence(first, invalid, third));

                CompleteForest(5);
                Teardown(rig.manager, rig.runner);

                CollectionAssert.AreEqual(new[] { first, third }, r.Started,
                    "無効なクエストは飛ばして次の有効なクエストを開始するはず");
            }
        }

        // ── 7. 全件無効ならSequence完了を出さない ───────────────────────

        [Test]
        public void AllEntriesInvalid_DoesNotPublishSequenceCompleted()
        {
            using (var r = new Recorder())
            {
                var rig = MakeRig(MakeSequence(null, MakeInvalidQuest("不正1"), MakeInvalidQuest("不正2")));
                Teardown(rig.manager, rig.runner);

                Assert.AreEqual(0, r.Started.Count, "1本も開始できないはず");
                Assert.AreEqual(0, r.SequenceCompleted.Count,
                    "何も達成していないのにSequence完了を出してはいけない");
            }
        }

        // ── 8. Sequence未設定・空Sequenceでは何もしない ─────────────────

        [Test]
        public void NoSequenceOrEmptySequence_DoesNothing()
        {
            using (var r = new Recorder())
            {
                var noSequence = MakeRig(null);
                Teardown(noSequence.manager, noSequence.runner);

                var empty = MakeRig(MakeSequence());
                Teardown(empty.manager, empty.runner);

                Assert.AreEqual(0, r.Started.Count,           "クエストを開始してはいけない");
                Assert.AreEqual(0, r.SequenceCompleted.Count, "Sequence完了を出してはいけない");
            }
        }

        // ── 9. 現在のクエスト以外の達成では進まない ─────────────────────

        [Test]
        public void CompletionOfOtherQuest_DoesNotAdvance()
        {
            var first    = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var second   = MakeClusterQuest("2本目", TerrainClusterCategory.River, 3);
            var stranger = MakeClusterQuest("無関係", TerrainClusterCategory.Field, 2);

            using (var r = new Recorder())
            {
                var rig = MakeRig(MakeSequence(first, second));

                // 別のQuestManagerが出したような、現在の出題とは無関係な達成通知
                EventBus.Publish(new QuestCompletedEvent(stranger));
                Teardown(rig.manager, rig.runner);

                CollectionAssert.AreEqual(new[] { first }, r.Started,
                    "現在出題中でないクエストの達成では次へ進まないはず");
            }
        }

        // ── 10. OnDisable後は進行しない（購読対称性） ───────────────────

        [Test]
        public void AfterOnDisable_DoesNotAdvance()
        {
            var first  = MakeClusterQuest("1本目", TerrainClusterCategory.Forest, 5);
            var second = MakeClusterQuest("2本目", TerrainClusterCategory.River, 3);

            using (var r = new Recorder())
            {
                var rig = MakeRig(MakeSequence(first, second));
                InvokeLifecycle(rig.runner, "OnDisable");

                EventBus.Publish(new QuestCompletedEvent(first));
                InvokeLifecycle(rig.manager, "OnDisable");

                CollectionAssert.AreEqual(new[] { first }, r.Started,
                    "OnDisable後は達成通知を受け取らないはず");
            }
        }

        // ── 11. RunnerがQuestManagerの内部状態を触っていないこと ────────

        [Test]
        public void Runner_DoesNotTouchQuestManagerInternals()
        {
            var type  = typeof(QuestSequenceRunner);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                        BindingFlags.Static | BindingFlags.DeclaredOnly;

            // QuestManagerに対して使ってよいのは公開APIのSetQuestだけ。
            // リフレクション経由で内部状態をいじっていないことを構造的に確かめる
            foreach (var field in type.GetFields(flags))
            {
                Assert.AreNotEqual(typeof(FieldInfo), field.FieldType,
                    $"{field.Name} がリフレクションを保持しています");
            }

            foreach (var method in type.GetMethods(flags))
            {
                var body = method.GetMethodBody();
                if (body == null) continue;
                foreach (var local in body.LocalVariables)
                {
                    Assert.AreNotEqual(typeof(FieldInfo), local.LocalType,
                        $"{method.Name} がリフレクションで内部状態へ触れています");
                }
            }
        }
    }
}
