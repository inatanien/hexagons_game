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

        // ── Stage 8: 住み着いた森への固定 ──────────────────────────────────

        private static Vector3 GetBirdCenter(RewardBird bird)
        {
            var baseCenter = (Vector3)typeof(RewardBird)
                .GetField("_baseCenter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bird);
            var offset = (Vector3)typeof(RewardBird)
                .GetField("_centerOffset", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(bird);
            return baseCenter + offset;
        }

        /// <summary>テスト用のHexTileを指定ワールド座標に作る（森クラスターの代用）。</summary>
        private static HexTile MakeTileAt(Vector3 position)
        {
            var go = new GameObject("TestForestTile");
            go.transform.position = position;
            var tile = go.AddComponent<HexTile>();
            return tile;
        }

        private static void PublishForestGrowth(System.Collections.Generic.List<HexTile> tiles)
        {
            var metrics = new ForestGrowthMetrics(
                largestClusterSize: tiles.Count, totalForestTiles: tiles.Count);
            EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                terrainType: null, anchor: ElfVillage.HexGrid.HexCoord.Zero,
                affectedTiles: tiles, metrics: metrics));
        }

        // 12. 報酬発生時点の森クラスター中心に鳥が生成される

        [Test]
        public void SpawnedBirds_UseForestCenterAtRewardTime()
        {
            var spawner = MakeSpawner();
            var forestA = new System.Collections.Generic.List<HexTile>
            {
                MakeTileAt(new Vector3(10f, 0f, 10f)),
                MakeTileAt(new Vector3(11f, 0f, 10f)),
            };
            try
            {
                PublishForestGrowth(forestA);
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));

                var birds = spawner.GetComponentsInChildren<RewardBird>(true);
                Assert.GreaterOrEqual(birds.Length, 1);
                foreach (var b in birds)
                {
                    var c = GetBirdCenter(b);
                    Assert.AreEqual(10.5f, c.x, 0.5f, "森Aの中心付近に生成されるはず");
                    Assert.AreEqual(10f,   c.z, 0.5f, "森Aの中心付近に生成されるはず");
                }
            }
            finally
            {
                Teardown(spawner);
                foreach (var t in forestA) Object.DestroyImmediate(t.gameObject);
            }
        }

        // 13・14. 生成後に別の森が育っても既存鳥の中心が変わらない（＝移動しない）

        [Test]
        public void ExistingBirds_DoNotFollowDifferentForest()
        {
            var spawner = MakeSpawner();
            var forestA = new System.Collections.Generic.List<HexTile>
            {
                MakeTileAt(new Vector3(10f, 0f, 10f)),
                MakeTileAt(new Vector3(11f, 0f, 10f)),
            };
            var forestB = new System.Collections.Generic.List<HexTile>
            {
                MakeTileAt(new Vector3(-40f, 0f, -40f)),
                MakeTileAt(new Vector3(-41f, 0f, -40f)),
            };
            try
            {
                PublishForestGrowth(forestA);
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));

                var birds = spawner.GetComponentsInChildren<RewardBird>(true);
                var before = new System.Collections.Generic.List<Vector3>();
                foreach (var b in birds) before.Add(GetBirdCenter(b));

                // 全く別の場所に森Bが育つ
                PublishForestGrowth(forestB);

                for (int i = 0; i < birds.Length; i++)
                {
                    Assert.AreEqual(before[i], GetBirdCenter(birds[i]),
                        "別の場所の森が育っても、既に住み着いた鳥の中心は変わらないはず");
                }
            }
            finally
            {
                Teardown(spawner);
                foreach (var t in forestA) Object.DestroyImmediate(t.gameObject);
                foreach (var t in forestB) Object.DestroyImmediate(t.gameObject);
            }
        }

        // 自分の森が育った場合は追従する（承認済みの「自分の森だけ追従」仕様）

        [Test]
        public void ExistingBirds_DoFollowTheirOwnForestGrowth()
        {
            var spawner = MakeSpawner();
            var tileA1 = MakeTileAt(new Vector3(10f, 0f, 10f));
            var tileA2 = MakeTileAt(new Vector3(11f, 0f, 10f));
            var tileA3 = MakeTileAt(new Vector3(20f, 0f, 10f)); // 森Aが東へ大きく伸びる
            var forestA = new System.Collections.Generic.List<HexTile> { tileA1, tileA2 };
            try
            {
                PublishForestGrowth(forestA);
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));

                var birds = spawner.GetComponentsInChildren<RewardBird>(true);
                var before = GetBirdCenter(birds[0]);

                // 既存タイルを含んだまま森Aが成長（＝同じクラスター）
                PublishForestGrowth(new System.Collections.Generic.List<HexTile> { tileA1, tileA2, tileA3 });

                Assert.AreNotEqual(before, GetBirdCenter(birds[0]),
                    "自分が住み着いた森が育った場合は、その森に合わせて中心が更新されるはず");
            }
            finally
            {
                Teardown(spawner);
                Object.DestroyImmediate(tileA1.gameObject);
                Object.DestroyImmediate(tileA2.gameObject);
                Object.DestroyImmediate(tileA3.gameObject);
            }
        }

        // 15. 複数の鳥がそれぞれ生成時の中心を保持する（将来、別の森に別の鳥を出せる構造か）

        [Test]
        public void BirdsSpawnedAtDifferentTimes_KeepTheirOwnCenters()
        {
            var spawner = MakeSpawner();
            var forestA = new System.Collections.Generic.List<HexTile> { MakeTileAt(new Vector3(10f, 0f, 10f)) };
            var forestB = new System.Collections.Generic.List<HexTile> { MakeTileAt(new Vector3(-40f, 0f, -40f)) };
            try
            {
                PublishForestGrowth(forestA);
                EventBus.Publish(new RewardUnlockedEvent("forest_unlock_birds"));
                var firstBatch = spawner.GetComponentsInChildren<RewardBird>(true);
                var firstCenters = new System.Collections.Generic.List<Vector3>();
                foreach (var b in firstBatch) firstCenters.Add(GetBirdCenter(b));

                // 別の森Bで、将来の別報酬に相当する生成を直接行う（重複防止を迂回して2羽目群を作る）
                PublishForestGrowth(forestB);
                var spawnBird = typeof(BirdRewardSpawner).GetMethod("SpawnBird", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(spawnBird, "BirdRewardSpawnerにSpawnBirdメソッドが見つかりません");
                spawnBird.Invoke(spawner, new object[]
                {
                    new Vector3(-40f, 2.5f, -40f), 1.2f, 1.2f, 0, (System.Collections.Generic.IReadOnlyList<HexTile>)forestB
                });

                var all = spawner.GetComponentsInChildren<RewardBird>(true);
                Assert.AreEqual(firstBatch.Length + 1, all.Length, "2群目が生成されているはず");

                // 1群目の中心が、2群目の生成後も変わっていないこと
                for (int i = 0; i < firstBatch.Length; i++)
                {
                    Assert.AreEqual(firstCenters[i], GetBirdCenter(firstBatch[i]),
                        "別の森に新しい鳥を生成しても、既存の鳥は元の森の中心を保持するはず");
                }

                // 2群目は森B側の中心を持つこと
                var newest = all[all.Length - 1];
                Assert.Less(GetBirdCenter(newest).x, -30f, "2群目は森B付近の中心を持つはず");
            }
            finally
            {
                Teardown(spawner);
                foreach (var t in forestA) Object.DestroyImmediate(t.gameObject);
                foreach (var t in forestB) Object.DestroyImmediate(t.gameObject);
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
