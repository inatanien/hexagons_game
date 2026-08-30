// 役割: QuestConditionによるデータ駆動化（Step 2）を固定する。
//       同じQuestManagerのコードのまま、条件データを差し替えるだけで
//       森5枚クエストと川3枚クエストの両方が動くことを検証する
//       （questIdごとの分岐を増やさない、という原則が守られているかの確認）。
//
//       ★進捗は「一度到達した最大値」を保持し後退しない。
//         クラスター規模は今置いたタイルが属するクラスターの枚数なので、
//         離れた場所へ置くと小さい値が届く。そこで数字が戻るとプレイヤーは
//         何も失っていないのに損をした感覚になるため（本作の「ストレスを与えない」方針）。
//
//       ★実アセット（Quest_ForestCluster5 / Quest_RiverCluster3）の中身も固定する。
//         構造移行でクエストの意味が変わっていないことを保証するため。
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
    public class QuestConditionTests
    {
        private const string QuestFolder = "Assets/_Game/ScriptableObjects/QuestData/";

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

        private QuestDefinition MakeClusterQuest(TerrainClusterCategory category, int targetCount)
            => MakeQuest(new QuestCondition(QuestConditionKind.ClusterSize, category, targetCount));

        /// <summary>OnEnable→Startの順で呼ぶ（実際のUnityライフサイクルと同じ）。</summary>
        private QuestManager MakeQuestManager(QuestDefinition quest)
        {
            var go      = new GameObject("TestQuestManager");
            _created.Add(go);
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            InvokeLifecycle(manager, "OnEnable");
            InvokeLifecycle(manager, "Start");
            return manager;
        }

        private static void Teardown(QuestManager manager) => InvokeLifecycle(manager, "OnDisable");

        // ── 1〜2. 同じコードで森5枚と川3枚が動く ────────────────────────

        [Test]
        public void ClusterSizeCondition_Forest5_CompletesOnForestProgress()
        {
            var manager = MakeQuestManager(MakeClusterQuest(TerrainClusterCategory.Forest, 5));

            int completed = 0;
            System.Action<QuestCompletedEvent> handler = _ => completed++;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }

            Assert.AreEqual(1, completed, "森5枚の条件は森の進捗で達成するはず");
        }

        [Test]
        public void ClusterSizeCondition_River3_CompletesOnRiverProgress()
        {
            // ★森と同じQuestManagerのコードのまま、条件データだけを差し替えている
            var manager = MakeQuestManager(MakeClusterQuest(TerrainClusterCategory.River, 3));

            int completed = 0;
            System.Action<QuestCompletedEvent> handler = _ => completed++;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.River, 3));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }

            Assert.AreEqual(1, completed, "川3枚の条件は川の進捗で達成するはず（コードはForestと同一）");
        }

        // ── 3〜4. カテゴリ違いは双方向で無視される ──────────────────────

        [Test]
        public void RiverQuest_IgnoresForestProgress()
        {
            var manager = MakeQuestManager(MakeClusterQuest(TerrainClusterCategory.River, 3));

            int progressed = 0;
            System.Action<QuestProgressChangedEvent> handler = _ => progressed++;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 3));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }

            Assert.AreEqual(0, progressed, "川クエストは森の進捗を無視するはず");
        }

        [Test]
        public void ForestQuest_IgnoresRiverProgress()
        {
            var manager = MakeQuestManager(MakeClusterQuest(TerrainClusterCategory.Forest, 5));

            int progressed = 0;
            System.Action<QuestProgressChangedEvent> handler = _ => progressed++;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.River, 5));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }

            Assert.AreEqual(0, progressed, "森クエストは川の進捗を無視するはず");
        }

        // ── 5〜6. 不正データでは開始しない ──────────────────────────────

        [Test]
        public void ConditionIsNull_DoesNotStartAndDoesNotSubscribe()
        {
            var quest   = MakeQuest(null);
            var go      = new GameObject("TestQuestManager");
            _created.Add(go);
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);

            bool started    = false;
            int  progressed = 0;
            System.Action<QuestStartedEvent>         onStarted  = _ => started = true;
            System.Action<QuestProgressChangedEvent> onProgress = _ => progressed++;
            EventBus.Subscribe(onStarted);
            EventBus.Subscribe(onProgress);
            try
            {
                InvokeLifecycle(manager, "OnEnable");
                InvokeLifecycle(manager, "Start");
                // OnEnable後に進捗イベントを流しても購読していないので反応しない
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
            }
            finally
            {
                EventBus.Unsubscribe(onStarted);
                EventBus.Unsubscribe(onProgress);
                InvokeLifecycle(manager, "OnDisable");
            }

            Assert.IsFalse(started,      "condition未設定のクエストはQuestStartedEventを発行しないはず");
            Assert.AreEqual(0, progressed, "condition未設定のクエストは進捗イベントを購読しないはず");
        }

        [Test]
        public void TargetCountZero_DoesNotStartAndDoesNotSubscribe()
        {
            var go      = new GameObject("TestQuestManager");
            _created.Add(go);
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", MakeClusterQuest(TerrainClusterCategory.Forest, 0));

            bool started    = false;
            int  progressed = 0;
            System.Action<QuestStartedEvent>         onStarted  = _ => started = true;
            System.Action<QuestProgressChangedEvent> onProgress = _ => progressed++;
            EventBus.Subscribe(onStarted);
            EventBus.Subscribe(onProgress);
            try
            {
                InvokeLifecycle(manager, "OnEnable");
                InvokeLifecycle(manager, "Start");
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
            }
            finally
            {
                EventBus.Unsubscribe(onStarted);
                EventBus.Unsubscribe(onProgress);
                InvokeLifecycle(manager, "OnDisable");
            }

            Assert.IsFalse(started,        "targetCount<=0のクエストはQuestStartedEventを発行しないはず");
            Assert.AreEqual(0, progressed, "targetCount<=0のクエストは進捗イベントを購読しないはず");
        }

        // ── 7〜8. 実アセットの内容を固定する ────────────────────────────

        [Test]
        public void Asset_ForestCluster5_KeepsItsMeaningAfterMigration()
        {
            var quest = UnityEditor.AssetDatabase.LoadAssetAtPath<QuestDefinition>(
                QuestFolder + "Quest_ForestCluster5.asset");

            Assert.IsNotNull(quest, "Quest_ForestCluster5.assetが見つかりません");
            Assert.IsNotNull(quest.condition, "移行後はconditionを持つはず");
            Assert.AreEqual(QuestConditionKind.ClusterSize,   quest.condition.kind);
            Assert.AreEqual(TerrainClusterCategory.Forest,    quest.condition.category);
            Assert.AreEqual(5,                                quest.condition.targetCount);
            Assert.AreEqual(5,                                quest.TargetCount, "UIが参照するTargetCountも同じ値のはず");
            Assert.AreEqual("forest_unlock_birds",            quest.rewardId, "報酬は移行前と同じでなければならない");
        }

        [Test]
        public void Asset_RiverCluster3_HasExpectedCondition()
        {
            var quest = UnityEditor.AssetDatabase.LoadAssetAtPath<QuestDefinition>(
                QuestFolder + "Quest_RiverCluster3.asset");

            Assert.IsNotNull(quest, "Quest_RiverCluster3.assetが見つかりません");
            Assert.AreEqual(QuestConditionKind.ClusterSize, quest.condition.kind);
            Assert.AreEqual(TerrainClusterCategory.River,   quest.condition.category);
            Assert.AreEqual(3,                              quest.condition.targetCount);
            Assert.IsTrue(string.IsNullOrWhiteSpace(quest.rewardId), "報酬なしクエストとして作成しているはず");
        }

        // ── 9. 進捗は後退しない（到達最大値を保持する） ─────────────────

        [Test]
        public void Progress_DoesNotGoBackward_WhenSmallerClusterArrives()
        {
            var manager = MakeQuestManager(MakeClusterQuest(TerrainClusterCategory.Forest, 5));

            var received = new List<int>();
            System.Action<QuestProgressChangedEvent> handler = e => received.Add(e.CurrentCount);
            EventBus.Subscribe(handler);
            try
            {
                // 森を4枚つなげたあと、離れた場所へ1枚置いた状況
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 4));
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 1));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }

            CollectionAssert.AreEqual(new[] { 4 }, received,
                "小さいクラスターが届いても進捗は後退せず、通知も発行されないはず");
        }

        [Test]
        public void Progress_StillAdvances_WhenLargerClusterArrivesAfterSmallerOne()
        {
            var manager = MakeQuestManager(MakeClusterQuest(TerrainClusterCategory.Forest, 5));

            var received  = new List<int>();
            int completed = 0;
            System.Action<QuestProgressChangedEvent> onProgress  = e => received.Add(e.CurrentCount);
            System.Action<QuestCompletedEvent>       onCompleted = _ => completed++;
            EventBus.Subscribe(onProgress);
            EventBus.Subscribe(onCompleted);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 4));
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 1));
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
            }
            finally
            {
                EventBus.Unsubscribe(onProgress);
                EventBus.Unsubscribe(onCompleted);
                Teardown(manager);
            }

            CollectionAssert.AreEqual(new[] { 4, 5 }, received, "後退はしないが、より大きい値では進むはず");
            Assert.AreEqual(1, completed);
        }

        // ── 追加. activeQuest未設定でも購読しない ───────────────────────

        [Test]
        public void ActiveQuestIsNull_DoesNotStartAndDoesNotSubscribe()
        {
            var go      = new GameObject("TestQuestManager");
            _created.Add(go);
            var manager = go.AddComponent<QuestManager>();
            // _activeQuestは未設定のまま（Inspectorへ割り当て忘れた状態）

            bool started    = false;
            int  progressed = 0;
            System.Action<QuestStartedEvent>         onStarted  = _ => started = true;
            System.Action<QuestProgressChangedEvent> onProgress = _ => progressed++;
            EventBus.Subscribe(onStarted);
            EventBus.Subscribe(onProgress);
            try
            {
                InvokeLifecycle(manager, "OnEnable");
                InvokeLifecycle(manager, "Start");
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
            }
            finally
            {
                EventBus.Unsubscribe(onStarted);
                EventBus.Unsubscribe(onProgress);
                InvokeLifecycle(manager, "OnDisable");
            }

            Assert.IsFalse(started,        "_activeQuest未設定ではQuestStartedEventを発行しないはず");
            Assert.AreEqual(0, progressed, "_activeQuest未設定では進捗イベントを購読しないはず");
        }

        // ── 追加. 達成後は小さい値が届いても何も再発行しない ─────────────

        [Test]
        public void AfterCompletion_SmallerClusterSize_PublishesNothing()
        {
            var manager = MakeQuestManager(MakeClusterQuest(TerrainClusterCategory.Forest, 5));

            int progressed = 0;
            int completed  = 0;
            System.Action<QuestProgressChangedEvent> onProgress  = _ => progressed++;
            System.Action<QuestCompletedEvent>       onCompleted = _ => completed++;
            EventBus.Subscribe(onProgress);
            EventBus.Subscribe(onCompleted);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                // 達成後に離れた場所へ1枚置いた状況
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 1));
            }
            finally
            {
                EventBus.Unsubscribe(onProgress);
                EventBus.Unsubscribe(onCompleted);
                Teardown(manager);
            }

            Assert.AreEqual(1, progressed, "達成後の小さい値ではQuestProgressChangedEventを再発行しないはず");
            Assert.AreEqual(1, completed,  "達成後の小さい値ではQuestCompletedEventを再発行しないはず");
        }

        // ── 10. Enable/Disableを繰り返しても二重購読しない ───────────────

        [Test]
        public void RepeatedEnableDisable_DoesNotDoubleSubscribe()
        {
            var go      = new GameObject("TestQuestManager");
            _created.Add(go);
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", MakeClusterQuest(TerrainClusterCategory.Forest, 5));

            int progressed = 0;
            System.Action<QuestProgressChangedEvent> handler = _ => progressed++;
            EventBus.Subscribe(handler);
            try
            {
                InvokeLifecycle(manager, "OnEnable");
                InvokeLifecycle(manager, "OnDisable");
                InvokeLifecycle(manager, "OnEnable");

                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 3));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(manager, "OnDisable");
            }

            Assert.AreEqual(1, progressed, "Enable/Disableを繰り返しても進捗通知は1回だけのはず（二重購読の検出）");
        }
    }
}
