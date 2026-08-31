// 役割: 祝福の光柱（QuestCelebrationRaySystem）が実行時に正しく生き死にすることを確認する（Stage 4）。
//
//       ここで見るのは寿命と本数と向き。
//         ・なぞりが終わるまで立たないこと
//         ・穴からは立たないこと
//         ・一斉に立つこと
//         ・時間が来たら自分で消えること
//         ・差し替えで古い演出が新しい方を巻き込まないこと
//
//       色・太さ・高さの詰めはStage 5で実機を見ながら決めるので、ここでは数えられることだけを固定する。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class QuestCelebrationRayPlayModeTests
    {
        private const float StartDelay   = 0.05f;
        private const float RiseDuration = 0.1f;
        private const float FadeDuration = 0.1f;

        /// <summary>光柱1本あたりの頂点数（3段 × 3列）。QuestCelebrationRaySystemと合わせてある。</summary>
        private const int VerticesPerRay = 9;

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created)
                if (o != null) Object.Destroy(o);
            _created.Clear();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"{target.GetType().Name}に{fieldName}フィールドが見つかりません");
            field.SetValue(target, value);
        }

        private TileType MakeFieldType()
        {
            var variant = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            variant.category = TileCategory.Field;
            _created.Add(variant);

            var type = ScriptableObject.CreateInstance<TileType>();
            _created.Add(type);
            type.elements = new[] { new TileElement { variant = variant, areaWeight = 1f, visualOnly = false } };
            for (int d = 0; d < 6; d++) type.edges[d] = EdgeType.Field;
            return type;
        }

        private HexTile MakeTile(HexCoord coord, TileType type)
        {
            var go = new GameObject("RayTestTile_" + coord);
            _created.Add(go);

            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, tile.OuterRadius);
            tile.Place(type, 0);
            return tile;
        }

        private QuestCelebrationRaySystem MakeSystem()
        {
            var go = new GameObject("RaySystemRig");
            _created.Add(go);
            go.SetActive(false);

            var system = go.AddComponent<QuestCelebrationRaySystem>();
            SetPrivateField(system, "_startDelay", StartDelay);
            SetPrivateField(system, "_riseDuration", RiseDuration);
            SetPrivateField(system, "_fadeDuration", FadeDuration);

            go.SetActive(true);
            return system;
        }

        private static Mesh RayMesh(QuestCelebrationRaySystem system)
        {
            var filter = system.GetComponentInChildren<MeshFilter>(true);
            return filter != null ? filter.sharedMesh : null;
        }

        private static int RayCount(QuestCelebrationRaySystem system)
        {
            var mesh = RayMesh(system);
            return mesh == null ? 0 : mesh.vertexCount / VerticesPerRay;
        }

        private static void Celebrate(params HexTile[] tiles)
            => EventBus.Publish(new QuestTileSelectionResolvedEvent(tiles));

        private static void FinishTrace()
            => EventBus.Publish(new QuestOutlineTraceCompletedEvent());

        // ── 立ち上がるタイミング ────────────────────────────────────────

        [UnityTest]
        public IEnumerator RaysDoNotRise_BeforeTheTraceCompletes()
        {
            var system = MakeSystem();
            var tile   = MakeTile(HexCoord.Zero, MakeFieldType());
            yield return null;

            Celebrate(tile);
            yield return new WaitForSeconds(StartDelay + 0.1f);

            Assert.AreEqual(0, system.transform.childCount,
                "なぞりが終わるまでは立たないはず（3段構成の2段目を飛ばさない）");
        }

        [UnityTest]
        public IEnumerator RaysRise_AfterTheTraceCompletes()
        {
            var system = MakeSystem();
            var tile   = MakeTile(HexCoord.Zero, MakeFieldType());
            yield return null;

            Celebrate(tile);
            FinishTrace();
            yield return new WaitForSeconds(StartDelay + 0.05f);

            Assert.AreEqual(6, RayCount(system), "六角形1枚の外周は6辺なので6本のはず");
        }

        [UnityTest]
        public IEnumerator AllRaysRise_InTheSameFrame()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var tiles  = new[] { MakeTile(HexCoord.Zero, type), MakeTile(HexCoord.Zero.Neighbor(0), type) };
            yield return null;

            Celebrate(tiles);
            FinishTrace();
            yield return new WaitForSeconds(StartDelay + 0.02f);

            // 順に増えるのではなく、最初から全部あること
            int first = RayCount(system);
            Assert.AreEqual(10, first, "隣接2枚の外周は10辺");

            yield return null;
            Assert.AreEqual(first, RayCount(system), "後から本数が増えてはいけない（一斉に立つ）");
        }

        // ── 穴 ────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator HoleLoops_GetNoRays()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();

            var ring = new List<HexTile>();
            for (int d = 0; d < 6; d++) ring.Add(MakeTile(HexCoord.Zero.Neighbor(d), type));
            yield return null;

            Celebrate(ring.ToArray());
            FinishTrace();
            yield return new WaitForSeconds(StartDelay + 0.05f);

            // 外周18辺のみ。穴（6辺）から立つと24本になる
            Assert.AreEqual(18, RayCount(system), "穴の輪には立てないはず");
        }

        // ── 寿命 ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator RaysDestroyThemselves_WhenTheyFadeOut()
        {
            var system = MakeSystem();
            var tile   = MakeTile(HexCoord.Zero, MakeFieldType());
            yield return null;

            Celebrate(tile);
            FinishTrace();
            yield return new WaitForSeconds(StartDelay + RiseDuration + FadeDuration + 0.3f);

            Assert.AreEqual(0, system.transform.childCount, "消えたあとに生成物は残らないはず");
        }

        [UnityTest]
        public IEnumerator NewCelebration_ReplacesTheRunningRays()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var first  = MakeTile(HexCoord.Zero, type);
            var second = MakeTile(new HexCoord(5, 0), type);
            var third  = MakeTile(new HexCoord(5, 0).Neighbor(0), type);
            yield return null;

            Celebrate(first);
            FinishTrace();
            yield return new WaitForSeconds(StartDelay + 0.02f);
            Assert.AreEqual(6, RayCount(system));

            // 走っている途中で次の祝いが来る
            Celebrate(second, third);
            yield return null;
            Assert.AreEqual(0, system.transform.childCount, "新しい祝いが始まったら前の光柱は畳まれるはず");

            FinishTrace();
            yield return new WaitForSeconds(StartDelay + 0.05f);

            Assert.AreEqual(1, system.transform.childCount, "演出は常に1組だけのはず");
            Assert.AreEqual(10, RayCount(system));

            // 古いコルーチンが新しい方を消しにこないこと
            yield return new WaitForSeconds(RiseDuration * 0.5f);
            Assert.AreEqual(1, system.transform.childCount);
        }

        [UnityTest]
        public IEnumerator Disabling_RemovesTheRays()
        {
            var system = MakeSystem();
            var tile   = MakeTile(HexCoord.Zero, MakeFieldType());
            yield return null;

            Celebrate(tile);
            FinishTrace();
            yield return new WaitForSeconds(StartDelay + 0.02f);
            Assert.AreEqual(6, RayCount(system));

            system.enabled = false;
            yield return null;

            Assert.AreEqual(0, system.transform.childCount, "無効化したら生成物は残らないはず");
        }

        // ── 見た目の構造 ──────────────────────────────────────────────

        [UnityTest]
        public IEnumerator RaysStandVerticallyAndFaceTheCamera()
        {
            var camGo = new GameObject("RayTestCamera");
            _created.Add(camGo);
            camGo.transform.position = new Vector3(0f, 5f, -10f);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";

            var system = MakeSystem();
            var tile   = MakeTile(HexCoord.Zero, MakeFieldType());
            yield return null;

            Celebrate(tile);
            FinishTrace();
            yield return new WaitForSeconds(StartDelay + RiseDuration + 0.02f);

            var mesh     = RayMesh(system);
            var vertices = mesh.vertices;

            // 1本目: 足元の左端(0)・中心(1)・右端(2)、上端は 6,7,8
            Vector3 acrossBase = vertices[2] - vertices[0];
            Vector3 upFromBase = vertices[7] - vertices[1];

            Assert.AreEqual(0f, acrossBase.y, 1e-3f, "板の横方向は水平のはず");
            Assert.Less(Mathf.Abs(upFromBase.x) + Mathf.Abs(upFromBase.z), 1e-3f,
                "光柱は常に垂直に立つはず（縦は傾けない）");

            Vector3 toCamera = camGo.transform.position - vertices[1];
            toCamera.y = 0f;
            Assert.AreEqual(0f, Vector3.Dot(acrossBase.normalized, toCamera.normalized), 1e-2f,
                "板の面はカメラの方を向くはず（横方向はカメラ方向と直交する）");
        }

        [UnityTest]
        public IEnumerator RayEdgesAreTransparent_AndTheBaseIsBrightest()
        {
            var system = MakeSystem();
            var tile   = MakeTile(HexCoord.Zero, MakeFieldType());
            yield return null;

            Celebrate(tile);
            FinishTrace();
            yield return new WaitForSeconds(StartDelay + 0.02f);

            var colors = RayMesh(system).colors;

            // 足元の段: 左端(0) 中心(1) 右端(2)
            Assert.AreEqual(0f, colors[0].a, 1e-4f, "左端は透明のはず（硬い輪郭を作らない）");
            Assert.AreEqual(0f, colors[2].a, 1e-4f, "右端は透明のはず");
            Assert.Greater(colors[1].a, 0f,          "中心は光っているはず");

            // 上へ行くほど薄くなる: 足元(1) > 中腹(4) > 上端(7)
            Assert.Greater(colors[1].a, colors[4].a, "足元がいちばん明るいはず");
            Assert.AreEqual(0f, colors[7].a, 1e-4f,  "上端は透明のはず");
        }
    }
}
