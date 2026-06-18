// 役割: シーン上の1枚のHexタイルを表す MonoBehaviour。
//       TileData を保持し、見た目（色・回転）の反映と
//       配置済みフラグを管理する。

using UnityEngine;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public class HexTile : MonoBehaviour
    {
        [SerializeField] private Renderer tileRenderer;

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

        public void Highlight(bool on)
        {
            if (tileRenderer == null) return;
            Color base_ = Data.tileType != null ? Data.tileType.tileColor : Color.white;
            tileRenderer.material.color = on ? base_ * 1.4f : base_;
        }
    }
}
