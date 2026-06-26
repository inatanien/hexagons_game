// 役割: 無限グリッドのタイル管理・配置操作を担う MonoBehaviour。
//       EnsureTileExists で必要なタイルのみ生成し、
//       RegisterPlacement で差分のみ更新することで全スキャンを廃止。
//       プレビュー表示は TilePlacementPreview に委譲。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using ElfVillage.Core;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class HexGridManager : MonoBehaviour
    {
        [Header("タイルプレハブ")]
        [SerializeField] private GameObject hexTilePrefab;

        [Header("グリッド設定")]
        [SerializeField] private float tileSize = 1.1f;

        [Header("デッキ")]
        [SerializeField] private TileDeck tileDeck;

        [Header("プレビュー")]
        [SerializeField] private TilePlacementPreview preview;

        [Header("ワールド生成")]
        [Tooltip("タイルタイプの候補（座標ハッシュで決定論的に選択）")]
        [SerializeField] private TileType[] worldTileTypes;

        // ── グリッド状態 ─────────────────────────────────────────────
        // 生成済みタイルのみ格納（必要になった時点で EnsureTileExists が追加）
        private readonly Dictionary<HexCoord, HexTile> _grid = new();
        // 配置済み座標（O(1) 検索）
        private readonly HashSet<HexCoord> _placedCoords = new();
        // 配置可能座標（差分管理・O(1) 検索）
        private readonly HashSet<HexCoord> _availableCoords = new();
        // isActive == true のみキャッシュ（Start で確定）
        private TileType[] _activeWorldTypes;

        // ── 入力状態 ─────────────────────────────────────────────────
        private int   _currentRotation  = 0;
        private float _leftPressTime    = -1f;
        private bool  _firstTilePlaced  = false;

        // タップ vs 長押し判定しきい値（CameraController と合わせること）
        private const float PlaceTapThreshold = 0.2f;

        // ── ホバー状態 ────────────────────────────────────────────────
        private HexTile _hoveredTile;
        private bool    _hoveredCanPlace  = true;
        private bool    _hoveredEdgeMatch = true;

        private Camera _mainCamera;

        // ── ライフサイクル ────────────────────────────────────────────

        private void Start()
        {
            _mainCamera = Camera.main;

            // isActive なタイルタイプのみをワールド生成に使用するキャッシュを作成
            _activeWorldTypes = System.Array.FindAll(
                worldTileTypes ?? new TileType[0],
                t => t != null && t.isActive
            );

            // 初期タイルは中央のみ生成・配置可能に設定
            EnsureTileExists(HexCoord.Zero);
            _availableCoords.Add(HexCoord.Zero);
            _grid[HexCoord.Zero].SetAvailable(true);
        }

        private void Update()
        {
            HandleRotation();
            HandleHover();
            HandlePlacement();
        }

        // ── タイル生成（無限グリッドの核心） ─────────────────────────

        /// <summary>
        /// 指定座標のタイルが未生成なら生成する。
        /// ワールドタイプは座標ハッシュで決定論的に割り当て（同じ座標は常に同じ結果）。
        /// </summary>
        private void EnsureTileExists(HexCoord coord)
        {
            if (_grid.ContainsKey(coord)) return;

            var go   = Instantiate(hexTilePrefab, transform);
            var tile = go.GetComponent<HexTile>();
            tile.Initialize(coord, tileSize);
            go.name = coord.ToString();

            if (_activeWorldTypes != null && _activeWorldTypes.Length > 0)
            {
                int hash = Mathf.Abs(coord.q * 73856093 ^ coord.r * 19349663 ^ coord.s * 83492791);
                tile.SetWorldType(_activeWorldTypes[hash % _activeWorldTypes.Length]);
            }

            _grid[coord] = tile;
        }

        // ── 配置管理（差分更新） ──────────────────────────────────────

        /// <summary>
        /// タイル配置後に呼ぶ。隣接6マスのみ評価して差分を更新する。
        /// UpdateAvailableHighlights の全スキャンを廃止し O(6) に抑える。
        /// </summary>
        private void RegisterPlacement(HexCoord placedCoord)
        {
            _placedCoords.Add(placedCoord);
            _availableCoords.Remove(placedCoord); // 配置済みは候補から除外

            for (int dir = 0; dir < 6; dir++)
            {
                HexCoord neighbor = placedCoord.Neighbor(dir);
                if (_placedCoords.Contains(neighbor))   continue; // 既配置はスキップ
                if (_availableCoords.Contains(neighbor)) continue; // 既に候補のためスキップ

                EnsureTileExists(neighbor);
                _availableCoords.Add(neighbor);
                _grid[neighbor].SetAvailable(true);
            }
        }

        // ── 入力ハンドリング ──────────────────────────────────────────

        private void HandleRotation()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                _currentRotation = (_currentRotation + 1) % 6;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                _currentRotation = (_currentRotation + 1) % 6;
        }

        private void HandleHover()
        {
            if (_mainCamera == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            Ray     ray = _mainCamera.ScreenPointToRay(mouse.position.ReadValue());
            HexTile hit = RaycastTile(ray);

            TileType currentType  = tileDeck != null ? tileDeck.Current : null;
            bool     newCanPlace  = true;
            bool     newEdgeMatch = true;

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

            preview?.UpdatePreview(_hoveredTile, _hoveredCanPlace, currentType, _currentRotation);
        }

        private void HandlePlacement()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
                _leftPressTime = Time.time;

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

            HexCoord placedCoord = _hoveredTile.Data.coord;
            HexTile  placedTile  = _hoveredTile;
            placedTile.Place(currentType, _currentRotation);
            tileDeck.ConsumeTop();
            RegisterPlacement(placedCoord);
            CheckAndApplyConnections(placedCoord);
            // 接続有無に関わらず全配置で発行（成長評価システムの起点）
            EventBus.Publish(new TilePlacedEvent(placedTile, currentType, placedCoord));

            if (!_firstTilePlaced)
            {
                _firstTilePlaced = true;
                EventBus.Publish(new FirstTilePlacedEvent());
            }
        }

        // ── 接続チェック ──────────────────────────────────────────────

        /// <summary>
        /// 配置直後に呼ぶ。隣接6方向を走査し同種タイルへの接続を確定させる。
        /// 接続が1件以上あれば TileConnectedEvent を EventBus に発行する。
        /// 演出・サウンド・クエストなど他システムはこのイベントを購読して反応する。
        /// </summary>
        private void CheckAndApplyConnections(HexCoord coord)
        {
            if (!_grid.TryGetValue(coord, out HexTile placed)) return;
            if (placed.Data.tileType == null) return;

            var edges = new List<ConnectionEdge>();
            for (int dir = 0; dir < 6; dir++)
            {
                HexCoord nCoord = coord.Neighbor(dir);
                if (!_grid.TryGetValue(nCoord, out HexTile neighbor)) continue;
                if (!neighbor.IsPlaced) continue;
                bool sameType     = neighbor.Data.tileType == placed.Data.tileType;
                bool sameCategory = EdgeMatcher.SameCategory(placed.Data.tileType, neighbor.Data.tileType);
                if (!sameType && !sameCategory) continue;

                placed.MarkConnectedEdge(dir);
                neighbor.MarkConnectedEdge((dir + 3) % 6);
                edges.Add(new ConnectionEdge(dir, neighbor));
            }

            if (edges.Count > 0)
                EventBus.Publish(new TileConnectedEvent(placed, placed.Data.tileType, edges));
        }

        // ── ユーティリティ ────────────────────────────────────────────

        private HexTile RaycastTile(Ray ray)
        {
            if (Physics.Raycast(ray, out RaycastHit hit))
                return hit.collider.GetComponentInParent<HexTile>();
            return null;
        }

        public bool TryGetTile(HexCoord coord, out HexTile tile)
            => _grid.TryGetValue(coord, out tile);
    }
}
