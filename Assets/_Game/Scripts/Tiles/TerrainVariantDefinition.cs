// 役割: 地形カテゴリ1つ分の見た目（将来的には非見た目メタデータも）を持つ、
//       複数のTileTypeから再利用可能なバリアント定義。
//       同じForestカテゴリでも「若い森」「古い森」など複数アセットを用意できる。

using UnityEngine;

namespace ElfVillage.Tiles
{
    [CreateAssetMenu(fileName = "TerrainVariant_", menuName = "ElfVillage/TerrainVariantDefinition")]
    public class TerrainVariantDefinition : ScriptableObject
    {
        [Header("分類")]
        public TileCategory category;
        [Tooltip("表示・識別用の名前（例: \"若い森\"）。ロジックには使用しない")]
        public string variantName = "";

        [Header("見た目")]
        public TilePropType propType = TilePropType.None;
        [Tooltip("生成するプロップの数（Tree: 木の本数 / Flower: Billboardの枚数）")]
        public int propCount = 1;
        [Tooltip("木のバリエーションプレハブ（propType=Tree のみ有効・各要素空欄可）")]
        public GameObject[] propPrefabs;
        [Tooltip("花畑タイルの Billboard 用スプライト（propType=Flower のみ有効・空欄可）")]
        public Texture2D billboardSprite;
    }
}
