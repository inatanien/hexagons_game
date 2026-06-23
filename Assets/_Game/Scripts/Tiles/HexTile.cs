// 役割: シーン上の1枚のHexタイルを表す MonoBehaviour。
//       TileData を保持し、見た目（色・回転）の反映と
//       配置済みフラグを管理する。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class HexTile : MonoBehaviour
    {
        [SerializeField] private MeshFilter   meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;
        [SerializeField] private MeshCollider meshCollider;

        [Header("メッシュ設定")]
        [SerializeField] private float outerRadius = 0.95f;
        [SerializeField] private float tileHeight  = 0.15f;

        [Header("配置アニメーション")]
        [SerializeField] private AnimationCurve placementCurve = new AnimationCurve(
            new Keyframe(0f,    0f,   0f,  6f),
            new Keyframe(0.65f, 1.15f, 0f,  0f),
            new Keyframe(1f,    1f,   0f,  0f));

        private Renderer tileRenderer;
        private Color _defaultColor;
        private bool _isAvailable;

        // 世界生成タイプ（プレイヤーが配置する前に事前割り当てされる）
        private TileType _worldType;
        private GameObject _worldPropsRoot;

        // 接続状態（6方向・同種タイルと隣接しているか）
        private readonly bool[] _connectedEdges = new bool[6];

        // TileConnectionFX が自己初期化するために参照する
        public float OuterRadius => outerRadius;
        public float TileHeight  => tileHeight;

        private void Awake()
        {
            tileRenderer = meshRenderer;
            // sharedMaterial から元色をキャッシュ（material を触る前に取得しないとインスタンスが生まれる）
            _defaultColor = tileRenderer != null ? tileRenderer.sharedMaterial.color : Color.gray;
            Mesh mesh = HexMeshBuilder.Build(outerRadius, tileHeight);
            if (meshFilter   != null) meshFilter.sharedMesh   = mesh;
            if (meshCollider != null) meshCollider.sharedMesh  = mesh;

            // TileConnectionFX は Awake 内で GetComponent<HexTile>() を使って自己初期化する
            gameObject.AddComponent<TileConnectionFX>();
        }

        public TileData Data { get; private set; }
        public bool IsPlaced { get; private set; }

        public void Initialize(HexCoord coord, float tileSize)
        {
            Data = new TileData(coord, null, 0);
            transform.position = coord.ToWorldPosition(tileSize);
            IsPlaced = false;
            // 未配置タイルは最初は非表示
            if (tileRenderer  != null) tileRenderer.enabled  = false;
            if (meshCollider  != null) meshCollider.enabled  = false;
        }

        /// <summary>世界生成時にタイルタイプを事前割り当てする。プレイヤー配置より前に呼ぶ。</summary>
        public void SetWorldType(TileType type)
        {
            _worldType = type;
        }

        public void Place(TileType tileType, int rotation)
        {
            // 世界プロップをクリアしてプレイヤーのタイルに置き換える
            DestroyWorldProps();
            Data = new TileData(Data.coord, tileType, rotation);
            IsPlaced = true;
            // 配置済みタイルはクリック対象から除外（コライダー無効 → 左クリックでカメラパン可能になる）
            if (meshCollider != null) meshCollider.enabled = false;
            ApplyVisual();
            SpawnPropsFor(tileType, transform);
            StartCoroutine(PlacementAnim());
        }

        // ── プロップ生成（プレイヤー配置・世界生成共通）──────────────────────

        private void SpawnPropsFor(TileType type, Transform parent)
        {
            if (type == null) return;
            switch (type.propType)
            {
                case TilePropType.Tree:   SpawnTrees(type, parent);   break;
                case TilePropType.House:  SpawnHouse(parent);         break;
                case TilePropType.Water:  SpawnWater(type, parent);  break;
                case TilePropType.Flower: SpawnFlowers(type, parent); break;
            }
            SpawnDividers(type, parent);
        }

        // ── 分割線 ───────────────────────────────────────────────────

        // HexCoord の方向インデックス(0〜5) → XZ平面での世界角度（度、+X軸基準）
        // ToWorldPosition の式から導出: dir0→30°, dir1→330°, dir2→270°,
        //                               dir3→210°, dir4→150°, dir5→90°
        private static readonly float[] s_DirToWorldAngle = { 30f, 330f, 270f, 210f, 150f, 90f };

        // インスタンスメソッドからは serialized fields の値を渡して static を呼ぶ
        private void SpawnDividers(TileType type, Transform parent)
            => SpawnDividersFor(type, parent, outerRadius, tileHeight);

        /// <summary>
        /// プレビュー用プロップ生成。TilePlacementPreview から呼ぶ。
        /// 座標ハッシュを使わず均等配置するため実際の配置と位置が若干異なる。
        /// Water は川岸ラインのみ表示（パーティクルは省略）。
        /// </summary>
        public static void SpawnPropsPreview(TileType type, Transform parent,
                                              float outerRadius = 0.95f, float tileHeight = 0.15f)
        {
            if (type == null) return;
            switch (type.propType)
            {
                case TilePropType.Tree:   SpawnTreesStatic(type, parent, tileHeight);               break;
                case TilePropType.House:  SpawnHouseStatic(parent, tileHeight);                      break;
                case TilePropType.Flower: SpawnFlowersStatic(type, parent, tileHeight);             break;
                case TilePropType.Water:  SpawnWaterPreview(type, parent, outerRadius, tileHeight); break;
            }
        }

        private static void SpawnTreesStatic(TileType type, Transform parent, float tileHeight)
        {
            int   count  = Mathf.Max(1, type.propCount);
            float ground = tileHeight + 0.01f;
            for (int i = 0; i < count; i++)
            {
                float angle  = i * (360f / count) * Mathf.Deg2Rad;
                float radius = count > 1 ? 0.45f : 0f;
                var   offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                trunk.transform.SetParent(parent);
                trunk.transform.localPosition = offset + new Vector3(0f, ground + 0.20f, 0f);
                trunk.transform.localScale    = new Vector3(0.12f, 0.22f, 0.12f);
                SetPropMaterial(trunk, new Color(0.42f, 0.26f, 0.10f));

                var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.transform.SetParent(parent);
                crown.transform.localPosition = offset + new Vector3(0f, ground + 0.60f, 0f);
                crown.transform.localScale    = new Vector3(0.45f, 0.52f, 0.45f);
                SetPropMaterial(crown, new Color(0.10f, 0.44f, 0.10f));
            }
        }

        private static void SpawnHouseStatic(Transform parent, float tileHeight)
        {
            float ground = tileHeight + 0.01f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(parent);
            body.transform.localPosition = new Vector3(0f, ground + 0.22f, 0f);
            body.transform.localScale    = new Vector3(0.52f, 0.44f, 0.52f);
            SetPropMaterial(body, new Color(0.90f, 0.82f, 0.65f));

            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.SetParent(parent);
            roof.transform.localPosition = new Vector3(0f, ground + 0.58f, 0f);
            roof.transform.localScale    = new Vector3(0.62f, 0.16f, 0.62f);
            roof.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            SetPropMaterial(roof, new Color(0.68f, 0.22f, 0.12f));
        }

        private static void SpawnFlowersStatic(TileType type, Transform parent, float tileHeight)
        {
            int   count  = Mathf.Max(1, type.propCount);
            float ground = tileHeight + 0.01f;
            for (int i = 0; i < count; i++)
            {
                float angle  = i * (360f / count) * Mathf.Deg2Rad;
                var   offset = new Vector3(Mathf.Cos(angle) * 0.35f, 0f, Mathf.Sin(angle) * 0.35f);
                var   color  = FlowerPetalColor(i);

                var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stem.transform.SetParent(parent);
                stem.transform.localPosition = offset + new Vector3(0f, ground + 0.07f, 0f);
                stem.transform.localScale    = new Vector3(0.03f, 0.07f, 0.03f);
                SetPropMaterial(stem, new Color(0.25f, 0.55f, 0.15f));

                var petal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                petal.transform.SetParent(parent);
                petal.transform.localPosition = offset + new Vector3(0f, ground + 0.17f, 0f);
                petal.transform.localScale    = new Vector3(0.16f, 0.08f, 0.16f);
                SetPropMaterial(petal, color);

                var center = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                center.transform.SetParent(parent);
                center.transform.localPosition = offset + new Vector3(0f, ground + 0.19f, 0f);
                center.transform.localScale    = new Vector3(0.07f, 0.06f, 0.07f);
                SetPropMaterial(center, new Color(1.0f, 0.88f, 0.1f));
            }
        }

        // 川の2辺を探してベジェ川岸ラインを生成（パーティクルなし、プレビュー用）
        private static void SpawnWaterPreview(TileType type, Transform parent,
                                               float outerRadius, float tileHeight)
        {
            float inRadius = outerRadius * 0.866f;
            var   riverEdges   = new List<int>();
            var   fallbackEdges = new List<int>();
            for (int d = 0; d < 6; d++)
            {
                var e = type.GetEdge(d);
                if (e == EdgeType.River) riverEdges.Add(d);
                else if (e != EdgeType.None && e != EdgeType.Field && e != EdgeType.Forest)
                    fallbackEdges.Add(d);
            }

            int edgeA, edgeB;
            var src = riverEdges.Count >= 2 ? riverEdges
                    : fallbackEdges.Count >= 2 ? fallbackEdges
                    : null;

            if (src != null) { edgeA = src[0]; edgeB = src[1]; }
            else             { edgeA = 0;       edgeB = 1; }   // 辺情報がない場合の fallback

            CreateWaterFlowPreview(parent,
                EdgeCenter(edgeA, inRadius), EdgeCenter(edgeB, inRadius),
                outerRadius, tileHeight);
        }

        // SpawnWater の川岸ライン部分だけを静的に生成（パーティクルは含まない）
        private static void CreateWaterFlowPreview(Transform parent, Vector3 edgeA, Vector3 edgeB,
                                                    float outerRadius, float tileHeight)
        {
            float bankOffset = outerRadius * 0.25f;
            float y          = tileHeight + 0.01f;

            bool    isStraight = ((edgeA + edgeB) * 0.5f).sqrMagnitude < 0.01f;
            Vector3 ctrl       = isStraight ? (edgeA + edgeB) * 0.5f : Vector3.zero;

            const int N = 8;
            var pts = new Vector3[N + 1];
            for (int i = 0; i <= N; i++)
                pts[i] = QuadBezier(edgeA, ctrl, edgeB, (float)i / N);

            var inwardA  = -edgeA.normalized;
            var outwardB =  edgeB.normalized;
            var perpA    = new Vector3(-inwardA.z,  0f, inwardA.x);
            var perpB    = new Vector3(-outwardB.z, 0f, outwardB.x);
            var bankColor = new Color(0.25f, 0.50f, 0.20f);
            const float overshoot = 0.02f;

            // edgeA 側の端セグメント
            float lenA = (pts[1] - edgeA).magnitude + 0.01f;
            var   rotA = Quaternion.LookRotation(new Vector3(inwardA.x, 0f, inwardA.z), Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.transform.SetParent(parent);
                b.transform.localPosition = new Vector3(
                    edgeA.x + perpA.x * bankOffset * side + inwardA.x * (lenA - overshoot) * 0.5f, y,
                    edgeA.z + perpA.z * bankOffset * side + inwardA.z * (lenA - overshoot) * 0.5f);
                b.transform.localRotation = rotA;
                b.transform.localScale    = new Vector3(0.04f, 0.03f, lenA + overshoot);
                SetPropMaterial(b, bankColor);
            }

            // 中間セグメント
            for (int i = 1; i <= N - 2; i++)
            {
                var seg  = pts[i + 1] - pts[i];
                var mid  = (pts[i] + pts[i + 1]) * 0.5f;
                var len  = seg.magnitude + 0.01f;
                var d    = seg / len;
                var perp = new Vector3(-d.z, 0f, d.x);
                var rot  = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z), Vector3.up);
                for (int side = -1; side <= 1; side += 2)
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    b.transform.SetParent(parent);
                    b.transform.localPosition = new Vector3(
                        mid.x + perp.x * bankOffset * side, y,
                        mid.z + perp.z * bankOffset * side);
                    b.transform.localRotation = rot;
                    b.transform.localScale    = new Vector3(0.04f, 0.03f, len);
                    SetPropMaterial(b, bankColor);
                }
            }

            // edgeB 側の端セグメント
            float lenB = (edgeB - pts[N - 1]).magnitude + 0.01f;
            var   rotB = Quaternion.LookRotation(new Vector3(outwardB.x, 0f, outwardB.z), Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.transform.SetParent(parent);
                b.transform.localPosition = new Vector3(
                    edgeB.x + perpB.x * bankOffset * side - outwardB.x * (lenB - overshoot) * 0.5f, y,
                    edgeB.z + perpB.z * bankOffset * side - outwardB.z * (lenB - overshoot) * 0.5f);
                b.transform.localRotation = rotB;
                b.transform.localScale    = new Vector3(0.04f, 0.03f, lenB + overshoot);
                SetPropMaterial(b, bankColor);
            }
        }

        /// <summary>プレビューなど HexTile 外からも呼べる静的版。</summary>
        public static void SpawnDividersFor(TileType type, Transform parent,
                                            float outerRadius = 0.95f, float tileHeight = 0.15f)
        {
            if (type == null) return;
            float y = tileHeight + 0.018f;
            float R = outerRadius;

            switch (type.dividerType)
            {
                case TileDividerType.StraightH:
                    SpawnLine(parent, new Vector3(0f, y, 0f), Quaternion.identity,
                              new Vector3(R * 1.8f, 0.03f, 0.07f),
                              new Color(0.15f, 0.12f, 0.05f));
                    break;

                case TileDividerType.BendE:
                    float bLen = R * 0.866f;
                    var   bBlue = new Color(0.18f, 0.52f, 0.92f);
                    SpawnLine(parent, new Vector3(0.375f*R, y,  0.2165f*R),
                              Quaternion.Euler(0f, -30f, 0f),
                              new Vector3(bLen*0.93f, 0.03f, 0.08f), bBlue);
                    SpawnLine(parent, new Vector3(0.375f*R, y, -0.2165f*R),
                              Quaternion.Euler(0f,  30f, 0f),
                              new Vector3(bLen*0.93f, 0.03f, 0.08f), bBlue);
                    break;

                case TileDividerType.Hex6Spokes:
                    float innerR = R * 0.866f;
                    for (int i = 0; i < 6; i++)
                    {
                        float worldAngle = s_DirToWorldAngle[i];
                        float rad = worldAngle * Mathf.Deg2Rad;
                        var pos = new Vector3(innerR * 0.50f * Mathf.Cos(rad), y,
                                             innerR * 0.50f * Mathf.Sin(rad));
                        var rot = Quaternion.Euler(0f, -worldAngle, 0f);
                        var scl = new Vector3(innerR * 0.90f, 0.04f, 0.09f);
                        SpawnLine(parent, pos, rot, scl, EdgeTypeToColor(type.edges[i]));
                    }
                    break;

                case TileDividerType.VerticalPair:
                {
                    // 上辺(A-B)・下辺(E-D)をそれぞれ3等分した分割点同士を結ぶ2本縦線
                    // 分割点 X = ±R/6 (3等分なので辺長R の 1/6 ずつオフセット)
                    // 線の長さ = 上辺Z から下辺Z = 2 × inRadius = R√3
                    float inRadius2 = R * 0.866f;
                    float xOff      = R / 6f;
                    float lineLen   = inRadius2 * 2f;
                    var   riverCol  = new Color(0.18f, 0.52f, 0.92f);
                    SpawnLine(parent, new Vector3(-xOff, y, 0f), Quaternion.identity,
                              new Vector3(0.07f, 0.03f, lineLen), riverCol);
                    SpawnLine(parent, new Vector3( xOff, y, 0f), Quaternion.identity,
                              new Vector3(0.07f, 0.03f, lineLen), riverCol);
                    break;
                }

                case TileDividerType.BendPairWide:
                {
                    // dir0(右辺)→dir4(左上辺) の緩やかカーブ用。下向きアーチ2本。
                    // SpawnWater の bezier(中心通過・下方) と水パーティクルが整合する設計
                    float inRW = R * 0.866f;
                    var   colW = new Color(0.18f, 0.52f, 0.92f);
                    // Inner bank (下寄り): (5R/6, inR/3) → 底(0, inR/6) → (-5R/6, inR/3)
                    SpawnBankSegment(parent, new Vector3(5f*R/6f, 0f, inRW/3f),
                                             new Vector3(0f, 0f, inRW/6f), y, 0.07f, colW);
                    SpawnBankSegment(parent, new Vector3(0f, 0f, inRW/6f),
                                             new Vector3(-5f*R/6f, 0f, inRW/3f), y, 0.07f, colW);
                    // Outer bank (上寄り): (2R/3, 2inR/3) → 底(0, inR/2) → (-2R/3, 2inR/3)
                    SpawnBankSegment(parent, new Vector3(2f*R/3f, 0f, 2f*inRW/3f),
                                             new Vector3(0f, 0f, inRW/2f), y, 0.07f, colW);
                    SpawnBankSegment(parent, new Vector3(0f, 0f, inRW/2f),
                                             new Vector3(-2f*R/3f, 0f, 2f*inRW/3f), y, 0.07f, colW);
                    break;
                }

                case TileDividerType.BendPair:
                {
                    // dir0辺(右)→dir5辺(上) の3等分点を結ぶL字折れ線2本（川曲がりタイル用）
                    // Inner: V1(右上頂点)寄り。 Outer: V1から遠い側。
                    // 各折れ線は 水平セグメント + 垂直セグメント の2本で構成
                    float inR      = R * 0.866f;
                    var   col      = new Color(0.18f, 0.52f, 0.92f);
                    var   horizRot = Quaternion.LookRotation(Vector3.right, Vector3.up);

                    // Inner bank: (2R/3, 2inR/3) → corner(R/6, 2inR/3) → (R/6, inR)
                    SpawnLine(parent, new Vector3(5f * R / 12f, y, 2f * inR / 3f),
                              horizRot, new Vector3(0.07f, 0.03f, R / 2f), col);
                    SpawnLine(parent, new Vector3(R / 6f, y, 5f * inR / 6f),
                              Quaternion.identity, new Vector3(0.07f, 0.03f, inR / 3f), col);

                    // Outer bank: (5R/6, inR/3) → corner(-R/6, inR/3) → (-R/6, inR)
                    SpawnLine(parent, new Vector3(R / 3f, y, inR / 3f),
                              horizRot, new Vector3(0.07f, 0.03f, R), col);
                    SpawnLine(parent, new Vector3(-R / 6f, y, 2f * inR / 3f),
                              Quaternion.identity, new Vector3(0.07f, 0.03f, 2f * inR / 3f), col);
                    break;
                }
            }
        }

        public static Color EdgeTypeToColor(EdgeType edge)
        {
            switch (edge)
            {
                case EdgeType.Forest: return new Color(0.10f, 0.38f, 0.10f);
                case EdgeType.Field:  return new Color(0.68f, 0.82f, 0.28f);
                case EdgeType.River:  return new Color(0.18f, 0.52f, 0.92f);
                default:              return new Color(0.45f, 0.45f, 0.45f);
            }
        }

        private static void SpawnLine(Transform parent, Vector3 localPos, Quaternion localRot,
                                      Vector3 localScale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale    = localScale;
            SetPropMaterial(go, color);
        }

        // from→to を結ぶ1本のセグメントを生成（BendPairWide 用）
        private static void SpawnBankSegment(Transform parent, Vector3 from, Vector3 to,
                                             float y, float width, Color color)
        {
            var   center = new Vector3((from.x + to.x) * 0.5f, y, (from.z + to.z) * 0.5f);
            var   dir    = new Vector3(to.x - from.x, 0f, to.z - from.z);
            float len    = dir.magnitude;
            if (len < 0.001f) return;
            SpawnLine(parent, center, Quaternion.LookRotation(dir / len, Vector3.up),
                      new Vector3(width, 0.03f, len), color);
        }

        private void SpawnTrees(TileType type, Transform parent)
        {
            int count = Mathf.Max(1, type.propCount);
            for (int i = 0; i < count; i++)
            {
                // coord ベースの擬似乱数で木の位置を決定（再現性あり・Random.seed 不要）
                float angle  = (i * (360f / count) + Data.coord.q * 23f + Data.coord.r * 37f) * Mathf.Deg2Rad;
                float radius = count > 1 ? 0.45f : 0f;
                var   offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                SpawnSingleTree(parent, offset);
            }
        }

        private void SpawnSingleTree(Transform parent, Vector3 offset)
        {
            float ground = tileHeight + 0.01f;

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(parent);
            trunk.transform.localPosition = offset + new Vector3(0f, ground + 0.20f, 0f);
            trunk.transform.localScale    = new Vector3(0.12f, 0.22f, 0.12f);
            SetPropMaterial(trunk, new Color(0.42f, 0.26f, 0.10f));

            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.transform.SetParent(parent);
            crown.transform.localPosition = offset + new Vector3(0f, ground + 0.60f, 0f);
            crown.transform.localScale    = new Vector3(0.45f, 0.52f, 0.45f);
            SetPropMaterial(crown, new Color(0.10f, 0.44f, 0.10f));
        }

        private void SpawnFlowers(TileType type, Transform parent)
        {
            int count = Mathf.Max(1, type.propCount);
            for (int i = 0; i < count; i++)
            {
                // coord ベースの擬似乱数で花の位置・色を決定（再現性あり）
                int   seed   = Data.coord.q * 31 + Data.coord.r * 17 + i * 7;
                float angle  = (i * (360f / count) + (seed % 40) - 20f) * Mathf.Deg2Rad;
                float radius = 0.25f + (seed % 100) / 1000f * 3f; // 0.25〜0.55
                var   offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                var   color  = FlowerPetalColor(seed);
                SpawnSingleFlower(parent, offset, color);
            }
        }

        private void SpawnSingleFlower(Transform parent, Vector3 offset, Color petalColor)
        {
            float ground = tileHeight + 0.01f;

            // 茎
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.transform.SetParent(parent);
            stem.transform.localPosition = offset + new Vector3(0f, ground + 0.07f, 0f);
            stem.transform.localScale    = new Vector3(0.03f, 0.07f, 0.03f);
            SetPropMaterial(stem, new Color(0.25f, 0.55f, 0.15f));

            // 花びら（少し平たい球）
            var petal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            petal.transform.SetParent(parent);
            petal.transform.localPosition = offset + new Vector3(0f, ground + 0.17f, 0f);
            petal.transform.localScale    = new Vector3(0.16f, 0.08f, 0.16f);
            SetPropMaterial(petal, petalColor);

            // 花の中心（小さい黄色の球）
            var center = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            center.transform.SetParent(parent);
            center.transform.localPosition = offset + new Vector3(0f, ground + 0.19f, 0f);
            center.transform.localScale    = new Vector3(0.07f, 0.06f, 0.07f);
            SetPropMaterial(center, new Color(1.0f, 0.88f, 0.1f));
        }

        // 座標ハッシュから花びらの色を決定（ピンク・白・薄紫・薄青の4色）
        private static Color FlowerPetalColor(int seed)
        {
            switch (seed % 4)
            {
                case 0:  return new Color(1.0f, 0.55f, 0.70f); // ピンク
                case 1:  return new Color(0.95f, 0.95f, 0.95f); // 白
                case 2:  return new Color(0.75f, 0.60f, 0.95f); // 薄紫
                default: return new Color(0.60f, 0.80f, 1.00f); // 薄青
            }
        }

        private void SpawnHouse(Transform parent)
        {
            float ground = tileHeight + 0.01f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(parent);
            body.transform.localPosition = new Vector3(0f, ground + 0.22f, 0f);
            body.transform.localScale    = new Vector3(0.52f, 0.44f, 0.52f);
            SetPropMaterial(body, new Color(0.90f, 0.82f, 0.65f));

            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.SetParent(parent);
            roof.transform.localPosition = new Vector3(0f, ground + 0.58f, 0f);
            roof.transform.localScale    = new Vector3(0.62f, 0.16f, 0.62f);
            roof.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            SetPropMaterial(roof, new Color(0.68f, 0.22f, 0.12f));
        }

        private void SpawnWater(TileType type, Transform parent)
        {
            float inRadius = outerRadius * 0.866f;

            // 川の辺を探す優先順位:
            //   1. EdgeType.River が設定された辺
            //   2. Field・None・Forest 以外の辺（River_Bend など旧データ互換）
            //   3. 座標ハッシュによるランダム選択（ユーザー指定「6辺からランダム2辺」）
            var riverEdges   = new List<int>();
            var fallbackEdges = new List<int>();
            if (type != null)
            {
                for (int d = 0; d < 6; d++)
                {
                    var e = type.GetEdge(d);
                    if (e == EdgeType.River) riverEdges.Add(d);
                    else if (e != EdgeType.None && e != EdgeType.Field && e != EdgeType.Forest)
                        fallbackEdges.Add(d);
                }
            }

            int edgeA, edgeB;
            var src = riverEdges.Count >= 2 ? riverEdges
                    : fallbackEdges.Count >= 2 ? fallbackEdges
                    : null;

            if (src != null)
            {
                edgeA = src[0];
                edgeB = src[1];
            }
            else
            {
                // 座標ハッシュで再現性ある 2 辺を選択
                int h = Mathf.Abs(Data.coord.q * 31 + Data.coord.r * 17 + Data.coord.s * 7);
                edgeA = h % 6;
                edgeB = (edgeA + 1 + (h / 6) % 5) % 6;
            }

            var posA = EdgeCenter(edgeA, inRadius);
            var posB = EdgeCenter(edgeB, inRadius);
            CreateWaterFlow(parent, posA, posB);
        }

        // dir 方向の辺の中心をタイルローカル座標で返す（フラットトップ六角形）
        // s_DirToWorldAngle を使って ToWorldPosition と一致させる
        private static Vector3 EdgeCenter(int dir, float inRadius)
        {
            float angle = s_DirToWorldAngle[((dir % 6) + 6) % 6] * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * inRadius, 0f, Mathf.Sin(angle) * inRadius);
        }

        private void CreateWaterFlow(Transform parent, Vector3 edgeA, Vector3 edgeB)
        {
            float riverWidth = outerRadius * 0.5f;
            float bankOffset = riverWidth * 0.5f;
            float y          = tileHeight + 0.01f;

            // 2辺の中点がタイル中心に近い = 対向辺 = 直線。それ以外はタイル中心を制御点とするベジェ曲線
            bool    isStraight = ((edgeA + edgeB) * 0.5f).sqrMagnitude < 0.01f;
            Vector3 ctrl       = isStraight ? (edgeA + edgeB) * 0.5f : Vector3.zero;

            // 曲線を8分割でサンプリング
            const int N = 8;
            var pts = new Vector3[N + 1];
            for (int i = 0; i <= N; i++)
                pts[i] = QuadBezier(edgeA, ctrl, edgeB, (float)i / N);

            // 辺の法線方向（辺に垂直にタイル内へ向かう方向）
            // これで端のキューブの面が六角形の辺と完全に一致する
            var inwardA  = -edgeA.normalized;   // edgeA → タイル中心
            var outwardB =  edgeB.normalized;   // タイル中心 → edgeB
            var perpA    = new Vector3(-inwardA.z,  0f, inwardA.x);
            var perpB    = new Vector3(-outwardB.z, 0f, outwardB.x);

            // 隣接タイルとの継ぎ目の隙間をなくすため、端のキューブを境界から少しはみ出させる
            const float overshoot = 0.02f;

            // ── 端（edgeA側）: 辺上から overshoot だけ外へはみ出して配置 ──────
            float lenA = (pts[1] - edgeA).magnitude + 0.01f;
            var   rotA = Quaternion.LookRotation(new Vector3(inwardA.x, 0f, inwardA.z), Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bank.transform.SetParent(parent);
                bank.transform.localPosition = new Vector3(
                    edgeA.x + perpA.x * bankOffset * side + inwardA.x * (lenA - overshoot) * 0.5f, y,
                    edgeA.z + perpA.z * bankOffset * side + inwardA.z * (lenA - overshoot) * 0.5f);
                bank.transform.localRotation = rotA;
                bank.transform.localScale    = new Vector3(0.04f, 0.03f, lenA + overshoot);
                SetPropMaterial(bank, new Color(0.25f, 0.50f, 0.20f));
            }

            // ── 中間: ベジェ曲線に追従する通常セグメント ──────────────────
            for (int i = 1; i <= N - 2; i++)
            {
                var seg  = pts[i + 1] - pts[i];
                var mid  = (pts[i] + pts[i + 1]) * 0.5f;
                var len  = seg.magnitude + 0.01f;
                var dir  = seg / len;
                var perp = new Vector3(-dir.z, 0f, dir.x);
                var rot  = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z), Vector3.up);
                for (int side = -1; side <= 1; side += 2)
                {
                    var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bank.transform.SetParent(parent);
                    bank.transform.localPosition = new Vector3(
                        mid.x + perp.x * bankOffset * side, y,
                        mid.z + perp.z * bankOffset * side);
                    bank.transform.localRotation = rot;
                    bank.transform.localScale    = new Vector3(0.04f, 0.03f, len);
                    SetPropMaterial(bank, new Color(0.25f, 0.50f, 0.20f));
                }
            }

            // ── 端（edgeB側）: 辺上から overshoot だけ外へはみ出して配置 ──────
            float lenB = (edgeB - pts[N - 1]).magnitude + 0.01f;
            var   rotB = Quaternion.LookRotation(new Vector3(outwardB.x, 0f, outwardB.z), Vector3.up);
            for (int side = -1; side <= 1; side += 2)
            {
                var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bank.transform.SetParent(parent);
                bank.transform.localPosition = new Vector3(
                    edgeB.x + perpB.x * bankOffset * side - outwardB.x * (lenB - overshoot) * 0.5f, y,
                    edgeB.z + perpB.z * bankOffset * side - outwardB.z * (lenB - overshoot) * 0.5f);
                bank.transform.localRotation = rotB;
                bank.transform.localScale    = new Vector3(0.04f, 0.03f, lenB + overshoot);
                SetPropMaterial(bank, new Color(0.25f, 0.50f, 0.20f));
            }

            // ── パーティクル: 曲線を4分割してそれぞれにエミッターを配置 ────
            // 各エミッターが担当区間に沿って粒子を流し、合わせると自然な流れに見える
            const int psCount  = 4;
            float     segLen   = 0f;
            for (int i = 0; i < N; i++) segLen += (pts[i + 1] - pts[i]).magnitude;
            segLen /= psCount;

            for (int k = 0; k < psCount; k++)
            {
                float tm  = ((float)k + 0.5f) / psCount;
                float t0  = (float)k       / psCount;
                float t1  = (float)(k + 1) / psCount;
                var   pm  = QuadBezier(edgeA, ctrl, edgeB, tm);
                var   dir = (QuadBezier(edgeA, ctrl, edgeB, t1)
                           - QuadBezier(edgeA, ctrl, edgeB, t0)).normalized;

                var go = new GameObject("WaterPS");
                go.transform.SetParent(parent);
                go.transform.localPosition = new Vector3(pm.x, y + 0.015f, pm.z);
                go.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(dir.x, 0f, dir.z), Vector3.up);

                var ps  = go.AddComponent<ParticleSystem>();
                var mat = GetWaterMaterial();
                if (mat != null) go.GetComponent<ParticleSystemRenderer>().material = mat;
                SetupWaterParticles(ps, segLen, riverWidth);
                ps.Play();
            }
        }

        // 2次ベジェ曲線: P0→制御点P1→P2 を t∈[0,1] でサンプリング
        private static Vector3 QuadBezier(Vector3 p0, Vector3 p1, Vector3 p2, float t)
        {
            float mt = 1f - t;
            return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
        }

        private static void SetupWaterParticles(ParticleSystem ps, float segLen, float riverWidth)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            const float flowSpeed = 0.75f;

            var main = ps.main;
            main.loop            = true;
            main.duration        = 3f;
            main.maxParticles    = 25; // 4つのPSで合計100
            main.startLifetime   = new ParticleSystem.MinMaxCurve(
                segLen / flowSpeed * 0.85f, segLen / flowSpeed * 1.15f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(0.35f, 0.70f, 1.00f, 0.85f),
                new Color(0.65f, 0.90f, 1.00f, 0.95f));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var em = ps.emission;
            em.rateOverTime = 10f; // 4つ合計で 40/sec

            // 担当区間（1/4の長さ）の川幅のBox
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale     = new Vector3(riverWidth * 0.80f, 0.01f, segLen);

            // ローカルZ方向（各エミッターの向き = 曲線接線方向）に流す
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);   // TwoConstants に統一（Constantとの混在不可）
            vel.z = new ParticleSystem.MinMaxCurve(flowSpeed * 0.85f, flowSpeed * 1.15f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f,    0f),   new GradientAlphaKey(0.85f, 0.08f),
                        new GradientAlphaKey(0.85f, 0.85f), new GradientAlphaKey(0f,    1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        // マテリアルを一度だけ生成してキャッシュ（毎回生成すると URP Render Graph の
        // 透明パス再コンパイルが走り、置いた瞬間フレームがグレーになるバグを防ぐ）
        private static Material s_waterMat;

        private static Material GetWaterMaterial()
        {
            if (s_waterMat != null) return s_waterMat;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[HexTile] URP Particles/Unlit shader not found — water particles disabled");
                return null;
            }

            s_waterMat = new Material(shader) { name = "WaterFlow_Runtime" };
            // URP 17: キーワードベースの透明設定のみ使用。
            // _SrcBlend/_DstBlend を手動設定すると Render Graph の
            // ブレンドステート管理と競合してフレームが壊れることがある。
            s_waterMat.SetFloat("_Surface", 1f);
            s_waterMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            s_waterMat.SetColor("_BaseColor", Color.white);
            s_waterMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return s_waterMat;
        }

        private static void SetPropMaterial(GameObject go, Color color)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.color = color;
            // タイルへのレイキャストを妨げないようコライダーを除去
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }

        // ── 世界プロップ管理 ─────────────────────────────────────────────

        private void SpawnWorldProps()
        {
            if (_worldType == null || _worldPropsRoot != null) return;
            _worldPropsRoot = new GameObject("WorldProps");
            _worldPropsRoot.transform.SetParent(transform);
            _worldPropsRoot.transform.localPosition = Vector3.zero;
            _worldPropsRoot.transform.localRotation = Quaternion.identity;
            SpawnPropsFor(_worldType, _worldPropsRoot.transform);
        }

        private void DestroyWorldProps()
        {
            if (_worldPropsRoot != null)
            {
                Object.Destroy(_worldPropsRoot);
                _worldPropsRoot = null;
            }
        }

        // ── アニメーション ───────────────────────────────────────────────

        private IEnumerator PlacementAnim()
        {
            float duration = 0.35f;
            float elapsed  = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float s = placementCurve.Evaluate(elapsed / duration);
                transform.localScale = Vector3.one * s;
                yield return null;
            }
            transform.localScale = Vector3.one;
        }

        // ── 接続管理 ─────────────────────────────────────────────────

        /// <summary>接続状態を記録する。ビジュアル演出は EventBus 経由で TileConnectionFX が担当。</summary>
        public void MarkConnectedEdge(int dir)
        {
            _connectedEdges[dir] = true;
        }

        public bool IsEdgeConnected(int dir) => _connectedEdges[((dir % 6) + 6) % 6];

        public void SetRotation(int rotation)
        {
            Data = new TileData(Data.coord, Data.tileType, rotation);
            ApplyRotationVisual();
        }

        private void ApplyVisual()
        {
            if (tileRenderer == null) return;
            tileRenderer.enabled = true;
            if (Data.tileType != null)
                tileRenderer.material.color = Data.tileType.tileColor;
            ApplyRotationVisual();
        }

        private void ApplyRotationVisual()
        {
            transform.rotation = Quaternion.Euler(0f, Data.rotation * 60f, 0f);
        }

        /// <summary>「置けるマス」として常時ハイライトするかを設定する。</summary>
        public void SetAvailable(bool available)
        {
            _isAvailable = available;
            if (IsPlaced) return;

            if (tileRenderer != null) tileRenderer.enabled = available;
            if (meshCollider != null) meshCollider.enabled = available;

            if (available)
            {
                if (_worldType != null)
                {
                    // 世界タイプの色でそのまま表示（タイルを「発見」する感覚を演出）
                    tileRenderer.material.color = _worldType.tileColor;
                    SpawnWorldProps();
                }
                else
                {
                    tileRenderer.material.color = GetAvailableColor();
                }
            }
        }

        // 「置けるマス」の常時ハイライト色（淡い緑）
        private Color GetAvailableColor() =>
            Color.Lerp(_defaultColor, new Color(0.55f, 0.95f, 0.55f), 0.28f);

        public void Highlight(bool on, bool placeable = true)
        {
            if (tileRenderer == null) return;
            // このタイルが本来持つべき色（配置済み→タイル色、未配置→世界タイプ色）
            Color baseColor = Data.tileType != null ? Data.tileType.tileColor
                            : _worldType   != null ? _worldType.tileColor
                            : GetAvailableColor();

            if (!on || placeable)
            {
                // ホバー解除 or 配置可能: ゴーストタイルが視覚フィードバックを担うのでベースカラーを維持
                tileRenderer.material.color = baseColor;
                return;
            }
            // 配置不可: 赤みをつけてフィードバック
            tileRenderer.material.color = Color.Lerp(baseColor, Color.red, 0.55f);
        }
    }
}
