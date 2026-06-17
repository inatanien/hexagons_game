// 役割: HexCoord の単体テスト（EditMode）。
//       座標制約・距離・回転・ワールド変換・等値比較の正確性を検証する。

using NUnit.Framework;
using UnityEngine;
using ElfVillage.HexGrid;

namespace ElfVillage.Tests
{
    public class HexCoordTests
    {
        [Test]
        public void Constructor_SatisfiesCubeConstraint()
        {
            var h = new HexCoord(2, -1);
            Assert.AreEqual(0, h.q + h.r + h.s);
        }

        [Test]
        public void Constructor_InvalidSThrows()
        {
            Assert.Throws<System.ArgumentException>(() => new HexCoord(1, 1, 1));
        }

        [Test]
        public void Distance_AdjacentIsOne()
        {
            var a = HexCoord.Zero;
            var b = new HexCoord(1, 0);
            Assert.AreEqual(1, a.DistanceTo(b));
        }

        [Test]
        public void Distance_SymmetricAndCorrect()
        {
            var a = new HexCoord(0, 0);
            var b = new HexCoord(3, -1);
            Assert.AreEqual(a.DistanceTo(b), b.DistanceTo(a));
            Assert.AreEqual(3, a.DistanceTo(b));
        }

        [Test]
        public void Neighbor_SixDirectionsAreUnique()
        {
            var center = HexCoord.Zero;
            var neighbors = new System.Collections.Generic.HashSet<HexCoord>();
            foreach (var n in center.Neighbors())
                neighbors.Add(n);
            Assert.AreEqual(6, neighbors.Count);
        }

        [Test]
        public void RotateRight_SixTimesIsIdentity()
        {
            var h = new HexCoord(2, -1);
            Assert.AreEqual(h, h.RotateRight(6));
        }

        [Test]
        public void RotateRightAndLeft_AreInverse()
        {
            var h = new HexCoord(3, -2);
            Assert.AreEqual(h, h.RotateRight(2).RotateLeft(2));
        }

        [Test]
        public void Range_RadiusZeroIsOnlyCenter()
        {
            int count = 0;
            foreach (var _ in HexCoord.Range(0)) count++;
            Assert.AreEqual(1, count);
        }

        [Test]
        public void Range_RadiusOneIsSeven()
        {
            int count = 0;
            foreach (var _ in HexCoord.Range(1)) count++;
            Assert.AreEqual(7, count);
        }

        [Test]
        public void WorldRoundTrip_ReturnsOriginalCoord()
        {
            var original = new HexCoord(3, -2);
            Vector3 world = original.ToWorldPosition(1f);
            HexCoord roundTripped = HexCoord.FromWorldPosition(world, 1f);
            Assert.AreEqual(original, roundTripped);
        }

        [Test]
        public void Equality_SameQRareEqual()
        {
            Assert.AreEqual(new HexCoord(1, -1), new HexCoord(1, -1));
            Assert.AreNotEqual(new HexCoord(1, 0), new HexCoord(0, 1));
        }

        [Test]
        public void Addition_IsCorrect()
        {
            var a = new HexCoord(1, 0);
            var b = new HexCoord(0, 1);
            Assert.AreEqual(new HexCoord(1, 1), a + b);
        }
    }
}
