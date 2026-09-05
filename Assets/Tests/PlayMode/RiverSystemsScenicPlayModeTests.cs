// 役割: 景観川（RiverForest / RiverFlower）が、既存Riverとまったく同じように
//       川の各システムから扱われることを、実際にイベントを流して検証する。
//
//       ★背景
//         これらのシステムは以前 Scene の TileType[] に登録されたアセット参照で
//         川を判定しており、景観川6種を追加したとき4システムすべてで登録漏れが起き、
//         「川を繋いでも流れが出ない」不具合になった。
//         判定を TileType.HasCategory(River) へ揃えたので、
//         そのことを見た目ではなく挙動として固定する。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class RiverSystemsScenicPlayModeTests
    {
        private const string Dir = "Assets/_Game/ScriptableObjects/TileDefinitions/";

        /// <summary>ラベル | 配置タイル | 隣接タイル。既存×景観・景観×景観を網羅する。</summary>
        public static readonly string[] ConnectionCases =
        {
            "既存×景観森-Straight|TileType_River_Straight|TileType_RiverForest_Straight",
            "既存×景観森-Bend|TileType_River_Bend|TileType_RiverForest_Bend",
            "既存×景観森-WideBend|TileType_River_Wide_Bend|TileType_RiverForest_WideBend",
            "既存×景観花-Straight|TileType_River_Straight|TileType_RiverFlower_Straight",
            "既存×景観花-Bend|TileType_River_Bend|TileType_RiverFlower_Bend",
            "既存×景観花-WideBend|TileType_River_Wide_Bend|TileType_RiverFlower_WideBend",
            "景観×景観-森花|TileType_RiverForest_Straight|TileType_RiverFlower_Straight",
            "景観×景観-森森|TileType_RiverForest_Bend|TileType_RiverForest_Bend",
            "既存×既存(回帰)|TileType_River_Straight|TileType_River_Straight",
        };

        private readonly List<Object> _spawned = new();
        private GameObject _hostGo;

        [TearDown]
        public void TearDown()
        {
            if (_hostGo != null) { Object.DestroyImmediate(_hostGo); _hostGo = null; }
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private T Track<T>(T o) where T : Object { _spawned.Add(o); return o; }

        private static TileType Load(string name)
        {
            TileType t = null;
#if UNITY_EDITOR
            t = UnityEditor.AssetDatabase.LoadAssetAtPath<TileType>(Dir + name + ".asset");
#endif
            Assert.IsNotNull(t, name + " が見つからない");
            return t;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, field + " が見つからない");
            f.SetValue(target, value);
        }

        /// <summary>実配置と同じ経路（HexTile.Place）で1枚作る。</summary>
        private HexTile BuildTile(TileType type, HexCoord coord, int rotation = 0)
        {
            var go = Track(new GameObject("Tile" + coord));
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")
                                                    ?? Shader.Find("Standard")));
            var tile = go.AddComponent<HexTile>();
            SetPrivate(tile, "meshFilter",   mf);
            SetPrivate(tile, "meshRenderer", mr);
            tile.Initialize(coord, 1f);
            tile.Place(type, rotation);
            return tile;
        }

        // ★川3形状はどれもローカル dir0 が River 辺（Straight=0/3, Bend=0/5, WideBend=0/4）。
        //   TileData.GetEdge(d) は tileType.GetEdge(d - rotation) なので、
        //   A を rotation 0 で置けば A.GetEdge(0)   = local 0 = River、
        //   B を rotation 3 で置けば B.GetEdge(3)   = local 0 = River となり、
        //   形状に関係なく dir0 で向かい合う接続を作れる。
        private const int RotationA = 0;
        private const int RotationB = 3;

        /// <summary>WaterPS（タイル直下の子）が再生状態か。RiverFlowSystem が制御する対象。</summary>
        private static bool IsWaterFlowing(HexTile tile)
        {
            for (int i = 0; i < tile.transform.childCount; i++)
            {
                var c = tile.transform.GetChild(i);
                if (c.name == "WaterPS" && c.gameObject.activeSelf) return true;
            }
            return false;
        }

        private static int CountWaterPS(HexTile tile)
        {
            int n = 0;
            for (int i = 0; i < tile.transform.childCount; i++)
                if (tile.transform.GetChild(i).name == "WaterPS") n++;
            return n;
        }

        // ══ 1〜5. 流れの確立 ═════════════════════════════════════════════

        [UnityTest]
        public IEnumerator ConnectingRivers_StartsTheFlow(
            [ValueSource(nameof(ConnectionCases))] string testCase)
        {
            var parts    = testCase.Split('|');
            string label = parts[0];
            var typeA    = Load(parts[1]);
            var typeB    = Load(parts[2]);

            _hostGo = new GameObject("RiverFlowSystem");
            _hostGo.AddComponent<RiverFlowSystem>();      // OnEnable で購読開始
            yield return null;

            // dir0 と dir3 で向かい合う2枚。両方とも dir0 が River 辺。
            var coordA = new HexCoord(0, 0);
            var coordB = coordA.Neighbor(0);
            var tileA  = BuildTile(typeA, coordA, RotationA);
            var tileB  = BuildTile(typeB, coordB, RotationB);
            yield return null;

            Assert.Greater(CountWaterPS(tileA), 0, $"{label}: 前提としてWaterPSが生成されている");

            // 1枚目を配置（孤立）→ 流れは止まる
            EventBus.Publish(new TilePlacedEvent(tileB, typeB, coordB));
            yield return null;
            Assert.IsFalse(IsWaterFlowing(tileB), $"{label}: 孤立配置なのに流れている");

            // 2枚目を配置し、接続を通知
            EventBus.Publish(new TilePlacedEvent(tileA, typeA, coordA));
            yield return null;

            int dirAtoB = 0;
            Assert.AreEqual(EdgeType.River, tileA.Data.GetEdge(dirAtoB), $"{label}: A の dir0 が River 辺でない");
            Assert.AreEqual(EdgeType.River, tileB.Data.GetEdge(3),        $"{label}: B の dir3 が River 辺でない");

            EventBus.Publish(new TileConnectedEvent(tileA, typeA,
                new List<ConnectionEdge> { new ConnectionEdge(dirAtoB, tileB) }));
            yield return null;

            // ★本題: 両方の水流が再開していること
            Assert.IsTrue(IsWaterFlowing(tileA), $"{label}: 配置側の流れが確立していない");
            Assert.IsTrue(IsWaterFlowing(tileB), $"{label}: 隣接側の流れが確立していない");
        }

        /// <summary>
        /// WaterPS の「向きの状態」を1本の文字列へ畳む。
        /// ★ReverseWaterFlow は transform.forward ではなく velocityOverLifetime を反転するため、
        ///   GetWaterFlowDir() だけを見ても反転済みかどうかは分からない。
        ///   forward と速度の符号の両方を取る必要がある。
        /// </summary>
        private static string WaterFlowState(HexTile tile)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < tile.transform.childCount; i++)
            {
                var c = tile.transform.GetChild(i);
                if (c.name != "WaterPS") continue;
                var ps = c.GetComponent<ParticleSystem>();
                if (ps == null) continue;

                var vel = ps.velocityOverLifetime;
                sb.Append("active=").Append(c.gameObject.activeSelf)
                  .Append(" fwd=").Append(c.forward.ToString("F3"))
                  .Append(" vx=").Append(vel.x.constantMin.ToString("F3")).Append("/").Append(vel.x.constantMax.ToString("F3"))
                  .Append(" vz=").Append(vel.z.constantMin.ToString("F3")).Append("/").Append(vel.z.constantMax.ToString("F3"))
                  .Append(" | ");
            }
            return sb.ToString();
        }

        [UnityTest]
        public IEnumerator WaterFlowState_MatchesThePlainRiverBaseline(
            [ValueSource(nameof(ConnectionCases))] string testCase)
        {
            // 5. WaterPS の向き・停止・再開が既存Riverとまったく同じであること。
            // ★絶対的な向きを主張するのではなく、同じ形状の既存River同士で作った基準と
            //   一致することを見る。景観川で挙動が変わっていないことが確かめたい点なので、
            //   基準との一致こそが正しい観測になる。
            var parts    = testCase.Split('|');
            string label = parts[0];
            var typeA    = Load(parts[1]);
            var typeB    = Load(parts[2]);

            // 対応する「素の川」を選ぶ（形状はローカル辺の並びで決まるので同形状を使う）
            var plainA = Load(PlainCounterpart(parts[1]));
            var plainB = Load(PlainCounterpart(parts[2]));

            string baselineA = null, baselineB = null;
            yield return RunConnection(plainA, plainB, s => { baselineA = s.Item1; baselineB = s.Item2; });

            string actualA = null, actualB = null;
            yield return RunConnection(typeA, typeB, s => { actualA = s.Item1; actualB = s.Item2; });

            Assert.AreEqual(baselineA, actualA, $"{label}: 配置側のWaterPS状態が素の川と違う");
            Assert.AreEqual(baselineB, actualB, $"{label}: 隣接側のWaterPS状態が素の川と違う");
        }

        /// <summary>景観川アセット名 → 同じ形状の素の川アセット名。</summary>
        private static string PlainCounterpart(string name)
        {
            if (name.Contains("Straight")) return "TileType_River_Straight";
            if (name.Contains("WideBend")) return "TileType_River_Wide_Bend";
            if (name.Contains("Wide_Bend")) return "TileType_River_Wide_Bend";
            return "TileType_River_Bend";
        }

        /// <summary>2枚を接続し、双方のWaterPS状態を取り出してから後片付けする。</summary>
        private IEnumerator RunConnection(TileType typeA, TileType typeB,
                                           System.Action<System.Tuple<string, string>> onResult)
        {
            _hostGo = new GameObject("RiverFlowSystem");
            _hostGo.AddComponent<RiverFlowSystem>();
            yield return null;

            var coordA = new HexCoord(0, 0);
            var coordB = coordA.Neighbor(0);
            var tileA  = BuildTile(typeA, coordA, RotationA);
            var tileB  = BuildTile(typeB, coordB, RotationB);
            yield return null;

            EventBus.Publish(new TilePlacedEvent(tileB, typeB, coordB));
            EventBus.Publish(new TilePlacedEvent(tileA, typeA, coordA));
            yield return null;
            EventBus.Publish(new TileConnectedEvent(tileA, typeA,
                new List<ConnectionEdge> { new ConnectionEdge(0, tileB) }));
            yield return null;

            onResult(System.Tuple.Create(WaterFlowState(tileA), WaterFlowState(tileB)));
            TearDown();
        }

        // ══ 6〜7. 川クラスターと魚 ═══════════════════════════════════════

        /// <summary>_grid をリフレクションで埋めた HexGridManager（Awake/Start は走らせない）。</summary>
        private HexGridManager BuildGridManager(Dictionary<HexCoord, HexTile> tiles)
        {
            var go = Track(new GameObject("HexGridManager"));
            go.SetActive(false);                       // Start を走らせない（プレハブ生成を避ける）
            var mgr = go.AddComponent<HexGridManager>();
            var f   = typeof(HexGridManager).GetField("_grid", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "_grid が見つからない");
            var grid = (Dictionary<HexCoord, HexTile>)f.GetValue(mgr);
            foreach (var kv in tiles) grid[kv.Key] = kv.Value;
            return mgr;
        }

        /// <summary>一直線に count 枚の川を並べ、座標→タイルの辞書を返す。</summary>
        private Dictionary<HexCoord, HexTile> BuildRiverLine(string[] typeNames)
        {
            var map = new Dictionary<HexCoord, HexTile>();
            var c   = new HexCoord(0, 0);
            for (int i = 0; i < typeNames.Length; i++)
            {
                map[c] = BuildTile(Load(typeNames[i]), c);
                c = c.Neighbor(0);
            }
            return map;
        }

        [UnityTest]
        public IEnumerator ScenicRivers_CountTowardTheRiverCluster()
        {
            // 7. 魚の発生条件（既定 8枚）が、景観川を混ぜても成立すること。
            // ★一直線に並べるので直線だけを使う。曲がり・緩カーブは向かい合う辺がRiverにならず、
            //   水路が繋がらないので1本の川として数えられない（StraightRiverLine のコメント参照）。
            var names = new[] {
                "TileType_River_Straight",        "TileType_RiverForest_Straight",
                "TileType_RiverFlower_Straight",  "TileType_River_Straight",
                "TileType_RiverForest_Straight",  "TileType_RiverFlower_Straight",
                "TileType_River_Straight",        "TileType_RiverForest_Straight" };

            var map = BuildRiverLine(names);
            var mgr = BuildGridManager(map);

            _hostGo = new GameObject("RiverGrowthEvaluator");
            var eval = _hostGo.AddComponent<RiverGrowthEvaluator>();
            SetPrivate(eval, "_gridManager", mgr);
            SetPrivate(eval, "_threshold", 8);
            _hostGo.SetActive(true);
            yield return null;

            RiverClusterEvent received = null;
            System.Action<RiverClusterEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                var last = new HexCoord(0, 0);
                foreach (var kv in map) last = kv.Key;   // どの座標から評価しても同じクラスターになる
                EventBus.Publish(new TilePlacedEvent(map[last], map[last].Data.tileType, last));
                yield return null;

                Assert.IsNotNull(received, "8枚繋がっているのに RiverClusterEvent が発行されない");
                Assert.AreEqual(8, received.Tiles.Count, "クラスター枚数が想定と違う（景観川が数えられていない）");
            }
            finally { EventBus.Unsubscribe(handler); }
        }

        [UnityTest]
        public IEnumerator RiverCluster_DoesNotCountRiversThatAreOnlyAdjacent()
        {
            // ★クエスト「川を3枚つなげよう」と魚の閾値が見ているクラスターも、
            //   隣にあるだけの川を数えない。
            //   3枚目に曲がりを回転させずに挟むと、その dir3 は Field なので
            //   2枚目との水路は繋がらない。手前2枚だけのクラスターになる。
            var names = new[] {
                "TileType_River_Straight", "TileType_River_Straight", "TileType_River_Bend",
                "TileType_River_Straight", "TileType_River_Straight" };

            var map = BuildRiverLine(names);
            var mgr = BuildGridManager(map);

            _hostGo = new GameObject("RiverGrowthEvaluator");
            var eval = _hostGo.AddComponent<RiverGrowthEvaluator>();
            SetPrivate(eval, "_gridManager", mgr);
            SetPrivate(eval, "_threshold", 8);
            _hostGo.SetActive(true);
            yield return null;

            TerrainGrowthEvent<RiverGrowthMetrics> received = null;
            System.Action<TerrainGrowthEvent<RiverGrowthMetrics>> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                var start = new HexCoord(0, 0);
                EventBus.Publish(new TilePlacedEvent(map[start], map[start].Data.tileType, start));
                yield return null;

                Assert.IsNotNull(received, "進捗イベントは閾値と無関係に毎回出るはず");
                Assert.AreEqual(2, received.Metrics.LargestClusterSize,
                    "水路が途切れているのに1本の川として数えている");
            }
            finally { EventBus.Unsubscribe(handler); }
        }

        [UnityTest]
        public IEnumerator NonRiverTiles_AreNotCountedInTheCluster()
        {
            // 12. River以外を River と誤判定しないこと。
            var map = new Dictionary<HexCoord, HexTile>();
            var c   = new HexCoord(0, 0);
            var names = new[] {
                "TileType_RiverForest_Straight", "TileType_RiverFlower_Straight", "TileType_River_Straight",
                "TileType_Forest",               // ★ここでクラスターが途切れるはず
                "TileType_River_Straight",       "TileType_RiverForest_Bend" };
            foreach (var n in names) { map[c] = BuildTile(Load(n), c); c = c.Neighbor(0); }

            var mgr = BuildGridManager(map);
            _hostGo = new GameObject("RiverGrowthEvaluator");
            var eval = _hostGo.AddComponent<RiverGrowthEvaluator>();
            SetPrivate(eval, "_gridManager", mgr);
            SetPrivate(eval, "_threshold", 1);        // 必ず発行させて枚数を見る
            _hostGo.SetActive(true);
            yield return null;

            RiverClusterEvent received = null;
            System.Action<RiverClusterEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                var start = new HexCoord(0, 0);
                EventBus.Publish(new TilePlacedEvent(map[start], map[start].Data.tileType, start));
                yield return null;

                Assert.IsNotNull(received, "RiverClusterEvent が発行されない");
                Assert.AreEqual(3, received.Tiles.Count,
                    "森タイルでクラスターが途切れていない（Riverでないものを数えている）");
            }
            finally { EventBus.Unsubscribe(handler); }
        }

        // ══ 8. 橋 ═══════════════════════════════════════════════════════

        // ★一直線に並べるので、どのタイルも dir0 と dir3 の両方が River でなければ繋がらない。
        //   その条件を満たすのは直線だけ（曲がり・緩カーブは辺の組み合わせが違う）。
        //   曲がりを混ぜたい場合は、向かい合う辺が River になるよう回転を組む必要がある。
        private static readonly string[] StraightRiverLine = {
            "TileType_RiverForest_Straight", "TileType_RiverFlower_Straight", "TileType_River_Straight",
            "TileType_RiverForest_Straight", "TileType_RiverFlower_Straight" };

        private RiverBridgeEvaluator BuildBridgeEvaluator(Dictionary<HexCoord, HexTile> map)
        {
            var mgr = BuildGridManager(map);
            _hostGo = new GameObject("RiverBridgeEvaluator");
            var eval = _hostGo.AddComponent<RiverBridgeEvaluator>();
            SetPrivate(eval, "_gridManager", mgr);
            SetPrivate(eval, "_interval", 5);
            _hostGo.SetActive(true);
            return eval;
        }

        [UnityTest]
        public IEnumerator ScenicRivers_TriggerTheBridgeEvaluator()
        {
            // 景観川だけを5枚並べても橋の節目に到達すること。
            var map = BuildRiverLine(StraightRiverLine);
            BuildBridgeEvaluator(map);
            yield return null;

            RiverBridgeEvent received = null;
            System.Action<RiverBridgeEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                var start = new HexCoord(0, 0);
                EventBus.Publish(new TilePlacedEvent(map[start], map[start].Data.tileType, start));
                yield return null;

                Assert.IsNotNull(received, "景観川5枚で RiverBridgeEvent が発行されない");
                Assert.AreEqual(5, received.ClusterSize, "クラスター枚数が想定と違う");
            }
            finally { EventBus.Unsubscribe(handler); }
        }

        [UnityTest]
        public IEnumerator Bridge_IsNotBuiltOnASharpBend()
        {
            // ★節目に到達した1枚が曲がりでも、橋そのものは失わない。
            //   同じ川の中の直線へ架け替える。
            //   ここで発行を見送ると、橋を待っているクエストがそのぶん足踏みする。
            var names = new[] {
                "TileType_River_Bend",      "TileType_River_Straight", "TileType_River_Straight",
                "TileType_River_Straight",  "TileType_River_Straight" };

            var map = BuildRiverLine(names);
            BuildBridgeEvaluator(map);
            yield return null;

            RiverBridgeEvent received = null;
            System.Action<RiverBridgeEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                var start = new HexCoord(0, 0);
                EventBus.Publish(new TilePlacedEvent(map[start], map[start].Data.tileType, start));
                yield return null;

                Assert.IsNotNull(received, "曲れが節目でも橋は架かるはず");
                Assert.AreNotSame(map[start], received.BridgeTile, "曲がりのタイルへ橋が架かっている");

                var picked = received.BridgeTile.Data;
                Assert.IsTrue(RiverChannelLayout.CanHostBridge(picked.tileType, picked.coord.q, picked.coord.r, picked.coord.s),
                    "橋を架けられない形状のタイルが選ばれている");
                Assert.AreEqual(new HexCoord(0, 0).Neighbor(0), picked.coord,
                    "置いたタイルにいちばん近い直線が選ばれていない");
            }
            finally { EventBus.Unsubscribe(handler); }
        }

        [UnityTest]
        public IEnumerator Bridge_IsNotBuiltTwiceOnTheSameTile()
        {
            var map = BuildRiverLine(StraightRiverLine);
            BuildBridgeEvaluator(map);
            yield return null;

            var received = new List<RiverBridgeEvent>();
            System.Action<RiverBridgeEvent> handler = e => received.Add(e);
            EventBus.Subscribe(handler);
            try
            {
                var start = new HexCoord(0, 0);
                EventBus.Publish(new TilePlacedEvent(map[start], map[start].Data.tileType, start));
                yield return null;
                EventBus.Publish(new TilePlacedEvent(map[start], map[start].Data.tileType, start));
                yield return null;

                Assert.AreEqual(2, received.Count, "2回とも節目に到達しているはず");
                Assert.AreNotSame(received[0].BridgeTile, received[1].BridgeTile,
                    "同じタイルへ2本目の橋が架かっている");
            }
            finally { EventBus.Unsubscribe(handler); }
        }

        [UnityTest]
        public IEnumerator Bridge_DoesNotCountRiversThatAreOnlyAdjacent()
        {
            // ★隣にあるだけの川は数えない。
            //   3枚目に曲がりを回転させずに挟むと、その dir3 は Field なので
            //   2枚目との水路は繋がらない。手前2枚だけのクラスターになり、節目に届かない。
            var names = new[] {
                "TileType_River_Straight", "TileType_River_Straight", "TileType_River_Bend",
                "TileType_River_Straight", "TileType_River_Straight" };

            var map = BuildRiverLine(names);
            BuildBridgeEvaluator(map);
            yield return null;

            RiverBridgeEvent received = null;
            System.Action<RiverBridgeEvent> handler = e => received = e;
            EventBus.Subscribe(handler);
            try
            {
                var start = new HexCoord(0, 0);
                EventBus.Publish(new TilePlacedEvent(map[start], map[start].Data.tileType, start));
                yield return null;

                Assert.IsNull(received, "水路が途切れているのに1本の川として数えている");
            }
            finally { EventBus.Unsubscribe(handler); }
        }

        // ※ 森×川シナジーの登録確認は Phase1_v002 の設定を見る必要があるため
        //   RiverCategoryJudgmentTests（EditMode）側で行う。
        //   PlayModeテストは専用の一時シーンで走るので、実シーンの設定を読めない。

        // ══ 11. 既存Riverの回帰 ═════════════════════════════════════════

        [UnityTest]
        public IEnumerator PlainRivers_KeepTheirExistingBehaviour()
        {
            // 既存River同士でも、これまでどおり孤立で停止 → 接続で再開する。
            _hostGo = new GameObject("RiverFlowSystem");
            _hostGo.AddComponent<RiverFlowSystem>();
            yield return null;

            var type   = Load("TileType_River_Straight");
            var coordA = new HexCoord(0, 0);
            var coordB = coordA.Neighbor(0);
            var tileA  = BuildTile(type, coordA);
            var tileB  = BuildTile(type, coordB);
            yield return null;

            EventBus.Publish(new TilePlacedEvent(tileA, type, coordA));
            yield return null;
            Assert.IsFalse(IsWaterFlowing(tileA), "孤立した既存Riverが流れている");

            EventBus.Publish(new TilePlacedEvent(tileB, type, coordB));
            yield return null;
            EventBus.Publish(new TileConnectedEvent(tileB, type,
                new List<ConnectionEdge> { new ConnectionEdge(3, tileA) }));
            yield return null;

            Assert.IsTrue(IsWaterFlowing(tileA), "既存River同士で流れが確立しない（回帰）");
            Assert.IsTrue(IsWaterFlowing(tileB), "既存River同士で流れが確立しない（回帰）");
        }

        [UnityTest]
        public IEnumerator NonRiverTile_IsIgnoredByTheFlowSystem()
        {
            // 12. 森タイルを配置しても RiverFlowSystem は反応しない。
            _hostGo = new GameObject("RiverFlowSystem");
            _hostGo.AddComponent<RiverFlowSystem>();
            yield return null;

            var forest = Load("TileType_Forest");
            var coord  = new HexCoord(0, 0);
            var tile   = BuildTile(forest, coord);
            yield return null;

            EventBus.Publish(new TilePlacedEvent(tile, forest, coord));
            yield return null;

            Assert.AreEqual(0, CountWaterPS(tile), "森タイルに WaterPS が生えている");
            Assert.DoesNotThrow(() => EventBus.Publish(new TileConnectedEvent(tile, forest,
                new List<ConnectionEdge>())), "森タイルの接続で例外が出る");
        }
    }
}
