// 役割: 木のビルボード画像を「決定論的な重み付き抽選」で選ぶ純粋関数。
//
//       ★なぜ均等選択をやめるのか
//         本数を増やさずに森の密度を上げたい。横に広がる樹形の絵は隣の木との
//         隙間を埋めてくれるので、そちらを少し多めに出すと同じ本数でも森が詰まって見える。
//
//       ★なぜ細身を残すのか
//         横広だけにすると全部同じシルエットに見えて「描き割り」になってしまう。
//         尖った針葉樹が時々混ざることで、森に高さのリズムが出る。
//
//       ★重みは画像の「名前」で決める
//         Inspectorの配列順を入れ替えても森の見た目が変わらないようにするため、
//         配列の位置ではなくテクスチャ名から重みを引く。

using System;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class TreeVariantWeights
    {
        // 重みは百分率。既定の10種で合計100になるよう配分している。
        public const int WideWeight     = 13;   // 横広4種 → 13×4 = 52
        public const int StandardWeight = 10;   // 標準3種 → 10×3 = 30
        public const int SlimWeight     = 6;    // 細身3種 →  6×3 = 18

        // 名前の一部で判定する（"Tree_03_Wide_Dome" の連番が変わっても効くように）。
        private static readonly string[] s_WideNames =
            { "Rounded_Layered", "Wide_Dome", "Deep_Green_Oval", "Lime_Egg" };

        private static readonly string[] s_SlimNames =
            { "Columnar", "Compact_Conifer", "Slender_Conifer" };

        /// <summary>
        /// テクスチャ名から重みを引く。既知の分類に当てはまらない名前は標準扱いにする
        /// （将来画像が増えたときに、重み表を更新し忘れても木が消えないようにするため）。
        /// </summary>
        public static int WeightForName(string textureName)
        {
            if (string.IsNullOrEmpty(textureName)) return StandardWeight;

            foreach (var key in s_WideNames)
                if (Contains(textureName, key)) return WideWeight;

            foreach (var key in s_SlimNames)
                if (Contains(textureName, key)) return SlimWeight;

            return StandardWeight;
        }

        private static bool Contains(string haystack, string needle)
            => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>テクスチャ名の並びから重み配列を作る（配列の順序は入力と対応する）。</summary>
        public static int[] BuildWeights(string[] textureNames)
        {
            if (textureNames == null) return new int[0];

            var weights = new int[textureNames.Length];
            for (int i = 0; i < textureNames.Length; i++)
                weights[i] = WeightForName(textureNames[i]);
            return weights;
        }

        /// <summary>
        /// seedから決定論的に1つ選ぶ。同じseedなら常に同じ結果を返す。
        /// 区間は [0,w0), [w0,w0+w1), … と隙間なく連続させるため、抜けも重複も起きない。
        /// </summary>
        public static int Select(int[] weights, int seed)
        {
            if (weights == null || weights.Length == 0) return 0;

            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += Mathf.Max(0, weights[i]);

            // 全ての重みが0（または負）のときは均等抽選へ倒す。木が1本も出なくなるより良い。
            if (total <= 0) return (int)(TileVisualHash.Mix(seed) % (uint)weights.Length);

            int pick = (int)(TileVisualHash.Mix(seed) % (uint)total);

            int cumulative = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                cumulative += Mathf.Max(0, weights[i]);
                if (pick < cumulative) return i;
            }

            // ここへは到達しない（pick < total が保証されている）。念のための安全側。
            return weights.Length - 1;
        }
    }
}
