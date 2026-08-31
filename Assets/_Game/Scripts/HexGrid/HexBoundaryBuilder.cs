// 役割: タイル群の「外周」を、1個以上の閉じた輪として求める純粋関数。
//       座標のつながりだけを見る。タイルの種類・カテゴリ・クエスト・見た目には一切依存しない。
//
//       手順:
//         1. 対象を集合にする（重複は自然に落ちる）
//         2. 各タイルの6辺のうち、隣が対象に入っていない辺だけを外周辺として拾う
//         3. 外周辺を辿って閉じた輪にする
//         4. 輪の包含関係から外周か穴かを判定し、外周は反時計回り・穴は時計回りへ揃える
//
//       ★辿るときに「角を共有しているか」で次の辺を選ばない。
//         六角形では、辺を共有せず角だけで接する2枚（斜め隣）が存在する。
//         角のIDで繋ぐと、離れた2つの領域が1本の自己交差した輪へ誤って繋がる。
//         ここでは常に「辺を共有するタイルへ乗り換える」形で辿るので、
//         角だけで接する領域どうしは別の輪になる。
//
//       ★外周か穴かをsigned areaの符号だけで決めない。
//         離れ小島は2つとも外周なので、符号だけでは区別できない。
//         輪の中に入る点を使って「他の輪にいくつ含まれているか」を数え、
//         偶数なら外周・奇数なら穴とする。これなら穴の中の島まで同じ規則で扱える。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.HexGrid
{
    public static class HexBoundaryBuilder
    {
        /// <summary>外周辺1本。所属タイルと、その外側を向いている方向。</summary>
        private readonly struct BoundaryEdge
        {
            public readonly HexCoord Tile;
            public readonly int      Direction;

            public BoundaryEdge(HexCoord tile, int direction)
            {
                Tile      = tile;
                Direction = direction;
            }

            /// <summary>この辺の始点の角index。辺は始点→始点+1の向き（上から見て反時計回り）。</summary>
            public int StartCornerIndex => ((-Direction % 6) + 6) % 6;

            public HexCorner StartCorner => HexCorner.Of(Tile, StartCornerIndex);

            public bool Equals(BoundaryEdge other) => Tile == other.Tile && Direction == other.Direction;
            public override bool Equals(object obj) => obj is BoundaryEdge e && Equals(e);
            public override int GetHashCode() => System.HashCode.Combine(Tile, Direction);
        }

        /// <summary>
        /// タイル群の外周を、1個以上の閉じた輪として返す。
        /// 輪は角の並びで、隣り合う角どうしが必ず1辺分だけ離れている。
        /// tilesがnullや空なら空の結果を返す（HexCoordは値型なので、要素がnullになることはない）。
        /// </summary>
        public static List<List<HexCorner>> BuildLoops(IEnumerable<HexCoord> tiles)
        {
            var loops = new List<List<HexCorner>>();
            if (tiles == null) return loops;

            var set = new HashSet<HexCoord>(tiles);
            if (set.Count == 0) return loops;

            // ★HashSetの列挙順に結果が左右されないよう、必ず並べ替えてから辿る。
            //   輪の開始点が毎回変わると、光が走り始める位置も毎回変わってしまう
            var ordered = new List<HexCoord>(set);
            ordered.Sort(CompareCoord);

            var visited   = new HashSet<BoundaryEdge>();
            int edgeLimit = set.Count * 6;   // 無限ループ避け（外周辺は必ずこれ以下）

            foreach (var tile in ordered)
            {
                for (int d = 0; d < 6; d++)
                {
                    var start = new BoundaryEdge(tile, d);
                    if (set.Contains(tile.Neighbor(d))) continue;  // 共有辺は外周ではない
                    if (visited.Contains(start)) continue;         // 別の輪で消費済み

                    loops.Add(TraceLoop(start, set, visited, edgeLimit));
                }
            }

            NormalizeLoops(loops);
            return loops;
        }

        // ── 輪を辿る ──────────────────────────────────────────────────

        private static List<HexCorner> TraceLoop(BoundaryEdge start, HashSet<HexCoord> set,
                                                  HashSet<BoundaryEdge> visited, int edgeLimit)
        {
            var loop    = new List<HexCorner>();
            var current = start;

            for (int guard = 0; guard <= edgeLimit; guard++)
            {
                visited.Add(current);
                loop.Add(current.StartCorner);

                current = NextEdge(current, set);
                if (current.Equals(start)) return loop;
            }

            // ここへ来るのは辿り方が壊れているとき。黙って半端な輪を返さない
            Debug.LogError("[HexBoundaryBuilder] 外周が閉じませんでした。辿り方を確認してください。");
            return loop;
        }

        /// <summary>
        /// 終点の角を軸に、次の外周辺を求める。
        /// 同じタイルで1つ隣の辺が外周ならそれを使い、内側なら辺を共有するタイルへ乗り換える。
        /// 乗り換えは「辺の共有」でしか起きないので、角だけで接する別領域へ飛び移ることはない。
        /// </summary>
        private static BoundaryEdge NextEdge(BoundaryEdge edge, HashSet<HexCoord> set)
        {
            HexCoord tile = edge.Tile;
            int      dir  = (edge.Direction + 5) % 6;   // 反時計回りに1つ進む

            // 角には最大3枚しか集まらないので乗り換えは高々1回。念のため回数を抑える
            for (int guard = 0; guard < 3 && set.Contains(tile.Neighbor(dir)); guard++)
            {
                tile = tile.Neighbor(dir);
                dir  = (dir + 2) % 6;
            }

            return new BoundaryEdge(tile, dir);
        }

        // ── 向きと並びを揃える ────────────────────────────────────────

        private static void NormalizeLoops(List<List<HexCorner>> loops)
        {
            var polygons = new List<Vector2[]>(loops.Count);
            foreach (var loop in loops) polygons.Add(ToPolygon(loop));

            for (int i = 0; i < loops.Count; i++)
            {
                // 他の輪にいくつ含まれているかで、外周か穴かを決める。
                // 偶数なら外周（離れ小島も、穴の中の島も外周）、奇数なら穴
                Vector2 inside = PointInside(loops[i], polygons[i]);
                int depth = 0;
                for (int j = 0; j < loops.Count; j++)
                    if (j != i && Contains(polygons[j], inside)) depth++;

                bool wantCounterClockwise = depth % 2 == 0;
                if (SignedArea(polygons[i]) > 0f != wantCounterClockwise)
                    loops[i].Reverse();

                RotateToSmallestCornerFirst(loops[i]);
            }

            // 輪の並びも安定させる。Stage 3で光を走らせる順番が毎回同じになるように
            loops.Sort((a, b) => a[0].CompareTo(b[0]));
        }

        /// <summary>先頭を「いちばん小さい角」に回す。輪は循環なので、向きは変えずに開始点だけ動かす。</summary>
        private static void RotateToSmallestCornerFirst(List<HexCorner> loop)
        {
            int min = 0;
            for (int i = 1; i < loop.Count; i++)
                if (loop[i].CompareTo(loop[min]) < 0) min = i;

            if (min == 0) return;

            var rotated = new List<HexCorner>(loop.Count);
            for (int i = 0; i < loop.Count; i++) rotated.Add(loop[(min + i) % loop.Count]);

            loop.Clear();
            loop.AddRange(rotated);
        }

        // ── 幾何（XZ平面。tileSize=1で十分。向きと包含は大きさに依らない） ──

        private static Vector2[] ToPolygon(List<HexCorner> loop)
        {
            var polygon = new Vector2[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 p = loop[i].ToWorldPosition();
                polygon[i] = new Vector2(p.x, p.z);
            }
            return polygon;
        }

        /// <summary>上から見て反時計回りなら正。</summary>
        private static float SignedArea(Vector2[] polygon)
        {
            float sum = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                sum += a.x * b.y - b.x * a.y;
            }
            return sum * 0.5f;
        }

        /// <summary>
        /// その輪の内側にある点を1つ返す。
        /// タイルの中心は必ずタイルの辺・角の上に乗らないので、包含判定が境界上で揺れない。
        /// 外周の輪ならタイル自身の中心が内側、穴の輪なら外側にある空きタイルの中心が内側になる。
        /// </summary>
        private static Vector2 PointInside(List<HexCorner> loop, Vector2[] polygon)
        {
            // 輪の最初の角に集まる3タイルのうち、内側にある中心を選ぶ
            HexCorner corner = loop[0];
            foreach (var tile in new[] { corner.A, corner.B, corner.C })
            {
                Vector3 p = tile.ToWorldPosition();
                var     v = new Vector2(p.x, p.z);
                if (Contains(polygon, v)) return v;
            }

            // 3枚とも外側になることは無い（角は必ず輪の上にあるため）が、
            // 万一のときは重心を返して判定を続ける
            Vector2 centroid = Vector2.zero;
            foreach (var v in polygon) centroid += v;
            return centroid / polygon.Length;
        }

        /// <summary>多角形の内側か（レイキャスト法）。判定点はタイル中心なので境界上には来ない。</summary>
        private static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if (a.y > point.y != b.y > point.y &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static int CompareCoord(HexCoord a, HexCoord b)
        {
            if (a.q != b.q) return a.q < b.q ? -1 : 1;
            if (a.r != b.r) return a.r < b.r ? -1 : 1;
            return 0;
        }
    }
}
