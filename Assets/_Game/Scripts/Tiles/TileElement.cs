// 役割: タイルが持つ「地形要素」1つ分。categoryはTileElement自身では保持せず、
//       variantが唯一の情報源とすることで、両者が食い違う矛盾データを構造的に防ぐ。

using UnityEngine;

namespace ElfVillage.Tiles
{
    [System.Serializable]
    public class TileElement
    {
        [Tooltip("見た目・分類の唯一の情報源。未設定の場合、この要素は無効カテゴリとして扱われる")]
        public TerrainVariantDefinition variant;
        [Range(0f, 1f)]
        [Tooltip("このタイル内でこの要素が占める比重。プロップ数の配分等に使用")]
        public float areaWeight = 0.5f;

        /// <summary>
        /// variant未設定の場合はnullを返す。TileCategoryのdefault値（Forest）への
        /// フォールバックは行わない。未設定データを誤って森として扱わないための設計。
        /// </summary>
        public TileCategory? Category => variant != null ? variant.category : (TileCategory?)null;
    }
}
