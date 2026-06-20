// 役割: シーン上の1枚のHexタイルを表す MonoBehaviour。
//       TileData を保持し、見た目（色・回転）の反映と
//       配置済みフラグを管理する。

using System.Collections;
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
                case TilePropType.Tree:  SpawnTrees(type, parent);  break;
                case TilePropType.House: SpawnHouse(parent);        break;
                case TilePropType.Water: SpawnWater(parent);        break;
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

        private void SpawnWater(Transform parent)
        {
            // Plane は 10×10 単位なので、0.165 倍で約 1.65 の正方形
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.transform.SetParent(parent);
            water.transform.localPosition = new Vector3(0f, tileHeight + 0.008f, 0f);
            water.transform.localScale    = new Vector3(0.165f, 1f, 0.165f);
            SetPropMaterial(water, new Color(0.18f, 0.52f, 0.92f));
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
