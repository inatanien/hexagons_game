// 役割: タイル1枚ぶんの木陰の「形の決まり方」を固定する。
//       向き・反転・大きさが座標から決定論的に決まらないと、
//       配置ゴーストと実タイルで影の形が変わってしまう。

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class ForestTileShadeTests
    {
        // ══ 高さ ═════════════════════════════════════════════════════════

        [Test]
        public void LocalY_IsBetweenGroundAndTrees()
        {
            const float tileHeight = 0.30f;

            var lift = typeof(HexTile).GetField("PropLiftY", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(lift, "HexTile.PropLiftY が見つからない");
            float treeGround = HexMeshBuilder.TopY(tileHeight) + (float)lift.GetValue(null);

            float shadeY  = TileShadeLayout.LocalY(tileHeight);
            float meshTop = HexMeshBuilder.TopY(tileHeight);

            Assert.Greater(shadeY, meshTop,  "木陰が地面へめり込むとZ-fightingが出る");
            Assert.Less(shadeY, treeGround,  "木陰が木より上にあると木を隠してしまう");
        }

        [Test]
        public void LocalY_UsesTheSharedTopYDefinition()
        {
            // 自前で tileHeight*0.5 を書き直していないこと（接地バグの再発防止）。
            foreach (var h in new[] { 0.10f, 0.30f, 0.60f })
                Assert.AreEqual(HexMeshBuilder.TopY(h) + TileShadeLayout.LiftY,
                                TileShadeLayout.LocalY(h), 0.0001f, $"tileHeight={h}");
        }

        // ══ 決定論 ═══════════════════════════════════════════════════════

        [Test]
        public void Layout_IsDeterministicPerCoord()
        {
            for (int q = -12; q <= 12; q++)
                for (int r = -12; r <= 12; r++)
                {
                    Assert.AreEqual(TileShadeLayout.RotationDeg(q, r),    TileShadeLayout.RotationDeg(q, r));
                    Assert.AreEqual(TileShadeLayout.IsMirrored(q, r),     TileShadeLayout.IsMirrored(q, r));
                    Assert.AreEqual(TileShadeLayout.SizeMultiplier(q, r), TileShadeLayout.SizeMultiplier(q, r));
                }
        }

        // ══ 見た目のばらつき ═════════════════════════════════════════════

        [Test]
        public void Rotation_CoversFullCircle()
        {
            // 6分割のどの区画にも入ること＝六角形の輪郭に沿った向きに固まらないこと。
            var buckets = new bool[6];
            for (int q = -12; q <= 12; q++)
                for (int r = -12; r <= 12; r++)
                {
                    float deg = TileShadeLayout.RotationDeg(q, r);
                    Assert.IsTrue(deg >= 0f && deg < 360f, $"({q},{r}) で範囲外 {deg}");
                    buckets[Mathf.Clamp((int)(deg / 60f), 0, 5)] = true;
                }
            foreach (var b in buckets) Assert.IsTrue(b, "木陰の向きが特定方向へ偏っている");
        }

        [Test]
        public void Mirror_IsRoughlyBalanced()
        {
            int mirrored = 0, total = 0;
            for (int q = -15; q <= 15; q++)
                for (int r = -15; r <= 15; r++)
                {
                    total++;
                    if (TileShadeLayout.IsMirrored(q, r)) mirrored++;
                }
            float ratio = mirrored * 100f / total;
            Assert.AreEqual(50f, ratio, 12f, $"左右反転の偏りが大きい（実測 {ratio:F1}%）");
        }

        [Test]
        public void Size_StaysWithinJitter()
        {
            for (int q = -20; q <= 20; q++)
                for (int r = -20; r <= 20; r++)
                {
                    float m = TileShadeLayout.SizeMultiplier(q, r);
                    Assert.GreaterOrEqual(m, 1f - TileShadeLayout.SizeJitter - 0.0001f, $"({q},{r}) {m}");
                    Assert.LessOrEqual(m,    1f + TileShadeLayout.SizeJitter + 0.0001f, $"({q},{r}) {m}");
                }
        }

        [Test]
        public void NeighbourTiles_DoNotShareTheSameLook()
        {
            // 隣同士で向きも反転も同じだと、地面に六角形の繰り返し模様が見えてしまう。
            int identical = 0, total = 0;
            for (int q = -10; q <= 10; q++)
                for (int r = -10; r <= 10; r++)
                {
                    total++;
                    bool sameRot = Mathf.Abs(TileShadeLayout.RotationDeg(q, r)
                                            - TileShadeLayout.RotationDeg(q + 1, r)) < 5f;
                    bool sameMir = TileShadeLayout.IsMirrored(q, r) == TileShadeLayout.IsMirrored(q + 1, r);
                    if (sameRot && sameMir) identical++;
                }
            Assert.Less(identical * 100f / total, 3f, "隣接タイルの木陰が同じ見た目になりすぎている");
        }
    }
}
