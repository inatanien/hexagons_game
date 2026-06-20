// 役割: タイル配置プレビュー（ゴーストタイル）の表示を担う MonoBehaviour。
//       HexGridManager から分離し、見た目の責務を単独で管理する。

using UnityEngine;

namespace ElfVillage.Tiles
{
    public class TilePlacementPreview : MonoBehaviour
    {
        private GameObject   _previewGO;
        private MeshRenderer _previewRenderer;
        private Material     _previewMat;
        private Material     _previewMarkerMat;
        private GameObject   _dividersRoot;
        private TileType     _lastType;

        private void Awake() => Setup();

        private void OnDestroy()
        {
            if (_previewMat       != null) Destroy(_previewMat);
            if (_previewMarkerMat != null) Destroy(_previewMarkerMat);
        }

        private void Setup()
        {
            _previewGO = new GameObject("TilePlacementPreview_GO");

            var mf = _previewGO.AddComponent<MeshFilter>();
            mf.sharedMesh = HexMeshBuilder.Build(0.95f, 0.15f);

            _previewRenderer = _previewGO.AddComponent<MeshRenderer>();
            _previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _previewRenderer.receiveShadows    = false;

            _previewMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _previewMat.SetFloat("_Surface", 1f);
            _previewMat.SetFloat("_Blend",   0f);
            _previewMat.SetFloat("_ZWrite",  0f);
            _previewMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _previewMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _previewRenderer.material = _previewMat;

            // 回転インジケーター（edge0方向を示す白い球）
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.SetParent(_previewGO.transform);
            marker.transform.localPosition = new Vector3(0.62f, 0.17f, 0f);
            marker.transform.localScale    = new Vector3(0.22f, 0.22f, 0.22f);
            Destroy(marker.GetComponent<Collider>());
            _previewMarkerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _previewMarkerMat.SetColor("_BaseColor", Color.white);
            var markerMR = marker.GetComponent<MeshRenderer>();
            markerMR.material          = _previewMarkerMat;
            markerMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerMR.receiveShadows    = false;

            _previewGO.SetActive(false);
        }

        /// <summary>
        /// ホバー中タイルの状態を受け取り、プレビューを更新する。
        /// 毎フレーム HandleHover から呼ばれることを前提とする。
        /// </summary>
        public void UpdatePreview(HexTile hoveredTile, bool canPlace, TileType tileType, int rotation)
        {
            if (_previewGO == null) return;

            bool show = hoveredTile != null && !hoveredTile.IsPlaced && canPlace && tileType != null;
            _previewGO.SetActive(show);
            if (!show) return;

            _previewGO.transform.position = hoveredTile.transform.position + Vector3.up * 0.02f;
            _previewGO.transform.rotation = Quaternion.Euler(0f, rotation * 60f, 0f);

            Color tc = tileType.tileColor;
            _previewMat.SetColor("_BaseColor", new Color(tc.r, tc.g, tc.b, 0.58f));

            // デッキタイルが変わったときだけ分割線を再生成
            if (_lastType != tileType)
            {
                _lastType = tileType;
                if (_dividersRoot != null) { Destroy(_dividersRoot); _dividersRoot = null; }

                if (tileType.dividerType != TileDividerType.None)
                {
                    _dividersRoot = new GameObject("PreviewDividers");
                    _dividersRoot.transform.SetParent(_previewGO.transform);
                    _dividersRoot.transform.localPosition = Vector3.zero;
                    _dividersRoot.transform.localRotation = Quaternion.identity;
                    HexTile.SpawnDividersFor(tileType, _dividersRoot.transform);
                }
            }
        }

        public void Hide()
        {
            if (_previewGO != null) _previewGO.SetActive(false);
        }
    }
}
