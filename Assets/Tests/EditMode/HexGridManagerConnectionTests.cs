// 役割: HexGridManager.CheckAndApplyConnections（Session 2B でEdgeMatcher方向APIへ接続）の単体テスト。
//       private メンバーへはリフレクションでアクセスする（EditMode下ではUnityライフサイクルが
//       自動実行されないため、_grid を直接注入して安全にテストできる）。

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class HexGridManagerConnectionTests
    {
        private static readonly BindingFlags NonPublicInstance =
            BindingFlags.NonPublic | BindingFlags.Instance;

        // ── テストヘルパー ────────────────────────────────────────────

        private static TileType MakeTileType(string category, params EdgeType[] edges)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.tileCategory = category;
            t.edges = edges;
            return t;
        }

        private static HexTile MakePlacedTile(HexCoord coord, TileType type, int rotation = 0)
        {
            var go = new GameObject();
            var tile = go.AddComponent<HexTile>();
            tile.Initialize(coord, 1f);
            tile.Place(type, rotation);
            return tile;
        }

        private static HexGridManager MakeManagerWithGrid(Dictionary<HexCoord, HexTile> seed)
        {
            var go  = new GameObject();
            var mgr = go.AddComponent<HexGridManager>();
            var gridField = typeof(HexGridManager).GetField("_grid", NonPublicInstance);
            var grid = (Dictionary<HexCoord, HexTile>)gridField.GetValue(mgr);
            foreach (var kv in seed) grid[kv.Key] = kv.Value;
            return mgr;
        }

        private static void InvokeCheckAndApplyConnections(HexGridManager mgr, HexCoord coord)
        {
            var method = typeof(HexGridManager).GetMethod("CheckAndApplyConnections", NonPublicInstance);
            method.Invoke(mgr, new object[] { coord });
        }

        // TileConnectedEvent を1回分だけ捕捉するヘルパー（テスト後は必ずUnsubscribeすること）
        private static List<TileConnectedEvent> CaptureConnectedEvents(Action action)
        {
            var captured = new List<TileConnectedEvent>();
            Action<TileConnectedEvent> handler = e => captured.Add(e);
            EventBus.Subscribe(handler);
            try { action(); }
            finally { EventBus.Unsubscribe(handler); }
            return captured;
        }

        // ── 接続成立／不成立 ──────────────────────────────────────────

        [Test]
        public void ForestVsForest_OpposingEdges_ConnectsAndFiresEvent()
        {
            var forestA = MakeTileType("Forest", EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
                                                   EdgeType.Forest, EdgeType.Forest, EdgeType.Forest);
            var forestB = MakeTileType("Forest", EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
                                                   EdgeType.Forest, EdgeType.Forest, EdgeType.Forest);

            var coordA = HexCoord.Zero;
            var coordB = HexCoord.Zero.Neighbor(0);
            var tileA  = MakePlacedTile(coordA, forestA);
            var tileB  = MakePlacedTile(coordB, forestB);

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile> { [coordA] = tileA, [coordB] = tileB });

            var events = CaptureConnectedEvents(() => InvokeCheckAndApplyConnections(mgr, coordB));

            Assert.AreEqual(1, events.Count, "TileConnectedEventは1回だけ発行されるべき");
            Assert.AreEqual(1, events[0].Edges.Count);
            Assert.AreEqual(tileB, events[0].PlacedTile);
        }

        [Test]
        public void ForestVsField_DifferentCategory_PlaceableButNoConnectionEvent()
        {
            // CheckAndApplyConnectionsは配置可否(IsPlaceable)を判定しない。
            // ここではカテゴリ不一致により「接続」イベントが発生しないことだけを確認する。
            var forestA = MakeTileType("Forest", EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
                                                   EdgeType.Forest, EdgeType.Forest, EdgeType.Forest);
            var fieldB  = MakeTileType("Field",  EdgeType.Field,  EdgeType.Field,  EdgeType.Field,
                                                   EdgeType.Field,  EdgeType.Field,  EdgeType.Field);

            var coordA = HexCoord.Zero;
            var coordB = HexCoord.Zero.Neighbor(0);
            var tileA  = MakePlacedTile(coordA, forestA);
            var tileB  = MakePlacedTile(coordB, fieldB);

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile> { [coordA] = tileA, [coordB] = tileB });

            var events = CaptureConnectedEvents(() => InvokeCheckAndApplyConnections(mgr, coordB));

            Assert.AreEqual(0, events.Count);
            Assert.IsFalse(tileB.IsEdgeConnected(3));
        }

        [Test]
        public void NoneEdges_SameCategory_ConnectsButDoesNotOpenRiver_LegacyRelaxedGate()
        {
            // [Legacy/Relaxed behavior] 主接続ゲート(sameType/sameCategory)はEdgeTypeを一切見ないため、
            // カテゴリさえ一致すればNone辺同士でも「接続」自体は成立してしまう（Session 2Bで意図的に
            // 維持した既存の緩い仕様）。将来EdgeMatcher.TryGetConnectedCategoryベースの厳密な辺接続へ
            // 主接続ゲート自体を切り替える際は、このテストの期待値（接続イベントが発生する）も
            // 見直し対象になる。川の開放は現時点でもEdgeType.River同士の厳密一致でのみ起こることを確認する。
            var a = MakeTileType("Misc", EdgeType.None, EdgeType.None, EdgeType.None,
                                          EdgeType.None, EdgeType.None, EdgeType.None);
            var b = MakeTileType("Misc", EdgeType.None, EdgeType.None, EdgeType.None,
                                          EdgeType.None, EdgeType.None, EdgeType.None);

            var coordA = HexCoord.Zero;
            var coordB = HexCoord.Zero.Neighbor(0);
            var tileA  = MakePlacedTile(coordA, a);
            var tileB  = MakePlacedTile(coordB, b);

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile> { [coordA] = tileA, [coordB] = tileB });
            var events = CaptureConnectedEvents(() => InvokeCheckAndApplyConnections(mgr, coordB));

            Assert.AreEqual(1, events.Count, "カテゴリ一致のため接続イベント自体は発生する");
            Assert.IsFalse(tileB.IsRiverEdgeOpen(3));
            Assert.IsFalse(tileA.IsRiverEdgeOpen(0));
        }

        // ── River開放（EdgeMatcher.TryGetConnectedCategory経由） ─────────

        [Test]
        public void RiverVsRiver_OpposingRiverEdges_OpensRiverOnBothTiles()
        {
            var riverA = MakeTileType("River", EdgeType.River, EdgeType.Field, EdgeType.Field,
                                                 EdgeType.Field, EdgeType.Field, EdgeType.Field);
            var riverB = MakeTileType("River", EdgeType.Field, EdgeType.Field, EdgeType.Field,
                                                 EdgeType.River, EdgeType.Field, EdgeType.Field);

            var coordA = HexCoord.Zero;
            var coordB = HexCoord.Zero.Neighbor(0);
            var tileA  = MakePlacedTile(coordA, riverA);
            var tileB  = MakePlacedTile(coordB, riverB);

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile> { [coordA] = tileA, [coordB] = tileB });
            InvokeCheckAndApplyConnections(mgr, coordB);

            // coordB→coordA は方向3。riverB の dir3 は River、riverA の反対方向(0)も River。
            Assert.IsTrue(tileB.IsRiverEdgeOpen(3));
            Assert.IsTrue(tileA.IsRiverEdgeOpen(0));
        }

        [Test]
        public void RiverVsRiver_FieldEdgesFacing_DoesNotOpenRiverDespiteSameCategory()
        {
            // 両方Riverカテゴリで「接続」自体はするが、向き合っている辺はField同士なので
            // 川は開放されないことを確認する（tileCategoryではなくedgesが情報源であることの検証）。
            var riverA = MakeTileType("River", EdgeType.Field, EdgeType.Field, EdgeType.Field,
                                                 EdgeType.Field, EdgeType.Field, EdgeType.Field);
            var riverB = MakeTileType("River", EdgeType.Field, EdgeType.Field, EdgeType.Field,
                                                 EdgeType.Field, EdgeType.Field, EdgeType.Field);

            var coordA = HexCoord.Zero;
            var coordB = HexCoord.Zero.Neighbor(0);
            var tileA  = MakePlacedTile(coordA, riverA);
            var tileB  = MakePlacedTile(coordB, riverB);

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile> { [coordA] = tileA, [coordB] = tileB });
            var events = CaptureConnectedEvents(() => InvokeCheckAndApplyConnections(mgr, coordB));

            Assert.AreEqual(1, events.Count, "カテゴリ一致により接続イベントは発生する");
            Assert.IsFalse(tileB.IsRiverEdgeOpen(3));
            Assert.IsFalse(tileA.IsRiverEdgeOpen(0));
        }

        [Test]
        public void RiverCheck_UsesOppositeDirection_NotSameDirection()
        {
            // placed(coordB)のdir3=River、neighbor(coordA)は同方向(dir3)がRiverだが
            // 反対方向であるdir0はFieldにしてある。実装が同方向を誤って比較していないか確認する。
            var riverA = MakeTileType("River", EdgeType.Field, EdgeType.Field, EdgeType.Field,
                                                 EdgeType.River, EdgeType.Field, EdgeType.Field); // dir0=Field, dir3=River
            var riverB = MakeTileType("River", EdgeType.Field, EdgeType.Field, EdgeType.Field,
                                                 EdgeType.River, EdgeType.Field, EdgeType.Field); // dir3=River

            var coordA = HexCoord.Zero;
            var coordB = HexCoord.Zero.Neighbor(0);
            var tileA  = MakePlacedTile(coordA, riverA);
            var tileB  = MakePlacedTile(coordB, riverB);

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile> { [coordA] = tileA, [coordB] = tileB });
            InvokeCheckAndApplyConnections(mgr, coordB);

            // coordB→coordA方向は3(River)、その反対はcoordA視点のdir0(=Field)なので不一致→川は開かない
            Assert.IsFalse(tileB.IsRiverEdgeOpen(3));
            Assert.IsFalse(tileA.IsRiverEdgeOpen(0));
        }

        // ── 複数方向の同時接続 ────────────────────────────────────────

        [Test]
        public void PlacedTile_MultipleNeighbors_EachConnectionProcessedOnce()
        {
            var forest = new Func<TileType>(() => MakeTileType("Forest",
                EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
                EdgeType.Forest, EdgeType.Forest, EdgeType.Forest));

            var coordCenter = HexCoord.Zero;
            var coordN0     = coordCenter.Neighbor(0);
            var coordN1     = coordCenter.Neighbor(1);

            var tileN0     = MakePlacedTile(coordN0, forest());
            var tileN1     = MakePlacedTile(coordN1, forest());
            var tileCenter = MakePlacedTile(coordCenter, forest());

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile>
            {
                [coordCenter] = tileCenter,
                [coordN0]     = tileN0,
                [coordN1]     = tileN1,
            });

            var events = CaptureConnectedEvents(() => InvokeCheckAndApplyConnections(mgr, coordCenter));

            Assert.AreEqual(1, events.Count, "1回の配置につきTileConnectedEventは1つだけ");
            Assert.AreEqual(2, events[0].Edges.Count, "2方向とも接続として1回ずつ数えられる");
        }

        // ── legacy TileType（elements未設定）・Village ────────────────

        [Test]
        public void WorksWithLegacyTileType_ElementsNotSet()
        {
            var riverA = MakeTileType("River", EdgeType.River, EdgeType.Field, EdgeType.Field,
                                                 EdgeType.Field, EdgeType.Field, EdgeType.Field);
            var riverB = MakeTileType("River", EdgeType.Field, EdgeType.Field, EdgeType.Field,
                                                 EdgeType.River, EdgeType.Field, EdgeType.Field);
            Assert.IsNull(riverA.elements);
            Assert.IsNull(riverB.elements);

            var coordA = HexCoord.Zero;
            var coordB = HexCoord.Zero.Neighbor(0);
            var tileA  = MakePlacedTile(coordA, riverA);
            var tileB  = MakePlacedTile(coordB, riverB);

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile> { [coordA] = tileA, [coordB] = tileB });
            InvokeCheckAndApplyConnections(mgr, coordB);

            Assert.IsTrue(tileB.IsRiverEdgeOpen(3));
        }

        [Test]
        public void VillageCategory_NeverOpensRiver()
        {
            // VillageはEdgeTypeに対応しないため、TryGetConnectedCategoryがVillageを返すことはない
            // （Session 2Aで保証済み）。ここではHexGridManager経由でも川が開かないことだけ確認する。
            var villageA = MakeTileType("Village", EdgeType.Road, EdgeType.Field, EdgeType.Field,
                                                     EdgeType.Field, EdgeType.Field, EdgeType.Field);
            var villageB = MakeTileType("Village", EdgeType.Field, EdgeType.Field, EdgeType.Field,
                                                     EdgeType.Road, EdgeType.Field, EdgeType.Field);

            var coordA = HexCoord.Zero;
            var coordB = HexCoord.Zero.Neighbor(0);
            var tileA  = MakePlacedTile(coordA, villageA);
            var tileB  = MakePlacedTile(coordB, villageB);

            var mgr = MakeManagerWithGrid(new Dictionary<HexCoord, HexTile> { [coordA] = tileA, [coordB] = tileB });
            InvokeCheckAndApplyConnections(mgr, coordB);

            Assert.IsFalse(tileB.IsRiverEdgeOpen(3));
            Assert.IsFalse(tileA.IsRiverEdgeOpen(0));
        }
    }
}
