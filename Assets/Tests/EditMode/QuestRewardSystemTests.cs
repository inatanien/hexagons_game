// 役割: QuestRewardSystem（Stage 4 最小報酬システム）の単体テスト。
//       QuestManagerを直接参照していないことも、リフレクションで構造的に検証する。

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
            q.targetCategory = TerrainClusterCategory.Forest;
            q.targetCount    = 5;
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

        // ── 未対応のrewardIdでは何も発行されない（switchの既定分岐の確認） ─────

        [Test]
        public void QuestCompleted_WithUnknownRewardId_DoesNotPublish()
        {
            var system = MakeSystem();
            try
            {
                int receivedCount = 0;
                System.Action<RewardUnlockedEvent> handler = e => receivedCount++;
                EventBus.Subscribe(handler);
                try
                {
                    var quest = MakeQuest("花畑を広げよう", "flower_unlock_butterflies");
                    EventBus.Publish(new QuestCompletedEvent(quest));

                    Assert.AreEqual(0, receivedCount, "Stage 4では未対応のrewardIdは無視されるはず");
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
