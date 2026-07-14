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
        None   = 0,
        Tree   = 1,
        House  = 2,
        Water  = 3,
        Flower = 4,
    }

    // タイル内部に描く分割線の種類
    public enum TileDividerType
    {
        None         = 0,
        StraightH    = 1, // X軸方向の直線（Forest_Edge 旧バージョン）
        BendE        = 2, // East頂点のV字線（River_Bend 旧バージョン）
        Hex6Spokes   = 3, // 6方向スポーク（EdgeType 色で6分割を表現）
        VerticalPair = 4, // 上下辺を3等分した分割点を結ぶ2本の縦線（川直線タイル用）
        BendPair     = 5, // dir0辺→dir5辺 の3等分点を結ぶL字折れ線2本（川曲がりタイル用）
        BendPairWide = 6, // dir0辺→dir4辺 の3等分点を結ぶ下向きアーチ2本（川・緩カーブ用）
    }

    [CreateAssetMenu(fileName = "TileType_", menuName = "ElfVillage/TileType")]
    public class TileType : ScriptableObject
    {
        [Header("基本情報")]
        public string tileName     = "Unnamed";
        public Color  tileColor    = Color.white;
        [Tooltip("同一カテゴリのタイル同士を接続扱いにする（例: \"River\", \"Forest\"）。空欄は同一 SO のみ接続")]
        public string tileCategory = "";
        [Tooltip("地面に貼るテクスチャ（空欄可）。設定すると tileColor の単色塗りの代わりにこの画像を表示する。\n" +
                  "空欄の場合は従来どおり tileColor の単色のまま。")]
        public Texture2D groundTexture = null;

        [Header("有効フラグ")]
        [Tooltip("false にするとデッキ・ワールド生成の両方に出現しなくなる。データは保持されるので後から true に戻せる")]
        public bool isActive = true;

        [Header("プロップ設定")]
        public TilePropType propType  = TilePropType.None;
        [Tooltip("生成するプロップの数（Tree: 木の本数 / Flower: Billboardの枚数）")]
        public int          propCount = 1;
        [Tooltip("木のバリエーションプレハブ（propType=Tree のみ有効・各要素空欄可）。\n" +
                  "木を配置するたびにこの中からランダムに1つ選ばれる。\n" +
                  "空欄のバリエーションは標準プリミティブの木にフォールバックするので、\n" +
                  "後から実モデルに差し替えるまでは何も設定しなくてよい。")]
        public GameObject[] treeVariantPrefabs = new GameObject[10];
        [Tooltip("花畑タイルの Billboard 用スプライト（propType=Flower のみ有効・空欄可）。\n" +
                  "空欄の場合はコードで生成した仮スプライトにフォールバックする。")]
        public Texture2D billboardSprite = null;

        [Header("分割線")]
        public TileDividerType dividerType = TileDividerType.None;

        [Header("接続コネクター設定")]
        [Tooltip("コネクターの色。alpha=0 のとき tileColor を自動使用する")]
        public Color      connectorColor  = new Color(0f, 0f, 0f, 0f);
        [Tooltip("カスタムコネクタープレハブ。null のとき標準 Cube コネクターを使用")]
        public GameObject connectorPrefab = null;

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
