// 役割: 1枚のタイルが各カテゴリへ寄与する「エフェクト用の重み」を算出する純粋関数群。
//       複合タイル（森＋花畑など）は HasEffectCategory() だとどちらのカテゴリでも
//       1枚として数えられ、物理1枚が複数カテゴリで合計2枚分の演出条件へ寄与していた。
//       その結果、序盤の少ない枚数でも複数の演出条件を同時に満たしてしまう（Stage 8）。
//       ここでは TileElement.areaWeight を正規化して按分することで、
//       1枚のタイルが全カテゴリ合計でちょうど1.0だけ寄与するようにする。
//
//       例: TileType_ForestFlower（Forest 0.7 / Field 0.3）
//           → Forest 0.7、Field 0.3（合計1.0）
//           単一属性タイル（areaWeight 1.0）は従来どおり 1.0（挙動不変）。
//
//       ★ここで算出する重みは「演出の発生しきい値」専用。
//         クエスト進捗・接続判定・デッキ抽選には一切使わない。
//         それらは従来どおり実タイル数（int）で扱う（ForestGrowthMetrics.LargestClusterSize等）。
//
//       areaWeight は visualOnly 要素にも設定されているため EffectiveElements を情報源とする
//       （HasEffectCategory/GetEffectCategories と同じ「見た目上そのカテゴリを持つか」の基準）。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class TerrainEffectWeight
    {
        /// <summary>
        /// このタイルが指定カテゴリへ寄与する重み（0〜1）。
        /// elements[]がある場合はareaWeightを正規化して按分し、
        /// 未設定のlegacyタイルは従来どおり該当カテゴリなら1.0を返す。
        /// </summary>
        public static float Of(TileType type, TileCategory category)
        {
            if (type == null) return 0f;

            int   elementCount = 0;
            int   matchCount   = 0;
            float totalWeight  = 0f;
            float matchWeight  = 0f;

            foreach (var e in type.EffectiveElements)
            {
                // EffectiveElements は variant != null のみを返すため variant は常に有効。
                float w = HexTile.SafeWeight(e.areaWeight);
                elementCount++;
                totalWeight += w;

                if (e.variant.category == category)
                {
                    matchCount++;
                    matchWeight += w;
                }
            }

            if (elementCount > 0)
            {
                // 通常経路: areaWeightで按分する。
                if (totalWeight > 0f) return matchWeight / totalWeight;

                // 全要素のareaWeightが0（不正データ）の場合は要素数で均等割りする
                // （HexTile.SpawnPropsForElementsのフォールバックと同じ考え方）。
                return (float)matchCount / elementCount;
            }

            // elements未設定のlegacyタイルは従来どおり1枚として数える（挙動不変）。
            return type.HasEffectCategory(category) ? 1f : 0f;
        }

        /// <summary>タイル集合が指定カテゴリへ寄与する重みの合計。</summary>
        public static float SumFor(IEnumerable<HexTile> tiles, TileCategory category)
        {
            if (tiles == null) return 0f;

            float sum = 0f;
            foreach (var tile in tiles)
            {
                if (tile == null) continue;
                sum += Of(tile.Data.tileType, category);
            }
            return sum;
        }
    }
}
