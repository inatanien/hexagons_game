// 役割: Hex グリッドの座標を表す値型。
//       Cube Coordinates (q, r, s) を使い、隣接取得・距離計算・回転・
//       Unity ワールド座標との相互変換を提供する。
//       純粋 C# のため MonoBehaviour に依存せず単体テスト可能。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.HexGrid
{
    /// <summary>
    /// Cube Coordinates によるHex座標。q + r + s = 0 を常に満たす。
    /// 参考: https://www.redblobgames.com/grids/hexagons/
    /// </summary>
    [Serializable]
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        public readonly int q;
        public readonly int r;
        public readonly int s;

        public HexCoord(int q, int r)
        {
            this.q = q;
            this.r = r;
            this.s = -q - r;
        }

        public HexCoord(int q, int r, int s)
        {
            if (q + r + s != 0)
                throw new ArgumentException($"Cube Coordinates の制約違反: q({q})+r({r})+s({s}) != 0");
            this.q = q;
            this.r = r;
            this.s = s;
        }

        public static readonly HexCoord Zero = new HexCoord(0, 0);

        // ── 6方向（フラットトップ配置） ──────────────────────────────
        private static readonly HexCoord[] Directions = new HexCoord[]
        {
            new HexCoord( 1,  0), // 0: 右
            new HexCoord( 1, -1), // 1: 右上
            new HexCoord( 0, -1), // 2: 左上
            new HexCoord(-1,  0), // 3: 左
            new HexCoord(-1,  1), // 4: 左下
            new HexCoord( 0,  1), // 5: 右下
        };

        /// <summary>direction 0〜5 の隣接セル座標を返す。</summary>
        public HexCoord Neighbor(int direction)
        {
            return this + Directions[((direction % 6) + 6) % 6];
        }

        /// <summary>隣接する全6セルを返す。</summary>
        public IEnumerable<HexCoord> Neighbors()
        {
            for (int i = 0; i < 6; i++)
                yield return Neighbor(i);
        }

        // ── 距離 ────────────────────────────────────────────────────
        public int DistanceTo(HexCoord other)
        {
            return (Math.Abs(q - other.q) + Math.Abs(r - other.r) + Math.Abs(s - other.s)) / 2;
        }

        // ── 回転（原点中心、60°単位） ────────────────────────────────
        /// <summary>原点を中心に 60°×steps だけ右回転する。</summary>
        public HexCoord RotateRight(int steps = 1)
        {
            HexCoord c = this;
            steps = ((steps % 6) + 6) % 6;
            for (int i = 0; i < steps; i++)
                c = new HexCoord(-c.s, -c.q, -c.r);
            return c;
        }

        /// <summary>原点を中心に 60°×steps だけ左回転する。</summary>
        public HexCoord RotateLeft(int steps = 1) => RotateRight(6 - steps);

        // ── 指定範囲内の全座標 ──────────────────────────────────────
        /// <summary>原点から radius 以内の全HexCoord を返す。</summary>
        public static IEnumerable<HexCoord> Range(int radius)
        {
            for (int q = -radius; q <= radius; q++)
            {
                int r1 = Math.Max(-radius, -q - radius);
                int r2 = Math.Min(radius, -q + radius);
                for (int r = r1; r <= r2; r++)
                    yield return new HexCoord(q, r);
            }
        }

        // ── Unityワールド座標変換（フラットトップ、サイズ1） ────────────
        /// <summary>
        /// HexCoord → Unity Vector3（Y=0の平面上）。
        /// size はタイルの外接円半径。
        /// </summary>
        public Vector3 ToWorldPosition(float size = 1f)
        {
            float x = size * (3f / 2f * q);
            float z = size * (MathF.Sqrt(3f) * (r + q / 2f));
            return new Vector3(x, 0f, z);
        }

        /// <summary>ワールド座標 → 最近傍の HexCoord（フラットトップ）。</summary>
        public static HexCoord FromWorldPosition(Vector3 worldPos, float size = 1f)
        {
            float q = (2f / 3f * worldPos.x) / size;
            float r = (-1f / 3f * worldPos.x + MathF.Sqrt(3f) / 3f * worldPos.z) / size;
            return RoundToHex(q, r, -q - r);
        }

        // 浮動小数 Cube座標を最近傍の整数 HexCoord に丸める
        private static HexCoord RoundToHex(float fq, float fr, float fs)
        {
            int rq = Mathf.RoundToInt(fq);
            int rr = Mathf.RoundToInt(fr);
            int rs = Mathf.RoundToInt(fs);

            float dq = Math.Abs(rq - fq);
            float dr = Math.Abs(rr - fr);
            float ds = Math.Abs(rs - fs);

            if (dq > dr && dq > ds)
                rq = -rr - rs;
            else if (dr > ds)
                rr = -rq - rs;
            // else rs を補正（自動的に q+r+s=0 が成立）

            return new HexCoord(rq, rr, -rq - rr);
        }

        // ── 演算子 ─────────────────────────────────────────────────
        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.q + b.q, a.r + b.r);
        public static HexCoord operator -(HexCoord a, HexCoord b) => new HexCoord(a.q - b.q, a.r - b.r);
        public static HexCoord operator *(HexCoord a, int k)       => new HexCoord(a.q * k, a.r * k);

        // ── 等値比較 ───────────────────────────────────────────────
        public bool Equals(HexCoord other) => q == other.q && r == other.r;
        public override bool Equals(object obj) => obj is HexCoord h && Equals(h);
        public override int GetHashCode() => HashCode.Combine(q, r);
        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);

        public override string ToString() => $"Hex({q},{r},{s})";
    }
}
