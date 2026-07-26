// 役割: タイルの見た目を「乱数を使わずに」ばらつかせるための整数ハッシュ。
//
//       ★なぜ専用のハッシュが要るのか
//         木のseedは `q*92821 + r*68917 + i*40361` のような等差数列で作られている。
//         これをそのまま `% 10` すると、iが1増えるたびに一定の歩幅で値が進むため、
//         「重み付き抽選」に使うと特定のvariantだけが規則的に並んでしまう。
//         一度かき混ぜて（アバランチさせて）から使う必要がある。
//
//       ★なぜUnityEngine.Randomを使わないのか
//         このプロジェクトの見た目は全て HexCoord から決定論的に決まる。
//         そうしないと「配置ゴーストと実際に置いたタイルの見た目が一致しない」
//         「同じタイルを再生成すると別の森になる」という問題が起きる。

namespace ElfVillage.Tiles
{
    public static class TileVisualHash
    {
        /// <summary>
        /// 32bit整数をかき混ぜる。隣り合う入力が全く違う出力になることだけを目的とし、
        /// 暗号強度は要求しない（見た目のばらつき用）。
        /// </summary>
        public static uint Mix(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= 2747636419u;
                x *= 2654435769u;
                x ^= x >> 16;
                x *= 2654435769u;
                x ^= x >> 16;
                x *= 2654435769u;
                return x;
            }
        }

        /// <summary>タイル座標(q, r)からのハッシュ。同じタイルなら常に同じ値。</summary>
        public static uint Mix(int q, int r)
        {
            unchecked
            {
                return Mix((int)(Mix(q) ^ (Mix(r) * 2246822519u)));
            }
        }

        /// <summary>ハッシュを [0, 1) の実数へ写す。</summary>
        public static float Unit(uint hash) => (hash >> 8) / 16777216f;   // 上位24bitを使う
    }
}
