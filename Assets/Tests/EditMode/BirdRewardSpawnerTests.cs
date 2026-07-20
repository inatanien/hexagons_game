// 役割: BirdRewardSpawner（Stage 5 鳥の報酬出現）の単体テスト。
//       移動計算はRewardBird.ComputePosition()という純粋関数へ切り出してあるため、
//       Play Modeやコルーチンの実時間経過なしにEditModeで直接検証できる。

using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class BirdRewardSpawnerTests
    {
        private static void InvokeLifecycle(Component c, string methodName)
        {
            var method = c.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, $"{c.GetType().Name}に{methodName}メソッドが見つかりません（リフレクション対象名の変更を確認してください）");
            method.Invoke(c, null);
        }

        private static BirdRewardSpawner MakeSpawner()
        {
            var go = new GameObject("TestBirdRewardSpawner");
            var spawner = go.AddComponent<BirdRewardSpawner>();
            InvokeLifecycle(spawner, "Awake");
            InvokeLifecycle(spawner, "OnEnable");
            return spawner;
        }

        private static void Teardown(BirdRewardSpawner spawner)
        {
            InvokeLifecycle(spawner, "OnDisable");
            Object.DestroyImmediate(spawner.gameObject);
        }

        private static int CountBirds(BirdRewardSpawner spawner) =>
            spawner.GetComponentsInChildren<RewardBird>(true).Length;

        // ── 1. forest_unlock_birdsで鳥が生成される ──────────────────────────

        [Test]
        public void RewardUnlocked_ForestUnlockBirds_SpawnsBirds()
        {
            var spawner = MakeSpawner();
            try
            {
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));

                int count = CountBirds(spawner);
                Assert.GreaterOrEqual(count, 1, "forest_unlock_birdsで最低1羽は生成されるはず");
            }
            finally
            {
                Teardown(spawner);
            }
        }

        // ── 2. 他のrewardIdでは生成されない ─────────────────────────────────

        [Test]
        public void RewardUnlocked_OtherRewardId_DoesNotSpawnBirds()
        {
            var spawner = MakeSpawner();
            try
            {
                EventBus.Publish(new RewardUnlockedEvent("flower_unlock_butterflies"));

                Assert.AreEqual(0, CountBirds(spawner), "対応外のrewardIdでは鳥は生成されないはず");
            }
            finally
            {
                Teardown(spawner);
            }
        }

        // ── 3. 同じ報酬イベントを複数回受けても重複生成されない ──────────────

        [Test]
        public void RewardUnlocked_PublishedTwice_DoesNotDuplicateBirds()
        {
            var spawner = MakeSpawner();
            try
            {
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));
                int firstCount = CountBirds(spawner);

                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));
                int secondCount = CountBirds(spawner);

                Assert.AreEqual(firstCount, secondCount, "同じ報酬イベントを再度受けても鳥は追加生成されないはず");
            }
            finally
            {
                Teardown(spawner);
            }
        }

        // ── 4. OnDisable後はイベントを受け取らない ──────────────────────────

        [Test]
        public void AfterOnDisable_DoesNotSpawnOnFurtherEvents()
        {
            var spawner = MakeSpawner();
            InvokeLifecycle(spawner, "OnDisable");
            try
            {
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));

                Assert.AreEqual(0, CountBirds(spawner), "OnDisable後はイベントを受け取らず鳥は生成されないはず");
            }
            finally
            {
                Object.DestroyImmediate(spawner.gameObject);
            }
        }

        // ── 5. OnEnableし直しても重複購読しない ─────────────────────────────

        [Test]
        public void ReEnabling_DoesNotDoubleSubscribe()
        {
            var spawner = MakeSpawner();
            try
            {
                InvokeLifecycle(spawner, "OnDisable");
                InvokeLifecycle(spawner, "OnEnable");

                var handlersField = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
                var handlers = (System.Collections.IDictionary)handlersField.GetValue(null);
                var del = (System.Delegate)handlers[typeof(RewardUnlockedEvent)];

                int countForThisSpawner = 0;
                foreach (var d in del.GetInvocationList())
                    if (System.Object.ReferenceEquals(d.Target, spawner))
                        countForThisSpawner++;

                Assert.AreEqual(1, countForThisSpawner,
                    "OnDisable→OnEnableを経てもRewardUnlockedEventの購読はこのSpawnerにつき1つだけのはず");
            }
            finally
            {
                Teardown(spawner);
            }
        }

        // ── 6. 生成数が設定範囲内である ──────────────────────────────────────

        [Test]
        public void RewardUnlocked_SpawnsCountWithinConfiguredRange()
        {
            var spawner = MakeSpawner();
            try
            {
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));

                int count = CountBirds(spawner);
                Assert.GreaterOrEqual(count, 1);
                Assert.LessOrEqual(count, 3, "Stage 5の仕様上、生成数は1〜3羽の範囲内のはず");
            }
            finally
            {
                Teardown(spawner);
            }
        }

        // ── 7. 鳥の移動が指定範囲内に収まる（純粋関数を直接検証） ───────────────

        [Test]
        public void ComputePosition_StaysWithinConfiguredRadiusAndBobRange()
        {
            var center = new Vector3(5f, 2f, -3f);
            const float radius = 1.5f;
            const float bobAmplitude = 0.2f;
            const float angularSpeed = 0.25f;
            const float bobFrequency = 0.7f;
            const float phase = 0.3f;

            for (float t = 0f; t <= 50f; t += 0.37f)
            {
                var pos = RewardBird.ComputePosition(center, radius, angularSpeed, bobAmplitude, bobFrequency, phase, t);

                float horizontalDistance = new Vector2(pos.x - center.x, pos.z - center.z).magnitude;
                Assert.AreEqual(radius, horizontalDistance, 0.01f, $"t={t}で水平距離がradiusから外れています");

                float verticalOffset = Mathf.Abs(pos.y - center.y);
                Assert.LessOrEqual(verticalOffset, bobAmplitude + 0.01f, $"t={t}で垂直方向の振れ幅がbobAmplitudeを超えています");
            }
        }
    }
}
