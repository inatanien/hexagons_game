// 役割: Stage 15「森クラスタが十分育つまで精霊を生成しない」判定の検証。
//       ★判定に使うのは「これからhomeになる対象クラスタ自身の枚数」であること。
//         世界全体の最大クラスタサイズで判定すると、別の場所に大きな森があるだけで
//         1枚の森へ精霊が生まれてしまう。
//       実生成の挙動はPlayMode（SpiritSpawnPlayModeTests）で検証する。

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Spirits;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class SpiritSpawnTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private static HexTile MakeTileAt(Vector3 p)
        {
            var go = new GameObject("TestTile");
            go.transform.position = p;
            return go.AddComponent<HexTile>();
        }

        private static void DestroyTiles(IEnumerable<HexTile> tiles)
        {
            foreach (var t in tiles) if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        private static ForestSpiritSpawner MakeSpawner()
            => new GameObject("TestSpawner").AddComponent<ForestSpiritSpawner>();

        private static int CountUnique(ForestSpiritSpawner spawner, IReadOnlyList<HexTile> tiles)
        {
            var m = typeof(ForestSpiritSpawner).GetMethod("CountUniqueTiles", Priv);
            Assert.IsNotNull(m, "CountUniqueTilesが見つかりません");
            return (int)m.Invoke(spawner, new object[] { tiles });
        }

        // ══ ShouldSpawn ═════════════════════════════════════════════════

        [Test]
        public void ShouldSpawn_AlreadySpawned_IsFalse()
        {
            // 既に1体いるなら、どれだけ大きな森でも生成しない（Stage 15は1体だけ）。
            Assert.IsFalse(SpiritSpawnPolicy.ShouldSpawn(alreadySpawned: true, affectedClusterSize: 100, minimumClusterSize: 4));
            Assert.IsFalse(SpiritSpawnPolicy.ShouldSpawn(alreadySpawned: true, affectedClusterSize: 4,   minimumClusterSize: 4));
        }

        [Test]
        public void ShouldSpawn_BelowMinimum_IsFalse()
        {
            for (int size = 0; size < 4; size++)
                Assert.IsFalse(SpiritSpawnPolicy.ShouldSpawn(false, size, 4),
                    $"クラスタ{size}枚で生成された（最小4枚のはず）");
        }

        [Test]
        public void ShouldSpawn_AtMinimum_IsTrue()
            => Assert.IsTrue(SpiritSpawnPolicy.ShouldSpawn(false, 4, 4), "ちょうど4枚で生成されるべき");

        [Test]
        public void ShouldSpawn_AboveMinimum_IsTrue()
        {
            for (int size = 5; size <= 20; size++)
                Assert.IsTrue(SpiritSpawnPolicy.ShouldSpawn(false, size, 4), $"クラスタ{size}枚で生成されない");
        }

        [Test]
        public void ShouldSpawn_InvalidMinimum_IsCorrectedSafely()
        {
            // 0以下の設定は「1枚以上あれば生成」へ倒す（生成が永久に起きない方が気づきにくいため）。
            foreach (var bad in new[] { 0, -1, -999, int.MinValue })
            {
                Assert.IsFalse(SpiritSpawnPolicy.ShouldSpawn(false, 0, bad), $"最小={bad} で0枚から生成された");
                Assert.IsTrue(SpiritSpawnPolicy.ShouldSpawn(false, 1, bad),  $"最小={bad} で1枚でも生成されない");
            }

            // 極端に大きい設定も例外にならず、単に生成されないだけ。
            Assert.IsFalse(SpiritSpawnPolicy.ShouldSpawn(false, 50, int.MaxValue));
        }

        [Test]
        public void ShouldSpawn_NegativeClusterSize_IsFalse()
        {
            foreach (var bad in new[] { -1, -100, int.MinValue })
                Assert.IsFalse(SpiritSpawnPolicy.ShouldSpawn(false, bad, 4), $"クラスタ={bad} で生成された");
        }

        [Test]
        public void SafeMinimum_ClampsIntoValidRange()
        {
            Assert.AreEqual(1, SpiritSpawnPolicy.SafeMinimum(0));
            Assert.AreEqual(1, SpiritSpawnPolicy.SafeMinimum(-5));
            Assert.AreEqual(1, SpiritSpawnPolicy.SafeMinimum(int.MinValue));
            Assert.AreEqual(4, SpiritSpawnPolicy.SafeMinimum(4));
            Assert.GreaterOrEqual(SpiritSpawnPolicy.SafeMinimum(int.MaxValue), 1);
        }

        // ══ 対象クラスタのサイズ計算 ════════════════════════════════════

        [Test]
        public void CountUniqueTiles_CountsDistinctNonNullTiles()
        {
            var spawner = MakeSpawner();
            var tiles = new List<HexTile>
            {
                MakeTileAt(Vector3.zero),
                MakeTileAt(new Vector3(1.5f, 0f, 0f)),
                MakeTileAt(new Vector3(3f, 0f, 0f)),
            };
            try
            {
                Assert.AreEqual(3, CountUnique(spawner, tiles));
            }
            finally { Object.DestroyImmediate(spawner.gameObject); DestroyTiles(tiles); }
        }

        [Test]
        public void CountUniqueTiles_IgnoresDuplicatesAndNulls()
        {
            var spawner = MakeSpawner();
            var a = MakeTileAt(Vector3.zero);
            var b = MakeTileAt(new Vector3(1.5f, 0f, 0f));

            // 同じタイルが重複して入っていても、実際のhomeの広さは2枚ぶんしかない。
            var tiles = new List<HexTile> { a, b, a, null, b, a, null };
            try
            {
                Assert.AreEqual(2, CountUnique(spawner, tiles),
                    "重複とnullを除いたユニーク件数で数えるべき");
            }
            finally { Object.DestroyImmediate(spawner.gameObject); DestroyTiles(new[] { a, b }); }
        }

        [Test]
        public void CountUniqueTiles_EmptyOrNull_IsZero()
        {
            var spawner = MakeSpawner();
            try
            {
                Assert.AreEqual(0, CountUnique(spawner, null));
                Assert.AreEqual(0, CountUnique(spawner, new List<HexTile>()));
                Assert.AreEqual(0, CountUnique(spawner, new List<HexTile> { null, null }));
            }
            finally { Object.DestroyImmediate(spawner.gameObject); }
        }

        [Test]
        public void CountUniqueTiles_ReusesBuffer_AcrossCalls()
        {
            // 使い回しバッファが前回の内容を持ち越さないこと。
            var spawner = MakeSpawner();
            var big   = new List<HexTile>();
            var small = new List<HexTile>();
            try
            {
                for (int i = 0; i < 6; i++) big.Add(MakeTileAt(new Vector3(i * 1.5f, 0f, 0f)));
                small.Add(big[0]);

                Assert.AreEqual(6, CountUnique(spawner, big));
                Assert.AreEqual(1, CountUnique(spawner, small), "前回のバッファ内容が残っている");
                Assert.AreEqual(6, CountUnique(spawner, big));
            }
            finally { Object.DestroyImmediate(spawner.gameObject); DestroyTiles(big); }
        }

        [Test]
        public void SpawnDecision_UsesTargetClusterSize_NotWorldLargest()
        {
            // ★世界のどこかに大きな森があっても、今回変化した小さなクラスタでは生成しない。
            var spawner = MakeSpawner();
            var smallCluster = new List<HexTile> { MakeTileAt(Vector3.zero) };
            try
            {
                int targetSize = CountUnique(spawner, smallCluster);
                Assert.AreEqual(1, targetSize);

                // 世界最大が100枚あろうと、対象が1枚なら生成しない。
                Assert.IsFalse(SpiritSpawnPolicy.ShouldSpawn(false, targetSize, 4),
                    "1枚だけのクラスタへ精霊が生成されてしまう");
            }
            finally { Object.DestroyImmediate(spawner.gameObject); DestroyTiles(smallCluster); }
        }

        // ══ 本編用の設定値の妥当性 ══════════════════════════════════════

        [Test]
        public void SpawnerDefaults_AreSuitableForMainScene()
        {
            var spawner = MakeSpawner();
            try
            {
                var t = typeof(ForestSpiritSpawner);

                int minCluster = (int)t.GetField("_minClusterSizeToSpawn", Priv).GetValue(spawner);
                Assert.GreaterOrEqual(minCluster, 2,
                    "本編の既定が1のままだと、最初の森タイル1枚で精霊が生まれてしまう");

                var mode = t.GetField("_personalityMode", Priv).GetValue(spawner);
                Assert.AreEqual(ForestSpiritSpawner.PersonalitySelectionMode.DeterministicFromHome, mode,
                    "本番の既定はDeterministicFromHomeであるべき（Fixedは検証用）");

                float minExtent   = (float)t.GetField("_minExtent", Priv).GetValue(spawner);
                float extentInset = (float)t.GetField("_extentInset", Priv).GetValue(spawner);
                Assert.Greater(minExtent, 0f);
                Assert.GreaterOrEqual(extentInset, 0f);
            }
            finally { Object.DestroyImmediate(spawner.gameObject); }
        }

        [Test]
        public void SpiritGrowthThresholds_AreUnchangedInStage15()
        {
            // Stage 15ではGrowth閾値を変更しない（計測は行うが確定はしない）。
            var go = new GameObject("TestSpirit");
            var spirit = go.AddComponent<ForestSpirit>();
            try
            {
                var t = typeof(ForestSpirit);
                Assert.AreEqual(8f,  (float)t.GetField("_growthThresholdFluff", Priv).GetValue(spirit), 0.0001f);
                Assert.AreEqual(20f, (float)t.GetField("_growthThresholdBloom", Priv).GetValue(spirit), 0.0001f);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
