// 役割: EdgeMatcher の単体テスト（EditMode）。
//
//       ★「置けるか」と「辺が合うか」は別の判定であることを守る。
//         IsPlaceable   ... 配置済みタイルへ隣接しているか（最初の1枚は自由）
//         IsEdgeCompatible ... 隣接タイルと辺が合うか（スコアリングとプレビューの演出用）
//         辺が合わなくても置ける。プレイヤーの手を止めない、という本作の方針から来ている。
//
//       回転あり・隣接なしのケース、回転済みタイル同士の接続判定もここで見る。

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
        public void IsPlaceable_MismatchedNeighbor_StillReturnsTrue()
        {
            var forestType = MakeTileType(EdgeType.Forest);
            var fieldType  = MakeTileType(EdgeType.Field);
            var neighborCoord = HexCoord.Zero.Neighbor(0);
            var grid = new Dictionary<HexCoord, HexTile>
            {
                [neighborCoord] = MakePlacedTile(neighborCoord, forestType)
            };

            // ★辺が合わなくても置ける。配置の条件は「配置済みタイルへ隣接していること」だけ。
            //   辺の一致はIsEdgeCompatibleが受け持ち、スコアリングとプレビューのグロー表示に使う。
            //   ここをfalseにすると「置ける場所を探させるゲーム」になり、
            //   急かさない・ストレスを与えないという方針から外れる
            Assert.IsTrue(EdgeMatcher.IsPlaceable(HexCoord.Zero, fieldType, 0, grid),
                "辺が合わないことは配置を妨げないはず");
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

        // ── IsEdgeCompatible ────────────────────────────────────────────
        // 辺の一致は配置を妨げなくなった代わりに、こちらが受け持っている。
        // 「合わない辺は合わないと分かる」ことは、スコアリングとプレビューの演出の土台なので
        // ここで固定しておく。

        [Test]
        public void IsEdgeCompatible_MismatchedNeighbor_ReturnsFalse()
        {
            var forestType = MakeTileType(EdgeType.Forest);
            var fieldType  = MakeTileType(EdgeType.Field);
            var neighborCoord = HexCoord.Zero.Neighbor(0);
            var grid = new Dictionary<HexCoord, HexTile>
            {
                [neighborCoord] = MakePlacedTile(neighborCoord, forestType)
            };

            Assert.IsFalse(EdgeMatcher.IsEdgeCompatible(HexCoord.Zero, fieldType, 0, grid),
                "Forestの隣にFieldなので辺は合わないはず");
        }

        [Test]
        public void IsEdgeCompatible_MatchingNeighbor_ReturnsTrue()
        {
            var forestType = MakeTileType(EdgeType.Forest);
            var neighborCoord = HexCoord.Zero.Neighbor(0);
            var grid = new Dictionary<HexCoord, HexTile>
            {
                [neighborCoord] = MakePlacedTile(neighborCoord, forestType)
            };

            Assert.IsTrue(EdgeMatcher.IsEdgeCompatible(HexCoord.Zero, forestType, 0, grid));
        }

        [Test]
        public void IsEdgeCompatible_NoNeighbors_ReturnsTrue()
        {
            var grid = new Dictionary<HexCoord, HexTile>();
            Assert.IsTrue(EdgeMatcher.IsEdgeCompatible(HexCoord.Zero, MakeTileType(EdgeType.Forest), 0, grid),
                "隣が無ければ突き合わせる相手もいないので合っている扱い");
        }

        [Test]
        public void IsEdgeCompatible_SameCategory_IgnoresEdgeTypes()
        {
            // 同じカテゴリのタイル同士は、辺の種別が違っても互換として扱う
            // （景観違いの同種タイルが並んだときに、境目で不一致と言われないようにするため）
            var a = MakeTileType(EdgeType.Forest);
            var b = MakeTileType(EdgeType.Field);
            a.tileCategory = "Forest";
            b.tileCategory = "Forest";

            var neighborCoord = HexCoord.Zero.Neighbor(0);
            var grid = new Dictionary<HexCoord, HexTile>
            {
                [neighborCoord] = MakePlacedTile(neighborCoord, a)
            };

            Assert.IsTrue(EdgeMatcher.IsEdgeCompatible(HexCoord.Zero, b, 0, grid),
                "同一カテゴリなら辺の種別によらず互換のはず");
        }

        [Test]
        public void IsEdgeCompatible_UnplacedNeighborIgnored_ReturnsTrue()
        {
            var forestType = MakeTileType(EdgeType.Forest);
            var fieldType  = MakeTileType(EdgeType.Field);
            var neighborCoord = HexCoord.Zero.Neighbor(0);

            var go = new GameObject();
            var unplaced = go.AddComponent<HexTile>();
            unplaced.Initialize(neighborCoord, 1f);   // Place()は呼ばない

            var grid = new Dictionary<HexCoord, HexTile>
            {
                [neighborCoord] = unplaced
            };

            Assert.IsTrue(EdgeMatcher.IsEdgeCompatible(HexCoord.Zero, fieldType, 0, grid),
                "未配置のタイルは突き合わせの相手にしないはず");
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
