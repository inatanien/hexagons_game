// 役割: UI上に六角形を描画するカスタム Graphic コンポーネント。
//       MaskableGraphic を継承するため、通常の UI Image と同様に扱える。

using UnityEngine;
using UnityEngine.UI;

namespace ElfVillage.Tiles
{
    [AddComponentMenu("UI/Hex Image")]
    public class HexImage : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var rect = rectTransform.rect;
            float cx = rect.center.x;
            float cy = rect.center.y;
            // 短辺の半分を半径にする（はみ出さない）
            float r  = Mathf.Min(rect.width, rect.height) * 0.5f;

            // 中心頂点
            UIVertex center = UIVertex.simpleVert;
            center.color    = color;
            center.position = new Vector3(cx, cy);
            vh.AddVert(center);

            // 外周6頂点（ポインティートップ: 90°スタート）
            for (int i = 0; i < 6; i++)
            {
                float angle = (90f - i * 60f) * Mathf.Deg2Rad;
                UIVertex v  = UIVertex.simpleVert;
                v.color     = color;
                v.position  = new Vector3(cx + r * Mathf.Cos(angle), cy + r * Mathf.Sin(angle));
                vh.AddVert(v);
            }

            // 6三角形（中心 + 隣接外周2頂点）
            for (int i = 0; i < 6; i++)
                vh.AddTriangle(0, i + 1, (i + 1) % 6 + 1);
        }
    }
}
