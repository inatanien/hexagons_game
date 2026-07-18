// 役割: タイル配置プレビュー（ゴーストタイル）の表示を担う MonoBehaviour。
//       HexGridManager から分離し、見た目の責務を単独で管理する。

using UnityEngine;
using ElfVillage.HexGrid;

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
        // Session 11: 複合要素タイル（TileType.HasVisualElements）のみ、座標が変わった際に
        // 再生成する。legacyタイルは座標に依存しない見た目のため対象外（不要な再生成を避ける）。
        private HexCoord      _lastCoord;

        // 同じ tileCategory の隣接タイルがある辺を示すグロー表示（6方向ぶん）
        private readonly GameObject[] _synergyEdgeGOs = new GameObject[6];
        private Material _synergyMat;
        private bool     _synergyActive;

        // dir(0〜5) → ワールド角度（度）。HexTile.EdgeCenter と同じ変換式。
        private static readonly float[] s_DirToWorldAngle = { 30f, 330f, 270f, 210f, 150f, 90f };

        private void Awake() => Setup();

        private void OnDestroy()
        {
            if (_previewMat       != null) Destroy(_previewMat);
            if (_previewMarkerMat != null) Destroy(_previewMarkerMat);
            if (_synergyMat       != null) Destroy(_synergyMat);
        }

        private void Update()
        {
            // 光っている辺があるときだけ、ゆっくり明滅させて「シナジーがある」ことを優しく伝える
            if (!_synergyActive || _synergyMat == null) return;
            Color c = _synergyMat.GetColor("_BaseColor");
            c.a = Mathf.Lerp(0.35f, 0.85f, (Mathf.Sin(Time.time * 2.5f) + 1f) * 0.5f);
            _synergyMat.SetColor("_BaseColor", c);
        }

        private void Setup()
        {
            _previewGO = new GameObject("TilePlacementPreview_GO");

            var mf = _previewGO.AddComponent<MeshFilter>();
            mf.sharedMesh = HexMeshBuilder.Build(2.0f, 0.30f);

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
            marker.transform.localPosition = new Vector3(1.13f, 0.25f, 0f);
            marker.transform.localScale    = new Vector3(0.40f, 0.40f, 0.40f);
            Destroy(marker.GetComponent<Collider>());
            _previewMarkerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _previewMarkerMat.SetColor("_BaseColor", Color.white);
            var markerMR = marker.GetComponent<MeshRenderer>();
            markerMR.material          = _previewMarkerMat;
            markerMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markerMR.receiveShadows    = false;

            BuildSynergyEdges();

            _previewGO.SetActive(false);
        }

        // 6方向ぶんの境界線グローを生成（初期状態は全て非表示）。
        // 辺の両端座標は HexTile.EdgeCenter と同じ角度規則（s_DirToWorldAngle ± 30°）で求める。
        private void BuildSynergyEdges()
        {
            const float outerRadius = 2.0f;
            const float tileHeight  = 0.30f;
            float topY = tileHeight * 0.5f + 0.015f;

            _synergyMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _synergyMat.SetFloat("_Surface", 1f);
            _synergyMat.SetFloat("_Blend",   0f);
            _synergyMat.SetFloat("_ZWrite",  0f);
            _synergyMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _synergyMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            _synergyMat.SetColor("_BaseColor", new Color(1.0f, 0.90f, 0.50f, 0.6f));

            var root = new GameObject("SynergyGlow");
            root.transform.SetParent(_previewGO.transform);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            for (int dir = 0; dir < 6; dir++)
            {
                float centerAngle = s_DirToWorldAngle[dir];
                Vector3 a = EdgeCorner(centerAngle - 30f, outerRadius, topY);
                Vector3 b = EdgeCorner(centerAngle + 30f, outerRadius, topY);

                var go = new GameObject("SynergyEdge_" + dir);
                go.transform.SetParent(root.transform);
                go.transform.localPosition = Vector3.zero;

                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial     = _synergyMat;
                lr.startWidth         = 0.12f;
                lr.endWidth           = 0.12f;
                lr.useWorldSpace      = false;
                lr.numCapVertices     = 4;
                lr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows     = false;
                lr.positionCount      = 2;
                lr.SetPositions(new[] { a, b });

                go.SetActive(false);
                _synergyEdgeGOs[dir] = go;
            }
        }

        private static Vector3 EdgeCorner(float angleDeg, float radius, float y)
        {
            float a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
        }

        public void UpdatePreview(HexTile hoveredTile, bool canPlace, TileType tileType, int rotation,
                                   bool[] synergyEdges = null)
        {
            if (_previewGO == null) return;

            bool show = hoveredTile != null && !hoveredTile.IsPlaced && canPlace && tileType != null;
            _previewGO.SetActive(show);
            if (!show)
            {
                SetSynergyEdges(null);
                return;
            }

            // synergyEdges はワールド絶対方向（隣接セルの方向）。プレビューのメッシュ自体は
            // 無回転のローカル座標で作られ、GameObject の transform.rotation で rotation*60°
            // だけ回転させて見せているため、ローカル辺インデックスは world - rotation になる
            // （TileData.GetEdge の `tileType.GetEdge(direction - rotation)` と同じ変換）。
            bool[] localSynergy = null;
            if (synergyEdges != null)
            {
                localSynergy = new bool[6];
                for (int worldDir = 0; worldDir < 6; worldDir++)
                {
                    int localDir = ((worldDir - rotation) % 6 + 6) % 6;
                    localSynergy[localDir] = synergyEdges[worldDir];
                }
            }
            SetSynergyEdges(localSynergy);

            _previewGO.transform.position = hoveredTile.transform.position + Vector3.up * 0.02f;
            _previewGO.transform.rotation = Quaternion.Euler(0f, rotation * 60f, 0f);

            // 地面は実タイルと同じ「groundTexture × tileColor」を半透明化したものを表示する
            // （Session 11）。EffectivePreviewColorはUI識別専用のためここでは使わない。
            // groundTexture未設定時は前のTileTypeのテクスチャが残らないよう必ずnullへ戻す。
            _previewMat.SetTexture("_BaseMap", tileType.groundTexture);
            Color tc = tileType.tileColor;
            _previewMat.SetColor("_BaseColor", new Color(tc.r, tc.g, tc.b, 0.58f));

            // 再生成条件（Session 11）:
            //  - TileTypeが変わった場合: legacy/複合の両方で再生成
            //  - 座標だけが変わった場合: 座標シードを使う複合要素タイルだけ再生成
            //    （legacyのSpawnPropsPreviewは座標を使わず見た目が変化しないため不要な再生成を避ける）
            bool usesElementLayout = tileType.HasVisualElements;
            HexCoord hoveredCoord  = hoveredTile.Data.coord;
            bool needsRebuild = _lastType != tileType
                             || (usesElementLayout && _lastCoord != hoveredCoord);

            if (needsRebuild)
            {
                _lastType  = tileType;
                _lastCoord = hoveredCoord;
                if (_dividersRoot != null) { Destroy(_dividersRoot); _dividersRoot = null; }

                _dividersRoot = new GameObject("PreviewVisuals");
                _dividersRoot.transform.SetParent(_previewGO.transform);
                _dividersRoot.transform.localPosition = Vector3.zero;
                _dividersRoot.transform.localRotation = Quaternion.identity;
                HexTile.SpawnDividersFor(tileType, _dividersRoot.transform);
                TilePropVisualBuilder.SpawnProps(tileType, _dividersRoot.transform, hoveredCoord);
            }
        }

        // ローカル辺インデックス基準の bool[6] を受け取り、該当する辺のグローだけ表示する
        private void SetSynergyEdges(bool[] localSynergyEdges)
        {
            _synergyActive = false;
            for (int i = 0; i < 6; i++)
            {
                bool on = localSynergyEdges != null && localSynergyEdges[i];
                if (on) _synergyActive = true;
                _synergyEdgeGOs[i]?.SetActive(on);
            }
        }

        public void Hide()
        {
            if (_previewGO != null) _previewGO.SetActive(false);
            SetSynergyEdges(null);
        }
    }
}
