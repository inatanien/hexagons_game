// 役割: 陸地装飾（landDecoration）が、実配置と配置ゴーストで同じ見た目になり、
//       かつ川へ侵入しないことを、実際に生成した結果から検証する。
//
//       ★川タイルは「溝メッシュ・水パーティクル・川岸」が従来どおり出ることも同時に守る。
//         装飾を足したせいで川そのものが壊れていないか、ここで気づけるようにする。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class LandDecorationPlayModeTests
    {
        private const string DecoratedPath =
            "Assets/_Game/ScriptableObjects/TileDefinitions/TileType_RiverForest_Straight.asset";
        private const string PlainPath =
            "Assets/_Game/ScriptableObjects/TileDefinitions/TileType_River_Straight.asset";

        private readonly List<Object> _spawned = new();

        private TileType _decorated;
        private TileType _plain;

        [SetUp]
        public void SetUp()
        {
#if UNITY_EDITOR
            _decorated = UnityEditor.AssetDatabase.LoadAssetAtPath<TileType>(DecoratedPath);
            _plain     = UnityEditor.AssetDatabase.LoadAssetAtPath<TileType>(PlainPath);
#endif
            Assert.IsNotNull(_decorated, "TileType_RiverForest_Straight が見つからない");
            Assert.IsNotNull(_plain,     "TileType_River_Straight が見つからない");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private T Track<T>(T o) where T : Object { _spawned.Add(o); return o; }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " が見つからない");
            f.SetValue(target, value);
        }

        /// <summary>実配置と同じ経路（HexTile.Place）で1枚作る。</summary>
        private HexTile BuildPlaced(TileType type, HexCoord coord)
        {
            var go = Track(new GameObject("PlacedTile"));
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")
                                                    ?? Shader.Find("Standard")));

            var tile = go.AddComponent<HexTile>();
            SetPrivate(tile, "meshFilter",   mf);
            SetPrivate(tile, "meshRenderer", mr);

            tile.Initialize(coord, 0f);
            go.transform.position = Vector3.zero;
            tile.Place(type, 0);
            return tile;
        }

        /// <summary>配置ゴーストと同じ経路（TilePropVisualBuilder）で1枚作る。</summary>
        private GameObject BuildGhost(TileType type, HexCoord coord)
        {
            var root = Track(new GameObject("Ghost"));
            TilePropVisualBuilder.SpawnProps(type, root.transform, coord);
            return root;
        }

        /// <summary>
        /// 陸地装飾のローカルXZ位置を、比較しやすい順序でそろえて取り出す。
        ///
        /// ★木の見た目は環境で変わる。TreeBillboardSystemがシーンに居れば板1枚、
        ///   居なければプリミティブ（円柱＋球）になる。テストは表現に依存させたくないので、
        ///   LandDecoration 直下の子の位置をそのまま見る。
        ///   高さは表現によって違う（板は中心、円柱と球は別々）ため、XZだけを比較する。
        /// </summary>
        private static List<Vector3> DecorationPositions(Transform root)
        {
            var list = new List<Vector3>();

            var host = FindLandDecoration(root);
            if (host == null) return list;

            for (int i = 0; i < host.childCount; i++)
            {
                var p = host.GetChild(i).localPosition;
                list.Add(new Vector3(p.x, 0f, p.z));
            }

            list.Sort((x, y) => x.x != y.x ? x.x.CompareTo(y.x) : x.z.CompareTo(y.z));
            return list;
        }

        private static Transform FindLandDecoration(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "LandDecoration") return t;
            return null;
        }

        private static readonly HexCoord[] s_Coords =
        {
            new HexCoord(0, 0), new HexCoord(3, -2), new HexCoord(-4, 5), new HexCoord(7, 1),
        };

        // ══ 実配置とゴーストの一致 ═══════════════════════════════════════

        [UnityTest]
        public IEnumerator Decoration_MatchesBetweenPlacedAndGhost()
        {
            // ★置く前と置いた後で木が1本もずれないこと。
            foreach (var coord in s_Coords)
            {
                var placed = BuildPlaced(_decorated, coord);
                var ghost  = BuildGhost(_decorated, coord);
                yield return null;

                var p = DecorationPositions(placed.transform);
                var g = DecorationPositions(ghost.transform);

                Assert.Greater(p.Count, 0, $"{coord}: 実配置に木が無い");
                Assert.AreEqual(p.Count, g.Count, $"{coord}: 本数が違う");
                for (int i = 0; i < p.Count; i++)
                    Assert.Less((p[i] - g[i]).magnitude, 0.0001f,
                        $"{coord}: {i}本目が {p[i]} と {g[i]} でずれている");

                TearDown();
            }
        }

        // ══ 川へ侵入しないこと ═══════════════════════════════════════════

        [UnityTest]
        public IEnumerator Decoration_NeverStandsInTheRiver()
        {
            float halfWidth = RiverChannelLayout.ChannelHalfWidth(2.0f);

            foreach (var coord in s_Coords)
            {
                var placed = BuildPlaced(_decorated, coord);
                yield return null;

                Assert.IsTrue(RiverChannelLayout.TryGetChannel(_decorated, 2.0f, coord.q, coord.r, coord.s,
                    out Vector3 a, out Vector3 ctrl, out Vector3 b), "流路を取得できない");

                foreach (var pos in DecorationPositions(placed.transform))
                {
                    float d = RiverChannelLayout.DistanceToCenterline(
                        new Vector3(pos.x, 0f, pos.z), a, ctrl, b);

                    Assert.Greater(d, halfWidth,
                        $"{coord}: 木が溝の中に立っている（中心線から{d:F3}、半幅{halfWidth:F3}）");
                    Assert.GreaterOrEqual(d, HexTileLandDecorationClearance(),
                        $"{coord}: 木が岸ぎわに寄りすぎている（中心線から{d:F3}）");
                }

                TearDown();
            }
        }

        /// <summary>production が使っている clearance（internal const）。</summary>
        private static float HexTileLandDecorationClearance()
        {
            var f = typeof(HexTile).GetField("LandDecorationClearance",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "HexTile.LandDecorationClearance が見つからない");
            return (float)f.GetValue(null);
        }

        // ══ 川そのものが従来どおりであること ═════════════════════════════

        [UnityTest]
        public IEnumerator DecoratedRiver_KeepsChannelWaterAndBanks()
        {
            var plain     = BuildPlaced(_plain,     new HexCoord(3, -2));
            yield return null;
            int plainPs    = CountWaterParticles(plain.transform);
            int plainBanks = CountBankCubes(plain.transform);
            var plainMesh  = plain.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(plainMesh.name.Contains("RiverChannel"), "前提: 素の川に溝メッシュがある");
            string plainMeshName = plainMesh.name;
            int    plainSubMesh  = plainMesh.subMeshCount;
            TearDown();

            var decorated = BuildPlaced(_decorated, new HexCoord(3, -2));
            yield return null;
            var decoratedMesh = decorated.GetComponent<MeshFilter>().sharedMesh;

            Assert.AreEqual(plainMeshName, decoratedMesh.name, "溝メッシュが差し替わっている");
            Assert.AreEqual(plainSubMesh,  decoratedMesh.subMeshCount, "サブメッシュ数が違う");
            Assert.AreEqual(plainPs,       CountWaterParticles(decorated.transform), "水パーティクルの数が違う");
            Assert.AreEqual(plainBanks,    CountBankCubes(decorated.transform), "川岸の数が違う");
        }

        [UnityTest]
        public IEnumerator DecoratedRiver_KeepsWaterParticlesAsDirectChildren()
        {
            // ★GetWaterFlowDir / ReverseWaterFlow は transform 直下しか探さない。
            //   装飾のラッパーが割り込んで WaterPS が潜ると、川の流れ向き調整が壊れる。
            var tile = BuildPlaced(_decorated, new HexCoord(3, -2));
            yield return null;

            int direct = 0;
            for (int i = 0; i < tile.transform.childCount; i++)
                if (tile.transform.GetChild(i).name == "WaterPS") direct++;

            Assert.Greater(direct, 0, "WaterPS が直下の子に居ない");
            Assert.AreNotEqual(Vector3.zero, tile.GetWaterFlowDir(), "流れの向きが取れない");
        }

        // ══ 既存タイルへの無影響 ═════════════════════════════════════════

        [UnityTest]
        public IEnumerator PlainRiver_GetsNoDecoration()
        {
            var tile = BuildPlaced(_plain, new HexCoord(3, -2));
            yield return null;

            Assert.AreEqual(0, DecorationPositions(tile.transform).Count, "素の川に木が生えている");
            Assert.IsNull(tile.transform.Find("LandDecoration"), "素の川に LandDecoration が付いている");
        }

        [UnityTest]
        public IEnumerator PlainRiver_GhostAlsoGetsNoDecoration()
        {
            var ghost = BuildGhost(_plain, new HexCoord(3, -2));
            yield return null;

            Assert.AreEqual(0, DecorationPositions(ghost.transform).Count, "素の川のゴーストに木が生えている");
        }

        // ══ 補助 ═════════════════════════════════════════════════════════

        private static int CountWaterParticles(Transform root)
        {
            int n = 0;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.gameObject.name == "WaterPS") n++;
            return n;
        }

        private static int CountBankCubes(Transform root)
        {
            int n = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.transform == root) continue;
                if (mf.sharedMesh != null && mf.sharedMesh.name.Contains("Cube")) n++;
            }
            return n;
        }
    }
}
