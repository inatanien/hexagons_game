// 役割: 木がタイルの「実際の上面」へ接地していることを固定する。
//       六角柱メッシュの上面は height*0.5 にあるのに、プロップは長らく
//       tileHeight+0.01 を地面として使っており、木が0.10だけ宙に浮いていた。
//       同じ間違いへ戻らないようテストで留める。

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class ForestTreeGroundingTests
    {
        /// <summary>木の接地高さ。productionと同じ組み立て（上面 + プロップの下駄）で作る。</summary>
        private static float TreeGroundY(float tileHeight)
        {
            var lift = typeof(HexTile).GetField("PropLiftY", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(lift, "HexTile.PropLiftY が見つからない");
            return HexMeshBuilder.TopY(tileHeight) + (float)lift.GetValue(null);
        }

        [Test]
        public void MeshTopY_MatchesTheGeneratedMesh()
        {
            // TopY が「Build()が実際に作る上面」と一致していること。
            // ここがずれると、プロップの接地が全て狂う。定数ではなく実物と突き合わせる。
            const float height = 0.30f;
            var mesh = HexMeshBuilder.Build(2.0f, height);

            float highest = float.MinValue;
            foreach (var v in mesh.vertices) highest = Mathf.Max(highest, v.y);

            Assert.AreEqual(highest, HexMeshBuilder.TopY(height), 0.0001f,
                "HexMeshBuilder.TopY がメッシュの実際の上面と一致していない");
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MeshTopY_ScalesWithHeight()
        {
            foreach (var h in new[] { 0.10f, 0.30f, 0.60f, 1.00f })
                Assert.AreEqual(h * 0.5f, HexMeshBuilder.TopY(h), 0.0001f, $"height={h}");
        }

        [Test]
        public void TreeGround_SitsOnTheMeshTopSurface()
        {
            const float tileHeight = 0.30f;
            float meshTop = HexMeshBuilder.TopY(tileHeight);
            float ground  = TreeGroundY(tileHeight);

            Assert.AreEqual(meshTop, ground, 0.02f, "木の接地高さがタイル上面から離れている（浮き／めり込み）");
            Assert.Greater(ground, meshTop, "地面より下だと木がタイルへ埋まる");
        }

        [Test]
        public void TreeGround_IsNotTheOldPropAnchor()
        {
            // 旧値（tileHeight + 0.01）へ戻っていないことの回帰テスト。木が0.10浮く。
            const float tileHeight = 0.30f;
            float oldAnchor = tileHeight + 0.01f;
            Assert.Greater(Mathf.Abs(oldAnchor - TreeGroundY(tileHeight)), 0.05f,
                "プロップ共通のアンカー（tileHeight+0.01）へ戻っている");
        }
    }
}
