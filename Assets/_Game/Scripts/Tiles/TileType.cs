// 役割: タイルの種類を定義する ScriptableObject。
//       辺の接続種別（EdgeType）と外観情報を保持し、データ駆動でタイルを追加できる。

using UnityEngine;

namespace ElfVillage.Tiles
{
    public enum EdgeType
    {
        None = 0,
        Forest,
        Field,
        River,
        Road,
    }

    public enum TilePropType
    {
        None  = 0,
        Tree  = 1,
        House = 2,
        Water = 3,
    }

    // タイル内部に描く分割線の種類
    public enum TileDividerType
    {
        None       = 0,
        StraightH  = 1, // X軸方向の直線（Forest_Edge 旧バージョン）
        BendE      = 2, // East頂点のV字線（River_Bend 旧バージョン）
        Hex6Spokes = 3, // 6方向スポーク（EdgeType 色で6分割を表現）
    }

    [CreateAssetMenu(fileName = "TileType_", menuName = "ElfVillage/TileType")]
    public class TileType : ScriptableObject
    {
        [Header("基本情報")]
        public string tileName = "Unnamed";
        public Color tileColor = Color.white;

        [Header("プロップ設定")]
        public TilePropType propType  = TilePropType.None;
        [Tooltip("生成するプロップの数（Tree のみ有効）")]
        public int          propCount = 1;

        [Header("分割線")]
        public TileDividerType dividerType = TileDividerType.None;

        [Header("辺の接続種別（direction 0〜5）")]
        [Tooltip("direction 0=右, 1=右上, 2=左上, 3=左, 4=左下, 5=右下")]
        public EdgeType[] edges = new EdgeType[6];

        public EdgeType GetEdge(int direction)
        {
            int d = ((direction % 6) + 6) % 6;
            return edges[d];
        }
    }
}
