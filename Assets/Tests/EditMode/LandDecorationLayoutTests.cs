// 役割: 陸地装飾（landDecoration）が「見た目だけ」であることと、
//       木や花が川へ侵入しないことを固定する。
//
//       ★このテストの主目的は2つ。
//         1. landDecoration をどう設定しても、カテゴリ判定の結果が
//            素の川タイルとまったく同じであること。
//            接続・デッキ・シナジー・成長エフェクトへ漏れ出したら、
//            「見た目専用」という前提そのものが崩れる。
//         2. 川へ近すぎる候補が確実に捨てられること。
//            木が水面に立つのは、この機能で最も避けたい絵になる。

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class LandDecorationLayoutTests
    {
        private const float OuterRadius   = 2.0f;
        private const float GoldenAngle   = 137.50776f;
        private const float MaxRadius     = 1.70f;
        private const int   CandidateCount = 24;

        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Track<T>(T o) where T : Object { _created.Add(o); return o; }

        // 現行3種の川が使う辺の組み合わせ。関数は形状非依存だが、3つとも通しておく。
        private static readonly int[][] s_ShapeEdges = { new[] { 0, 3 }, new[] { 0, 5 }, new[] { 0, 4 } };
        private static readonly string[] s_ShapeNames = { "Straight", "Bend", "WideBend" };

        private static void GetChannel(int shape, out Vector3 a, out Vector3 ctrl, out Vector3 b)
        {
            a = RiverChannelLayout.EdgeCenter(s_ShapeEdges[shape][0], OuterRadius);
            b = RiverChannelLayout.EdgeCenter(s_ShapeEdges[shape][1], OuterRadius);
            bool isStraight = ((a + b) * 0.5f).sqrMagnitude < 0.01f;
            ctrl = isStraight ? (a + b) * 0.5f : Vector3.zero;
        }

        private TerrainVariantDefinition MakeVariant(TileCategory category, TilePropType propType)
        {
            var v = Track(ScriptableObject.CreateInstance<TerrainVariantDefinition>());
            v.category  = category;
            v.propType  = propType;
            v.propCount = 24;
            return v;
        }

        private TileType MakeRiverType(TerrainVariantDefinition decoration, int candidateCount)
        {
            var t = Track(ScriptableObject.CreateInstance<TileType>());
            t.propType     = TilePropType.Water;
            t.tileCategory = "River";
            t.edges = new[] { EdgeType.River, EdgeType.Field, EdgeType.Field,
                              EdgeType.River, EdgeType.Field, EdgeType.Field };
            t.elements                     = new TileElement[0];
            t.landDecoration               = decoration;
            t.landDecorationCandidateCount = candidateCount;
            return t;
        }

        // ══ 川へ侵入しないこと（この機能の中核） ═════════════════════════

        [Test]
        public void Placements_NeverLandInsideTheClearance()
        {
            foreach (float clearance in new[] { 0.70f, 0.75f, 0.80f, 0.95f })
            {
                for (int s = 0; s < s_ShapeEdges.Length; s++)
                {
                    GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);

                    foreach (var coord in new[] { new Vector2Int(0, 0), new Vector2Int(3, -2),
                                                  new Vector2Int(-4, 5), new Vector2Int(7, 1),
                                                  new Vector2Int(-2, -6), new Vector2Int(11, -3) })
                    {
                        var placements = LandDecorationLayout.ComputePlacements(
                            CandidateCount, coord.x, coord.y, GoldenAngle, MaxRadius,
                            true, a, ctrl, b, clearance);

                        foreach (var p in placements)
                        {
                            float d = RiverChannelLayout.DistanceToCenterline(p.LocalOffset, a, ctrl, b);
                            Assert.GreaterOrEqual(d, clearance,
                                $"{s_ShapeNames[s]} coord={coord} clearance={clearance} で " +
                                $"中心線から{d:F3}の位置に置かれている");
                        }
                    }
                }
            }
        }

        [Test]
        public void Placements_KeepSomeTreesOnEveryShape()
        {
            // 除外しすぎて1本も残らないと、川タイルが単なる川に見えてしまう。
            for (int s = 0; s < s_ShapeEdges.Length; s++)
            {
                GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);
                var placements = LandDecorationLayout.ComputePlacements(
                    CandidateCount, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, 0.80f);

                Assert.Greater(placements.Count, 3, $"{s_ShapeNames[s]} で木が少なすぎる");
                Assert.Less(placements.Count, CandidateCount, $"{s_ShapeNames[s]} で1つも除外されていない");
            }
        }

        [Test]
        public void LargerClearance_NeverKeepsMorePlacements()
        {
            // clearanceを広げたのに本数が増えたら、除外の向きが逆になっている。
            for (int s = 0; s < s_ShapeEdges.Length; s++)
            {
                GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);
                int prev = int.MaxValue;
                foreach (float clearance in new[] { 0.50f, 0.70f, 0.75f, 0.80f, 0.95f })
                {
                    int count = LandDecorationLayout.ComputePlacements(
                        CandidateCount, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, clearance).Count;
                    Assert.LessOrEqual(count, prev, $"{s_ShapeNames[s]} clearance={clearance} で本数が増えた");
                    prev = count;
                }
            }
        }

        [Test]
        public void ClearanceIsAnArgument_NotABakedConstant()
        {
            // ★木と花で別の余白を使えるように、clearanceは必ず引数で効くこと。
            GetChannel(0, out Vector3 a, out Vector3 ctrl, out Vector3 b);
            int loose = LandDecorationLayout.ComputePlacements(
                CandidateCount, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, 0.55f).Count;
            int tight = LandDecorationLayout.ComputePlacements(
                CandidateCount, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, 1.10f).Count;

            Assert.Greater(loose, tight, "clearanceを変えても結果が変わらない（内部へ埋め込まれている疑い）");
        }

        [Test]
        public void WithoutAChannel_NoCandidateIsDropped()
        {
            // 川を持たないタイルでは除外そのものを行わない。
            var placements = LandDecorationLayout.ComputePlacements(
                CandidateCount, 3, -2, GoldenAngle, MaxRadius,
                hasChannel: false, Vector3.zero, Vector3.zero, Vector3.zero, clearance: 5f);

            Assert.AreEqual(CandidateCount, placements.Count);
        }

        [Test]
        public void ZeroOrNegativeCandidateCount_ProducesNothing()
        {
            GetChannel(0, out Vector3 a, out Vector3 ctrl, out Vector3 b);
            foreach (int n in new[] { 0, -1, -100 })
                Assert.AreEqual(0, LandDecorationLayout.ComputePlacements(
                    n, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, 0.80f).Count, $"count={n}");
        }

        // ══ 決定論（実配置とプレビューが一致する土台） ═══════════════════

        [Test]
        public void Placements_AreDeterministicForTheSameCoord()
        {
            GetChannel(0, out Vector3 a, out Vector3 ctrl, out Vector3 b);

            var first  = LandDecorationLayout.ComputePlacements(CandidateCount, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, 0.80f);
            var second = LandDecorationLayout.ComputePlacements(CandidateCount, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, 0.80f);

            Assert.AreEqual(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.AreEqual(first[i].LocalOffset, second[i].LocalOffset, $"[{i}] の位置が違う");
                Assert.AreEqual(first[i].Seed,        second[i].Seed,        $"[{i}] の種が違う");
            }
        }

        [Test]
        public void Placements_DifferBetweenCoords()
        {
            // 全タイルが同じ並びだと、川沿いの森が判で押したように見える。
            GetChannel(0, out Vector3 a, out Vector3 ctrl, out Vector3 b);
            var here  = LandDecorationLayout.ComputePlacements(CandidateCount, 0, 0,  GoldenAngle, MaxRadius, true, a, ctrl, b, 0.80f);
            var there = LandDecorationLayout.ComputePlacements(CandidateCount, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, 0.80f);

            bool identical = here.Count == there.Count;
            if (identical)
                for (int i = 0; i < here.Count; i++)
                    if (here[i].LocalOffset != there[i].LocalOffset) { identical = false; break; }

            Assert.IsFalse(identical, "座標が違っても同じ並びになっている");
        }

        [Test]
        public void Seeds_AreAlwaysNonNegative()
        {
            // seed % 配列長 を添字に使う箇所があるため、負だと例外になる。
            foreach (var coord in new[] { new Vector2Int(0, 0), new Vector2Int(-9999, -9999),
                                          new Vector2Int(99999, -99999), new Vector2Int(-7, 13) })
                for (int i = 0; i < 64; i++)
                    Assert.GreaterOrEqual(LandDecorationLayout.ComputeSeed(coord.x, coord.y, i), 0,
                        $"coord={coord} index={i}");
        }

        [Test]
        public void Placements_StayWithinTheTile()
        {
            GetChannel(0, out Vector3 a, out Vector3 ctrl, out Vector3 b);
            var placements = LandDecorationLayout.ComputePlacements(
                CandidateCount, 3, -2, GoldenAngle, MaxRadius, true, a, ctrl, b, 0.80f);

            foreach (var p in placements)
            {
                Assert.AreEqual(0f, p.LocalOffset.y, 0.0001f, "オフセットに高さが混ざっている");
                // 半径ジッターぶんの余裕を見て maxRadius + 0.06 までを許す
                Assert.LessOrEqual(new Vector2(p.LocalOffset.x, p.LocalOffset.z).magnitude, MaxRadius + 0.06f,
                    "タイルの外へはみ出している");
            }
        }

        // ══ ゲーム属性へ一切参加しないこと（今回の中核） ═════════════════

        /// <summary>カテゴリ関連APIの結果を1本の文字列へ畳む。</summary>
        private static string CategorySnapshot(TileType t)
        {
            var effective = new List<string>();
            foreach (var c in t.GetEffectiveCategories()) effective.Add(c.ToString());
            var effect = new List<string>();
            foreach (var c in t.GetEffectCategories()) effect.Add(c.ToString());

            var sb = new System.Text.StringBuilder();
            sb.Append("Effective=[").Append(string.Join(",", effective.ToArray())).Append("]");
            sb.Append(" Effect=[").Append(string.Join(",", effect.ToArray())).Append("]");
            foreach (TileCategory c in System.Enum.GetValues(typeof(TileCategory)))
            {
                sb.Append(" ").Append(c).Append(":has=").Append(t.HasCategory(c));
                sb.Append(",eff=").Append(t.HasEffectCategory(c));
                sb.Append(",w=").Append(TerrainEffectWeight.Of(t, c).ToString("F4"));
            }
            return sb.ToString();
        }

        [Test]
        public void LandDecoration_DoesNotChangeAnyCategoryResult()
        {
            // ★どんな variant を設定しても、素の川タイルと完全に同じ結果でなければならない。
            var plain    = MakeRiverType(null, 0);
            string baseline = CategorySnapshot(plain);

            var variants = new[]
            {
                MakeVariant(TileCategory.Forest,  TilePropType.Tree),
                MakeVariant(TileCategory.Field,   TilePropType.Flower),
                MakeVariant(TileCategory.River,   TilePropType.Tree),
                MakeVariant(TileCategory.Village, TilePropType.Tree),
            };

            foreach (var v in variants)
            {
                var decorated = MakeRiverType(v, 24);
                Assert.AreEqual(baseline, CategorySnapshot(decorated),
                    $"landDecoration に {v.category}/{v.propType} を設定するとカテゴリ判定が変わる");
            }
        }

        [Test]
        public void LandDecoration_DoesNotAffectEdgeMatching()
        {
            var plain     = MakeRiverType(null, 0);
            var decorated = MakeRiverType(MakeVariant(TileCategory.Forest, TilePropType.Tree), 24);

            var forestTile = Track(ScriptableObject.CreateInstance<TileType>());
            forestTile.tileCategory = "Forest";
            forestTile.edges        = new EdgeType[6];

            Assert.AreEqual(EdgeMatcher.SameCategory(plain, plain),
                            EdgeMatcher.SameCategory(decorated, plain), "川同士の判定が変わっている");
            Assert.IsFalse(EdgeMatcher.SameCategory(decorated, forestTile),
                "陸地装飾が森として扱われている");

            for (int d = 0; d < 6; d++)
            {
                Assert.IsTrue(EdgeMatcher.TryGetEdgeType(decorated, d, out EdgeType decoratedEdge));
                Assert.IsTrue(EdgeMatcher.TryGetEdgeType(plain, d, out EdgeType plainEdge));
                Assert.AreEqual(plainEdge, decoratedEdge, $"dir{d} の辺が変わっている");
            }
        }

        [Test]
        public void HasLandDecoration_RequiresBothVariantAndCount()
        {
            Assert.IsFalse(MakeRiverType(null, 24).HasLandDecoration, "variant未設定なのに有効");
            Assert.IsFalse(MakeRiverType(MakeVariant(TileCategory.Forest, TilePropType.Tree), 0).HasLandDecoration,
                "候補数0なのに有効");
            Assert.IsTrue(MakeRiverType(MakeVariant(TileCategory.Forest, TilePropType.Tree), 24).HasLandDecoration);
        }

        [Test]
        public void ExistingRiverTiles_HaveNoLandDecoration()
        {
            // 既存の川3種へ装飾が付いていないこと（今回の変更が既存の見た目を変えないことの固定）。
            foreach (var name in new[] { "TileType_River_Straight", "TileType_River_Bend", "TileType_River_Wide_Bend" })
            {
                var t = UnityEditor.AssetDatabase.LoadAssetAtPath<TileType>(
                    "Assets/_Game/ScriptableObjects/TileDefinitions/" + name + ".asset");
                Assert.IsNotNull(t, name + " が見つからない");
                Assert.IsFalse(t.HasLandDecoration, name + " に陸地装飾が付いている");
            }
        }

        [Test]
        public void RiverForestAsset_IsPureRiverWithDecoration()
        {
            // 試作アセットが「ゲーム上は純粋なRiver」であることを、実アセットに対して固定する。
            var plain = UnityEditor.AssetDatabase.LoadAssetAtPath<TileType>(
                "Assets/_Game/ScriptableObjects/TileDefinitions/TileType_River_Straight.asset");
            var decorated = UnityEditor.AssetDatabase.LoadAssetAtPath<TileType>(
                "Assets/_Game/ScriptableObjects/TileDefinitions/TileType_RiverForest_Straight.asset");
            Assert.IsNotNull(plain);
            Assert.IsNotNull(decorated, "TileType_RiverForest_Straight が見つからない");

            Assert.AreEqual(CategorySnapshot(plain), CategorySnapshot(decorated),
                "試作アセットのカテゴリ判定が River_Straight と違う");
            Assert.AreEqual(plain.tileCategory, decorated.tileCategory);
            Assert.AreEqual(plain.propType,     decorated.propType, "溝メッシュの条件(propType=Water)が失われている");
            Assert.IsFalse(decorated.HasVisualElements, "elements を使っている（カテゴリへ漏れる）");
            Assert.IsTrue(decorated.HasLandDecoration);

            for (int d = 0; d < 6; d++)
                Assert.AreEqual(plain.GetEdge(d), decorated.GetEdge(d), $"dir{d} の辺が違う");
        }
    }
}
