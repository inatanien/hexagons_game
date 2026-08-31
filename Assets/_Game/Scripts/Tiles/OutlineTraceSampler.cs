// 役割: 閉じた輪の上を走る「光の先端と尾」の点列を切り出す純粋関数。
//       Renderer・GameObject・Time・Coroutineには一切依存しない。
//
//       ★頂点番号ではなく「輪に沿った距離」で扱う。
//         頂点番号で補間すると、辺の長さが揃っていない輪へ使ったときに
//         速度が場所によって変わり、尾の長さも見た目で伸び縮みする。
//         距離で扱えば、どんな形の輪でも見た目の速度と尾の長さが一定になる。
//
//       ★輪の終端はまたいでよい。
//         距離は一周の長さで折り返すので、尾が輪の終わりから始まりへ回り込んでも
//         点列は隣り合う頂点を順に辿ったままで、線が輪の反対側へ飛ぶことはない。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class OutlineTraceSampler
    {
        /// <summary>閉じた輪の一周の長さ（最後の点から最初の点へ戻る分を含む）。</summary>
        public static float Perimeter(IReadOnlyList<Vector3> loop)
        {
            if (loop == null || loop.Count < 2) return 0f;

            float sum = 0f;
            for (int i = 0; i < loop.Count; i++)
                sum += SegmentLength(loop, i);
            return sum;
        }

        /// <summary>
        /// 輪に沿って startDistance から endDistance までを切り出し、results へ書き込む。
        /// 先頭が尾、末尾が光の先端になる（LineRendererの太さ・色のカーブと向きが揃う）。
        /// 距離は一周の長さで折り返すため、輪の終端をまたぐ指定もそのまま渡してよい。
        /// </summary>
        public static void Sample(IReadOnlyList<Vector3> loop, float startDistance, float endDistance,
                                   List<Vector3> results)
        {
            if (results == null) return;
            results.Clear();

            if (loop == null || loop.Count < 2) return;

            float perimeter = Perimeter(loop);
            if (perimeter <= 0f) return;

            // 尾が一周より長くなっても、輪を二重に描かない
            float span = Mathf.Clamp(endDistance - startDistance, 0f, perimeter);

            int   segment = 0;
            float offset  = 0f;
            Locate(loop, Wrap(startDistance, perimeter), ref segment, ref offset);

            results.Add(PointOnSegment(loop, segment, offset));

            float remainingInSegment = SegmentLength(loop, segment) - offset;
            float left = span;

            // 途中の頂点はそのまま拾う。こうすることで輪の角がそのまま線の角になる
            for (int guard = 0; guard <= loop.Count && left > remainingInSegment; guard++)
            {
                left    -= remainingInSegment;
                segment  = (segment + 1) % loop.Count;

                results.Add(loop[segment]);
                remainingInSegment = SegmentLength(loop, segment);

                // 長さ0の辺が続いても止まらないようにする
                if (remainingInSegment <= 0f) remainingInSegment = 0f;
            }

            results.Add(PointOnSegment(loop, segment, SegmentLength(loop, segment) - remainingInSegment + left));
        }

        // ── 小物 ──────────────────────────────────────────────────────

        private static float SegmentLength(IReadOnlyList<Vector3> loop, int index)
            => Vector3.Distance(loop[index], loop[(index + 1) % loop.Count]);

        private static Vector3 PointOnSegment(IReadOnlyList<Vector3> loop, int index, float distance)
        {
            Vector3 a      = loop[index];
            Vector3 b      = loop[(index + 1) % loop.Count];
            float   length = Vector3.Distance(a, b);
            if (length <= 0f) return a;

            return Vector3.Lerp(a, b, Mathf.Clamp01(distance / length));
        }

        /// <summary>輪に沿った距離が、どの辺のどこにあたるかを求める。</summary>
        private static void Locate(IReadOnlyList<Vector3> loop, float distance, ref int segment, ref float offset)
        {
            float remaining = distance;
            for (int i = 0; i < loop.Count; i++)
            {
                float length = SegmentLength(loop, i);
                if (remaining <= length || i == loop.Count - 1)
                {
                    segment = i;
                    offset  = Mathf.Clamp(remaining, 0f, length);
                    return;
                }
                remaining -= length;
            }
        }

        private static float Wrap(float distance, float perimeter)
        {
            float wrapped = distance % perimeter;
            return wrapped < 0f ? wrapped + perimeter : wrapped;
        }
    }
}
