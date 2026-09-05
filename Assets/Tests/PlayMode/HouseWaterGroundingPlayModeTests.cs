// 役割: House / Water の接地を、実際に生成した見た目から検証する。
//       ★このStageの核心は「実配置とゴーストが同じ高さになること」なので、
//         両経路を並べて比較する形で固定する。
//       Phase1_v002は開かず、最小Hierarchyを構築する方針を維持する。

using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class HouseWaterGroundingPlayModeTests
    {
        private const float TileHeight = 0.30f;

        private readonly List<GameObject> _spawned = new();
        private readonly List<Object>     _assets  = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            foreach (var a  in _assets)  if (a  != null) Object.DestroyImmediate(a);
            _spawned.Clear();
            _assets.Clear();
        }

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }
        private T TrackAsset<T>(T a) where T : Object { _assets.Add(a); return a; }

        private static float GroundY(float tileHeight)
        {
            var lift = typeof(HexTile).GetField("PropLiftY", BindingFlags.NonPublic | BindingFlags.Static);
            return HexMeshBuilder.TopY(tileHeight) + (float)lift.GetValue(null);
        }

        private TileType MakeTile(TilePropType propType)
        {
            var t = TrackAsset(ScriptableObject.CreateInstance<TileType>());
            t.propType = propType;
            t.edges    = new EdgeType[6];
            return t;
        }

        /// <summary>配置ゴーストと同じ経路（HexTile.SpawnPropsPreview）で生成する。</summary>
        private GameObject BuildPreview(TileType type)
        {
            var root = Track(new GameObject("Preview"));
            HexTile.SpawnPropsPreview(type, root.transform, 2.0f, TileHeight);
            return root;
        }

        /// <summary>
        /// 実配置と同じ経路（HexTile.Place）で生成する。
        /// ★HexTileはMeshFilter等を[SerializeField]で参照するため、素のGameObjectでは
        ///   メッシュ差し替え（川の溝）が働かない。実際に付けて参照も繋いでおく。
        /// </summary>
        private HexTile BuildPlaced(TileType type)
        {
            var go = Track(new GameObject("PlacedTile"));
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TrackAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")
                                                        ?? Shader.Find("Standard")));

            var tile = go.AddComponent<HexTile>();
            SetPrivate(tile, "meshFilter",   mf);
            SetPrivate(tile, "meshRenderer", mr);

            tile.Initialize(new HexCoord(0, 0), 1f);
            go.transform.position = Vector3.zero;
            tile.Place(type, 0);
            return tile;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " が見つからない");
            f.SetValue(target, value);
        }

        /// <summary>
        /// 子のCubeのうち、最も低いもののアンカー（localPosition.y）。
        /// production側が接地ルールで直接代入している値そのものなので、
        /// 「ゴーストと実配置が同じ規則か」の比較にはこちらを使う。
        ///
        /// ★ワールドのboundsは使わない。配置アニメ(PlacementAnim)がタイルのスケールを
        ///   毎フレーム書き換えるため、ワールド座標では縮小途中の値を拾ってしまう。
        /// </summary>
        private static float LowestCubeLocalY(Transform root)
            => LowestCube(root, bottom: false);

        /// <summary>
        /// 子のCubeのうち、最も低い「見た目の下端」のローカルY。
        /// Cubeプリミティブは -0.5〜0.5 なので、下端 = localPosition.y - localScale.y*0.5。
        /// 「地面に接して見えるか」の判定にはこちらを使う。
        /// </summary>
        private static float LowestCubeBottomLocalY(Transform root)
            => LowestCube(root, bottom: true);

        private static float LowestCube(Transform root, bool bottom)
        {
            float lowest = float.MaxValue;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.transform == root) continue;
                if (mf.sharedMesh == null || !mf.sharedMesh.name.Contains("Cube")) continue;

                var t = mf.transform;
                float y = bottom ? t.localPosition.y - t.localScale.y * 0.5f
                                 : t.localPosition.y;
                lowest = Mathf.Min(lowest, y);
            }
            return lowest;
        }

        // ══ House ════════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator House_SitsOnTheGround_WhenPlaced()
        {
            var tile = BuildPlaced(MakeTile(TilePropType.House));
            yield return null;

            float ground = GroundY(TileHeight);
            float bottom = LowestCubeBottomLocalY(tile.transform);

            Assert.AreNotEqual(float.MaxValue, bottom, "家が生成されていない");
            Assert.AreEqual(ground, bottom, 0.02f,
                $"家の下端({bottom:F3})が地面({ground:F3})に接していない");
        }

        [UnityTest]
        public IEnumerator House_IsNotFloatingAtTheOldAnchor()
        {
            var tile = BuildPlaced(MakeTile(TilePropType.House));
            yield return null;

            // 旧値 0.31 に居たら失敗させる（0.15の浮き）
            float bottom = LowestCubeBottomLocalY(tile.transform);
            Assert.Greater(Mathf.Abs(bottom - (TileHeight + 0.01f)), 0.05f,
                $"家が旧アンカー({TileHeight + 0.01f:F3})に居る＝0.15浮いている");
        }

        [UnityTest]
        public IEnumerator House_GhostMatchesPlaced()
        {
            // ★このStageの核心。置く前と置いた後で家の高さが変わらないこと。
            var type   = MakeTile(TilePropType.House);
            var placed = BuildPlaced(type);
            var ghost  = BuildPreview(type);
            yield return null;

            float placedY = LowestCubeLocalY(placed.transform);
            float ghostY  = LowestCubeLocalY(ghost.transform);

            Assert.AreNotEqual(float.MaxValue, placedY, "実配置の家が無い");
            Assert.AreNotEqual(float.MaxValue, ghostY,  "ゴーストの家が無い");
            Assert.AreEqual(placedY, ghostY, 0.0001f,
                $"ゴースト({ghostY:F3})と実配置({placedY:F3})で家の高さが違う");

            // ゴースト単体でも接地していること（下端で見る）
            float ghostBottom = LowestCubeBottomLocalY(ghost.transform);
            Assert.AreEqual(GroundY(TileHeight), ghostBottom, 0.02f,
                $"ゴーストの家の下端({ghostBottom:F3})が接地ルールから外れている");
        }

        // ══ Water（川岸） ════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator RiverGhost_ShowsTheChannelAtTheGroundingHeight()
        {
            // ★ゴーストが川の位置を示せているか。
            //   岸が地形の斜面になり、目印だった緑のキューブが無くなったので、
            //   代わりに水面の幅の帯を出している。これが消えると、
            //   置く前にどこへ川が通るのか分からなくなる。
            // 実配置0.16 / ゴースト0.31 で0.15ずれていた不具合の回帰テストでもある。
            var type  = MakeTile(TilePropType.Water);
            var ghost = BuildPreview(type);
            yield return null;

            float ghostY = LowestCubeLocalY(ghost.transform);

            Assert.AreNotEqual(float.MaxValue, ghostY, "ゴーストに川の帯が無い");
            Assert.AreEqual(GroundY(TileHeight), ghostY, 0.0001f, "川の帯が接地ルールから外れている");
        }

        // ══ Water（水面は溝の中に留まる） ════════════════════════════════

        [UnityTest]
        public IEnumerator WaterParticles_StayInsideTheChannel()
        {
            // ★水パーティクルを接地ルールへ機械的に置き換えたら、ここで失敗する。
            var tile = BuildPlaced(MakeTile(TilePropType.Water));
            yield return null;

            float land  = HexMeshBuilder.TopY(TileHeight);
            int   found = 0;

            foreach (var ps in tile.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.gameObject.name != "WaterPS") continue;
                found++;
                float y = ps.transform.localPosition.y;
                Assert.Less(y, land,
                    $"水パーティクル({y:F3})が陸地上面({land:F3})より高い＝溝から浮き上がっている");
            }

            Assert.Greater(found, 0, "水パーティクルが生成されていない");
        }

        [UnityTest]
        public IEnumerator RiverTile_KeepsTheCarvedChannelMesh()
        {
            // 溝メッシュが平坦なHexMeshBuilderへ戻っていないこと。
            var tile = BuildPlaced(MakeTile(TilePropType.Water));
            yield return null;

            var mf = tile.GetComponent<MeshFilter>();
            Assert.IsNotNull(mf.sharedMesh, "メッシュが無い");
            Assert.IsTrue(mf.sharedMesh.name.Contains("RiverChannel"),
                $"川タイルのメッシュが {mf.sharedMesh.name} になっている（溝が失われた）");

            float lowestVertex = float.MaxValue;
            foreach (var v in mf.sharedMesh.vertices) lowestVertex = Mathf.Min(lowestVertex, v.y);

            // 柱の底面(-0.15)より下は無いが、溝があるので天面より十分低い頂点が存在する
            Assert.Less(lowestVertex, HexMeshBuilder.TopY(TileHeight), "天面より低い頂点が無い＝溝が無い");
        }

        [UnityTest]
        public IEnumerator RiverBank_SitsAboveTheWaterSurface()
        {
            // 岸は地形の斜面（サブメッシュ2）。水面（サブメッシュ1）より必ず高い位置にある。
            // ★役割が入れ替わると、水が岸へ乗り上げた見た目になる。
            var tile = BuildPlaced(MakeTile(TilePropType.Water));
            yield return null;

            var mesh = tile.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(3, mesh.subMeshCount, "岸のサブメッシュが無い");

            var verts = mesh.vertices;

            float lowestBank = float.MaxValue;
            foreach (var i in mesh.GetTriangles(2)) lowestBank = Mathf.Min(lowestBank, verts[i].y);

            float lowestWater = float.MaxValue;
            foreach (var i in mesh.GetTriangles(1)) lowestWater = Mathf.Min(lowestWater, verts[i].y);

            Assert.AreNotEqual(float.MaxValue, lowestBank,  "岸の三角形が無い");
            Assert.AreNotEqual(float.MaxValue, lowestWater, "水面の三角形が無い");
            Assert.Greater(lowestBank, lowestWater,
                $"岸のいちばん低いところ({lowestBank:F3})が水面の底({lowestWater:F3})より低い。役割が入れ替わっている");
        }
    }
}
