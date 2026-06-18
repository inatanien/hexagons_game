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
    }
}
