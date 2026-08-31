// 役割: 外周を走る光（QuestCelebrationOutlineSystem）が実行時に正しく生き死にすることを確認する（Stage 3）。
//
//       ここで見るのは主に寿命と本数。
//         ・対象タイルの外周を、輪の数だけ描いているか
//         ・実際のタイルの縁と一致しているか
//         ・時間が来たら自分で消えるか
//         ・途中で次の祝いが来ても、古い演出が新しい方を巻き込まないか
//
//       見た目の詰め（色・太さ・尾の長さ）はStage 5で実機を見ながら決めるので、
//       ここでは数えられることと壊れないことだけを固定する。

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
    public class QuestCelebrationOutlinePlayModeTests
    {
        private const float TraceDuration = 0.15f;
        private const float FadeDuration  = 0.1f;

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
            var go = new GameObject("OutlineTestTile_" + coord);
            _created.Add(go);

            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, tile.OuterRadius);   // グリッド間隔と見た目の大きさを揃える
            tile.Place(type, 0);
            return tile;
        }

        private QuestCelebrationOutlineSystem MakeSystem()
        {
            var go = new GameObject("OutlineSystemRig");
            _created.Add(go);
            go.SetActive(false);

            var system = go.AddComponent<QuestCelebrationOutlineSystem>();
            SetPrivateField(system, "_traceDuration", TraceDuration);
            SetPrivateField(system, "_fadeOutDuration", FadeDuration);

            go.SetActive(true);
            return system;
        }

        private static LineRenderer[] LinesUnder(QuestCelebrationOutlineSystem system)
            => system.GetComponentsInChildren<LineRenderer>(includeInactive: true);

        private static void Celebrate(params HexTile[] tiles)
            => EventBus.Publish(new QuestTileSelectionResolvedEvent(tiles));

        // ── 生成 ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator SingleTile_CreatesOneLine()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var tile   = MakeTile(HexCoord.Zero, type);
            yield return null;

            Celebrate(tile);
            yield return null;

            Assert.AreEqual(1, LinesUnder(system).Length, "輪1本につきLineRenderer1本のはず");
        }

        [UnityTest]
        public IEnumerator RingWithHole_CreatesOneLinePerLoop()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();

            var ring = new List<HexTile>();
            for (int d = 0; d < 6; d++) ring.Add(MakeTile(HexCoord.Zero.Neighbor(d), type));
            yield return null;

            Celebrate(ring.ToArray());
            yield return null;

            Assert.AreEqual(2, LinesUnder(system).Length, "外周と穴で2本になるはず");
        }

        [UnityTest]
        public IEnumerator EmptySelection_CreatesNothing()
        {
            var system = MakeSystem();
            yield return null;

            Celebrate();
            yield return null;

            Assert.AreEqual(0, LinesUnder(system).Length);
        }

        [UnityTest]
        public IEnumerator NullTiles_AreIgnoredSafely()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var tile   = MakeTile(HexCoord.Zero, type);
            yield return null;

            EventBus.Publish(new QuestTileSelectionResolvedEvent(new List<HexTile> { null, tile, null }));
            yield return null;

            Assert.AreEqual(1, LinesUnder(system).Length, "欠けたタイルは無視して残りで描くはず");
        }

        [UnityTest]
        public IEnumerator DuplicatedTiles_DoNotDuplicateTheOutline()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var tile   = MakeTile(HexCoord.Zero, type);
            yield return null;

            Celebrate(tile, tile, tile);
            yield return null;

            Assert.AreEqual(1, LinesUnder(system).Length, "同じタイルが重複しても輪は増えないはず");
        }

        // ── 幾何 ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator LinePoints_LieOnTheActualTileRim()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var tile   = MakeTile(new HexCoord(2, -1), type);
            yield return null;

            Celebrate(tile);
            yield return null;   // 走り始めて点が入る

            var line = LinesUnder(system)[0];
            Assert.Greater(line.positionCount, 1);

            // ★見えている六角形の縁の上にあること。
            //   グリッド間隔（HexGridManager.tileSize）ではなく、
            //   タイル自身のtransformとouterRadiusを基準にしているかの確認
            for (int i = 0; i < line.positionCount; i++)
            {
                Vector3 p      = line.GetPosition(i);
                var     flat   = new Vector2(p.x - tile.transform.position.x, p.z - tile.transform.position.z);
                float   radius = flat.magnitude;

                // 六角形の縁は、中心から内接半径(R*cos30)〜外接半径(R)の範囲にある
                Assert.GreaterOrEqual(radius, tile.OuterRadius * Mathf.Cos(Mathf.Deg2Rad * 30f) - 1e-3f,
                    "タイルの内側へ食い込んではいけない");
                Assert.LessOrEqual(radius, tile.OuterRadius + 1e-3f, "タイルの外へはみ出してはいけない");
            }

            float expectedY = HexMeshBuilder.TopY(tile.TileHeight);
            Assert.Greater(line.GetPosition(0).y, expectedY, "タイル上面より上に浮かせているはず");
        }

        // ── 寿命 ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator AfterTracing_TheOutlineDestroysItself()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var tile   = MakeTile(HexCoord.Zero, type);
            yield return null;

            Celebrate(tile);
            yield return new WaitForSeconds(TraceDuration + FadeDuration + 0.2f);

            Assert.AreEqual(0, LinesUnder(system).Length, "走り終えたら生成物は残らないはず");
            Assert.AreEqual(0, system.transform.childCount, "親GameObjectごと消えているはず");
        }

        [UnityTest]
        public IEnumerator NewCelebration_ReplacesTheRunningOne()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var first  = MakeTile(HexCoord.Zero, type);
            var second = MakeTile(new HexCoord(5, 0), type);
            var third  = MakeTile(new HexCoord(5, 1), type);
            yield return null;

            Celebrate(first);
            yield return null;

            // 走っている途中で次の祝いが来る
            Celebrate(second, third);
            yield return null;

            Assert.AreEqual(1, system.transform.childCount, "演出は常に1組だけのはず");
            Assert.AreEqual(1, LinesUnder(system).Length, "隣接2枚なので輪は1本");

            // ★古い演出のコルーチンが、新しい親を消しにこないこと
            yield return new WaitForSeconds(TraceDuration * 0.5f);
            Assert.AreEqual(1, system.transform.childCount,
                "差し替え前のコルーチンが新しい演出を破棄してはいけない");
        }

        [UnityTest]
        public IEnumerator Disabling_RemovesTheOutline()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var tile   = MakeTile(HexCoord.Zero, type);
            yield return null;

            Celebrate(tile);
            yield return null;
            Assert.AreEqual(1, LinesUnder(system).Length);

            system.enabled = false;
            yield return null;

            Assert.AreEqual(0, system.transform.childCount, "無効化したら生成物は残らないはず");
        }

        // ── マテリアル ────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator AllLines_ShareTheSameMaterial()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();

            var ring = new List<HexTile>();
            for (int d = 0; d < 6; d++) ring.Add(MakeTile(HexCoord.Zero.Neighbor(d), type));
            yield return null;

            Celebrate(ring.ToArray());
            yield return null;

            var lines = LinesUnder(system);
            Assert.AreEqual(2, lines.Length);
            Assert.AreSame(lines[0].sharedMaterial, lines[1].sharedMaterial,
                "マテリアルは祝いごとに作らず、システムが1つ持って共有するはず");
        }

        // ── 光の向き ──────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator HeadIsBrighterAndThickerThanTheTail()
        {
            var system = MakeSystem();
            var type   = MakeFieldType();
            var tile   = MakeTile(HexCoord.Zero, type);
            yield return null;

            Celebrate(tile);
            yield return null;

            var line = LinesUnder(system)[0];

            // 点列は尾→先端の順なので、カーブの0が尾・1が先端に対応していること
            Assert.Greater(line.widthCurve.Evaluate(1f), line.widthCurve.Evaluate(0f), "先端の方が太いはず");
            Assert.Greater(line.colorGradient.Evaluate(1f).a, line.colorGradient.Evaluate(0f).a,
                "先端の方が明るいはず");
        }
    }
}
