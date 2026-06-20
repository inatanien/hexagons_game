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
        [SerializeField] private int radius = 3;
        [SerializeField] private float tileSize = 1.1f;

        [Header("タイルプレハブ")]
        [SerializeField] private GameObject hexTilePrefab;

        [Header("配置するタイル種別（テスト用）")]
        [SerializeField] private TileType[] availableTileTypes;

        private readonly Dictionary<HexCoord, HexTile> _grid = new();
        private int _currentTileIndex = 0;
        private int _currentRotation = 0;
        private HexTile _hoveredTile;
        private bool _hoveredPlaceable = true;

        private Camera _mainCamera;

        private void Start()
        {
            _mainCamera = Camera.main;
            GenerateGrid();
            UpdateAvailableHighlights();
        }

        private void Update()
        {
            HandleHover();
            HandleRotation();
            HandlePlacement();
        }

        private void GenerateGrid()
        {
            foreach (HexCoord coord in HexCoord.Range(radius))
            {
                var go = Instantiate(hexTilePrefab, transform);
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
            Vector2 screenPos = mouse.position.ReadValue();
            Ray ray = _mainCamera.ScreenPointToRay(screenPos);
            HexTile hit = RaycastTile(ray);

            bool newPlaceable = true;
            if (hit != null && !hit.IsPlaced && availableTileTypes != null && availableTileTypes.Length > 0)
            {
                TileType type = availableTileTypes[_currentTileIndex % availableTileTypes.Length];
                newPlaceable = EdgeMatcher.IsPlaceable(hit.Data.coord, type, _currentRotation, _grid);
            }

            if (hit != _hoveredTile || newPlaceable != _hoveredPlaceable)
            {
                _hoveredTile?.Highlight(false);
                _hoveredTile = hit;
                _hoveredPlaceable = newPlaceable;
                if (_hoveredTile != null && !_hoveredTile.IsPlaced)
                    _hoveredTile.Highlight(true, _hoveredPlaceable);
            }
        }

private void HandleRotation()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0f) _currentRotation = (_currentRotation + 1) % 6;
            else if (scroll < 0f) _currentRotation = (_currentRotation + 5) % 6;
        }

private void HandlePlacement()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;
            if (_hoveredTile == null) return;
            if (_hoveredTile.IsPlaced) return;
            if (availableTileTypes == null || availableTileTypes.Length == 0) return;
            if (!_hoveredPlaceable) return;

            TileType type = availableTileTypes[_currentTileIndex % availableTileTypes.Length];
            _hoveredTile.Place(type, _currentRotation);
            _currentTileIndex++;
            UpdateAvailableHighlights();
        }

        private HexTile RaycastTile(Ray ray)
        {
            if (Physics.Raycast(ray, out RaycastHit hit))
                return hit.collider.GetComponentInParent<HexTile>();
            return null;
        }

        /// <summary>
        /// 配置済みタイルに隣接する未配置マスを「置けるマス」としてハイライト更新する。
        /// タイル配置のたびに呼ぶ。
        /// </summary>
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
                    // 最初の1枚はグリッド全体が対象
                    available = true;
                }
                else
                {
                    // 隣接に配置済みがあれば置けるマス
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
