// 役割: TerrainClusterProgressRelay（Tiles→Core変換）とQuestManager（Stage 1: 森クエスト単体）の
//       単体テスト。QuestManagerはMaterial/Rendererを扱わないためEditModeで安全に検証できる
//       （EffectCategoryClusterTests.cs等と同じ判断）。
//       EditModeではAddComponent直後にOnEnable/OnDisableが自動発火しないため、
//       リフレクションで明示的に呼び出す（既存テストと同じ手法）。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using ElfVillage.Quest;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class QuestManagerTests
    {
        // ── ライフサイクル呼び出しヘルパー ────────────────────────────
        // メソッドが見つからない場合はAssert.Failで明確に失敗させる（対象クラスの
        // リファクタリング等でメソッド名が変わった際に原因がすぐ分かるようにするため）。

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

        private static object GetPrivateField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}に{fieldName}フィールドが見つかりません");
            return field.GetValue(target);
        }

        // ── テスト用オブジェクト生成ヘルパー ──────────────────────────

        private static QuestDefinition MakeQuest(TerrainClusterCategory category, int targetCount)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.title          = "テストクエスト";
            q.targetCategory = category;
            q.targetCount    = targetCount;
            return q;
        }

        // OnEnable→Startの順で呼ぶ（実際のUnityライフサイクルと同じ）。QuestStartedEventの
        // タイミング自体を検証するテストは、これを使わず個別にOnEnable/Startを呼び分ける。
        private static QuestManager MakeQuestManager(QuestDefinition quest)
        {
            var go      = new GameObject("TestQuestManager");
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            InvokeLifecycle(manager, "OnEnable");
            InvokeLifecycle(manager, "Start");
            return manager;
        }

        private static void Teardown(QuestManager manager)
        {
            InvokeLifecycle(manager, "OnDisable");
            Object.DestroyImmediate(manager.gameObject);
        }

        private static TerrainClusterProgressRelay MakeRelay()
        {
            var go    = new GameObject("TestRelay");
            var relay = go.AddComponent<TerrainClusterProgressRelay>();
            InvokeLifecycle(relay, "OnEnable");
            return relay;
        }

        private static void Teardown(TerrainClusterProgressRelay relay)
        {
            InvokeLifecycle(relay, "OnDisable");
            Object.DestroyImmediate(relay.gameObject);
        }

        private static void PublishForestGrowth(int clusterSize)
        {
            var tileType = ScriptableObject.CreateInstance<TileType>();
            var metrics  = new ForestGrowthMetrics(largestClusterSize: clusterSize, totalForestTiles: clusterSize);
            EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                terrainType:   tileType,
                anchor:        HexCoord.Zero,
                affectedTiles: new List<HexTile>(),
                metrics:       metrics
            ));
        }

        // ── 1. RelayがLargestClusterSizeを正しくCoreイベントへ変換する ──────

        [Test]
        public void Relay_ConvertsForestGrowthMetrics_ToTerrainClusterProgressEvent()
        {
            var relay = MakeRelay();
            TerrainClusterProgressEvent received = null;
            System.Action<TerrainClusterProgressEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                PublishForestGrowth(4);

                Assert.IsNotNull(received);
                Assert.AreEqual(TerrainClusterCategory.Forest, received.Category);
                Assert.AreEqual(4, received.ClusterSize);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(relay);
            }
        }

        // ── 2. QuestManagerが異なるカテゴリのイベントを無視する ────────────

        [Test]
        public void QuestManager_IgnoresProgressEvent_WithDifferentCategory()
        {
            var quest   = MakeQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeQuestManager(quest);

            QuestProgressChangedEvent received = null;
            System.Action<QuestProgressChangedEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Field, 3));

                Assert.IsNull(received, "対象外カテゴリ(Field)のイベントでは進捗が更新されないはず");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }
        }

        // ── 3. 森クラスターの進捗が正しく更新される ────────────────────

        [Test]
        public void QuestManager_UpdatesProgress_OnMatchingCategoryEvent()
        {
            var quest   = MakeQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeQuestManager(quest);

            QuestProgressChangedEvent received = null;
            System.Action<QuestProgressChangedEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 3));

                Assert.IsNotNull(received);
                Assert.AreEqual(3, received.CurrentCount);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }
        }

        // ── 4. targetCountを超えてもクランプされる ─────────────────────

        [Test]
        public void QuestManager_ClampsProgress_ToTargetCount()
        {
            var quest   = MakeQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeQuestManager(quest);

            QuestProgressChangedEvent received = null;
            System.Action<QuestProgressChangedEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 9));

                Assert.IsNotNull(received);
                Assert.AreEqual(5, received.CurrentCount, "targetCount(5)を超えた値は5にクランプされるはず");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }
        }

        // ── 5. targetCount到達時にQuestCompletedEventが1回だけ発行される ────

        [Test]
        public void QuestManager_PublishesQuestCompletedEvent_OnceOnReachingTarget()
        {
            var quest   = MakeQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeQuestManager(quest);

            int completedCount = 0;
            System.Action<QuestCompletedEvent> handler = e => completedCount++;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));

                Assert.AreEqual(1, completedCount);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }
        }

        // ── 6. 達成後に追加イベントを受けても重複達成しない ─────────────────

        [Test]
        public void QuestManager_DoesNotDuplicateCompletion_OnAdditionalEvents()
        {
            var quest   = MakeQuest(TerrainClusterCategory.Forest, 5);
            var manager = MakeQuestManager(quest);

            int completedCount = 0;
            System.Action<QuestCompletedEvent> handler = e => completedCount++;
            EventBus.Subscribe(handler);
            try
            {
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 6));
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));

                Assert.AreEqual(1, completedCount, "達成後に追加の進捗イベントが来てもQuestCompletedEventは重複発行されないはず");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                Teardown(manager);
            }
        }

        // ── 7. Quest開始時にQuestStartedEventが発行される（OnEnableではなくStartで） ──

        [Test]
        public void QuestManager_DoesNotPublishQuestStartedEvent_DuringOnEnable()
        {
            var quest = MakeQuest(TerrainClusterCategory.Forest, 5);

            bool received = false;
            System.Action<QuestStartedEvent> handler = e => received = true;
            EventBus.Subscribe(handler);

            var go      = new GameObject("TestQuestManagerOnEnableOnly");
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            try
            {
                InvokeLifecycle(manager, "OnEnable");

                Assert.IsFalse(received, "OnEnable完了時点ではQuestStartedEventはまだ発行されていないはず");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(manager, "OnDisable");
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void QuestManager_PublishesQuestStartedEvent_OnStart()
        {
            var quest = MakeQuest(TerrainClusterCategory.Forest, 5);

            QuestStartedEvent received = null;
            System.Action<QuestStartedEvent> handler = e => received = e;
            EventBus.Subscribe(handler);

            var go      = new GameObject("TestQuestManagerOnStart");
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            try
            {
                InvokeLifecycle(manager, "OnEnable");
                Assert.IsNull(received, "前提: OnEnable直後はまだ発行されていない");

                InvokeLifecycle(manager, "Start");

                Assert.IsNotNull(received, "Startを呼んだ時点でQuestStartedEventが発行されるはず");
                Assert.AreEqual(quest, received.Quest);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(manager, "OnDisable");
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void QuestManager_DoesNotDuplicateQuestStartedEvent_WhenStartCalledTwice()
        {
            // 通常のUnityライフサイクルではStartは1回しか呼ばれないが、_startedガードの
            // 防御を明示的に確認する（重複呼び出しがあっても2回発行されないこと）。
            var quest = MakeQuest(TerrainClusterCategory.Forest, 5);

            int startedCount = 0;
            System.Action<QuestStartedEvent> handler = e => startedCount++;
            EventBus.Subscribe(handler);

            var go      = new GameObject("TestQuestManagerDoubleStart");
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            try
            {
                InvokeLifecycle(manager, "OnEnable");
                InvokeLifecycle(manager, "Start");
                InvokeLifecycle(manager, "Start"); // 2回目（本来は起こらないが防御を確認）

                Assert.AreEqual(1, startedCount, "Startが複数回呼ばれてもQuestStartedEventは1回だけのはず");
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(manager, "OnDisable");
                Object.DestroyImmediate(go);
            }
        }

        // ── 8. 無効なtargetCount（0以下）の扱い ───────────────────────────
        // 仕様: targetCountが0以下のクエストはQuestManagerが開始しない
        //       （購読もQuestStartedEventの発行も行わない。警告ログのみ出す）。

        [Test]
        public void QuestManager_DoesNotStart_WhenTargetCountIsZeroOrLess()
        {
            var quest = MakeQuest(TerrainClusterCategory.Forest, 0);

            bool startedReceived = false;
            System.Action<QuestStartedEvent> handler = e => startedReceived = true;
            EventBus.Subscribe(handler);

            var go      = new GameObject("TestQuestManagerInvalid");
            var manager = go.AddComponent<QuestManager>();
            SetPrivateField(manager, "_activeQuest", quest);
            try
            {
                InvokeLifecycle(manager, "OnEnable");
                InvokeLifecycle(manager, "Start");

                Assert.IsFalse(startedReceived, "targetCount<=0のクエストはOnEnable・Startを通しても一切QuestStartedEventを発行しないはず");

                // 購読していないはずなので、進捗イベントを送っても何も起きない（例外も出ない）ことを確認
                Assert.DoesNotThrow(() =>
                    EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 1)));
            }
            finally
            {
                EventBus.Unsubscribe(handler);
                InvokeLifecycle(manager, "OnDisable"); // 未購読の場合は内部でno-opになる想定
                Object.DestroyImmediate(go);
            }
        }
    }
}
