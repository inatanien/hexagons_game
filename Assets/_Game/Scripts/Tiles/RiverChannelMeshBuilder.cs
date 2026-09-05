// 役割: 川タイル専用に、天面へ流路の溝を彫り込んだ六角柱メッシュを生成する静的ユーティリティ。
//       外周（六角形の輪郭）・側面・底面は HexMeshBuilder と同一形状を維持し、
//       天面のみ edgeA→edgeB の流路に沿って凹ませる（辺の境界では深さ0にして隣接タイルと段差なく繋がる）。
//
//       ★流路そのもの（中心線の形・半幅）は RiverChannelLayout が持つ。
//         ここは「その流路をどれだけ深く彫るか」だけを決める。
//
//       ★断面は「平らな水面 → なだらかな岸の斜面 → 平らな草地」の3段。
//         色の境目は斜面の下端・上端にぴったり重なるので、
//         平地に線を引いたようには見えない。
//
//       ★断面は中心線までの距離だけで決まり、形状で分岐しない。
//         流路はどの形状でも辺に垂直に出入りするため、辺を共有する2枚のタイルは
//         その辺の上で同じ距離を測る。だから直線と曲がりを繋いでも継ぎ目が揃う。
//
//       ★天面の法線は三角形の向きからではなく地形の式から直接求める（SurfaceNormal）。
//         RecalculateNormalsに任せると、三角格子の向きが交互に変わるぶん陰影が
//         ぎざぎざに見えるため。
//         木や花を川へ生やさないための除外判定も同じ RiverChannelLayout を通るため、
//         川幅を変えれば溝も除外範囲も一緒に動く。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class RiverChannelMeshBuilder
    {
        // 流路パラメータ（Build と CenterlineHeight で共有し、ズレを防ぐ）
        // ★流路の半幅と中心線の形は RiverChannelLayout が唯一の情報源。
        //   ここで独自に持つと、木や花の川よけ判定と溝の形が食い違う。
        private const float RampRatio      = 0.28f; // t空間での、辺境界からの立ち上がり割合
        private const float MaxDepthRatio  = 0.65f; // タイル厚み比。溝の最大深さ（0.5=厚みの半分）

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
            // 平らな水面の外端 → 岸の斜面 → 平らな草地。値は RiverChannelLayout が唯一の情報源。
            float waterHalf  = RiverChannelLayout.ChannelHalfWidth(outerRadius);
            float bankOuter  = RiverChannelLayout.BankOuterRadius(outerRadius);
            float slopeBand  = bankOuter - waterHalf;

            int S = Mathf.Max(2, subdivisions);

            var verts       = new List<Vector3>();
            var uvs         = new List<Vector2>();
            var depths      = new List<float>(); // 頂点ごとの実際の深さ（三角形分割の境界計算に使う）
            var isChannel   = new List<bool>(); // 陰影に頼らず色分けするための、頂点ごとの「水路内か」フラグ
            // 色の境界に使う2つの量。どちらも「中心線までの距離」から作った符号付きの余裕で、
            // 0より大きければその内側。
            // ★深さで切らない理由: 深さは斜面の上端で smoothstep の平らな部分に入るため、
            //   2頂点の深さを直線で結んで境界を求めると、交点が1マス近くばらつく。
            //   実測で 0.049（水面の半幅の約1割）揺れており、これが水際のギザギザの正体だった。
            //   距離は位置に対して直線なので、同じやり方でも交点が正確に決まる。
            var waterField  = new List<float>(); // 平らな水面の中か
            var bankField   = new List<float>(); // 岸の斜面の中か
            var landTris    = new List<int>();
            var channelTris = new List<int>();
            var bankTris    = new List<int>();
            // 天面だけを2段階に切りたいので、いったんここへ受ける（底面・側面は切らない）
            var topLandTris = new List<int>();

            const float channelThreshold = 0.001f;

            int AddVertex(Vector3 flat, bool isRimBoundary)
            {
                Vector3 xz = new Vector3(flat.x, 0f, flat.z);
                // 六角形の外周は、開いている端の水路幅の中でなければ必ず深さ0にして、
                // 隣接タイルと段差なく繋がるようにする（開いている場合は溝底のまま繋げる）。
                // 開いている端は斜面の外端まで、隣タイルと同じ高さのまま繋げる
                bool forceLand = isRimBoundary
                    && !(openA && (xz - new Vector3(edgeA.x, 0f, edgeA.z)).magnitude <= bankOuter)
                    && !(openB && (xz - new Vector3(edgeB.x, 0f, edgeB.z)).magnitude <= bankOuter);

                float dist  = RiverChannelLayout.DistanceToCenterline(xz, edgeA, ctrl, edgeB);
                float depth = forceLand
                    ? 0f
                    : ComputeDepth(xz, edgeA, ctrl, edgeB, waterHalf, slopeBand, maxDepth, openA, openB);
                verts.Add(new Vector3(flat.x, h - depth, flat.z));
                uvs.Add(new Vector2(0.5f + 0.5f * flat.x / outerRadius, 0.5f + 0.5f * flat.z / outerRadius));
                depths.Add(depth);
                isChannel.Add(depth > channelThreshold);
                // 六角形の外周で陸地へ戻すときは色も陸地側へ寄せる（隣接タイルと段差なく繋ぐため）。
                // わずかに負の値にするのは、境界の頂点が辺のすぐ内側へ落ちるようにするため。
                waterField.Add(forceLand ? -0.001f : waterHalf - dist);
                bankField.Add(forceLand ? -0.001f : bankOuter - dist);
                return verts.Count - 1;
            }

            // 横方向の距離を量にして境界頂点を作る。水際にも岸際にも同じやり方を使う。
            int LateralCrossVertex(List<float> field, int ia, int ib)
            {
                float fa = field[ia], fb = field[ib];
                float t  = fa / (fa - fb);
                verts.Add(Vector3.Lerp(verts[ia], verts[ib], t));
                uvs.Add(Vector2.Lerp(uvs[ia], uvs[ib], t));
                depths.Add(Mathf.Lerp(depths[ia], depths[ib], t));
                bankField.Add(Mathf.Lerp(bankField[ia], bankField[ib], t));
                waterField.Add(Mathf.Lerp(waterField[ia], waterField[ib], t));
                return verts.Count - 1;
            }

            // 三角形(i0,i1,i2)を、頂点ごとの深さに応じて陸地/水路に振り分ける。
            // 三角形の3頂点が水路と陸地に分かれる場合は、境界(depth==channelThreshold)を
            // 通る新しい頂点を挿入して正確に分割することで、輪郭が三角格子の形に
            // 引きずられず（ガタガタにならず）、かつ1枚のメッシュのまま境界が繋がるようにする
            // （オーバーレイを重ねる方式だと地形本体との間でチラつき・貫通が起きるため）。
            // 三角形を、量fieldが閾値を超えるかどうかで inside/outside へ振り分ける。
            // またぐ場合は境界の位置に頂点を挿入して正確に分割する。
            // ★水際（深さ）と岸際（横方向の距離）で同じ手続きを使い回すために切り出してある。
            //   別々に書くと、片方だけ境界の作り方が変わってズレる。
            void SplitTri(int i0, int i1, int i2,
                          List<float> field, float threshold,
                          List<int> insideTris, List<int> outsideTris,
                          System.Func<int, int, int> cross)
            {
                bool in0 = field[i0] > threshold;
                bool in1 = field[i1] > threshold;
                bool in2 = field[i2] > threshold;
                int  cnt = (in0 ? 1 : 0) + (in1 ? 1 : 0) + (in2 ? 1 : 0);

                if (cnt == 0) { outsideTris.Add(i0); outsideTris.Add(i1); outsideTris.Add(i2); return; }
                if (cnt == 3) { insideTris.Add(i0);  insideTris.Add(i1);  insideTris.Add(i2);  return; }

                // p0が少数派（cnt==1なら内側の1点、cnt==2なら外側の1点）になるよう回転させる
                int p0, p1, p2;
                bool loneIsInside = cnt == 1;
                bool lone0 = cnt == 1 ? in0 : !in0;
                bool lone1 = cnt == 1 ? in1 : !in1;
                if (lone0)      { p0 = i0; p1 = i1; p2 = i2; }
                else if (lone1) { p0 = i1; p1 = i2; p2 = i0; }
                else            { p0 = i2; p1 = i0; p2 = i1; }

                int m01 = cross(p0, p1);
                int m20 = cross(p2, p0);

                var loneList  = loneIsInside ? insideTris  : outsideTris;
                var otherList = loneIsInside ? outsideTris : insideTris;

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

            // 天面の三角形を、まず水面とそれ以外へ振り分ける。
            void SplitTop(int i0, int i1, int i2)
                => SplitTri(i0, i1, i2, waterField, 0f, channelTris, topLandTris,
                            (a, b) => LateralCrossVertex(waterField, a, b));

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
                        SplitTop(i00, i10, i01);

                        if (p + q + 2 <= S)
                        {
                            int i11 = localIdx[i][p + 1, q + 1];
                            SplitTop(i10, i11, i01);
                        }
                    }
                }
            }

            // ── 天面の陸地側を、水際の少し外まで「岸」として切り分ける ──────
            // ★水と陸の境界（上のパス）は一切動かさない。動かすと溝の輪郭と
            //   プロップの川よけ判定がずれる。ここで切るのは陸地側の三角形だけ。
            for (int i = 0; i < topLandTris.Count; i += 3)
                SplitTri(topLandTris[i], topLandTris[i + 1], topLandTris[i + 2],
                         bankField, 0f, bankTris, landTris,
                         (a, b) => LateralCrossVertex(bankField, a, b));

            // ★ここまでの頂点がすべて天面。法線を式から求め直すのはこの範囲だけ
            //   （底面・側面はこの後に足されるので、RecalculateNormals の結果をそのまま使う）
            int topVertexCount = verts.Count;

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
                    // 岸の斜面の外端（中心線からbankOuterの距離）を分割の基準にする。
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
                    Vector3 s1 = refCenter - tangent * bankOuter; // rimThis側（斜面の外端）
                    Vector3 s2 = refCenter + tangent * bankOuter; // rimNext側（斜面の外端）

                    AddFlatWallQuad(verts, uvs, landTris, rimThis, s1, h, -h);
                    AddFlatWallQuad(verts, uvs, landTris, s2, rimNext, h, -h);

                    const int midDivisions = 8;
                    Vector3 prev = s1;
                    for (int m = 1; m <= midDivisions; m++)
                    {
                        Vector3 cur = Vector3.Lerp(s1, s2, (float)m / midDivisions);
                        float depthPrev = ComputeDepth(new Vector3(prev.x, 0f, prev.z), edgeA, ctrl, edgeB,
                                                        waterHalf, slopeBand, maxDepth, openA, openB);
                        float depthCur  = ComputeDepth(new Vector3(cur.x, 0f, cur.z), edgeA, ctrl, edgeB,
                                                        waterHalf, slopeBand, maxDepth, openA, openB);
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
            // サブメッシュ0=草地(既存タイル色) / 1=水面(専用マテリアルで暗い水色) / 2=岸の斜面
            mesh.subMeshCount = 3;
            mesh.SetTriangles(landTris, 0);
            mesh.SetTriangles(channelTris, 1);
            mesh.SetTriangles(bankTris, 2);
            mesh.RecalculateNormals();

            // 天面だけ、法線を地形の式から求め直す。
            // ★RecalculateNormals は隣り合う三角形の向きを平均するので、
            //   三角格子の向きが交互に変わるぶん法線が振れ、斜面がぎざぎざに光る。
            //   面の傾きは式から正確に出せるので、そちらを使う。
            var normals = mesh.normals;
            for (int i = 0; i < topVertexCount; i++)
            {
                normals[i] = SurfaceNormal(new Vector3(verts[i].x, 0f, verts[i].z), edgeA, ctrl, edgeB,
                                            waterHalf, slopeBand, maxDepth, openA, openB);
            }
            mesh.normals = normals;
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
            return h - maxDepth * LongitudinalWeight(t, openA, openB);
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
                                           float waterHalf, float slopeBand, float maxDepth,
                                           bool openA, bool openB)
        {
            // 中心線までの距離と、その位置の曲線パラメータtは RiverChannelLayout が算出する。
            // ★木や花の川よけ判定もまったく同じ関数を通るので、
            //   「溝の中」と「プロップを置いてはいけない場所」が定義上ずれない。
            float dist  = RiverChannelLayout.DistanceToCenterline(p, edgeA, ctrl, edgeB, out float tBest);

            // 平らな水面 → なだらかな斜面 → 平らな草地
            float wLat;
            if (dist <= waterHalf)                 wLat = 1f;
            else if (dist <= waterHalf + slopeBand) wLat = Mathf.SmoothStep(1f, 0f, (dist - waterHalf) / slopeBand);
            else                                    wLat = 0f;

            return maxDepth * wLat * LongitudinalWeight(tBest, openA, openB);
        }

        /// <summary>
        /// 辺境界(t=0/1)で0、流路の中ほどで1になる縦断方向の係数。開いている端は1のまま
        /// （陸地の高さへ戻さず、隣のタイルへ溝底のまま繋げる）。
        /// ★深さ・中心線の高さ・岸の帯が、すべてこの1つの式を通るようにしてある。
        ///   別々に書くと、片方だけ端の扱いが変わって継ぎ目が割れる。
        /// </summary>
        private static float LongitudinalWeight(float t, bool openA, bool openB)
        {
            float wLongA = openA ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / RampRatio));
            float wLongB = openB ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - t) / RampRatio));
            return Mathf.Min(wLongA, wLongB);
        }

        /// <summary>
        /// 天面の法線を、三角形の向きからではなく地形の式から直接求める。
        ///
        /// ★深さは「横方向の落ち込み × 縦断方向の立ち上がり」の積なので、
        ///   その勾配も2項の和で書ける。積の微分をそのまま素直に書き下してある。
        ///   斜面の両端では SmoothStep の傾きが0になるため、
        ///   水面との境も草地との境も折れ目が出ずに滑らかに繋がる。
        /// </summary>
        private static Vector3 SurfaceNormal(Vector3 p, Vector3 edgeA, Vector3 ctrl, Vector3 edgeB,
                                              float waterHalf, float slopeBand, float maxDepth,
                                              bool openA, bool openB)
        {
            float dist = RiverChannelLayout.DistanceToCenterline(p, edgeA, ctrl, edgeB, out float t);

            // 横方向の落ち込みと、その距離に対する傾き
            float u = (dist - waterHalf) / slopeBand;
            float wLat, dLat;
            if (u <= 0f)      { wLat = 1f; dLat = 0f; }
            else if (u >= 1f) { wLat = 0f; dLat = 0f; }
            else              { wLat = 1f - u * u * (3f - 2f * u); dLat = -6f * u * (1f - u) / slopeBand; }

            // 縦断方向の立ち上がりと、そのtに対する傾き
            float wLong = LongitudinalWeight(t, openA, openB);
            float dLong = LongitudinalSlope(t, openA, openB);

            // 距離の勾配は「いちばん近い中心線上の点から外向きの単位ベクトル」
            Vector3 nearest = RiverChannelLayout.QuadBezier(edgeA, ctrl, edgeB, t);
            Vector3 outward = new Vector3(p.x - nearest.x, 0f, p.z - nearest.z);
            float   outLen  = outward.magnitude;
            Vector3 gradDist = outLen > 0.0001f ? outward / outLen : Vector3.zero;

            // tの勾配は接線方向。2次ベジェの微分をそのまま使う
            Vector3 tangent = 2f * (1f - t) * (ctrl - edgeA) + 2f * t * (edgeB - ctrl);
            tangent.y = 0f;
            float   spd2    = tangent.sqrMagnitude;
            Vector3 gradT   = spd2 > 0.0001f ? tangent / spd2 : Vector3.zero;

            Vector3 gradDepth = maxDepth * (dLat * wLong * gradDist + wLat * dLong * gradT);

            // 面は y = h - depth なので、法線は (∂depth/∂x, 1, ∂depth/∂z)
            return new Vector3(gradDepth.x, 1f, gradDepth.z).normalized;
        }

        /// <summary>縦断方向の立ち上がり係数の、tに対する傾き。</summary>
        private static float LongitudinalSlope(float t, bool openA, bool openB)
        {
            float tA = Mathf.Clamp01(t / RampRatio);
            float tB = Mathf.Clamp01((1f - t) / RampRatio);
            float wA = openA ? 1f : tA * tA * (3f - 2f * tA);
            float wB = openB ? 1f : tB * tB * (3f - 2f * tB);

            // 小さいほうが深さを決めているので、そちらの傾きを返す
            if (wA <= wB)
                return (openA || tA <= 0f || tA >= 1f) ? 0f :  6f * tA * (1f - tA) / RampRatio;
            return     (openB || tB <= 0f || tB >= 1f) ? 0f : -6f * tB * (1f - tB) / RampRatio;
        }
    }
}
