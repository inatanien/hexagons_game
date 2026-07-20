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
        //    X/Zで異なる周波数・半幅（リサージュ曲線）でも、中心からextentX/extentZ・
        //    bobAmplitudeの範囲を超えないことを、非対称な矩形で確認する。

        [Test]
        public void ComputePosition_StaysWithinConfiguredExtentAndBobRange()
        {
            var center = new Vector3(5f, 2f, -3f);
            const float extentX = 2.2f;
            const float extentZ = 1.1f;
            const float freqX = 0.25f;
            const float freqZ = 0.41f; // extentX/extentZと異なる周波数（円軌道にならないことの確認も兼ねる）
            const float bobAmplitude = 0.2f;
            const float bobFrequency = 0.7f;
            const float phaseX = 0.3f;
            const float phaseZ = 1.1f;

            for (float t = 0f; t <= 50f; t += 0.37f)
            {
                var pos = RewardBird.ComputePosition(center, extentX, extentZ, freqX, freqZ, bobAmplitude, bobFrequency, phaseX, phaseZ, t);

                Assert.LessOrEqual(Mathf.Abs(pos.x - center.x), extentX + 0.01f, $"t={t}でX方向の振れ幅がextentXを超えています");
                Assert.LessOrEqual(Mathf.Abs(pos.z - center.z), extentZ + 0.01f, $"t={t}でZ方向の振れ幅がextentZを超えています");
                Assert.LessOrEqual(Mathf.Abs(pos.y - center.y), bobAmplitude + 0.01f, $"t={t}で垂直方向の振れ幅がbobAmplitudeを超えています");
            }
        }

        // ── 昼夜サイクルでの出現・消失 ──────────────────────────────────────

        private static string GetBirdState(RewardBird bird)
        {
            var field = typeof(RewardBird).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "RewardBirdに_stateフィールドが見つかりません");
            return field.GetValue(bird).ToString();
        }

        // ── 8. 夜になると全ての鳥が隠れる方向へ遷移する ─────────────────────

        [Test]
        public void TimeOfDayNight_TransitionsAllBirdsToHiding()
        {
            var spawner = MakeSpawner();
            try
            {
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));
                var birds = spawner.GetComponentsInChildren<RewardBird>(true);
                Assert.GreaterOrEqual(birds.Length, 1, "前提: 鳥が生成されていること");

                EventBus.Publish(new TimeOfDayEvent(TimeOfDayEvent.Phase.Night));

                foreach (var bird in birds)
                    Assert.AreEqual("FlyingToHide", GetBirdState(bird), "夜になったら隠れる方向へ遷移するはず");
            }
            finally
            {
                Teardown(spawner);
            }
        }

        // ── 9. 朝になると全ての鳥が現れる方向へ遷移する ─────────────────────

        [Test]
        public void TimeOfDayMorning_TransitionsAllBirdsToShowing()
        {
            var spawner = MakeSpawner();
            try
            {
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));
                var birds = spawner.GetComponentsInChildren<RewardBird>(true);

                EventBus.Publish(new TimeOfDayEvent(TimeOfDayEvent.Phase.Night));
                EventBus.Publish(new TimeOfDayEvent(TimeOfDayEvent.Phase.Morning));

                foreach (var bird in birds)
                    Assert.AreEqual("FlyingToShow", GetBirdState(bird), "朝になったら現れる方向へ遷移するはず");
            }
            finally
            {
                Teardown(spawner);
            }
        }

        // ── 10. OnDisable後はTimeOfDayEventを受けても鳥の状態が変わらない ────

        [Test]
        public void AfterOnDisable_TimeOfDayEventDoesNotAffectBirds()
        {
            var spawner = MakeSpawner();
            EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));
            var birds = spawner.GetComponentsInChildren<RewardBird>(true);
            InvokeLifecycle(spawner, "OnDisable");
            try
            {
                EventBus.Publish(new TimeOfDayEvent(TimeOfDayEvent.Phase.Night));

                foreach (var bird in birds)
                    Assert.AreEqual("Patrolling", GetBirdState(bird), "OnDisable後は夜になっても隠れないはず");
            }
            finally
            {
                Object.DestroyImmediate(spawner.gameObject);
            }
        }

        // ── 11. 一番近い森タイルが正しく選ばれる ─────────────────────────────

        [Test]
        public void FindNearestForestTile_ReturnsClosestTrackedPosition()
        {
            var spawner = MakeSpawner();
            try
            {
                var listField = typeof(BirdRewardSpawner).GetField("_forestTilePositions", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(listField, "BirdRewardSpawnerに_forestTilePositionsフィールドが見つかりません");
                var list = (System.Collections.Generic.List<Vector3>)listField.GetValue(spawner);
                list.Add(new Vector3(0f, 0f, 0f));
                list.Add(new Vector3(10f, 0f, 0f));
                list.Add(new Vector3(3f, 0f, 3f));

                var method = typeof(BirdRewardSpawner).GetMethod("FindNearestForestTile", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(method, "BirdRewardSpawnerにFindNearestForestTileメソッドが見つかりません");
                var nearest = (Vector3)method.Invoke(spawner, new object[] { new Vector3(2f, 0f, 2f) });

                Assert.AreEqual(new Vector3(3f, 0f, 3f), nearest, "(2,0,2)に一番近いのは(3,0,3)のはず");
            }
            finally
            {
                Teardown(spawner);
            }
        }
    }
}
