// 役割: EdgeMatcher の単体テスト（EditMode）。
//       辺一致・不一致・回転あり・隣接なしのケースを検証する。

using System.Collections.Generic;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class EdgeMatcherTests
    {
        // テスト用の TileType を動的に生成するヘルパー
        private static TileType MakeTileType(EdgeType fill)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.edges = new EdgeType[6];
            for (int i = 0; i < 6; i++) t.edges[i] = fill;
            return t;
        }

        // テスト用の配置済み HexTile を生成するヘルパー（GameObject不使用）
        private static HexTile MakePlacedTile(HexCoord coord, TileType type, int rotation = 0)
        {
            var go = new GameObject();
            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, 1f);
            tile.Place(type, rotation);
            return tile;
        }

        [Test]
        public void IsPlaceable_NoNeighbors_ReturnsTrue()
        {
            var grid = new Dictionary<HexCoord, HexTile>();
            var type = MakeTileType(EdgeType.Forest);
            Assert.IsTrue(EdgeMatcher.IsPlaceable(HexCoord.Zero, type, 0, grid));
        }

        [Test]
        public void IsPlaceable_MatchingNeighbor_ReturnsTrue()
        {
            var forestType = MakeTileType(EdgeType.Forest);
            var neighborCoord = HexCoord.Zero.Neighbor(0); // 右隣
            var grid = new Dictionary<HexCoord, HexTile>
            {
                [neighborCoord] = MakePlacedTile(neighborCoord, forestType)
            };
            Assert.IsTrue(EdgeMatcher.IsPlaceable(HexCoord.Zero, forestType, 0, grid));
        }

        [Test]
        public void IsPlaceable_MismatchedNeighbor_ReturnsFalse()
        {
            var forestType = MakeTileType(EdgeType.Forest);
            var fieldType  = MakeTileType(EdgeType.Field);
            var neighborCoord = HexCoord.Zero.Neighbor(0);
            var grid = new Dictionary<HexCoord, HexTile>
            {
                [neighborCoord] = MakePlacedTile(neighborCoord, forestType)
            };
            // Forest隣に Field を置こうとするので NG
            Assert.IsFalse(EdgeMatcher.IsPlaceable(HexCoord.Zero, fieldType, 0, grid));
        }

        [Test]
        public void IsPlaceable_UnplacedNeighborIgnored_ReturnsTrue()
        {
            var forestType = MakeTileType(EdgeType.Forest);
            var fieldType  = MakeTileType(EdgeType.Field);
            var neighborCoord = HexCoord.Zero.Neighbor(0);

            // 未配置タイルは無視される
            var go = new GameObject();
            var unplacedTile = go.AddComponent<HexTile>();
            unplacedTile.Initialize(neighborCoord, 1f);
            // Place() を呼ばず IsPlaced = false のまま

            var grid = new Dictionary<HexCoord, HexTile>
            {
                [neighborCoord] = unplacedTile
            };
            Assert.IsTrue(EdgeMatcher.IsPlaceable(HexCoord.Zero, fieldType, 0, grid));
        }

        [Test]
        public void HasAnyPlaced_EmptyGrid_ReturnsFalse()
        {
            var grid = new Dictionary<HexCoord, HexTile>();
            Assert.IsFalse(EdgeMatcher.HasAnyPlaced(grid));
        }

        [Test]
        public void HasAnyPlaced_WithPlacedTile_ReturnsTrue()
        {
            var type = MakeTileType(EdgeType.Forest);
            var grid = new Dictionary<HexCoord, HexTile>
            {
                [HexCoord.Zero] = MakePlacedTile(HexCoord.Zero, type)
            };
            Assert.IsTrue(EdgeMatcher.HasAnyPlaced(grid));
        }

        // ── TryGetEdgeType / AreEdgesCompatible / TryGetConnectedCategory の回転対応 ──────
        // 川底の盛り上がり判定（HexGridManager.CheckAndApplyConnections）が、回転済みタイル同士の
        // 接続で辺を取り違えていた回帰の再現・修正確認。TileData.GetEdge（direction - rotation）と
        // 同じ規則で判定できているかを検証する。

        // River_Bend相当（ローカル方向0と5がRiver、隣接2辺のカーブ）
        private static TileType MakeBendType()
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.edges = new[]
            {
                EdgeType.River, EdgeType.Field, EdgeType.Field,
                EdgeType.Field, EdgeType.Field, EdgeType.River,
            };
            return t;
        }

        [Test]
        public void TryGetEdgeType_WithRotation_MatchesTileDataGetEdge()
        {
            var type = MakeBendType();
            for (int rotation = 0; rotation < 6; rotation++)
            {
                var data = new TileData(HexCoord.Zero, type, rotation);
                for (int dir = 0; dir < 6; dir++)
                {
                    EdgeMatcher.TryGetEdgeType(type, dir, rotation, out EdgeType viaMatcher);
                    Assert.AreEqual(data.GetEdge(dir), viaMatcher,
                        $"rotation={rotation}, dir={dir}: EdgeMatcherとTileData.GetEdgeの結果が一致しない");
                }
            }
        }

        [Test]
        public void TryGetConnectedCategory_RotatedBendNeighbor_PreviouslyMismatched_NowCorrect()
        {
            // 実際のRiver_Bend資産同士で発見した回帰ケース: placed(rot=0)のdir=5に対し、
            // neighbor(rot=2)は本来「開いている(River同士接続)」はずが、rotation未対応の
            // 旧実装ではfalse（閉じている）と誤判定し、本来盛り上がるべきでない場所で
            // 川底が盛り上がっていた。
            var placedType   = MakeBendType();
            var neighborType = MakeBendType();

            bool matched = EdgeMatcher.TryGetConnectedCategory(
                    placedType, 5, 0, neighborType, 2, out TileCategory category)
                && category == TileCategory.River;

            var placedData   = new TileData(HexCoord.Zero, placedType, 0);
            var neighborData = new TileData(HexCoord.Zero.Neighbor(5), neighborType, 2);
            bool expected = placedData.CanConnect(neighborData, 5)
                && placedData.GetEdge(5) == EdgeType.River;

            Assert.IsTrue(expected, "テスト前提: このケースは本来Riverで接続しているはず");
            Assert.AreEqual(expected, matched, "回転を考慮したTryGetConnectedCategoryはTileData基準の正解と一致するはず");
        }

        [Test]
        public void TryGetConnectedCategory_NoRotationOverload_StillDefaultsToZero()
        {
            // 既存の（rotation引数なし）呼び出しが、rotation=0を渡した場合と同じ結果になることを
            // 確認する（後方互換）。
            var placedType   = MakeBendType();
            var neighborType = MakeBendType();

            bool viaOldOverload = EdgeMatcher.TryGetConnectedCategory(
                    placedType, 5, neighborType, out TileCategory catOld)
                && catOld == TileCategory.River;
            bool viaNewOverloadZero = EdgeMatcher.TryGetConnectedCategory(
                    placedType, 5, 0, neighborType, 0, out TileCategory catNew)
                && catNew == TileCategory.River;

            Assert.AreEqual(viaNewOverloadZero, viaOldOverload, "rotation引数なしの呼び出しはrotation=0指定と同じ結果になるはず");
        }
    }
}
