// 役割: 川の流路（中心線と半幅）の純粋関数と、それを溝メッシュへ委譲したことの等価性を固定する。
//
//       ★このテストの主目的は2つ。
//         1. 「溝の形」と「木や花を置いてはいけない範囲」が同じ情報源から出ること。
//            別々に実装されると、川幅を1つ変えただけで木が水に立つ／岸が不自然に空く。
//         2. 断面が形状に依らず「中心線までの距離」だけで決まること。
//            これが直線と曲がりを繋いだときに継ぎ目が揃うことの根拠になっている。
//         3. 生成される溝メッシュが不用意に変わっていないこと（基準値を実測して埋め込んである）。

using NUnit.Framework;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class RiverChannelLayoutTests
    {
        private const float OuterRadius = 2.0f;
        private const float TileHeight  = 0.30f;

        // 現行の3種の川タイルが使う辺の組み合わせ（実アセットから採取）
        private static readonly int[][] s_ShapeEdges =
        {
            new[] { 0, 3 },   // River_Straight（対辺）
            new[] { 0, 5 },   // River_Bend（隣接辺）
            new[] { 0, 4 },   // River_Wide_Bend（1つ飛ばし）
        };
        private static readonly string[] s_ShapeNames = { "Straight", "Bend", "WideBend" };

        private static void GetChannel(int shape, out Vector3 a, out Vector3 ctrl, out Vector3 b)
        {
            a = RiverChannelLayout.EdgeCenter(s_ShapeEdges[shape][0], OuterRadius);
            b = RiverChannelLayout.EdgeCenter(s_ShapeEdges[shape][1], OuterRadius);
            bool isStraight = ((a + b) * 0.5f).sqrMagnitude < 0.01f;
            ctrl = isStraight ? (a + b) * 0.5f : Vector3.zero;
        }

        private static TileType MakeType(TilePropType propType, params EdgeType[] edges)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.propType = propType;
            t.edges    = new EdgeType[6];
            for (int i = 0; i < 6 && i < edges.Length; i++) t.edges[i] = edges[i];
            return t;
        }

        // ══ 流路の半幅 ═══════════════════════════════════════════════════

        [Test]
        public void ChannelHalfWidth_IsWhereTheSlopeBegins()
        {
            // 半幅は「平らな水面の外端」。ここから岸の斜面が始まる。
            // かつては川岸を表す緑のキューブの位置だったが、キューブは撤去され、
            // 岸は地形の斜面そのものになった。
            Assert.AreEqual(0.42f, RiverChannelLayout.ChannelHalfWidth(2.0f), 0.0001f);
        }

        [Test]
        public void BankOuterRadius_IsOutsideTheWaterEdge()
        {
            // 斜面は必ず水面より外にある。逆転すると断面が裏返る。
            Assert.Greater(RiverChannelLayout.BankOuterRadius(OuterRadius),
                           RiverChannelLayout.ChannelHalfWidth(OuterRadius));
            Assert.AreEqual(0.76f, RiverChannelLayout.BankOuterRadius(2.0f), 0.0001f);
        }

        [Test]
        public void ChannelHalfWidth_ScalesWithOuterRadius()
        {
            Assert.AreEqual(0.21f, RiverChannelLayout.ChannelHalfWidth(1.0f), 0.0001f);
            Assert.AreEqual(0.84f, RiverChannelLayout.ChannelHalfWidth(4.0f), 0.0001f);
        }

        [Test]
        public void BankOuterRadius_ScalesWithOuterRadius()
        {
            Assert.AreEqual(0.38f, RiverChannelLayout.BankOuterRadius(1.0f), 0.0001f);
            Assert.AreEqual(1.52f, RiverChannelLayout.BankOuterRadius(4.0f), 0.0001f);
        }

        [Test]
        public void SharedEdge_MeasuresTheSameDistanceFromBothTiles()
        {
            // ★これが「直線と曲がりを繋いでも継ぎ目が揃う」ことの根拠。
            //   断面（高さも色も）は中心線までの距離だけの関数なので、
            //   辺の上で両側のタイルが同じ距離を測るなら、継ぎ目は自動的に一致する。
            //
            //   効いているのは、流路の制御点がどの形状でもタイル中心にあることで、
            //   端点での接線が必ず辺の法線方向になる（＝辺に垂直に出入りする）こと。
            //   形状ごとに制御点をずらすと、この性質が壊れて継ぎ目に段差が出る。
            const int share = 0;                 // 手前のタイルが隣と接する辺
            const int facing = (share + 3) % 6;  // 隣のタイルから見た同じ辺

            // 隣のタイルの中心は、辺の中点の2倍だけ離れている
            Vector3 offset = 2f * RiverChannelLayout.EdgeCenter(share, OuterRadius);

            // 共有する辺の両端（六角形の頂点）。辺dirは頂点k..k+1に挟まれる
            int   k  = (6 - share) % 6;
            Vector3 c0 = RimCorner(k), c1 = RimCorner(k + 1);

            // share を含む流路と、facing を含む流路のすべての組み合わせ
            var near = new[] { new[] { share, (share + 3) % 6 }, new[] { share, (share + 5) % 6 }, new[] { share, (share + 4) % 6 } };
            var far  = new[] { new[] { facing, (facing + 3) % 6 }, new[] { facing, (facing + 5) % 6 }, new[] { facing, (facing + 4) % 6 } };

            for (int i = 0; i < near.Length; i++)
            {
                for (int j = 0; j < far.Length; j++)
                {
                    GetChannelFor(near[i], out Vector3 na, out Vector3 nc, out Vector3 nb);
                    GetChannelFor(far[j],  out Vector3 fa, out Vector3 fc, out Vector3 fb);

                    for (int t = 1; t < 20; t++)
                    {
                        Vector3 onEdge   = Vector3.Lerp(c0, c1, t / 20f);
                        float   dNear    = RiverChannelLayout.DistanceToCenterline(onEdge, na, nc, nb);
                        float   dFar     = RiverChannelLayout.DistanceToCenterline(onEdge - offset, fa, fc, fb);

                        Assert.AreEqual(dNear, dFar, 0.002f,
                            $"{s_ShapeNames[i]}と{s_ShapeNames[j]}を繋いだ辺の上で、測る距離が食い違う");
                    }
                }
            }
        }

        /// <summary>六角形の頂点（フラットトップ、60度刻み）。</summary>
        private static Vector3 RimCorner(int i)
        {
            float angle = Mathf.Deg2Rad * (60f * (((i % 6) + 6) % 6));
            return new Vector3(OuterRadius * Mathf.Cos(angle), 0f, OuterRadius * Mathf.Sin(angle));
        }

        private static void GetChannelFor(int[] edges, out Vector3 a, out Vector3 ctrl, out Vector3 b)
        {
            a = RiverChannelLayout.EdgeCenter(edges[0], OuterRadius);
            b = RiverChannelLayout.EdgeCenter(edges[1], OuterRadius);
            bool isStraight = ((a + b) * 0.5f).sqrMagnitude < 0.01f;
            ctrl = isStraight ? (a + b) * 0.5f : Vector3.zero;
        }

        // ══ 中心線までの距離 ═════════════════════════════════════════════

        [Test]
        public void DistanceToCenterline_IsZeroOnTheCurveItself()
        {
            for (int s = 0; s < s_ShapeEdges.Length; s++)
            {
                GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    Vector3 onCurve = RiverChannelLayout.QuadBezier(a, ctrl, b, t);
                    // 曲線は24分割の折れ線で近似するため、曲がりでは弦とのわずかな差が残る
                    Assert.Less(RiverChannelLayout.DistanceToCenterline(onCurve, a, ctrl, b), 0.01f,
                        $"{s_ShapeNames[s]} t={t} で中心線から離れている");
                }
            }
        }

        [Test]
        public void DistanceToCenterline_IsZeroAtBothEdges()
        {
            for (int s = 0; s < s_ShapeEdges.Length; s++)
            {
                GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);
                Assert.AreEqual(0f, RiverChannelLayout.DistanceToCenterline(a, a, ctrl, b), 0.0001f);
                Assert.AreEqual(0f, RiverChannelLayout.DistanceToCenterline(b, a, ctrl, b), 0.0001f);
            }
        }

        [Test]
        public void DistanceToCenterline_ReportsTheNearestParameter()
        {
            // 端点では t が 0/1 側へ寄る。溝の立ち上がり（RampRatio）の計算がこれに依存する。
            GetChannel(0, out Vector3 a, out Vector3 ctrl, out Vector3 b);

            RiverChannelLayout.DistanceToCenterline(a, a, ctrl, b, out float tAtA);
            RiverChannelLayout.DistanceToCenterline(b, a, ctrl, b, out float tAtB);
            RiverChannelLayout.DistanceToCenterline(RiverChannelLayout.QuadBezier(a, ctrl, b, 0.5f),
                                                     a, ctrl, b, out float tAtMid);

            Assert.AreEqual(0f,   tAtA,   0.01f);
            Assert.AreEqual(1f,   tAtB,   0.01f);
            Assert.AreEqual(0.5f, tAtMid, 0.01f);
        }

        [Test]
        public void DistanceToCenterline_GrowsSidewaysFromTheCurve()
        {
            // 中心線に垂直へ離すと距離がそのまま増える（幅の判定が距離として使えることの確認）。
            GetChannel(0, out Vector3 a, out Vector3 ctrl, out Vector3 b);
            Vector3 mid     = RiverChannelLayout.QuadBezier(a, ctrl, b, 0.5f);
            Vector3 along   = (b - a).normalized;
            Vector3 sideways = Vector3.Cross(Vector3.up, along).normalized;

            foreach (float d in new[] { 0.25f, 0.5f, 0.75f, 1.0f })
                Assert.AreEqual(d, RiverChannelLayout.DistanceToCenterline(mid + sideways * d, a, ctrl, b), 0.01f,
                    $"横に{d}離した点の距離が合わない");
        }

        [Test]
        public void IsTooCloseToChannel_UsesTheGivenClearance()
        {
            // ★clearance は引数。ここに固定値を持たせると、木と花で別の余白を使えなくなる。
            GetChannel(0, out Vector3 a, out Vector3 ctrl, out Vector3 b);
            Vector3 mid      = RiverChannelLayout.QuadBezier(a, ctrl, b, 0.5f);
            Vector3 sideways = Vector3.Cross(Vector3.up, (b - a).normalized).normalized;
            Vector3 at070    = mid + sideways * 0.70f;

            Assert.IsTrue(RiverChannelLayout.IsTooCloseToChannel(at070, a, ctrl, b, 0.80f),
                "clearance 0.80 では 0.70 の点は近すぎ扱いになるはず");
            Assert.IsFalse(RiverChannelLayout.IsTooCloseToChannel(at070, a, ctrl, b, 0.60f),
                "clearance 0.60 では 0.70 の点は許容されるはず");
        }

        // ══ 溝の形と距離が同じ情報源であること（今回の中核） ═════════════

        [Test]
        public void PointsInsideTheHalfWidth_AreCarvedByTheMesh()
        {
            // ★距離 <= 半幅 の点は必ず溝の中（天面が下がっている）。
            //   ここが崩れると「川よけの判定」と「実際の溝」がずれる。
            float halfWidth = RiverChannelLayout.ChannelHalfWidth(OuterRadius);
            float landY     = HexMeshBuilder.TopY(TileHeight);

            for (int s = 0; s < s_ShapeEdges.Length; s++)
            {
                GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);
                var mesh = RiverChannelMeshBuilder.Build(OuterRadius, TileHeight, a, b, ctrl);
                try
                {
                    int inside = 0, carved = 0;
                    foreach (var v in mesh.vertices)
                    {
                        // 天面の頂点だけを見る（側面・底面は対象外）
                        if (v.y < -landY + 0.0001f) continue;

                        float d = RiverChannelLayout.DistanceToCenterline(new Vector3(v.x, 0f, v.z), a, ctrl, b);
                        if (d > halfWidth) continue;

                        inside++;
                        if (v.y < landY - 0.0001f) carved++;
                    }

                    Assert.Greater(inside, 0, $"{s_ShapeNames[s]}: 半幅の中に天面の頂点が無い");
                    // 辺の境界（t=0/1付近）は閉じた端なので深さ0へ戻る。そこを除けば必ず彫られている。
                    Assert.Greater((float)carved / inside, 0.5f,
                        $"{s_ShapeNames[s]}: 半幅の中なのに彫られていない頂点が多すぎる ({carved}/{inside})");
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        [Test]
        public void PointsFarFromTheChannel_AreLeftAsLand()
        {
            // 逆向きの固定。半幅より外は彫られていない。
            // ★許容 0.005 の理由
            //   色の境界をまたぐ三角形は、境界の等高線上へ新しい頂点を挿入して分割される。
            //   その頂点の高さは両端の直線補間なので、斜面が曲がっているぶんだけ
            //   真の地形よりわずかに下へ落ちる（弦と弧の差）。実測で最大0.0024。
            //   タイルの厚み0.30に対して1%未満で、地形として彫られているわけではない。
            float bankOuter = RiverChannelLayout.BankOuterRadius(OuterRadius);
            float landY     = HexMeshBuilder.TopY(TileHeight);

            for (int s = 0; s < s_ShapeEdges.Length; s++)
            {
                GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);
                var mesh = RiverChannelMeshBuilder.Build(OuterRadius, TileHeight, a, b, ctrl);
                try
                {
                    foreach (var v in mesh.vertices)
                    {
                        if (v.y < -landY + 0.0001f) continue;
                        float d = RiverChannelLayout.DistanceToCenterline(new Vector3(v.x, 0f, v.z), a, ctrl, b);
                        if (d <= bankOuter + 0.0001f) continue;

                        Assert.AreEqual(landY, v.y, 0.005f,
                            $"{s_ShapeNames[s]}: 斜面の外端({bankOuter:F2})より{d:F3}外の頂点が彫られている");
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        [Test]
        public void DeepCarvingStaysWellInsideTheHalfWidth()
        {
            // 上のテストの許容 0.002 が「実は溝が外へはみ出している」を見逃さないための対の固定。
            // 平らな水面（＝最大深さまで彫られている）の範囲が、水面の外端に収まっていること。
            // ★岸の斜面は水面の外にあるので、浅い彫りは外側にも存在してよい。
            //   斜面は SmoothStep なので上端の傾きが0に近く、
            //   「9割の深さ」までなら水面の外端より少し外にも現れる。
            //   それを外れと数えないよう、ここでは最大深さそのものを基準にする。
            float halfWidth  = RiverChannelLayout.ChannelHalfWidth(OuterRadius);
            float maxDepth   = TileHeight * 0.65f;          // MaxDepthRatio
            float deepEnough = maxDepth * 0.999f;
            float landY     = HexMeshBuilder.TopY(TileHeight);

            for (int s = 0; s < s_ShapeEdges.Length; s++)
            {
                GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);
                var mesh = RiverChannelMeshBuilder.Build(OuterRadius, TileHeight, a, b, ctrl);
                try
                {
                    int deep = 0;
                    foreach (var v in mesh.vertices)
                    {
                        if (v.y < -landY + 0.0001f) continue;
                        if (landY - v.y < deepEnough) continue;

                        deep++;
                        float d = RiverChannelLayout.DistanceToCenterline(new Vector3(v.x, 0f, v.z), a, ctrl, b);
                        Assert.LessOrEqual(d, halfWidth + 0.02f,
                            $"{s_ShapeNames[s]}: 中心線から{d:F3}（水面の外端{halfWidth:F3}の外）が最大深さ{landY - v.y:F3}で彫られている");
                    }
                    Assert.Greater(deep, 0, $"{s_ShapeNames[s]}: はっきり彫られた頂点が1つも無い");
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        // ══ TileType からの流路取得 ═══════════════════════════════════════

        [Test]
        public void TryGetChannel_ReadsTheEdgesOfAllThreeRiverShapes()
        {
            // 実アセットと同じ edges を与え、実測どおりの端点が返ること。
            var cases = new[]
            {
                new { edges = new[] { EdgeType.River, EdgeType.Field, EdgeType.Field, EdgeType.River, EdgeType.Field, EdgeType.Field }, a = 0, b = 3 },
                new { edges = new[] { EdgeType.River, EdgeType.Field, EdgeType.Field, EdgeType.Field, EdgeType.Field, EdgeType.River }, a = 0, b = 5 },
                new { edges = new[] { EdgeType.River, EdgeType.Field, EdgeType.Field, EdgeType.Field, EdgeType.River, EdgeType.Field }, a = 0, b = 4 },
            };

            foreach (var c in cases)
            {
                var type = MakeType(TilePropType.Water, c.edges);
                try
                {
                    Assert.IsTrue(RiverChannelLayout.TryGetChannel(type, OuterRadius, 0, 0, 0,
                        out Vector3 a, out Vector3 ctrl, out Vector3 b));

                    Assert.AreEqual(RiverChannelLayout.EdgeCenter(c.a, OuterRadius), a);
                    Assert.AreEqual(RiverChannelLayout.EdgeCenter(c.b, OuterRadius), b);
                    // ★3形状とも制御点はタイル中心。だから形状別の分岐が要らない。
                    Assert.AreEqual(0f, ctrl.magnitude, 0.0001f, "制御点がタイル中心でない");
                }
                finally { Object.DestroyImmediate(type); }
            }
        }

        [Test]
        public void TryGetChannel_RejectsNonRiverTiles()
        {
            foreach (var prop in new[] { TilePropType.None, TilePropType.Tree, TilePropType.Flower, TilePropType.House })
            {
                var type = MakeType(prop, EdgeType.River, EdgeType.Field, EdgeType.Field,
                                          EdgeType.River, EdgeType.Field, EdgeType.Field);
                try
                {
                    Assert.IsFalse(RiverChannelLayout.TryGetChannel(type, OuterRadius, 0, 0, 0, out _, out _, out _),
                        $"propType={prop} で流路を返してしまっている");
                }
                finally { Object.DestroyImmediate(type); }
            }

            Assert.IsFalse(RiverChannelLayout.TryGetChannel(null, OuterRadius, 0, 0, 0, out _, out _, out _));
        }

        [Test]
        public void TryGetChannelEdgeIndices_FallsBackDeterministically()
        {
            // River辺が2本に満たないデータでも流路が消えないよう座標ハッシュへ落ちる。
            // 同じ座標なら常に同じ2辺になること（見た目が毎回変わらない）。
            var type = MakeType(TilePropType.Water, EdgeType.River, EdgeType.Field, EdgeType.Field,
                                                     EdgeType.Field, EdgeType.Field, EdgeType.Field);
            try
            {
                Assert.IsTrue(RiverChannelLayout.TryGetChannelEdgeIndices(type, 3, -2, -1, out int a1, out int b1));
                Assert.IsTrue(RiverChannelLayout.TryGetChannelEdgeIndices(type, 3, -2, -1, out int a2, out int b2));
                Assert.AreEqual(a1, a2);
                Assert.AreEqual(b1, b2);
                Assert.AreNotEqual(a1, b1, "同じ辺が2回選ばれている");
                Assert.IsTrue(a1 >= 0 && a1 < 6 && b1 >= 0 && b1 < 6);
            }
            finally { Object.DestroyImmediate(type); }
        }

        // ══ リファクタ前後でメッシュが変わっていないこと ═════════════════

        /// <summary>
        /// 現行の断面から実測した基準値。
        /// 形式: verts | subMesh | 各subMeshのindex数 | 頂点ハッシュ | index ハッシュ | bounds.center | bounds.size
        ///
        /// ★意図せず溝の形が変わることを防ぐための固定値。
        ///   断面（水面の半幅・岸の斜面の外端・深さ）を意図して変えたときだけ取り直すこと。
        ///   前回の取り直し: 岸の斜面の外端を 0.45 から 0.38 へ詰めたとき。
        /// </summary>
        private static readonly string[] s_MeshBaseline =
        {
            "2136|3|4338|2538|2910|-885954487146664702|-4072231593841069441|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "2070|3|4230|2529|2775|2775216574524027484|1292282515585070823|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "2070|3|4230|2529|2775|-1969794835800127294|4551115915116086337|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "2004|3|4122|2520|2640|-5032760682735142436|4561404705250368973|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1928|3|5376|1752|2034|2497177884268122481|-2168526110032132022|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1862|3|5268|1743|1899|8534396267633477936|-7517986597080232394|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1862|3|5268|1743|1899|8842773696334554823|-2762657984779884012|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1796|3|5160|1734|1764|-7762951460829594106|7787301949035532796|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "2056|3|4656|2256|2634|-162120364842611834|-617597861303242177|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1990|3|4548|2247|2499|8658733172094802814|-4390216268877461833|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1990|3|4548|2247|2499|3454423074234858847|6382578245501098675|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1924|3|4440|2238|2364|2432570657874573527|-8754867976167103325|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
        };

        /// <summary>頂点位置・index列・subMesh数・boundsを、順序込みで1本の文字列へ畳む。</summary>
        private static string Digest(Mesh m)
        {
            var  v  = m.vertices;
            long hv = 17;
            unchecked
            {
                for (int i = 0; i < v.Length; i++)
                {
                    hv = hv * 31 + Mathf.RoundToInt(v[i].x * 100000f);
                    hv = hv * 31 + Mathf.RoundToInt(v[i].y * 100000f);
                    hv = hv * 31 + Mathf.RoundToInt(v[i].z * 100000f);
                }
            }

            long hi  = 17;
            var  sbi = new System.Text.StringBuilder();
            unchecked
            {
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    var tri = m.GetTriangles(s);
                    sbi.Append("|").Append(tri.Length);
                    for (int i = 0; i < tri.Length; i++) hi = hi * 31 + tri[i];
                }
            }

            var b = m.bounds;
            return v.Length + "|" + m.subMeshCount + sbi + "|" + hv + "|" + hi
                 + "|" + b.center.ToString("F6") + "|" + b.size.ToString("F6");
        }

        [Test]
        public void Build_MatchesTheRecordedMesh_ForEveryShapeAndOpenCombination()
        {
            // ★溝の形を不用意に変えていないことの固定。
            //   3形状 × 開放端4通り = 12通りすべてを見る。
            int idx = 0;
            for (int s = 0; s < s_ShapeEdges.Length; s++)
            {
                GetChannel(s, out Vector3 a, out Vector3 ctrl, out Vector3 b);

                foreach (var open in new[]
                {
                    new[] { false, false },
                    new[] { true,  false },
                    new[] { false, true  },
                    new[] { true,  true  },
                })
                {
                    var mesh = RiverChannelMeshBuilder.Build(OuterRadius, TileHeight, a, b, ctrl, open[0], open[1]);
                    try
                    {
                        Assert.AreEqual(s_MeshBaseline[idx], Digest(mesh),
                            $"{s_ShapeNames[s]} openA={open[0]} openB={open[1]} でメッシュが変わっている");
                    }
                    finally { Object.DestroyImmediate(mesh); }
                    idx++;
                }
            }
            Assert.AreEqual(s_MeshBaseline.Length, idx, "基準値の件数と検証件数が合っていない");
        }

        [Test]
        public void CenterlineHeight_IsUnchangedByTheRefactor()
        {
            // 深さプロファイル（MaxDepthRatio / RampRatio）は移動していないことの確認。
            float land = HexMeshBuilder.TopY(TileHeight);
            Assert.AreEqual(land,    RiverChannelMeshBuilder.CenterlineHeight(0f,   TileHeight), 0.0001f);
            Assert.AreEqual(land,    RiverChannelMeshBuilder.CenterlineHeight(1f,   TileHeight), 0.0001f);
            Assert.AreEqual(-0.045f, RiverChannelMeshBuilder.CenterlineHeight(0.5f, TileHeight), 0.0001f);
            Assert.AreEqual(-0.045f, RiverChannelMeshBuilder.CenterlineHeight(0f,   TileHeight, openA: true, openB: true), 0.0001f);
        }
    }
}
