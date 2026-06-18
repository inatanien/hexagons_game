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

    [CreateAssetMenu(fileName = "TileType_", menuName = "ElfVillage/TileType")]
    public class TileType : ScriptableObject
    {
        [Header("基本情報")]
        public string tileName = "Unnamed";
        public Color tileColor = Color.white;

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
