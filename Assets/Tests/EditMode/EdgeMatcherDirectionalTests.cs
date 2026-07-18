// 役割: EdgeMatcher の方向ベース接続判定API（Session 2A で追加）の単体テスト（EditMode）。
//       GetOppositeDirection / TryGetEdgeType / AreEdgesCompatible / TryGetConnectedCategory を検証する。

using NUnit.Framework;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class EdgeMatcherDirectionalTests
    {
        // テスト用の TileType を動的に生成するヘルパー（アセットは作成しない）
        private static TileType MakeTileType(EdgeType fill)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.edges = new EdgeType[6];
            for (int i = 0; i < 6; i++) t.edges[i] = fill;
            return t;
        }

        private static TileType MakeTileType(params EdgeType[] edges)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.edges = edges;
            return t;
        }

        // ── GetOppositeDirection ──────────────────────────────────────

        [TestCase(0, 3)]
        [TestCase(1, 4)]
        [TestCase(2, 5)]
        [TestCase(3, 0)]
        [TestCase(4, 1)]
        [TestCase(5, 2)]
        public void GetOppositeDirection_ReturnsCorrectOpposite(int direction, int expected)
        {
            Assert.AreEqual(expected, EdgeMatcher.GetOppositeDirection(direction));
        }

        // ── TryGetEdgeType ────────────────────────────────────────────

        [Test]
        public void TryGetEdgeType_NullTileType_ReturnsFalse()
        {
            bool result = EdgeMatcher.TryGetEdgeType(null, 0, out EdgeType edge);
            Assert.IsFalse(result);
            Assert.AreEqual(EdgeType.None, edge);
        }

        [Test]
        public void TryGetEdgeType_NullEdges_ReturnsFalse()
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.edges = null;
            Assert.IsFalse(EdgeMatcher.TryGetEdgeType(t, 0, out _));
        }

        [Test]
        public void TryGetEdgeType_ShortEdgesArray_ReturnsFalse()
        {
            var t = MakeTileType(EdgeType.Forest, EdgeType.Field); // 長さ2（本来6）
            Assert.IsFalse(EdgeMatcher.TryGetEdgeType(t, 5, out _));
        }

        [TestCase(-1)]
        [TestCase(6)]
        [TestCase(100)]
        public void TryGetEdgeType_DirectionOutOfRange_ReturnsFalse(int direction)
        {
            var t = MakeTileType(EdgeType.Forest);
            Assert.IsFalse(EdgeMatcher.TryGetEdgeType(t, direction, out _));
        }

        [Test]
        public void TryGetEdgeType_ValidInput_ReturnsTrueWithCorrectEdge()
        {
            var t = MakeTileType(EdgeType.River);
            bool result = EdgeMatcher.TryGetEdgeType(t, 2, out EdgeType edge);
            Assert.IsTrue(result);
            Assert.AreEqual(EdgeType.River, edge);
        }

        // ── AreEdgesCompatible ────────────────────────────────────────

        [Test]
        public void AreEdgesCompatible_ForestVsForest_OpposingDirections_ReturnsTrue()
        {
            var forestA = MakeTileType(EdgeType.Forest);
            var forestB = MakeTileType(EdgeType.Forest);
            Assert.IsTrue(EdgeMatcher.AreEdgesCompatible(forestA, 0, forestB));
        }

        [Test]
        public void AreEdgesCompatible_ForestVsField_ReturnsFalse()
        {
            var forest = MakeTileType(EdgeType.Forest);
            var field  = MakeTileType(EdgeType.Field);
            Assert.IsFalse(EdgeMatcher.AreEdgesCompatible(forest, 0, field));
        }

        [Test]
        public void AreEdgesCompatible_NoneVsNone_ReturnsFalse()
        {
            var noneA = MakeTileType(EdgeType.None);
            var noneB = MakeTileType(EdgeType.None);
            Assert.IsFalse(EdgeMatcher.AreEdgesCompatible(noneA, 0, noneB));
        }

        [Test]
        public void AreEdgesCompatible_UsesOppositeDirection_NotSameDirection()
        {
            // sourceのdir1=Forest。neighborはdir1=Forestだが、反対方向であるdir4=Fieldにしている。
            // 実装が「同じ方向」を誤って比較していないかを確認する。
            var source = MakeTileType(
                EdgeType.None, EdgeType.Forest, EdgeType.None,
                EdgeType.None, EdgeType.None,   EdgeType.None);
            var neighbor = MakeTileType(
                EdgeType.None, EdgeType.Forest, EdgeType.None,
                EdgeType.None, EdgeType.Field,  EdgeType.None);

            // source dir1 と neighbor の反対方向(dir4=Field)を比較するのでfalseが正しい
            Assert.IsFalse(EdgeMatcher.AreEdgesCompatible(source, 1, neighbor));

            // neighbor側のdir4をForestに直すとtrueになることも確認する
            neighbor.edges[4] = EdgeType.Forest;
            Assert.IsTrue(EdgeMatcher.AreEdgesCompatible(source, 1, neighbor));
        }

        [TestCase(-1)]
        [TestCase(6)]
        public void AreEdgesCompatible_DirectionOutOfRange_ReturnsFalse(int direction)
        {
            var forestA = MakeTileType(EdgeType.Forest);
            var forestB = MakeTileType(EdgeType.Forest);
            Assert.IsFalse(EdgeMatcher.AreEdgesCompatible(forestA, direction, forestB));
        }

        [Test]
        public void AreEdgesCompatible_NullSource_ReturnsFalse()
        {
            var forestB = MakeTileType(EdgeType.Forest);
            Assert.IsFalse(EdgeMatcher.AreEdgesCompatible(null, 0, forestB));
        }

        [Test]
        public void AreEdgesCompatible_NullNeighbor_ReturnsFalse()
        {
            var forestA = MakeTileType(EdgeType.Forest);
            Assert.IsFalse(EdgeMatcher.AreEdgesCompatible(forestA, 0, null));
        }

        [Test]
        public void AreEdgesCompatible_WorksWithoutElements()
        {
            // elementsを一切設定していないTileType同士でも、edgesだけで判定できることを確認する
            var forestA = MakeTileType(EdgeType.Forest);
            var forestB = MakeTileType(EdgeType.Forest);
            Assert.IsNull(forestA.elements);
            Assert.IsNull(forestB.elements);
            Assert.IsTrue(EdgeMatcher.AreEdgesCompatible(forestA, 0, forestB));
        }

        // ── TryGetConnectedCategory ───────────────────────────────────

        [Test]
        public void TryGetConnectedCategory_MatchingForest_ReturnsForestCategory()
        {
            var forestA = MakeTileType(EdgeType.Forest);
            var forestB = MakeTileType(EdgeType.Forest);
            bool result = EdgeMatcher.TryGetConnectedCategory(forestA, 0, forestB, out TileCategory category);
            Assert.IsTrue(result);
            Assert.AreEqual(TileCategory.Forest, category);
        }

        [Test]
        public void TryGetConnectedCategory_MismatchedEdges_ReturnsFalse()
        {
            var forest = MakeTileType(EdgeType.Forest);
            var field  = MakeTileType(EdgeType.Field);
            bool result = EdgeMatcher.TryGetConnectedCategory(forest, 0, field, out TileCategory category);
            Assert.IsFalse(result);
            Assert.AreEqual(default(TileCategory), category);
        }

        [Test]
        public void TryGetConnectedCategory_WorksWithoutElements()
        {
            var riverA = MakeTileType(EdgeType.River);
            var riverB = MakeTileType(EdgeType.River);
            Assert.IsNull(riverA.elements);
            bool result = EdgeMatcher.TryGetConnectedCategory(riverA, 0, riverB, out TileCategory category);
            Assert.IsTrue(result);
            Assert.AreEqual(TileCategory.River, category);
        }

        [Test]
        public void TileCategoryMapping_NeverMapsToVillage()
        {
            // EdgeTypeにはVillageに相当する値が存在しないため、
            // どのEdgeTypeを渡してもTileCategory.Villageへは変換されないことを確認する。
            foreach (EdgeType edge in System.Enum.GetValues(typeof(EdgeType)))
            {
                var mapped = TileCategoryMapping.FromEdgeType(edge);
                if (mapped.HasValue)
                    Assert.AreNotEqual(TileCategory.Village, mapped.Value);
            }
        }
    }
}
