// 役割: タイル1枚ぶんの木陰の「向き・反転・大きさ」をタイル座標から決定論的に求める純粋関数。
//
//       ★なぜ向きと反転をばらすのか
//         同じ木陰の絵をそのまま並べると、六角形の格子模様が地面に浮かび上がってしまう。
//         タイルごとに回して左右反転させると、同じ1枚のテクスチャでも
//         「たまたまそういう形の影が落ちている」ように見える。
//
//       ★なぜ乱数を使わないのか
//         配置ゴーストと実際に置いたタイルで木陰の形が変わってはいけない。
//         再生成やセーブ復元でも同じ影である必要がある。

using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class TileShadeLayout
    {
        /// <summary>大きさのばらつき幅（±8%）。これ以上広げると隣タイルとの重なり量が揃わなくなる。</summary>
        public const float SizeJitter = 0.08f;

        /// <summary>木陰の面内回転（0〜360度）。</summary>
        public static float RotationDeg(int q, int r)
            => TileVisualHash.Unit(TileVisualHash.Mix(q, r)) * 360f;

        /// <summary>左右反転するか。回転だけでは作れない形の違いを足す。</summary>
        public static bool IsMirrored(int q, int r)
            => (TileVisualHash.Mix(q + 7919, r - 104729) & 1u) == 1u;

        /// <summary>大きさの倍率（1±SizeJitter）。</summary>
        public static float SizeMultiplier(int q, int r)
        {
            float u = TileVisualHash.Unit(TileVisualHash.Mix(q - 40361, r + 92821));
            return 1f - SizeJitter + u * (SizeJitter * 2f);
        }

        /// <summary>木陰を地面から浮かせる量。木（HexTile.PropLiftY = 0.01）より小さくして、木の下に敷く。</summary>
        public const float LiftY = 0.004f;

        /// <summary>
        /// 木陰を置くタイルローカルY。タイル上面のごくわずか上に置いてZ-fightingを防ぐ。
        /// 上面の定義は HexMeshBuilder.TopY に一本化してある（自前で *0.5 を書かない）。
        /// </summary>
        public static float LocalY(float tileHeight) => HexMeshBuilder.TopY(tileHeight) + LiftY;
    }
}
