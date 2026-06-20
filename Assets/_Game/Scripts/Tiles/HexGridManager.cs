// 役割: HexGridの生成・タイル管理・配置操作を担う MonoBehaviour。
//       起動時に指定半径のグリッドを生成し、クリックによるタイル配置と
//       マウスホイールによる回転を提供する。
//       Tilesアセンブリ所属（HexGrid + Tiles 両方を参照するため）。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class HexGridManager : MonoBehaviour
    {
        [Header("グリッド設定")]
        [SerializeField] private int radius = 11;
        [SerializeField] private float tileSize = 1.1f;

        [Header("タイルプレハブ")]
        [SerializeField] private GameObject hexTilePrefab;

        [Header("デッキ")]
        [SerializeField] private TileDeck tileDeck;

        [Header("ワールド生成")]
        [Tooltip("グリッド全タイルにランダム割り当てするタイルタイプ（Forest/森の縁/村/川 など）")]
        [SerializeField] private TileType[] worldTileTypes;

        private readonly Dictionary<HexCoord, HexTile> _grid = new();
        private int _currentRotation = 0;
        private float _leftPressTime = -1f;

        // タップ vs 長押し判定しきい値（CameraController と合わせること）
        private const float PlaceTapThreshold = 0.2f;

        private HexTile _hoveredTile;
        private bool _hoveredCanPlace = true;   // 隣接チェック（配置ブロックに使う）
        private bool _hoveredEdgeMatch = true;  // エッジ互換チェック（ホバー色ヒントに使う）

        // 配置プレビュー（ゴーストタイル）
        private GameObject   _previewGO;
        private MeshRenderer _previewRenderer;
        private Material     _previewMat;
        private Material     _previewMarkerMat;
        private GameObject   _previewDividersRoot;
        private TileType     _previewCurrentType;

        private Camera _mainCamera;

        private void Start()
        {
            _mainCamera = Camera.main;
            GenerateGrid();
            RandomizeWorldTypes();
            UpdateAvailableHighlights();
            SetupPreview();
        }

        private void OnDestroy()
        {
            if (_previewMat       != null) Destroy(_previewMat);
            if (_previewMarkerMat != null) Destroy(_previewMarkerMat);
        }

        private void Update()
        {
            HandleRotation(); // 先に回転を確定させてからホバー・配置を評価
            HandleHover();
            HandlePlacement();
        }

        private void RandomizeWorldTypes()
        {
            if (worldTileTypes == null || worldTileTypes.Length == 0) return;
            foreach (var kv in _grid)
                kv.Value.SetWorldType(worldTileTypes[Random.Range(0, worldTileTypes.Length)]);
        }

        private void GenerateGrid()
        {
            foreach (HexCoord coord in HexCoord.Range(radius))
            {
                var go   = Instantiate(hexTilePrefab, transform);
                var tile = go.GetComponent<HexTile>();
                tile.Initialize(coord, tileSize);
                _grid[coord] = tile;
                go.name = coord.ToString();
            }
        }

        private void HandleHover()
        {
            if (_mainCamera == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            Ray ray = _mainCamera.ScreenPointToRay(mouse.position.ReadValue());
            HexTile hit = RaycastTile(ray);

            TileType currentType = tileDeck != null ? tileDeck.Current : null;
            bool newCanPlace   = true;
            bool newEdgeMatch  = true;
            if (hit != null && !hit.IsPlaced && currentType != null)
            {
                newCanPlace  = EdgeMatcher.IsPlaceable(hit.Data.coord, currentType, _currentRotation, _grid);
                newEdgeMatch = newCanPlace && EdgeMatcher.IsEdgeCompatible(hit.Data.coord, currentType, _currentRotation, _grid);
            }

            if (hit != _hoveredTile || newCanPlace != _hoveredCanPlace || newEdgeMatch != _hoveredEdgeMatch)
            {
                _hoveredTile?.Highlight(false);
                _hoveredTile      = hit;
                _hoveredCanPlace  = newCanPlace;
                _hoveredEdgeMatch = newEdgeMatch;
                if (_hoveredTile != null && !_hoveredTile.IsPlaced)
                    _hoveredTile.Highlight(true, _hoveredCanPlace);
            }

            // プレビューは毎フレーム更新（回転変更にも追従する）
            UpdatePreview(currentType);
        }

        private void HandleRotation()
        {
            // 右クリック または R キーでタイルを回転
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                _currentRotation = (_currentRotation + 1) % 6;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                _currentRotation = (_currentRotation + 1) % 6;
        }

        // ── プレビュー（ゴーストタイル）────────────────────────────────

        private void SetupPreview()
        {
            _previewGO = new GameObject("TilePlacementPreview");

            var mf = _previewGO.AddComponent<MeshFilter>();
            mf.sharedMesh = HexMeshBuilder.Build(0.95f, 0.15f);

            _previewRenderer = _previewGO.AddComponent<MeshRenderer>();
            _previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _previewRenderer.receiveShadows    = false;

            // URP Lit で半透明マテリアルを生成
            _previewMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _previewMat.SetFloat("_Surface", 1f);
            _previewMat.SetFloat("_Blend",   0f);
            _previewMat.SetFloat("_ZWrite",  0f);
            _previewMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _previewMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _previewRenderer.material = _previewMat;

            // 回転インジケーター: edge0方向(East)の白い球
            // プレビューGOの子なので一緒に回転し、向きが一目でわかる
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.SetParent(_previewGO.transform);
            marker.transform.localPosition = new Vector3(0.62f, 0.17f, 0f);
            marker.transform.localScale    = new Vector3(0.22f, 0.22f, 0.22f);
            Destroy(marker.GetComponent<Collider>());
            _previewMarkerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _previewMarkerMat.SetColor("_BaseColor", Color.white);
            var markerMR = marker.GetComponent<MeshRenderer>();
            markerMR.material             = _previewMarkerMat;
            markerMR.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerMR.receiveShadows       = false;

            _previewGO.SetActive(false); // 最初は非表示
        }

        private void UpdatePreview(TileType currentType)
        {
            if (_previewGO == null) return;

            bool show = _hoveredTile != null
                     && !_hoveredTile.IsPlaced
                     && _hoveredCanPlace
                     && currentType != null;

            _previewGO.SetActive(show);
            if (!show) return;

            // ホバー中タイルの真上に重ねて表示、回転を反映
            _previewGO.transform.position = _hoveredTile.transform.position + Vector3.up * 0.02f;
            _previewGO.transform.rotation = Quaternion.Euler(0f, _currentRotation * 60f, 0f);

            Color tc = currentType.tileColor;
            _previewMat.SetColor("_BaseColor", new Color(tc.r, tc.g, tc.b, 0.58f));

            // デッキタイルが変わったら分割線を再生成（River_Bend のエッジパターンを表示）
            if (_previewCurrentType != currentType)
            {
                _previewCurrentType = currentType;
                if (_previewDividersRoot != null)
                {
                    Destroy(_previewDividersRoot);
                    _previewDividersRoot = null;
                }
                if (currentType.dividerType != TileDividerType.None)
                {
                    _previewDividersRoot = new GameObject("PreviewDividers");
                    _previewDividersRoot.transform.SetParent(_previewGO.transform);
                    _previewDividersRoot.transform.localPosition = Vector3.zero;
                    _previewDividersRoot.transform.localRotation = Quaternion.identity;
                    HexTile.SpawnDividersFor(currentType, _previewDividersRoot.transform);
                }
            }
        }

        private void HandlePlacement()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
                _leftPressTime = Time.time;

            // 素早いタップのリリース時のみ配置（長押しはカメラパンに譲る）
            if (!mouse.leftButton.wasReleasedThisFrame) return;
            if (_leftPressTime < 0f || Time.time - _leftPressTime >= PlaceTapThreshold)
            {
                _leftPressTime = -1f;
                return;
            }
            _leftPressTime = -1f;

            if (_hoveredTile == null || _hoveredTile.IsPlaced) return;
            if (!_hoveredCanPlace) return;

            TileType currentType = tileDeck != null ? tileDeck.Current : null;
            if (currentType == null) return;

            _hoveredTile.Place(currentType, _currentRotation);
            tileDeck.ConsumeTop();
            UpdateAvailableHighlights();
        }

        private HexTile RaycastTile(Ray ray)
        {
            if (Physics.Raycast(ray, out RaycastHit hit))
                return hit.collider.GetComponentInParent<HexTile>();
            return null;
        }

        /// <summary>配置済みタイルに隣接する未配置マスを「置けるマス」としてハイライト更新する。</summary>
        private void UpdateAvailableHighlights()
        {
            bool anyPlaced = EdgeMatcher.HasAnyPlaced(_grid);
            foreach (var kv in _grid)
            {
                HexTile tile = kv.Value;
                if (tile.IsPlaced) continue;

                bool available;
                if (!anyPlaced)
                {
                    // 最初の1枚は中央マス(0,0)のみを候補にする
                    available = (kv.Key.q == 0 && kv.Key.r == 0);
                }
                else
                {
                    available = false;
                    for (int dir = 0; dir < 6; dir++)
                    {
                        if (_grid.TryGetValue(kv.Key.Neighbor(dir), out HexTile neighbor) && neighbor.IsPlaced)
                        {
                            available = true;
                            break;
                        }
                    }
                }
                tile.SetAvailable(available);
            }
        }

        public bool TryGetTile(HexCoord coord, out HexTile tile)
            => _grid.TryGetValue(coord, out tile);
    }
}
