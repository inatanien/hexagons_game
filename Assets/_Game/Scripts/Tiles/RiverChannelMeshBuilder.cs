// 役割: 川タイル専用に、天面へ流路の溝を彫り込んだ六角柱メッシュを生成する静的ユーティリティ。
//       外周（六角形の輪郭）・側面・底面は HexMeshBuilder と同一形状を維持し、
//       天面のみ edgeA→edgeB の流路に沿って凹ませる（辺の境界では深さ0にして隣接タイルと段差なく繋がる）。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class RiverChannelMeshBuilder
    {
        // 流路パラメータ（Build と CenterlineHeight で共有し、ズレを防ぐ）
        private const float HalfWidthRatio = 0.25f; // outerRadius比。既存の水流表現(riverWidth=outerRadius*0.5)の半幅と一致
        private const float WallBandRatio  = 0.35f; // halfWidth比。壁の遷移帯（狭いほど壁が垂直に近い）
        private const float RampRatio      = 0.28f; // t空間での、辺境界からの立ち上がり割合
        private const float MaxDepthRatio  = 0.65f; // タイル厚み比。溝の最大深さ（0.5=厚みの半分）
        private const int   CurveSamples   = 24;

        /// <summary>
        /// フラットトップ六角柱の天面に、edgeA→ctrl→edgeB を結ぶ2次ベジェ流路の溝を彫り込んだメッシュを生成する。
        /// タイル境界（t=0/1）では、その端が閉じている場合は深さ0になり隣接タイルと段差なく繋がる。
        /// 端が開いている場合（openA/openB）は、その端の溝底の高さのまま隣タイルへ繋がる。
        /// </summary>
        /// <param name="edgeA">流路始点（タイルローカル座標、辺の中点）</param>
        /// <param name="edgeB">流路終点（タイルローカル座標、辺の中点）</param>
        /// <param name="ctrl">2次ベジェの制御点（タイルローカル座標）</param>
        /// <param name="openA">edgeA側が同種の川タイルと接続済みで、陸地の高さに戻さず溝底のまま繋げるか</param>
        /// <param name="openB">edgeB側が同種の川タイルと接続済みで、陸地の高さに戻さず溝底のまま繋げるか</param>
        public static Mesh Build(float outerRadius, float height, Vector3 edgeA, Vector3 edgeB, Vector3 ctrl,
                                  bool openA = false, bool openB = false, int subdivisions = 20)
        {
            var mesh = new Mesh { name = "HexTile_RiverChannel" };

            float h          = height * 0.5f;
            float maxDepth   = height * MaxDepthRatio;
            float halfWidth  = outerRadius * HalfWidthRatio;
            float wallBand   = halfWidth * WallBandRatio;

            int S = Mathf.Max(2, subdivisions);

            var verts       = new List<Vector3>();
            var uvs         = new List<Vector2>();
            var depths      = new List<float>(); // 頂点ごとの実際の深さ（三角形分割の境界計算に使う）
            var isChannel   = new List<bool>(); // 陰影に頼らず色分けするための、頂点ごとの「水路内か」フラグ
            var landTris    = new List<int>();
            var channelTris = new List<int>();

            const float channelThreshold = 0.001f;

            int AddVertex(Vector3 flat, bool isRimBoundary)
            {
                Vector3 xz = new Vector3(flat.x, 0f, flat.z);
                // 六角形の外周は、開いている端の水路幅の中でなければ必ず深さ0にして、
                // 隣接タイルと段差なく繋がるようにする（開いている場合は溝底のまま繋げる）。
                bool forceLand = isRimBoundary
                    && !(openA && (xz - new Vector3(edgeA.x, 0f, edgeA.z)).magnitude <= halfWidth)
                    && !(openB && (xz - new Vector3(edgeB.x, 0f, edgeB.z)).magnitude <= halfWidth);

                float depth = forceLand
                    ? 0f
                    : ComputeDepth(xz, edgeA, ctrl, edgeB, halfWidth, wallBand, maxDepth, openA, openB);
                verts.Add(new Vector3(flat.x, h - depth, flat.z));
                uvs.Add(new Vector2(0.5f + 0.5f * flat.x / outerRadius, 0.5f + 0.5f * flat.z / outerRadius));
                depths.Add(depth);
                isChannel.Add(depth > channelThreshold);
                return verts.Count - 1;
            }

            // 境界(depth==channelThreshold)上に新しい頂点を作り、そのインデックスを返す。
            int CrossVertex(int ia, int ib)
            {
                float da = depths[ia], db = depths[ib];
                float t  = (channelThreshold - da) / (db - da);
                verts.Add(Vector3.Lerp(verts[ia], verts[ib], t));
                uvs.Add(Vector2.Lerp(uvs[ia], uvs[ib], t));
                depths.Add(channelThreshold);
                return verts.Count - 1;
            }

            // 三角形(i0,i1,i2)を、頂点ごとの深さに応じて陸地/水路に振り分ける。
            // 三角形の3頂点が水路と陸地に分かれる場合は、境界(depth==channelThreshold)を
            // 通る新しい頂点を挿入して正確に分割することで、輪郭が三角格子の形に
            // 引きずられず（ガタガタにならず）、かつ1枚のメッシュのまま境界が繋がるようにする
            // （オーバーレイを重ねる方式だと地形本体との間でチラつき・貫通が起きるため）。
            void EmitTri(int i0, int i1, int i2)
            {
                bool in0 = depths[i0] > channelThreshold;
                bool in1 = depths[i1] > channelThreshold;
                bool in2 = depths[i2] > channelThreshold;
                int  cnt = (in0 ? 1 : 0) + (in1 ? 1 : 0) + (in2 ? 1 : 0);

                if (cnt == 0) { landTris.Add(i0);    landTris.Add(i1);    landTris.Add(i2);    return; }
                if (cnt == 3) { channelTris.Add(i0); channelTris.Add(i1); channelTris.Add(i2); return; }

                // p0が少数派（cnt==1なら水路側の1点、cnt==2なら陸地側の1点）になるよう回転させる
                int p0, p1, p2;
                bool loneIsChannel = cnt == 1;
                bool lone0 = cnt == 1 ? in0 : !in0;
                bool lone1 = cnt == 1 ? in1 : !in1;
                if (lone0)      { p0 = i0; p1 = i1; p2 = i2; }
                else if (lone1) { p0 = i1; p1 = i2; p2 = i0; }
                else            { p0 = i2; p1 = i0; p2 = i1; }

                int m01 = CrossVertex(p0, p1);
                int m20 = CrossVertex(p2, p0);

                var loneList  = loneIsChannel ? channelTris : landTris;
                var otherList = loneIsChannel ? landTris    : channelTris;

                loneList.Add(p0); loneList.Add(m01); loneList.Add(m20);

                otherList.Add(m01); otherList.Add(p1); otherList.Add(p2);
                otherList.Add(m01); otherList.Add(p2); otherList.Add(m20);
            }

            // ── 天面: 6ウェッジ（中心-辺i-辺i+1）をそれぞれS分割した三角格子 ──
            // 六角形の外周(p+q==S)は必ず深さ0（隣接タイルと段差なし）。
            // 中心(centerIdx)と各ウェッジ境界(スポーク線)の頂点はウェッジ間で共有し、
            // 継ぎ目のない滑らかな法線になるようにする。
            Vector3 center   = new Vector3(0f, h, 0f);
            int     centerIdx = AddVertex(center, isRimBoundary: false);

            var localIdx = new int[6][,];
            var q0Row    = new int[6][];
            var wedge0P0Col = new int[S + 1];

            for (int i = 0; i < 6; i++)
            {
                Vector3 rimThis = RimPoint(i,     outerRadius, h);
                Vector3 rimNext = RimPoint(i + 1, outerRadius, h);

                localIdx[i] = new int[S + 1, S + 1];
                q0Row[i]    = new int[S + 1];

                for (int q = 0; q <= S; q++)
                {
                    for (int p = 0; p <= S - q; p++)
                    {
                        int idx;
                        if (p == 0 && q == 0)
                        {
                            idx = centerIdx;
                        }
                        else if (q == 0)
                        {
                            // ウェッジiとウェッジ(i+1)の境界スポーク。ウェッジ5の分はウェッジ0のp=0列を再利用する。
                            idx = (i == 5) ? wedge0P0Col[p]
                                           : AddVertex(center + (float)p / S * (rimNext - center), isRimBoundary: p == S);
                            q0Row[i][p] = idx;
                        }
                        else if (p == 0)
                        {
                            // ウェッジ(i-1)とウェッジiの境界スポーク。ウェッジ0はここで新規作成し、後でウェッジ5から再利用する。
                            if (i == 0)
                            {
                                idx = AddVertex(center + (float)q / S * (rimThis - center), isRimBoundary: q == S);
                                wedge0P0Col[q] = idx;
                            }
                            else
                            {
                                idx = q0Row[i - 1][q];
                            }
                        }
                        else
                        {
                            float fp = (float)p / S;
                            float fq = (float)q / S;
                            idx = AddVertex(center + fp * (rimNext - center) + fq * (rimThis - center),
                                            isRimBoundary: p + q == S);
                        }

                        localIdx[i][p, q] = idx;
                    }
                }
            }

            // 地形の高さ計算グリッドの各三角形を、頂点の深さに応じて陸地/水路サブメッシュへ振り分ける。
            // 境界をまたぐ三角形は EmitTri が正確な位置で分割するため、1枚のメッシュのまま
            // 輪郭がガタガタにならず、かつオーバーレイ方式のようなチラつき・貫通も起きない。
            for (int i = 0; i < 6; i++)
            {
                for (int q = 0; q < S; q++)
                {
                    for (int p = 0; p < S - q; p++)
                    {
                        int i00 = localIdx[i][p,     q];
                        int i10 = localIdx[i][p + 1, q];
                        int i01 = localIdx[i][p,     q + 1];
                        EmitTri(i00, i10, i01);

                        if (p + q + 2 <= S)
                        {
                            int i11 = localIdx[i][p + 1, q + 1];
                            EmitTri(i10, i11, i01);
                        }
                    }
                }
            }

            // ── 底面: 変更なし（常にフラット） ──────────────────────────
            int bottomCenterIdx = verts.Count;
            verts.Add(new Vector3(0f, -h, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));

            int bottomRimStart = verts.Count;
            for (int i = 0; i < 6; i++)
            {
                Vector3 rp = RimPoint(i, outerRadius, -h);
                verts.Add(rp);
                uvs.Add(new Vector2(0.5f + 0.5f * rp.x / outerRadius, 0.5f + 0.5f * rp.z / outerRadius));
            }

            // 底面（反時計回り）── 陸地扱い
            for (int i = 0; i < 6; i++)
            {
                landTris.Add(bottomCenterIdx);
                landTris.Add(bottomRimStart + i);
                landTris.Add(bottomRimStart + (i + 1) % 6);
            }

            // ── 側面 ──────────────────────────────────────────────────
            // 川が実際に凹んでいる辺だけ、天面の外周と同じ高さプロファイルでS分割する
            // （開いている端は天面が溝底まで下がるため、壁も追従させないと浮いた板に見える）。
            // それ以外の辺（川が通っていない・閉じている辺）は、見た目・法線を変えないよう
            // 元のコーナー間フラット1枚四角形のまま（頂点も天面と共有しない）にする。
            for (int i = 0; i < 6; i++)
            {
                bool edgeHasOpenChannel = false;
                for (int p = 0; p <= S; p++)
                {
                    if (isChannel[localIdx[i][p, S - p]]) { edgeHasOpenChannel = true; break; }
                }

                if (edgeHasOpenChannel)
                {
                    // 川岸を表す緑キューブの端（中心線からhalfWidthの距離）を分割の基準にする。
                    // その外側（陸地）は独立頂点のフラット四角形のまま。
                    // 内側（水路の幅の中）だけ、天面と同じ高さ計算で滑らかに繋げる
                    // （天面はカーブしたままなのでフラットにすると隙間ができるため）。
                    Vector3 rimThis = RimPoint(i,     outerRadius, h);
                    Vector3 rimNext = RimPoint(i + 1, outerRadius, h);
                    Vector3 edgeMid = (rimThis + rimNext) * 0.5f;

                    float distToA = Vector2.Distance(new Vector2(edgeA.x, edgeA.z), new Vector2(edgeMid.x, edgeMid.z));
                    float distToB = Vector2.Distance(new Vector2(edgeB.x, edgeB.z), new Vector2(edgeMid.x, edgeMid.z));
                    Vector3 refCenter = distToA < distToB ? edgeA : edgeB;

                    Vector3 tangent = new Vector3(rimNext.x - rimThis.x, 0f, rimNext.z - rimThis.z).normalized;
                    Vector3 s1 = refCenter - tangent * halfWidth; // rimThis側（キューブの端）
                    Vector3 s2 = refCenter + tangent * halfWidth; // rimNext側（キューブの端）

                    AddFlatWallQuad(verts, uvs, landTris, rimThis, s1, h, -h);
                    AddFlatWallQuad(verts, uvs, landTris, s2, rimNext, h, -h);

                    const int midDivisions = 8;
                    Vector3 prev = s1;
                    for (int m = 1; m <= midDivisions; m++)
                    {
                        Vector3 cur = Vector3.Lerp(s1, s2, (float)m / midDivisions);
                        float depthPrev = ComputeDepth(new Vector3(prev.x, 0f, prev.z), edgeA, ctrl, edgeB,
                                                        halfWidth, wallBand, maxDepth, openA, openB);
                        float depthCur  = ComputeDepth(new Vector3(cur.x, 0f, cur.z), edgeA, ctrl, edgeB,
                                                        halfWidth, wallBand, maxDepth, openA, openB);
                        AddWallQuad(verts, uvs, landTris,
                                    new Vector3(prev.x, h - depthPrev, prev.z),
                                    new Vector3(cur.x,  h - depthCur,  cur.z), -h);
                        prev = cur;
                    }
                }
                else
                {
                    // 元のHexMeshBuilderと同じ、コーナー間フラット1枚四角形（独立頂点）
                    int topA = verts.Count;
                    verts.Add(RimPoint(i, outerRadius, h));
                    uvs.Add(new Vector2((float)i / 6f, 1f));
                    int topB = verts.Count;
                    verts.Add(RimPoint(i + 1, outerRadius, h));
                    uvs.Add(new Vector2((float)(i + 1) / 6f, 1f));
                    int botA = verts.Count;
                    verts.Add(RimPoint(i, outerRadius, -h));
                    uvs.Add(new Vector2((float)i / 6f, 0f));
                    int botB = verts.Count;
                    verts.Add(RimPoint(i + 1, outerRadius, -h));
                    uvs.Add(new Vector2((float)(i + 1) / 6f, 0f));

                    landTris.Add(topA); landTris.Add(topB); landTris.Add(botA);
                    landTris.Add(topB); landTris.Add(botB); landTris.Add(botA);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2; // サブメッシュ0=陸地(既存タイル色) / 1=水路(専用マテリアルで暗い水色を付け、陰影に頼らず視認できるようにする)
            mesh.SetTriangles(landTris, 0);
            mesh.SetTriangles(channelTris, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // from→to (XZ) を結ぶ、天面高さtopYでフラットな側壁四角形を独立頂点で追加する。
        private static void AddFlatWallQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                             Vector3 from, Vector3 to, float topY, float bottomY)
            => AddWallQuad(verts, uvs, tris,
                           new Vector3(from.x, topY, from.z), new Vector3(to.x, topY, to.z), bottomY);

        // top端点(topFrom/topTo)は個別に高さを持てる（天面の高さ計算に合わせて滑らかに繋ぐ場合に使う）。
        private static void AddWallQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                         Vector3 topFrom, Vector3 topTo, float bottomY)
        {
            Vector3 from = topFrom, to = topTo;
            int topA = verts.Count;
            verts.Add(topFrom);
            uvs.Add(new Vector2(0f, 1f));
            int topB = verts.Count;
            verts.Add(topTo);
            uvs.Add(new Vector2(1f, 1f));
            int botA = verts.Count;
            verts.Add(new Vector3(from.x, bottomY, from.z));
            uvs.Add(new Vector2(0f, 0f));
            int botB = verts.Count;
            verts.Add(new Vector3(to.x, bottomY, to.z));
            uvs.Add(new Vector2(1f, 0f));

            tris.Add(topA); tris.Add(topB); tris.Add(botA);
            tris.Add(topB); tris.Add(botB); tris.Add(botA);
        }

        /// <summary>
        /// 流路中心線（dist=0）における、パラメータt(0〜1)での天面高さを返す。
        /// 水流パーティクルなど、川の中心を流れる装飾の配置に使う。
        /// </summary>
        public static float CenterlineHeight(float t, float height, bool openA = false, bool openB = false)
        {
            float h        = height * 0.5f;
            float maxDepth = height * MaxDepthRatio;
            float wLongA   = openA ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / RampRatio));
            float wLongB   = openB ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / RampRatio));
            float wLong    = Mathf.Min(wLongA, wLongB);
            return h - maxDepth * wLong;
        }

        private static Vector3 RimPoint(int i, float outerRadius, float y)
        {
            float angle = Mathf.Deg2Rad * (60f * (((i % 6) + 6) % 6));
            return new Vector3(outerRadius * Mathf.Cos(angle), y, outerRadius * Mathf.Sin(angle));
        }

        // p: 深さを求めたい点(y=0平面上、x,zのみ使用)。流路曲線への最近傍距離と、
        // その位置の縦断方向係数(辺境界で0、中央で1。ただしopenA/openBの端は0にならず1のまま)・
        // 横断方向係数(壁で0、中央で1)の積で深さを決める。
        private static float ComputeDepth(Vector3 p, Vector3 edgeA, Vector3 ctrl, Vector3 edgeB,
                                           float halfWidth, float wallBand, float maxDepth,
                                           bool openA, bool openB)
        {
            // 曲線をCurveSamples本の線分に近似し、各線分へ最近傍点を射影して距離とtを求める。
            // 「最も近いサンプル点」だけを拾う方式だと、境界付近でtと距離の対応が不連続になり
            // 局所的に高さが跳ねる（凹みが浅い/深いを行き来する）フェースが出るため、線分単位で補間する。
            float   bestSqDist = float.MaxValue;
            float   bestT      = 0f;
            Vector3 prevPt     = QuadBezier(edgeA, ctrl, edgeB, 0f);
            for (int i = 1; i <= CurveSamples; i++)
            {
                float   t1  = (float)i / CurveSamples;
                Vector3 curPt = QuadBezier(edgeA, ctrl, edgeB, t1);
                Vector3 seg   = curPt - prevPt;
                float   segLenSq = seg.sqrMagnitude;
                float   s = segLenSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - prevPt, seg) / segLenSq) : 0f;
                Vector3 proj = prevPt + seg * s;
                float   d2   = (p - proj).sqrMagnitude;
                if (d2 < bestSqDist)
                {
                    bestSqDist = d2;
                    float t0 = (float)(i - 1) / CurveSamples;
                    bestT    = Mathf.Lerp(t0, t1, s);
                }
                prevPt = curPt;
            }

            float dist  = Mathf.Sqrt(bestSqDist);
            float tBest = bestT;

            float innerHalf = halfWidth - wallBand;
            float wLat;
            if (dist <= innerHalf)      wLat = 1f;
            else if (dist <= halfWidth) wLat = Mathf.SmoothStep(1f, 0f, (dist - innerHalf) / wallBand);
            else                        wLat = 0f;

            // edgeA側(t=0)・edgeB側(t=1)それぞれの立ち上がり係数。開いている端は1のまま（陸地の高さに戻らない）。
            float wLongA = openA ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tBest / RampRatio));
            float wLongB = openB ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - tBest) / RampRatio));
            float wLong  = Mathf.Min(wLongA, wLongB);

            return maxDepth * wLat * wLong;
        }

        private static Vector3 QuadBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float mt = 1f - t;
            return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
        }
    }
}
