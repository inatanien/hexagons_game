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
        private Camera _mainCamera;

        private void Start()
        {
            _mainCamera = Camera.main;
            GenerateGrid();
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

            if (hit != _hoveredTile)
            {
                _hoveredTile?.Highlight(false);
                _hoveredTile = hit;
                _hoveredTile?.Highlight(true);
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

            TileType type = availableTileTypes[_currentTileIndex % availableTileTypes.Length];
            _hoveredTile.Place(type, _currentRotation);
            _currentTileIndex++;
        }

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
