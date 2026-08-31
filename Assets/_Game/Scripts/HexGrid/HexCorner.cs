// 役割: 六角形の「角」を、座標ではなく同一性で表す値型。
//
//       ★world座標を丸めて突き合わせない。
//         隣り合う2枚が共有する角は、それぞれのタイル中心から計算すると浮動小数でずれる。
//         丸めで一致を取ると、丸め幅次第で輪郭が切れたり、無関係な角どうしが繋がったりする。
//         六角形の1つの角には必ず3枚のタイルが集まるので、その3つのHexCoordを
//         正規化した組を角のIDにする。どのタイルのどの角から作っても同じIDになり、
//         誤差ゼロで一致判定できる。
//
//       ★world座標が必要なときも、角度や半径を別実装で持たない。
//         3枚の中心の平均がちょうど共有する角になるので、HexCoord.ToWorldPosition だけを
//         基準にする（六角形の幾何がここと HexMeshBuilder の二重定義になるのを避ける）。

using System;
using UnityEngine;

namespace ElfVillage.HexGrid
{
    public readonly struct HexCorner : IEquatable<HexCorner>, IComparable<HexCorner>
    {
        /// <summary>この角に集まる3タイル。常に同じ並び順へ正規化してある。</summary>
        public readonly HexCoord A;
        public readonly HexCoord B;
        public readonly HexCoord C;

        private HexCorner(HexCoord a, HexCoord b, HexCoord c)
        {
            // 昇順に固定する。どのタイル・どの角から作っても同じIDになるための正規化
            if (Compare(a, b) > 0) (a, b) = (b, a);
            if (Compare(b, c) > 0) (b, c) = (c, b);
            if (Compare(a, b) > 0) (a, b) = (b, a);

            A = a;
            B = b;
            C = c;
        }

        /// <summary>
        /// タイルの角（0〜5、角度60°×index）を表すIDを作る。
        /// 角index i には、方向 -i と 1-i の隣接タイルが集まる
        /// （角の角度60iを挟む2辺の向きが、それぞれ 60i±30° にあたるため）。
        /// </summary>
        public static HexCorner Of(HexCoord tile, int cornerIndex)
        {
            int i = ((cornerIndex % 6) + 6) % 6;
            return new HexCorner(tile, tile.Neighbor(-i), tile.Neighbor(1 - i));
        }

        /// <summary>
        /// 角のワールド座標。3タイルの中心の平均がちょうど共有する角になる。
        /// </summary>
        /// <param name="tileSize">タイルの外接円半径（HexCoord.ToWorldPositionと同じ意味）。</param>
        /// <param name="y">高さ。タイル上面へ乗せるなら HexMeshBuilder.TopY を使う。</param>
        public Vector3 ToWorldPosition(float tileSize = 1f, float y = 0f)
        {
            Vector3 sum = A.ToWorldPosition(tileSize)
                        + B.ToWorldPosition(tileSize)
                        + C.ToWorldPosition(tileSize);
            return new Vector3(sum.x / 3f, y, sum.z / 3f);
        }

        // ── 比較・等値 ─────────────────────────────────────────────
        // 決定的な並び順を持たせるのは、輪郭の開始点や輪の並びを毎回同じにするため。

        private static int Compare(HexCoord a, HexCoord b)
        {
            if (a.q != b.q) return a.q < b.q ? -1 : 1;
            if (a.r != b.r) return a.r < b.r ? -1 : 1;
            return 0;
        }

        public int CompareTo(HexCorner other)
        {
            int c = Compare(A, other.A); if (c != 0) return c;
            c     = Compare(B, other.B); if (c != 0) return c;
            return  Compare(C, other.C);
        }

        public bool Equals(HexCorner other) => A == other.A && B == other.B && C == other.C;
        public override bool Equals(object obj) => obj is HexCorner h && Equals(h);
        public override int GetHashCode() => HashCode.Combine(A, B, C);

        public static bool operator ==(HexCorner a, HexCorner b) => a.Equals(b);
        public static bool operator !=(HexCorner a, HexCorner b) => !a.Equals(b);

        public override string ToString() => $"Corner[{A},{B},{C}]";
    }
}
