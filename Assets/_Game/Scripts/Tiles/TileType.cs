// 役割: タイルの種類を定義する ScriptableObject。
//       辺の接続種別（EdgeType）と外観情報を保持し、データ駆動でタイルを追加できる。

using System.Collections.Generic;
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

        [Header("複数要素タイル（任意・空なら従来どおり単一要素として扱われる）")]
        [Tooltip("このセッションではデータ追加のみ。接続判定・デッキ抽選・プロップ生成はまだこのフィールドを参照しない")]
        public TileElement[] elements;

        public EdgeType GetEdge(int direction)
        {
            int d = ((direction % 6) + 6) % 6;
            return edges[d];
        }

        // ── 有効カテゴリ取得（elements優先・legacyフォールバック） ───────────

        /// <summary>elements[]のうちvariantが設定されている項目のみを返す（無効要素は無視する）。
        /// VisualOnly要素も含む「見た目生成用」の全要素。HexTileのプロップ生成が参照する。</summary>
        public IEnumerable<TileElement> EffectiveElements
        {
            get
            {
                if (elements == null) yield break;
                foreach (var e in elements)
                    if (e != null && e.variant != null)
                        yield return e;
            }
        }

        /// <summary>EffectiveElementsのうち、visualOnlyではない（ゲームプレイに参加する）要素のみを返す。
        /// 接続判定・TileDeckのカテゴリ判定など、ゲームルールに関わる用途はこちらを使う。</summary>
        public IEnumerable<TileElement> GameplayElements
        {
            get
            {
                foreach (var e in EffectiveElements)
                    if (!e.visualOnly)
                        yield return e;
            }
        }

        /// <summary>
        /// このタイルが持つゲームプレイ上のカテゴリ集合を重複なく返す（VisualOnly要素のカテゴリは含まない）。
        /// elements[]に有効な要素が1件以上あればそれを情報源とし、legacyへはフォールバックしない
        /// （有効要素が全てVisualOnlyの場合は空集合を返す。legacyカテゴリを誤って復活させないため）。
        /// elements[]が未設定・空・有効要素0件の場合のみ、legacyのtileCategoryへフォールバックする。
        /// legacyカテゴリが変換不能な場合は何も返さない（Forest等のdefault値へは誤変換しない）。
        /// </summary>
        public IEnumerable<TileCategory> GetEffectiveCategories()
        {
            var seen = new HashSet<TileCategory>();
            bool anyElement = false;
            foreach (var e in EffectiveElements)
            {
                anyElement = true;
                if (e.visualOnly) continue;
                if (seen.Add(e.variant.category))
                    yield return e.variant.category;
            }
            if (anyElement) yield break;

            if (TryGetLegacyCategory(out TileCategory legacy))
                yield return legacy;
        }

        /// <summary>指定カテゴリを（elements優先・legacyフォールバック込みで）持っているか判定する。</summary>
        public bool HasCategory(TileCategory category)
        {
            foreach (var c in GetEffectiveCategories())
                if (c == category) return true;
            return false;
        }

        /// <summary>
        /// legacyのtileCategory（string）をTileCategoryへ変換する。空文字列や、
        /// TileCategoryのいずれの名前とも一致しない場合はfalseを返し、
        /// categoryへdefault値（Forest等）を誤って設定しない。
        /// </summary>
        public bool TryGetLegacyCategory(out TileCategory category)
        {
            if (!string.IsNullOrEmpty(tileCategory)
                && System.Enum.TryParse(tileCategory, true, out TileCategory parsed))
            {
                category = parsed;
                return true;
            }
            category = default;
            return false;
        }

        // edges[]とelements[]の整合性をチェックするだけの警告群。データの自動修正・保存ブロックは行わない。
        // elementsが未設定（null/空）のlegacy TileTypeは対象外（既存アセットに警告を出さないため）。
        private void OnValidate()
        {
            if (elements == null || elements.Length == 0) return;

            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i] != null && elements[i].variant == null)
                    Debug.LogWarning($"[TileType: {name}] elements[{i}] に variant が設定されていません。" +
                                      "未設定のままだと有効なカテゴリとして扱われません。", this);
            }

            // 有効な要素（variant設定済み）のうち、Gameplayに参加するもののカテゴリ一覧。
            // VisualOnly要素はedges整合性チェックの対象外（見た目専用のため辺を持つ必要がない）。
            var validCategories = new List<TileCategory>();
            foreach (var e in elements)
                if (e != null && e.variant != null && !e.visualOnly)
                    validCategories.Add(e.variant.category);

            // 1. 同一カテゴリの重複チェック
            var seen = new HashSet<TileCategory>();
            var duplicates = new HashSet<TileCategory>();
            foreach (var c in validCategories)
                if (!seen.Add(c)) duplicates.Add(c);
            foreach (var c in duplicates)
                Debug.LogWarning($"[TileType: {name}] elements に {c} カテゴリを持つ variant が重複しています。", this);

            var validCategorySet = new HashSet<TileCategory>(validCategories);

            // edges[]に登場するTileCategoryの集合
            var edgeCategories = new HashSet<TileCategory>();
            if (edges != null)
            {
                foreach (var edge in edges)
                {
                    var cat = TileCategoryMapping.FromEdgeType(edge);
                    if (cat.HasValue) edgeCategories.Add(cat.Value);
                }
            }

            // 2. edgesにあるがelementsにないカテゴリ
            foreach (var cat in edgeCategories)
            {
                if (!validCategorySet.Contains(cat))
                    Debug.LogWarning($"[TileType: {name}] elements に {cat} カテゴリがありませんが、" +
                                      $"edges に {cat} が含まれています。", this);
            }

            // 3. elementsにあるがedgesにないカテゴリ（Villageのように対応するEdgeTypeを持たないカテゴリは対象外）
            foreach (var cat in validCategorySet)
            {
                if (!CategoryHasEdgeMapping(cat)) continue;
                if (!edgeCategories.Contains(cat))
                    Debug.LogWarning($"[TileType: {name}] elements に {cat} カテゴリがありますが、" +
                                      "edges に対応する辺がありません。", this);
            }
        }

        /// <summary>このカテゴリに対応するEdgeTypeが存在するか（Villageのような要素はfalse）。</summary>
        private static bool CategoryHasEdgeMapping(TileCategory category)
        {
            foreach (EdgeType edge in System.Enum.GetValues(typeof(EdgeType)))
            {
                var mapped = TileCategoryMapping.FromEdgeType(edge);
                if (mapped.HasValue && mapped.Value == category) return true;
            }
            return false;
        }
    }
}
