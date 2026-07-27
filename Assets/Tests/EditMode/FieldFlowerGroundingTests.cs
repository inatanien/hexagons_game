// 役割: 花がタイルの「実際の上面」へ接地していることを固定する。
//       花は長らく tileHeight+0.02 を地面として使っており、0.17も宙に浮いていた
//       （六角柱メッシュの上面は height*0.5）。木と同じ間違いへ戻らないよう留める。

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class FieldFlowerGroundingTests
    {
        /// <summary>花の接地高さ。productionと同じ組み立て（上面 + プロップの下駄）で作る。</summary>
        private static float FlowerGroundY(float tileHeight)
        {
            var lift = typeof(HexTile).GetField("PropLiftY", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(lift, "HexTile.PropLiftY が見つからない");
            return HexMeshBuilder.TopY(tileHeight) + (float)lift.GetValue(null);
        }

        [Test]
        public void FlowerGround_SitsOnTheMeshTopSurface()
        {
            const float tileHeight = 0.30f;
            float meshTop = HexMeshBuilder.TopY(tileHeight);
            float ground  = FlowerGroundY(tileHeight);

            Assert.AreEqual(meshTop, ground, 0.02f, "花の接地高さがタイル上面から離れている（浮き／めり込み）");
            Assert.Greater(ground, meshTop, "地面より下だと花がタイルへ埋まる");
        }

        [Test]
        public void FlowerGround_IsNotTheOldAnchor()
        {
            // 旧値（tileHeight + 0.02）へ戻っていないことの回帰テスト。花が0.17浮く。
            const float tileHeight = 0.30f;
            float oldAnchor = tileHeight + 0.02f;
            Assert.Greater(Mathf.Abs(oldAnchor - FlowerGroundY(tileHeight)), 0.05f,
                "旧アンカー（tileHeight+0.02）へ戻っている");
        }

        [Test]
        public void FlowerGround_MatchesTreeGround()
        {
            // 木と花で接地の考え方が食い違わないこと（同じ PropLiftY を共有している）。
            const float tileHeight = 0.30f;
            Assert.AreEqual(HexMeshBuilder.TopY(tileHeight) + 0.01f, FlowerGroundY(tileHeight), 0.0001f);
        }

        [Test]
        public void FlowerGround_ScalesWithTileHeight()
        {
            foreach (var h in new[] { 0.10f, 0.30f, 0.60f, 1.00f })
                Assert.AreEqual(h * 0.5f, FlowerGroundY(h), 0.02f, $"tileHeight={h} で上面から離れている");
        }
    }
}
