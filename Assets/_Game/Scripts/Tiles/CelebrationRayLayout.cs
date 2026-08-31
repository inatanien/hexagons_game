// 役割: 祝福の光柱をどこに立てるかを決める純粋関数。
//       Renderer・GameObject・Time・Coroutineには依存しない。
//
//       ★立てるのは外周の「辺の中点」。
//         距離で等間隔に割ると、辺の途中の中途半端な位置に落ちて六角形のリズムから外れる。
//         中点なら、ヘクスの外周は辺の長さが揃っているので自然に等間隔になる。
//
//       ★本数は辺の数に比例させず、上限を持たせて間引く。
//         大きなクラスターで比例させると光の壁になり、
//         「祝福された一帯」ではなく「囲いを立てた」ように見えてしまう。
//         間引きは端を切り捨てず、輪全体から均等に取る。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class CelebrationRayLayout
    {
        /// <summary>
        /// 閉じた輪から、光柱を立てる位置（各辺の中点）を選ぶ。
        /// </summary>
        /// <param name="density">1辺1本を基準にした割合。1で全辺、0.5で半分。</param>
        /// <param name="minCount">下限。小さすぎる輪でも寂しくならないようにする。</param>
        /// <param name="maxCount">上限。大きなクラスターで光の壁にしないための歯止め。</param>
        public static void SelectPositions(IReadOnlyList<Vector3> loop, float density,
                                            int minCount, int maxCount, List<Vector3> results)
        {
            if (results == null) return;
            results.Clear();

            if (loop == null || loop.Count < 3) return;   // 輪にならない

            int edgeCount = loop.Count;                   // 閉じた輪では 辺の数 == 点の数
            int wanted    = Mathf.RoundToInt(edgeCount * Mathf.Clamp01(density));

            int limit = Mathf.Min(edgeCount, Mathf.Max(1, maxCount));
            int count = Mathf.Clamp(wanted, Mathf.Max(1, Mathf.Min(minCount, limit)), limit);

            for (int i = 0; i < count; i++)
            {
                // 端を切り捨てず、輪全体から均等に拾う
                int edge = Mathf.FloorToInt((float)i * edgeCount / count) % edgeCount;
                results.Add(Midpoint(loop, edge));
            }
        }

        /// <summary>辺の中点。閉じた輪なので最後の点から最初の点へ戻る辺も含む。</summary>
        private static Vector3 Midpoint(IReadOnlyList<Vector3> loop, int edge)
            => (loop[edge] + loop[(edge + 1) % loop.Count]) * 0.5f;
    }
}
