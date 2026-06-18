// 役割: シーン上の1枚のHexタイルを表す MonoBehaviour。
//       TileData を保持し、見た目（色・回転）の反映と
//       配置済みフラグを管理する。

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

        private Renderer tileRenderer;

        private void Awake()
        {
            tileRenderer = meshRenderer;
            Mesh mesh = HexMeshBuilder.Build(outerRadius, tileHeight);
            if (meshFilter   != null) meshFilter.sharedMesh   = mesh;
            if (meshCollider != null) meshCollider.sharedMesh  = mesh;
        }

        public TileData Data { get; private set; }
        public bool IsPlaced { get; private set; }

        public void Initialize(HexCoord coord, float tileSize)
        {
            Data = new TileData(coord, null, 0);
            transform.position = coord.ToWorldPosition(tileSize);
            IsPlaced = false;
        }

        public void Place(TileType tileType, int rotation)
        {
            Data = new TileData(Data.coord, tileType, rotation);
            IsPlaced = true;
            ApplyVisual();
        }

        public void SetRotation(int rotation)
        {
            Data = new TileData(Data.coord, Data.tileType, rotation);
            ApplyRotationVisual();
        }

        private void ApplyVisual()
        {
            if (tileRenderer == null) return;
            if (Data.tileType != null)
                tileRenderer.material.color = Data.tileType.tileColor;
            ApplyRotationVisual();
        }

        private void ApplyRotationVisual()
        {
            transform.rotation = Quaternion.Euler(0f, Data.rotation * 60f, 0f);
        }

public void Highlight(bool on, bool placeable = true)
        {
            if (tileRenderer == null) return;
            Color baseColor = Data.tileType != null ? Data.tileType.tileColor : Color.white;
            if (!on)
            {
                tileRenderer.material.color = baseColor;
                return;
            }
            // 配置可能=緑寄り、配置不可=赤寄りで明度を上げる
            tileRenderer.material.color = placeable
                ? Color.Lerp(baseColor, Color.green, 0.45f)
                : Color.Lerp(baseColor, Color.red,   0.55f);
        }
    }
}
