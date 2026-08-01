// 役割: 川の流路（中心線と幅）に関する幾何計算を、純粋関数として1箇所へ集める。
//       MonoBehaviour・UnityEngine.Randomに依存せず、同じ入力からは常に同じ結果を返す。
//
//       ★なぜ切り出すのか
//         溝の深さを決める計算（RiverChannelMeshBuilder）と、
//         「木や花を川へ生やさない」ための除外判定は、同じ中心線・同じ幅を見なければならない。
//         別々に実装すると、川幅をひとつ変えただけで木が水に立つ／岸が不自然に空く、
//         といった食い違いが静かに発生する。
//         そこで中心線までの距離と流路の半幅をここに集約し、
//         メッシュ側も配置側も必ずここを通るようにする。
//
//       ★形状で分岐しない
//         直線・曲がり・緩カーブの3種は、制御点が常にタイル中心（原点）の
//         2次ベジェで表せる。違いは端点2つだけなので、形状別の関数は持たない。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class RiverChannelLayout
    {
        /// <summary>outerRadius比。流路の半幅（＝川岸ラインが置かれる位置）。</summary>
        // 外部へはChannelHalfWidth()経由でのみ公開する。比率そのものを配ると、
        // 呼び出し側が独自にouterRadiusを掛け直すことになり、式が分かれるため。
        private const float HalfWidthRatio = 0.25f;

        /// <summary>中心線を折れ線で近似するときの分割数。</summary>
        private const int CurveSamples = 24;

        /// <summary>方向インデックス(0〜5) → XZ平面での世界角度（度）。HexTile.EdgeCenterと同じ規則。</summary>
        private static readonly float[] s_DirToWorldAngle = { 30f, 330f, 270f, 210f, 150f, 90f };

        /// <summary>
        /// 流路の半幅。溝メッシュの壁の位置であり、川岸ラインの位置でもある。
        /// 木や花を避けさせる距離は、これに各プロップの余白を足して決める。
        /// </summary>
        public static float ChannelHalfWidth(float outerRadius) => outerRadius * HalfWidthRatio;

        /// <summary>2次ベジェ。流路の中心線はこの曲線ひとつで表される。</summary>
        public static Vector3 QuadBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float mt = 1f - t;
            return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
        }

        /// <summary>dir方向の辺の中心（タイルローカル座標）。</summary>
        public static Vector3 EdgeCenter(int dir, float outerRadius)
        {
            float inRadius = outerRadius * 0.866f;
            float angle    = s_DirToWorldAngle[((dir % 6) + 6) % 6] * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * inRadius, 0f, Mathf.Sin(angle) * inRadius);
        }

        /// <summary>点pから流路中心線までの最短距離（XZ平面）。</summary>
        public static float DistanceToCenterline(Vector3 p, Vector3 edgeA, Vector3 ctrl, Vector3 edgeB)
            => DistanceToCenterline(p, edgeA, ctrl, edgeB, out _);

        /// <summary>
        /// 点pから流路中心線までの最短距離と、その最近傍点の曲線パラメータt(0〜1)。
        ///
        /// ★曲線をCurveSamples本の線分に近似し、線分単位で射影する。
        ///   「最も近いサンプル点」だけを拾う方式だと、境界付近でtと距離の対応が不連続になり、
        ///   溝の深さが局所的に跳ねるフェースが出る。
        /// </summary>
        public static float DistanceToCenterline(Vector3 p, Vector3 edgeA, Vector3 ctrl, Vector3 edgeB,
                                                  out float nearestT)
        {
            float   bestSqDist = float.MaxValue;
            float   bestT      = 0f;
            Vector3 prevPt     = QuadBezier(edgeA, ctrl, edgeB, 0f);

            for (int i = 1; i <= CurveSamples; i++)
            {
                float   t1       = (float)i / CurveSamples;
                Vector3 curPt    = QuadBezier(edgeA, ctrl, edgeB, t1);
                Vector3 seg      = curPt - prevPt;
                float   segLenSq = seg.sqrMagnitude;
                float   s        = segLenSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - prevPt, seg) / segLenSq) : 0f;
                Vector3 proj     = prevPt + seg * s;
                float   d2       = (p - proj).sqrMagnitude;
                if (d2 < bestSqDist)
                {
                    bestSqDist = d2;
                    float t0   = (float)(i - 1) / CurveSamples;
                    bestT      = Mathf.Lerp(t0, t1, s);
                }
                prevPt = curPt;
            }

            nearestT = bestT;
            return Mathf.Sqrt(bestSqDist);
        }

        /// <summary>
        /// 点pが川へ近すぎる（＝プロップを置いてはいけない）かどうか。
        /// clearanceは呼び出し側が決める。プロップの見た目の大きさによって適切な値が違うため、
        /// ここに固定値を持たせない。
        /// </summary>
        public static bool IsTooCloseToChannel(Vector3 p, Vector3 edgeA, Vector3 ctrl, Vector3 edgeB,
                                                float clearance)
            => DistanceToCenterline(p, edgeA, ctrl, edgeB) < clearance;

        /// <summary>
        /// TileTypeから流路の端点と制御点（すべてタイルローカル座標）を求める。
        /// 川タイル（propType==Water）でなければfalseを返す。
        ///
        /// 辺の選び方は既存のHexTile.ComputeRiverEdgeIndices／SpawnWaterと同じ規則:
        ///   1. EdgeType.Riverの辺が2本以上あればその先頭2本
        ///   2. なければNone/Field/Forest以外の辺の先頭2本（旧データ互換）
        ///   3. それでも足りなければ座標ハッシュで2辺
        /// </summary>
        public static bool TryGetChannel(TileType type, float outerRadius,
                                          int coordQ, int coordR, int coordS,
                                          out Vector3 edgeA, out Vector3 ctrl, out Vector3 edgeB)
        {
            edgeA = ctrl = edgeB = Vector3.zero;
            if (type == null || type.propType != TilePropType.Water) return false;

            if (!TryGetChannelEdgeIndices(type, coordQ, coordR, coordS, out int a, out int b)) return false;

            edgeA = EdgeCenter(a, outerRadius);
            edgeB = EdgeCenter(b, outerRadius);

            // 対辺（直線）なら2辺の中点、それ以外はタイル中心。
            // ★どちらの場合も結果はタイル中心（原点）になるが、
            //   「直線は中点を制御点にする」という元の意図を式として残しておく。
            bool isStraight = ((edgeA + edgeB) * 0.5f).sqrMagnitude < 0.01f;
            ctrl = isStraight ? (edgeA + edgeB) * 0.5f : Vector3.zero;
            return true;
        }

        /// <summary>流路が通る2辺のインデックスを求める（TryGetChannelの辺選択部分）。</summary>
        public static bool TryGetChannelEdgeIndices(TileType type, int coordQ, int coordR, int coordS,
                                                     out int edgeA, out int edgeB)
        {
            edgeA = edgeB = 0;
            if (type == null) return false;

            var riverEdges    = new List<int>();
            var fallbackEdges = new List<int>();
            for (int d = 0; d < 6; d++)
            {
                var e = type.GetEdge(d);
                if (e == EdgeType.River) riverEdges.Add(d);
                else if (e != EdgeType.None && e != EdgeType.Field && e != EdgeType.Forest)
                    fallbackEdges.Add(d);
            }

            var src = riverEdges.Count    >= 2 ? riverEdges
                    : fallbackEdges.Count >= 2 ? fallbackEdges
                    : null;

            if (src != null)
            {
                edgeA = src[0];
                edgeB = src[1];
                return true;
            }

            // 川辺が足りないデータでも流路が消えないよう、座標から再現性のある2辺を選ぶ
            int h = Mathf.Abs(coordQ * 31 + coordR * 17 + coordS * 7);
            edgeA = h % 6;
            edgeB = (edgeA + 1 + (h / 6) % 5) % 6;
            return true;
        }
    }
}
