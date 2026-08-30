// 役割: QuestRewardSystem（クエスト達成 → 報酬解放）の単体テスト。
//       QuestManagerを直接参照していないことも、リフレクションで構造的に検証する。
//
//       ★このシステムはrewardIdの内容を解釈しない。未知のIDでもそのまま発行し、
//         どの報酬に反応するかは受信側（BirdRewardSpawner等）の責務とする。
//       ★rewardIdは識別子なので前後の空白は意味を持たない。
//         正規化（Trim）が「発行するID」と「重複判定」の両方へ効いていることを固定する。

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Quest;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class QuestRewardSystemTests
    {
        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません（リフレクション対象名の変更を確認してください）");
            method.Invoke(c, null);
        }

        private static QuestDefinition MakeQuest(string title, string rewardId)
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>();
            q.title          = title;
            q.condition = new QuestCondition(QuestConditionKind.ClusterSize, TerrainClusterCategory.Forest, 5);
            q.rewardId       = rewardId;
            return q;
        }

        private static QuestRewardSystem MakeSystem()
        {
            var go = new GameObject("TestQuestRewardSystem");
            var system = go.AddComponent<QuestRewardSystem>();
            InvokeLifecycle(system, "OnEnable");
            return system;
        }

        private static void Teardown(QuestRewardSystem system)
        {
            InvokeLifecycle(system, "OnDisable");
            Object.DestroyImmediate(system.gameObject);
        }

        // ── 1. RewardUnlockedEventが1回だけ発行される ───────────────────────

        [Test]
        public void QuestCompleted_WithKnownRewardId_PublishesRewardUnlockedEventOnce()
        {
            var system = MakeSystem();
            try
            {
                int receivedCount = 0;
                string lastRewardId = null;
                System.Action<RewardUnlockedEvent> handler = e => { receivedCount++; lastRewardId = e.RewardId; };
                EventBus.Subscribe(handler);
                try
                {
                    var quest = MakeQuest("森を育てよう", "forest_unlock_birds");
                    EventBus.Publish(new QuestCompletedEvent(quest));

                    Assert.AreEqual(1, receivedCount, "RewardUnlockedEventは1回だけ発行されるはず");
                    Assert.AreEqual("forest_unlock_birds", lastRewardId);
                }
                finally
                {
                    EventBus.Unsubscribe(handler);
                }
            }
            finally
            {
                Teardown(system);
            }
        }

        // ── 2. QuestCompletedEvent重複時もRewardUnlockedEventは重複しない ──────

        [Test]
        public void QuestCompleted_PublishedTwiceWithSameRewardId_DoesNotDuplicateReward()
        {
            var system = MakeSystem();
            try
            {
                int receivedCount = 0;
                System.Action<RewardUnlockedEvent> handler = e => receivedCount++;
                EventBus.Subscribe(handler);
                try
                {
                    var questA = MakeQuest("森を育てよう", "forest_unlock_birds");
                    var questB = MakeQuest("森を育てよう（2回目）", "forest_unlock_birds");
                    EventBus.Publish(new QuestCompletedEvent(questA));
                    EventBus.Publish(new QuestCompletedEvent(questB));

                    Assert.AreEqual(1, receivedCount, "同じrewardIdが二重に解放されないはず");
                }
                finally
                {
                    EventBus.Unsubscribe(handler);
                }
            }
            finally
            {
                Teardown(system);
            }
        }

        // ── 2b. 未知のrewardIdでもそのまま発行される（switch撤去の本体確認） ───

        [Test]
        public void QuestCompleted_WithUnknownRewardId_PublishesRewardUnlockedEvent()
        {
            var system = MakeSystem();
            try
            {
                int    receivedCount = 0;
                string lastRewardId  = null;
                System.Action<RewardUnlockedEvent> handler = e => { receivedCount++; lastRewardId = e.RewardId; };
                EventBus.Subscribe(handler);
                try
                {
                    var quest = MakeQuest("花畑を広げよう", "flower_unlock_butterflies");
                    EventBus.Publish(new QuestCompletedEvent(quest));

                    Assert.AreEqual(1, receivedCount,
                        "QuestRewardSystemはrewardIdの内容を解釈せず、未知のIDでもそのまま発行するはず");
                    Assert.AreEqual("flower_unlock_butterflies", lastRewardId);
                }
                finally
                {
                    EventBus.Unsubscribe(handler);
                }
            }
            finally
            {
                Teardown(system);
            }
        }

        // ── 2c. 未知のrewardIdでも二重解放しない ────────────────────────────

        [Test]
        public void QuestCompleted_WithUnknownRewardId_DoesNotPublishTwice()
        {
            var system = MakeSystem();
            try
            {
                int receivedCount = 0;
                System.Action<RewardUnlockedEvent> handler = _ => receivedCount++;
                EventBus.Subscribe(handler);
                try
                {
                    var quest = MakeQuest("花畑を広げよう", "flower_unlock_butterflies");
                    EventBus.Publish(new QuestCompletedEvent(quest));
                    EventBus.Publish(new QuestCompletedEvent(quest));

                    Assert.AreEqual(1, receivedCount, "未知のIDでも解放済みとして記録され、2回目は発行されないはず");
                }
                finally
                {
                    EventBus.Unsubscribe(handler);
                }
            }
            finally
            {
                Teardown(system);
            }
        }

        // ── 2d. 報酬なしクエスト（null / 空 / 空白のみ）では発行しない ───────

        [Test]
        public void QuestCompleted_WithBlankRewardId_PublishesNothing()
        {
            foreach (string rewardId in new[] { null, "", "   " })
            {
                var system = MakeSystem();
                try
                {
                    int receivedCount = 0;
                    System.Action<RewardUnlockedEvent> handler = _ => receivedCount++;
                    EventBus.Subscribe(handler);
                    try
                    {
                        var quest = MakeQuest("静かな森をつくろう", rewardId);
                        EventBus.Publish(new QuestCompletedEvent(quest));

                        Assert.AreEqual(0, receivedCount,
                            $"rewardId=\"{rewardId}\" は報酬なしクエストとして扱い、発行しないはず");
                    }
                    finally
                    {
                        EventBus.Unsubscribe(handler);
                    }
                }
                finally
                {
                    Teardown(system);
                }
            }
        }

        // ── 2e. 前後の空白は正規化して発行される ────────────────────────────

        [Test]
        public void QuestCompleted_WithPaddedRewardId_PublishesTrimmedId()
        {
            var system = MakeSystem();
            try
            {
                string lastRewardId = null;
                System.Action<RewardUnlockedEvent> handler = e => lastRewardId = e.RewardId;
                EventBus.Subscribe(handler);
                try
                {
                    var quest = MakeQuest("森を育てよう", " forest_unlock_birds ");
                    EventBus.Publish(new QuestCompletedEvent(quest));

                    Assert.AreEqual("forest_unlock_birds", lastRewardId,
                        "受信側の完全一致が失敗しないよう、前後の空白を落としたIDを発行するはず");
                }
                finally
                {
                    EventBus.Unsubscribe(handler);
                }
            }
            finally
            {
                Teardown(system);
            }
        }

        // ── 2f. 正規化は重複判定にも効く ────────────────────────────────────

        [Test]
        public void QuestCompleted_PaddedVariantOfUnlockedId_DoesNotPublishAgain()
        {
            var system = MakeSystem();
            try
            {
                int receivedCount = 0;
                System.Action<RewardUnlockedEvent> handler = _ => receivedCount++;
                EventBus.Subscribe(handler);
                try
                {
                    EventBus.Publish(new QuestCompletedEvent(MakeQuest("森を育てよう",   "forest_unlock_birds")));
                    EventBus.Publish(new QuestCompletedEvent(MakeQuest("森をもっと育てよう", " forest_unlock_birds ")));

                    Assert.AreEqual(1, receivedCount,
                        "空白違いは同じ報酬なので、2回目は二重解放されないはず");
                }
                finally
                {
                    EventBus.Unsubscribe(handler);
                }
            }
            finally
            {
                Teardown(system);
            }
        }

        // ── 3. RewardSystemがQuestManagerを参照していないこと ───────────────────

        [Test]
        public void QuestRewardSystem_DoesNotReferenceQuestManager()
        {
            var type = typeof(QuestRewardSystem);
            var managerType = typeof(QuestManager);
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var field in type.GetFields(flags))
                Assert.AreNotEqual(managerType, field.FieldType, $"フィールド{field.Name}がQuestManager型を参照しています");

            var methods = type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags));
            foreach (var method in methods)
            {
                foreach (var param in method.GetParameters())
                    Assert.AreNotEqual(managerType, param.ParameterType, $"{method.Name}の引数がQuestManager型を参照しています");

                var body = method.GetMethodBody();
                if (body == null) continue;
                foreach (var local in body.LocalVariables)
                    Assert.AreNotEqual(managerType, local.LocalType, $"{method.Name}内のローカル変数がQuestManager型を参照しています");
            }
        }
    }
}
