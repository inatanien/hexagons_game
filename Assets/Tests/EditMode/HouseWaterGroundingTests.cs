// 役割: House の接地と、Water の「水面高さ」が別のルールであることを固定する。
//
//       ★このテストの主目的は、水路まわりを接地ルールへ機械的に置き換えてしまう
//         将来の変更を止めることにある。
//         House・川岸 → タイル上面（GroundWorldPosition と同じ規則）
//         水面・水パーティクル → 溝底（CenterlineHeight）
//         この2つを混同すると、水が溝から浮き上がる。

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class HouseWaterGroundingTests
    {
        private const float TileHeight = 0.30f;

        /// <summary>productionと同じ組み立て（上面 + プロップの下駄）。</summary>
        private static float GroundY(float tileHeight)
        {
            var lift = typeof(HexTile).GetField("PropLiftY", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(lift, "HexTile.PropLiftY が見つからない");
            return HexMeshBuilder.TopY(tileHeight) + (float)lift.GetValue(null);
        }

        // ══ House の接地 ═════════════════════════════════════════════════

        [Test]
        public void HouseGround_SitsOnTheMeshTopSurface()
        {
            float meshTop = HexMeshBuilder.TopY(TileHeight);
            float ground  = GroundY(TileHeight);

            Assert.AreEqual(meshTop, ground, 0.02f, "家の接地高さがタイル上面から離れている");
            Assert.Greater(ground, meshTop, "地面より下だと家がタイルへ埋まる");
        }

        [Test]
        public void HouseGround_IsNotTheOldAnchor()
        {
            // 旧値（tileHeight + 0.01 = 0.31）へ戻っていないことの回帰テスト。家が0.15浮く。
            float oldAnchor = TileHeight + 0.01f;
            Assert.Greater(Mathf.Abs(oldAnchor - GroundY(TileHeight)), 0.05f,
                "旧アンカー（tileHeight+0.01）へ戻っている");
        }

        [Test]
        public void HouseGround_MatchesTreeAndFlowerGround()
        {
            // 木・花・家がすべて同じ接地規則を共有していること。
            Assert.AreEqual(HexMeshBuilder.TopY(TileHeight) + 0.01f, GroundY(TileHeight), 0.0001f);
        }

        // ══ 水面は別ルール（機械的な置き換えへの回帰テスト） ═════════════

        [Test]
        public void CenterlineHeight_IsNotTheGroundRule()
        {
            // ★ここが一致してしまったら、水路の溝が失われたか、
            //   水面が接地ルールへ置き換えられたかのどちらか。
            float ground = GroundY(TileHeight);
            float center = RiverChannelMeshBuilder.CenterlineHeight(0.5f, TileHeight);

            Assert.Greater(Mathf.Abs(ground - center), 0.05f,
                "流路中心の高さが接地ルールと同じになっている（溝が消えている）");
            Assert.Less(center, ground, "水面が地面より高い");
        }

        [Test]
        public void CenterlineHeight_ReachesTheChannelFloorAtTheMiddle()
        {
            // 溝の最大深さは tileHeight × 0.65。中央（t=0.5）でそこまで下がる。
            const float maxDepthRatio = 0.65f;
            float expected = HexMeshBuilder.TopY(TileHeight) - TileHeight * maxDepthRatio;

            Assert.AreEqual(expected, RiverChannelMeshBuilder.CenterlineHeight(0.5f, TileHeight), 0.0001f,
                "流路中心の最深部が想定と違う（溝の深さ規則が変わった）");
            Assert.Less(expected, 0f, "最深部が地面より上（この地形では負になるはず）");
        }

        [Test]
        public void CenterlineHeight_RisesToLandAtClosedEdges()
        {
            // 閉じた端（隣に川が無い）では陸地の高さへ戻り、隣タイルと段差なく繋がる。
            float land = HexMeshBuilder.TopY(TileHeight);

            Assert.AreEqual(land, RiverChannelMeshBuilder.CenterlineHeight(0f, TileHeight), 0.0001f);
            Assert.AreEqual(land, RiverChannelMeshBuilder.CenterlineHeight(1f, TileHeight), 0.0001f);
        }

        [Test]
        public void CenterlineHeight_StaysAtTheFloorOnOpenEdges()
        {
            // 開いた端（隣も川）は溝底のまま繋がる。
            float middle = RiverChannelMeshBuilder.CenterlineHeight(0.5f, TileHeight);

            Assert.AreEqual(middle, RiverChannelMeshBuilder.CenterlineHeight(0f, TileHeight, openA: true, openB: true),
                0.0001f, "開いた端で陸地へ戻ってしまっている");
        }

        [Test]
        public void CenterlineHeight_NeverRisesAboveTheLandSurface()
        {
            float land = HexMeshBuilder.TopY(TileHeight);
            for (int i = 0; i <= 20; i++)
            {
                float t = i / 20f;
                Assert.LessOrEqual(RiverChannelMeshBuilder.CenterlineHeight(t, TileHeight), land + 0.0001f,
                    $"t={t} で水面が陸地より高い");
            }
        }

        // ══ 川岸は接地ルール側 ═══════════════════════════════════════════

        [Test]
        public void RiverBank_UsesTheGroundRule_NotTheWaterSurface()
        {
            // 川岸は「溝の縁」なので陸地の高さ。水面と混同してはいけない。
            float ground = GroundY(TileHeight);
            float water  = RiverChannelMeshBuilder.CenterlineHeight(0.5f, TileHeight);

            Assert.Greater(ground, water,
                "川岸（接地ルール）が水面（溝底）より低い。役割が入れ替わっている");
            Assert.Greater(ground - water, 0.15f,
                $"岸と水面の落差が小さすぎる（実測 {ground - water:F3}）。溝が浅くなっている可能性");
        }
    }
}
