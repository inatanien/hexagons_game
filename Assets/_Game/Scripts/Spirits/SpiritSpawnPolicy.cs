// 役割: 精霊を生成してよいかを判定する純粋関数（Stage 15）。
//
//       ★判定に使うのは「homeとして採用する対象クラスタ自身のサイズ」であること
//         本編では森タイルが1枚置かれるたびにTerrainGrowthEventが発行される
//         （ForestGrowthEvaluatorに閾値は無い）。そのまま生成すると、
//         プレイヤーが最初の森タイルを置いた瞬間に1枚だけの森へ住人が現れてしまう。
//
//         また、世界全体の最大クラスタサイズで判定してもいけない。
//         別の場所に大きな森があるとき、小さなクラスタの変化でも条件を満たしてしまい、
//         結果として1枚の森がhomeになる。
//         判定対象は必ず「これからhomeになるタイル集合そのもの」の枚数にすること。

using UnityEngine;

namespace ElfVillage.Spirits
{
    public static class SpiritSpawnPolicy
    {
        /// <summary>最小クラスタサイズが不正だったときに使う安全な下限（1枚＝必ず何かある）。</summary>
        public const int MinimumAllowedClusterSize = 1;

        /// <summary>
        /// 精霊を生成してよいか。
        /// </summary>
        /// <param name="alreadySpawned">既に生成済みか（Stage 15では1体だけ）。</param>
        /// <param name="affectedClusterSize">
        /// homeとして採用する対象クラスタの有効なユニークタイル数。
        /// 世界全体の最大クラスタサイズではないこと。
        /// </param>
        /// <param name="minimumClusterSize">生成に必要な最小枚数。不正値は安全側へ補正する。</param>
        public static bool ShouldSpawn(bool alreadySpawned, int affectedClusterSize, int minimumClusterSize)
        {
            if (alreadySpawned) return false;

            // 0以下や極端な値は「1枚以上あれば生成」へ倒す。
            // 生成が完全に止まる（＝精霊が永久に現れない）方が不具合として気づきにくいため、
            // 不正設定では止めるのではなく最小限の条件で通す。
            int minimum = SafeMinimum(minimumClusterSize);

            return affectedClusterSize >= minimum;
        }

        /// <summary>最小クラスタサイズの健全化。負値・0は1へ、極端な値は上限で丸める。</summary>
        public static int SafeMinimum(int minimumClusterSize)
            => Mathf.Clamp(minimumClusterSize, MinimumAllowedClusterSize, 10000);
    }
}
