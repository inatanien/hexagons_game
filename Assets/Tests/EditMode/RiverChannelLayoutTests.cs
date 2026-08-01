// 役割: 川の流路（中心線と半幅）の純粋関数と、それを溝メッシュへ委譲したことの等価性を固定する。
//
//       ★このテストの主目的は2つ。
//         1. 「溝の形」と「木や花を置いてはいけない範囲」が同じ情報源から出ること。
//            別々に実装されると、川幅を1つ変えただけで木が水に立つ／岸が不自然に空く。
//         2. RiverChannelLayout への切り出しで、生成される溝メッシュが1頂点も変わっていないこと。
//            基準値はリファクタ前の実装から実測して埋め込んである。

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
        public void ChannelHalfWidth_MatchesTheRiverBankPosition()
        {
            // 川岸ラインは HexTile.CreateWaterFlow で riverWidth(=outerRadius*0.5) の半分に置かれる。
            // 溝の壁と岸が同じ位置でなければ、岸のキューブが水に浮くか土に埋まる。
            float bankOffset = (OuterRadius * 0.5f) * 0.5f;
            Assert.AreEqual(bankOffset, RiverChannelLayout.ChannelHalfWidth(OuterRadius), 0.0001f);
            Assert.AreEqual(0.50f, RiverChannelLayout.ChannelHalfWidth(2.0f), 0.0001f);
        }

        [Test]
        public void ChannelHalfWidth_ScalesWithOuterRadius()
        {
            Assert.AreEqual(0.25f, RiverChannelLayout.ChannelHalfWidth(1.0f), 0.0001f);
            Assert.AreEqual(1.00f, RiverChannelLayout.ChannelHalfWidth(4.0f), 0.0001f);
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
            // ★許容 0.002 の理由
            //   陸地と水路の境界をまたぐ三角形は、深さ 0.001（channelThreshold）の等高線上へ
            //   新しい頂点を挿入して分割される。その頂点は2点間の直線補間で作られるため、
            //   XZ上の真の距離が半幅をわずかに超える位置に落ちることがあり、
            //   そのぶん 0.001 だけ下がって見える。地形として彫られているわけではない。
            float halfWidth = RiverChannelLayout.ChannelHalfWidth(OuterRadius);
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
                        if (d <= halfWidth + 0.0001f) continue;

                        Assert.AreEqual(landY, v.y, 0.002f,
                            $"{s_ShapeNames[s]}: 中心線から{d:F3}離れた陸地の頂点が彫られている");
                    }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
        }

        [Test]
        public void DeepCarvingStaysWellInsideTheHalfWidth()
        {
            // 上のテストの許容 0.002 が「実は溝が外へはみ出している」を見逃さないための対の固定。
            // はっきり彫られている（深さ 0.01 以上）頂点は、必ず半幅の内側にある。
            float halfWidth = RiverChannelLayout.ChannelHalfWidth(OuterRadius);
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
                        if (landY - v.y < 0.01f) continue;

                        deep++;
                        float d = RiverChannelLayout.DistanceToCenterline(new Vector3(v.x, 0f, v.z), a, ctrl, b);
                        Assert.LessOrEqual(d, halfWidth + 0.0001f,
                            $"{s_ShapeNames[s]}: 中心線から{d:F3}（半幅{halfWidth:F3}の外）が深さ{landY - v.y:F3}で彫られている");
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
        /// リファクタ前の実装から実測した基準値。
        /// 形式: verts | subMesh | sub0.idx | sub1.idx | 頂点ハッシュ | index ハッシュ | bounds.center | bounds.size
        /// </summary>
        private static readonly string[] s_MeshBaseline =
        {
            "1664|2|5616|2754|-4682549513587372444|-428198588079402083|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1674|2|5589|2757|6094137218931938528|-5986112451484035114|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1674|2|5589|2757|8557997316802829294|-1260706960937814478|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1684|2|5562|2760|-2419974614236443158|3022366079686496691|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1568|2|6138|1944|8481196757890878867|-1030806019024853010|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1574|2|6111|1935|154923692767863998|-2470000934409828694|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1574|2|6111|1935|6965116132820475128|7816041300819135792|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1580|2|6084|1926|3949479058134800611|-4402312765415624424|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1632|2|5694|2580|6989935818119677274|-1924590626023117436|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1638|2|5667|2571|-3439380624991298267|-1542844142608111662|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1638|2|5667|2571|6835990537420874866|-7359301142909056916|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
            "1644|2|5640|2562|-3726873292326997827|-991266043532267902|(0.000000, 0.000000, 0.000000)|(4.000000, 0.300000, 3.464102)",
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
        public void Build_MatchesThePreRefactorMesh_ForEveryShapeAndOpenCombination()
        {
            // ★RiverChannelLayout への切り出しは、溝の形を1頂点も変えてはいけない。
            //   3形状 × 開放端4通り = 12通りすべてを固定する。
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
