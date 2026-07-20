// 役割: Stage 1〜6で構築したQuest報酬フロー全体（TerrainClusterProgressEvent →
//       QuestManager → QuestCompletedEvent → QuestRewardSystem → RewardUnlockedEvent →
//       BirdRewardSpawner → RewardBird生成）を、途中のイベントを省略せず通しで検証する
//       エンドツーエンドテスト。
//
//       CoreのTerrainClusterProgressEventを起点にする（TerrainGrowthEvent<ForestGrowthMetrics>
//       から始めるとHexTile/TileType等の本番オブジェクト構築が必要になり脆くなるため。
//       これによりTerrainClusterProgressRelayはテスト対象外になるが、今回検証したい
//       Quest→Reward→Birdの本体フローには含まれない）。
//
//       QuestManager/QuestRewardSystem/BirdRewardSpawnerはいずれもOnEnableだけで
//       今回のフローに必要な購読が完結する（QuestStartedEventの発行だけがStartにあるが、
//       今回のフロー対象外）。そのため既存テストと同じ「リフレクションでOnEnable/Startを
//       明示的に呼び出す」手法をEditModeでそのまま使う。本番コードへテスト専用publicメソッドは
//       追加していない。
//
//       EventBusにReset/Clear APIは存在せず、追加もしない。各コンポーネントのOnDisableを
//       必ずteardownで呼びUnsubscribeさせることで、既存テストと同じ方法でEventBusを
//       クリーンに保つ。

using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Quest;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class QuestRewardEndToEndTests
    {
        // ── ライフサイクル呼び出しヘルパー（既存テストと同じ手法） ──────────

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

        // ── EventBus._handlers 反射ヘルパー（重複購読・後片付けの検証に使用） ──

        private static int CountSubscribersForTarget<T>(object target)
        {
            var handlersField = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            var handlers = (System.Collections.IDictionary)handlersField.GetValue(null);
            if (!handlers.Contains(typeof(T))) return 0;

            var del = (System.Delegate)handlers[typeof(T)];
            int count = 0;
            foreach (var d in del.GetInvocationList())
                if (System.Object.ReferenceEquals(d.Target, target))
                    count++;
            return count;
        }

        // ── テスト用QuestDefinition（本番アセットへは依存しない） ────────────

        private static QuestDefinition MakeQuest()
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.title          = "E2E Test Quest";
            q.description    = "エンドツーエンドテスト用";
            q.targetCategory = TerrainClusterCategory.Forest;
            q.targetCount    = 5;
            q.rewardId       = "forest_unlock_birds";
            return q;
        }

        // ── テスト用ハーネス（QuestManager / QuestRewardSystem / BirdRewardSpawnerを
        //    それぞれ別GameObjectに生成する。本番のSystems/Quest・Systems/Crittersの
        //    分離構成を模しており、GameObjectをまたいだEventBus経由の接続も暗に検証する） ──

        private readonly struct Harness
        {
            public readonly QuestDefinition    Quest;
            public readonly GameObject         QuestManagerGo;
            public readonly QuestManager       QuestManager;
            public readonly GameObject         RewardSystemGo;
            public readonly QuestRewardSystem  RewardSystem;
            public readonly GameObject         BirdSpawnerGo;
            public readonly BirdRewardSpawner  BirdSpawner;

            public Harness(QuestDefinition quest, GameObject qmGo, QuestManager qm,
                            GameObject rsGo, QuestRewardSystem rs,
                            GameObject bsGo, BirdRewardSpawner bs)
            {
                Quest = quest; QuestManagerGo = qmGo; QuestManager = qm;
                RewardSystemGo = rsGo; RewardSystem = rs;
                BirdSpawnerGo = bsGo; BirdSpawner = bs;
            }
        }

        private static Harness MakeHarness()
        {
            var quest = MakeQuest();

            var qmGo = new GameObject("Test_QuestManager");
            var qm = qmGo.AddComponent<QuestManager>();
            SetPrivateField(qm, "_activeQuest", quest);
            InvokeLifecycle(qm, "OnEnable");
            InvokeLifecycle(qm, "Start"); // 今回のフローには不要だが、本番のライフサイクルに忠実にするため呼ぶ

            var rsGo = new GameObject("Test_QuestRewardSystem");
            var rs = rsGo.AddComponent<QuestRewardSystem>();
            InvokeLifecycle(rs, "OnEnable");

            var bsGo = new GameObject("Test_BirdRewardSpawner");
            var bs = bsGo.AddComponent<BirdRewardSpawner>();
            InvokeLifecycle(bs, "Awake");
            InvokeLifecycle(bs, "OnEnable");

            return new Harness(quest, qmGo, qm, rsGo, rs, bsGo, bs);
        }

        private static void Teardown(Harness h)
        {
            InvokeLifecycle(h.BirdSpawner, "OnDisable");
            InvokeLifecycle(h.RewardSystem, "OnDisable");
            InvokeLifecycle(h.QuestManager, "OnDisable");
            Object.DestroyImmediate(h.BirdSpawnerGo);
            Object.DestroyImmediate(h.RewardSystemGo);
            Object.DestroyImmediate(h.QuestManagerGo);
        }

        private static int CountBirds(Harness h) => h.BirdSpawner.GetComponentsInChildren<RewardBird>(true).Length;

        // ── 1. 森カテゴリの進捗1〜4では鳥が生成されない ─────────────────────

        [Test]
        public void Progress1To4_Forest_DoesNotSpawnBirds()
        {
            var h = MakeHarness();
            try
            {
                for (int i = 1; i <= 4; i++)
                    EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, i));

                Assert.AreEqual(0, CountBirds(h), "進捗1〜4では鳥は生成されないはず");
            }
            finally
            {
                Teardown(h);
            }
        }

        // ── 2〜6. 進捗5で完走し、各段のイベントが1回ずつ・正しい内容で流れること ──

        [Test]
        public void Progress5_Forest_CompletesQuestAndSpawnsBirdsWithCorrectEvents()
        {
            var h = MakeHarness();
            try
            {
                int questCompletedCount = 0;
                QuestDefinition lastCompletedQuest = null;
                System.Action<QuestCompletedEvent> onCompleted = e => { questCompletedCount++; lastCompletedQuest = e.Quest; };

                int rewardUnlockedCount = 0;
                string lastRewardId = null;
                System.Action<RewardUnlockedEvent> onReward = e => { rewardUnlockedCount++; lastRewardId = e.RewardId; };

                EventBus.Subscribe(onCompleted);
                EventBus.Subscribe(onReward);
                try
                {
                    for (int i = 1; i <= 5; i++)
                        EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, i));

                    // 2. 進捗5でクエストが達成される
                    Assert.AreEqual(1, questCompletedCount, "QuestCompletedEventが発行されるはず");
                    Assert.AreEqual(h.Quest, lastCompletedQuest, "達成したクエストはテスト用QuestDefinitionのはず");

                    // 3. QuestCompletedEventが1回だけ発行される（上のAssertと合わせて回数を明示）
                    Assert.AreEqual(1, questCompletedCount, "QuestCompletedEventは1回だけのはず");

                    // 4. RewardUnlockedEventが1回だけ発行される
                    Assert.AreEqual(1, rewardUnlockedCount, "RewardUnlockedEventは1回だけのはず");

                    // 5. rewardIdがforest_unlock_birdsである
                    Assert.AreEqual("forest_unlock_birds", lastRewardId);

                    // 6. RewardBirdが設定範囲内の個体数だけ生成される（BirdRewardSpawnerの既定は1〜3羽）
                    int birdCount = CountBirds(h);
                    Assert.GreaterOrEqual(birdCount, 1);
                    Assert.LessOrEqual(birdCount, 3);
                }
                finally
                {
                    EventBus.Unsubscribe(onCompleted);
                    EventBus.Unsubscribe(onReward);
                }
            }
            finally
            {
                Teardown(h);
            }
        }

        // ── 7. 進捗5や同じイベントを再送しても鳥が重複生成されない ───────────

        [Test]
        public void ResendingProgress5_DoesNotDuplicateCompletionRewardOrBirds()
        {
            var h = MakeHarness();
            try
            {
                int questCompletedCount = 0;
                int rewardUnlockedCount = 0;
                System.Action<QuestCompletedEvent> onCompleted = e => questCompletedCount++;
                System.Action<RewardUnlockedEvent> onReward = e => rewardUnlockedCount++;

                EventBus.Subscribe(onCompleted);
                EventBus.Subscribe(onReward);
                try
                {
                    for (int i = 1; i <= 5; i++)
                        EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, i));

                    int birdCountAfterFirst = CountBirds(h);

                    // 進捗5を再送、さらに進捗が伸びたケース（6,7）も送る
                    EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 5));
                    EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 6));
                    EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, 7));

                    Assert.AreEqual(1, questCompletedCount, "QuestCompletedEventは再送しても1回のままのはず");
                    Assert.AreEqual(1, rewardUnlockedCount, "RewardUnlockedEventは再送しても1回のままのはず");
                    Assert.AreEqual(birdCountAfterFirst, CountBirds(h), "鳥の数は再送しても増えないはず");
                }
                finally
                {
                    EventBus.Unsubscribe(onCompleted);
                    EventBus.Unsubscribe(onReward);
                }
            }
            finally
            {
                Teardown(h);
            }
        }

        // ── 8. 異なるTerrainClusterCategoryでは進捗・達成しない ──────────────

        [Test]
        public void DifferentCategory_DoesNotProgressOrComplete()
        {
            var h = MakeHarness();
            try
            {
                int progressChangedCount = 0;
                int questCompletedCount = 0;
                System.Action<QuestProgressChangedEvent> onProgress = e => progressChangedCount++;
                System.Action<QuestCompletedEvent> onCompleted = e => questCompletedCount++;

                EventBus.Subscribe(onProgress);
                EventBus.Subscribe(onCompleted);
                try
                {
                    EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Field, 5));
                    EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.River, 5));

                    Assert.AreEqual(0, progressChangedCount, "対象外カテゴリでは進捗が変化しないはず");
                    Assert.AreEqual(0, questCompletedCount, "対象外カテゴリでは達成しないはず");
                    Assert.AreEqual(0, CountBirds(h), "対象外カテゴリでは鳥も生成されないはず");
                }
                finally
                {
                    EventBus.Unsubscribe(onProgress);
                    EventBus.Unsubscribe(onCompleted);
                }
            }
            finally
            {
                Teardown(h);
            }
        }

        // ── 9. コンポーネントをDisableした後は該当部分の処理が止まる ─────────
        //    BirdRewardSpawnerだけを先にDisableしておき、クエスト達成→報酬解放までは
        //    正常に流れるが、鳥だけは生成されないことを確認する
        //    （チェーンの特定の輪だけが止まり、それより上流は影響を受けないことの検証）。

        [Test]
        public void DisablingBirdRewardSpawner_StopsOnlyBirdSpawning_UpstreamStillWorks()
        {
            var h = MakeHarness();
            try
            {
                InvokeLifecycle(h.BirdSpawner, "OnDisable");

                int questCompletedCount = 0;
                int rewardUnlockedCount = 0;
                System.Action<QuestCompletedEvent> onCompleted = e => questCompletedCount++;
                System.Action<RewardUnlockedEvent> onReward = e => rewardUnlockedCount++;

                EventBus.Subscribe(onCompleted);
                EventBus.Subscribe(onReward);
                try
                {
                    for (int i = 1; i <= 5; i++)
                        EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, i));

                    Assert.AreEqual(1, questCompletedCount, "QuestManagerはBirdRewardSpawnerと無関係に達成するはず");
                    Assert.AreEqual(1, rewardUnlockedCount, "QuestRewardSystemはBirdRewardSpawnerと無関係に報酬を解放するはず");
                    Assert.AreEqual(0, CountBirds(h), "BirdRewardSpawnerがDisableのままなら鳥は生成されないはず");
                }
                finally
                {
                    EventBus.Unsubscribe(onCompleted);
                    EventBus.Unsubscribe(onReward);
                }
            }
            finally
            {
                // BirdSpawnerは既にOnDisable済みだが、Unsubscribeは冪等なので再度呼んでも問題ない
                Teardown(h);
            }
        }

        // ── 10. テスト終了後にEventBus購読と生成オブジェクトが残らない ───────

        [Test]
        public void AfterTeardown_NoEventBusSubscriptionsOrObjectsRemain()
        {
            var h = MakeHarness();

            for (int i = 1; i <= 5; i++)
                EventBus.Publish(new TerrainClusterProgressEvent(TerrainClusterCategory.Forest, i));
            Assert.GreaterOrEqual(CountBirds(h), 1, "前提: 達成して鳥が生成されていること");

            var qm = h.QuestManager;
            var rs = h.RewardSystem;
            var bs = h.BirdSpawner;

            Teardown(h);

            Assert.AreEqual(0, CountSubscribersForTarget<TerrainClusterProgressEvent>(qm),
                "teardown後、QuestManagerのTerrainClusterProgressEvent購読は残らないはず");
            Assert.AreEqual(0, CountSubscribersForTarget<QuestCompletedEvent>(rs),
                "teardown後、QuestRewardSystemのQuestCompletedEvent購読は残らないはず");
            Assert.AreEqual(0, CountSubscribersForTarget<RewardUnlockedEvent>(bs),
                "teardown後、BirdRewardSpawnerのRewardUnlockedEvent購読は残らないはず");

            Assert.IsNull(GameObject.Find("Test_QuestManager"));
            Assert.IsNull(GameObject.Find("Test_QuestRewardSystem"));
            Assert.IsNull(GameObject.Find("Test_BirdRewardSpawner"));
        }
    }
}
