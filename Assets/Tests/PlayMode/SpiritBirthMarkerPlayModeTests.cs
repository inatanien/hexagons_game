// 役割: 誕生の目印（地面に広がって消える光の輪）を実ライフサイクルで検証する。
//       このStageの核心は「精霊が漂い始めても、輪は生まれた場所に残る」ことなので、
//       位置に関する保証を厚めに固定する。
//       Phase1_v002は開かず、最小Hierarchyを構築する方針を維持する。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Spirits;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class SpiritBirthMarkerPlayModeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
        private const string MarkerName = "SpiritBirthMarker";

        private readonly List<GameObject> _spawned = new();

        private static void ClearEventBus()
        {
            var f = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "EventBus._handlers が見つかりません");
            ((System.Collections.IDictionary)f.GetValue(null)).Clear();
        }

        [SetUp]
        public void SetUp()
        {
            ClearEventBus();
            GameInteractionStateController.SetState(GameInteractionState.Playing);
        }

        [TearDown]
        public void TearDown()
        {
            GameInteractionStateController.SetState(GameInteractionState.Playing);
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearEventBus();
        }

        // ── ヘルパー ──────────────────────────────────────────────────

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        private List<HexTile> MakeForest(Vector3 origin, int count)
        {
            var root = Track(new GameObject("Forest"));
            var tiles = new List<HexTile>();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Tile" + i);
                go.transform.SetParent(root.transform, true);
                var tile = go.AddComponent<HexTile>();
                tile.Initialize(new HexCoord(i, -i), 1f);
                go.transform.position = origin + new Vector3(i * 1.5f, 0f, (i % 2) * 0.866f);
                tiles.Add(tile);
            }
            return tiles;
        }

        private ForestSpiritSpawner MakeSpiritsSystem()
        {
            var go = Track(new GameObject("Spirits"));
            var spawner = go.AddComponent<ForestSpiritSpawner>();
            typeof(ForestSpiritSpawner).GetField("_minClusterSizeToSpawn", Priv).SetValue(spawner, 4);
            return spawner;
        }

        private static void PublishForestGrowth(IReadOnlyList<HexTile> tiles)
            => EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                   null, HexCoord.Zero, tiles, new ForestGrowthMetrics(tiles.Count, tiles.Count)));

        private static ForestSpirit SpiritOf(ForestSpiritSpawner s) => s.GetComponentInChildren<ForestSpirit>(true);

        private static ParticleSystem MarkerOf(ForestSpirit spirit)
        {
            foreach (var ps in spirit.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.gameObject.name == MarkerName) return ps;
            return null;
        }

        /// <summary>目印の粒のワールド座標。simulationSpace=World なので position はそのままワールド。</summary>
        private static bool TryGetMarkerPosition(ForestSpirit spirit, out Vector3 position)
        {
            position = Vector3.zero;
            var ps = MarkerOf(spirit);
            if (ps == null) return false;

            var buffer = new ParticleSystem.Particle[8];
            int n = ps.GetParticles(buffer);
            if (n <= 0) return false;

            position = buffer[0].position;
            return true;
        }

        /// <summary>タイルが配る接地ルール（HexMeshBuilder.TopY + PropLiftY）で期待値を作る。</summary>
        private static float ExpectedGroundY(IReadOnlyList<HexTile> tiles)
        {
            float highest = float.MinValue;
            foreach (var t in tiles) highest = Mathf.Max(highest, t.GroundWorldPosition.y);
            return highest;
        }

        // ══ 高さ：精霊の浮遊Yではなく、渡された地面の高さ ════════════════

        [UnityTest]
        public IEnumerator Marker_UsesTheGroundHeight_NotTheSpiritHoverHeight()
        {
            var tiles = MakeForest(Vector3.zero, 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.IsNotNull(spirit, "精霊が生成されていない");
            Assert.IsTrue(TryGetMarkerPosition(spirit, out var marker), "目印の粒が出ていない");

            float expected = ExpectedGroundY(tiles);
            Assert.AreEqual(expected, marker.y, 0.001f,
                "目印のYがタイル上面（接地ルール）と一致しない");

            // ★精霊は空中に浮いている。目印がそのYを使っていないことを明示的に確かめる。
            Assert.Greater(spirit.transform.position.y, marker.y + 0.05f,
                "精霊が浮いていない（このテストが意味を持たない）");
            Assert.AreNotEqual(spirit.transform.position.y, marker.y,
                "目印が精霊の浮遊Yを使ってしまっている");
        }

        [UnityTest]
        public IEnumerator Marker_FollowsTheGivenWorldHeight()
        {
            // 別のワールド高さでも、その高さの地面へ正しく置かれること。
            var tiles = MakeForest(new Vector3(0f, 5f, 0f), 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.IsTrue(TryGetMarkerPosition(spirit, out var marker), "目印の粒が出ていない");

            float expected = ExpectedGroundY(tiles);
            Assert.AreEqual(expected, marker.y, 0.001f, $"高さ5の地面へ置かれていない（実測 {marker.y:F3}）");
            Assert.Greater(marker.y, 5f, "元の高さ（0付近）に取り残されている");
        }

        [UnityTest]
        public IEnumerator Marker_XZ_MatchesWhereTheSpiritWasBorn()
        {
            var tiles = MakeForest(new Vector3(7f, 0f, -3f), 4);
            var spawner = MakeSpiritsSystem();

            PublishForestGrowth(tiles);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.IsTrue(TryGetMarkerPosition(spirit, out var marker), "目印の粒が出ていない");

            // 誕生直後はまだほとんど動いていないので、XZは精霊の足元と一致する。
            Assert.AreEqual(spirit.transform.position.x, marker.x, 0.25f, "目印のXが誕生位置とずれている");
            Assert.AreEqual(spirit.transform.position.z, marker.z, 0.25f, "目印のZが誕生位置とずれている");
        }

        // ══ 精霊が動いても輪は残る（このStageの核心） ════════════════════

        [UnityTest]
        public IEnumerator Marker_StaysAtBirthPoint_WhenSpiritMovesAway()
        {
            var tiles = MakeForest(Vector3.zero, 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.IsTrue(TryGetMarkerPosition(spirit, out var born), "目印の粒が出ていない");

            // 精霊を大きく動かす（本編では自分で漂っていく）。
            spirit.transform.position += new Vector3(12f, 4f, -9f);
            yield return null;
            yield return null;

            Assert.IsTrue(TryGetMarkerPosition(spirit, out var after), "移動後に目印が消えた");

            // ★XYZすべてが誕生地点のまま。1軸でも追従したらこのStageの目的が崩れる。
            Assert.AreEqual(born.x, after.x, 0.001f, "目印のXが精霊へ追従した");
            Assert.AreEqual(born.y, after.y, 0.001f, "目印のYが精霊へ追従した");
            Assert.AreEqual(born.z, after.z, 0.001f, "目印のZが精霊へ追従した");
        }

        [UnityTest]
        public IEnumerator Marker_UsesWorldSimulationSpace()
        {
            // 上のテストが成立する前提そのものを固定する
            // （Localへ戻すと親の移動に引きずられる）。
            var tiles = MakeForest(Vector3.zero, 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var ps = MarkerOf(SpiritOf(spawner));
            Assert.IsNotNull(ps, "目印のParticleSystemが無い");
            Assert.AreEqual(ParticleSystemSimulationSpace.World, ps.main.simulationSpace,
                "simulationSpaceがWorldでない（精霊に追従してしまう）");
        }

        // ══ 重複呼び出しで増殖しない ═════════════════════════════════════

        [UnityTest]
        public IEnumerator Marker_DoesNotDuplicate_WhenBirthIsRequestedAgain()
        {
            var tiles = MakeForest(Vector3.zero, 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.AreEqual(1, CountMarkers(spirit), "誕生直後の目印が1つではない");
            Assert.IsTrue(TryGetMarkerPosition(spirit, out var first), "目印の粒が出ていない");

            // 誕生演出を手で呼び直す（本編では起きないが、将来の呼び出し追加への保険）。
            var method = typeof(ForestSpirit).GetMethod("BeginBirthPresentation", Priv);
            Assert.IsNotNull(method, "BeginBirthPresentation が見つからない");
            method.Invoke(spirit, new object[] { new Vector3(99f, 99f, 99f) });
            method.Invoke(spirit, new object[] { new Vector3(50f, 50f, 50f) });
            yield return null;

            Assert.AreEqual(1, CountMarkers(spirit), "ParticleSystemが増殖した");

            var ps = MarkerOf(spirit);
            Assert.AreEqual(1, ps.particleCount, "粒が増えた（輪が重なって濃くなる）");

            Assert.IsTrue(TryGetMarkerPosition(spirit, out var again), "目印が消えた");
            Assert.AreEqual(first.x, again.x, 0.001f, "2回目の呼び出しで目印が動いた");
            Assert.AreEqual(first.y, again.y, 0.001f, "2回目の呼び出しで目印が動いた");
            Assert.AreEqual(first.z, again.z, 0.001f, "2回目の呼び出しで目印が動いた");
        }

        private static int CountMarkers(ForestSpirit spirit)
        {
            int n = 0;
            foreach (var ps in spirit.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.gameObject.name == MarkerName) n++;
            return n;
        }

        // ══ 一度きり ═════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Marker_AppearsOnce_EvenWhenForestKeepsGrowing()
        {
            var tiles = MakeForest(Vector3.zero, 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.AreEqual(1, MarkerOf(spirit).particleCount, "誕生直後の粒が1つではない");

            // 森が育ち続けても、2つ目の輪は出ない。
            for (int i = 0; i < 3; i++)
            {
                PublishForestGrowth(tiles);
                yield return null;
            }

            Assert.AreEqual(1, CountMarkers(spirit), "ParticleSystemが増えた");
            Assert.AreEqual(1, MarkerOf(spirit).particleCount, "輪が追加で出た");
        }

        // ══ 一時停止 ═════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Marker_FreezesDuringSettings()
        {
            var tiles = MakeForest(Vector3.zero, 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var ps = MarkerOf(SpiritOf(spawner));
            Assert.IsNotNull(ps);

            GameInteractionStateController.SetState(GameInteractionState.Settings);
            yield return null;
            yield return null;

            Assert.IsTrue(ps.isPaused, "Settings中に目印が止まっていない");

            GameInteractionStateController.SetState(GameInteractionState.Playing);
            yield return null;
            yield return null;

            Assert.IsFalse(ps.isPaused, "解除後に目印が再開しない");
        }

        // ══ Collider / 後始末 ════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Marker_HasNoColliders()
        {
            var tiles = MakeForest(Vector3.zero, 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var ps = MarkerOf(SpiritOf(spawner));
            Assert.AreEqual(0, ps.GetComponentsInChildren<Collider>(true).Length,
                "目印にColliderが付いている（タイル選択のレイキャストを妨げる）");
        }

        [UnityTest]
        public IEnumerator DestroyingSpirit_LeavesNoMarker()
        {
            var tiles = MakeForest(Vector3.zero, 4);
            var spawner = MakeSpiritsSystem();
            PublishForestGrowth(tiles);
            yield return null;

            var spirit = SpiritOf(spawner);
            Object.DestroyImmediate(spirit.gameObject);
            yield return null;

            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                Assert.AreNotEqual(MarkerName, ps.gameObject.name, "精霊破棄後に目印が残っている");
        }
    }
}
