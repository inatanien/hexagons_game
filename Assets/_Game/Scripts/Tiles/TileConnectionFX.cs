// 役割: TileConnectedEvent を購読し、接続ビジュアルを担当する MonoBehaviour。
//       A: エッジコネクター（同種タイル間の境界を埋める常時表示）
//       B: 接続フラッシュ（配置成功の気持ちよさ演出）
//       C: 3枚三角形の中心穴埋め（コネクターが届かないコーナー部分をフィラーで塞ぐ）

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class TileConnectionFX : MonoBehaviour
    {
        private const float ConnectorSurfaceOffset = 0.003f;
        private const float ConnectorThickness     = 0.005f;
        private const float GapOverlap             = 0.025f;

        private readonly GameObject[] _connectors = new GameObject[6];
        private readonly List<GameObject> _fillers = new List<GameObject>();

        private HexTile  _ownerTile;
        private Renderer _tileRenderer;
        private float    _tileHeight;
        private float    _inRadius;
        private float    _edgeWidth;

        // ── 初期化 ────────────────────────────────────────────────────

        private void Awake()
        {
            _ownerTile    = GetComponent<HexTile>();
            _tileRenderer = GetComponent<MeshRenderer>();
            _tileHeight   = _ownerTile.TileHeight;
            _inRadius     = _ownerTile.OuterRadius * 0.866f;
            _edgeWidth    = _ownerTile.OuterRadius * 0.94f;
        }

        private void OnEnable()  => EventBus.Subscribe<TileConnectedEvent>(OnTileConnected);
        private void OnDisable() => EventBus.Unsubscribe<TileConnectedEvent>(OnTileConnected);

        private void OnDestroy()
        {
            foreach (var conn in _connectors) if (conn != null) Destroy(conn);
            foreach (var f    in _fillers)    if (f    != null) Destroy(f);
        }

        // ── イベントハンドラ ──────────────────────────────────────────

        private void OnTileConnected(TileConnectedEvent evt)
        {
            if (evt.PlacedTile != _ownerTile) return;

            Color connColor = ResolveConnectorColor(evt.TileType);
            foreach (var edge in evt.Edges)
                ShowConnector(edge.Direction, edge.Neighbor.transform, connColor);

            FillTriangleGaps(evt, connColor);
            PlayFlash();
        }

        private static Color ResolveConnectorColor(TileType type)
            => type.connectorColor.a > 0.01f ? type.connectorColor : type.tileColor;

        // ── A: エッジコネクター ────────────────────────────────────────

        private void ShowConnector(int dir, Transform neighborTransform, Color color)
        {
            if (_connectors[dir] == null)
                _connectors[dir] = CreateConnectorGO(dir);

            Vector3 myPos  = transform.position;
            Vector3 nPos   = neighborTransform.position;
            Vector3 midPos = (myPos + nPos) * 0.5f;
            midPos.y = myPos.y + _tileHeight * 0.5f + ConnectorSurfaceOffset;

            Vector3 toNeighbor = nPos - myPos;
            toNeighbor.y = 0f;
            float dist     = toNeighbor.magnitude;
            float gapDepth = Mathf.Max(dist - _inRadius * 2f + GapOverlap, 0.05f);
            toNeighbor.Normalize();

            var conn = _connectors[dir];
            conn.transform.position   = midPos;
            conn.transform.rotation   = Quaternion.LookRotation(toNeighbor, Vector3.up);
            conn.transform.localScale = new Vector3(_edgeWidth, ConnectorThickness, gapDepth);
            conn.GetComponent<Renderer>().material.color = color;
            conn.SetActive(true);
        }

        private GameObject CreateConnectorGO(int dir)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Conn_{gameObject.name}_d{dir}";
            Destroy(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            go.SetActive(false);
            return go;
        }

        // ── C: 3枚三角形の中心穴埋め ──────────────────────────────────

        // 接続方向が連続（dir N と dir N+1 mod 6）しているとき、
        // 配置タイル・隣A・隣B の3枚が三角形をなす。
        // 3枚の重心（＝共有頂点）にフィラーを置いてコーナー隙間を埋める。
        private void FillTriangleGaps(TileConnectedEvent evt, Color color)
        {
            if (evt.Edges.Count < 2) return;

            var byDir = new Dictionary<int, ConnectionEdge>(evt.Edges.Count);
            foreach (var edge in evt.Edges)
                byDir[edge.Direction] = edge;

            foreach (var edge in evt.Edges)
            {
                int nextDir = (edge.Direction + 1) % 6;
                if (!byDir.TryGetValue(nextDir, out ConnectionEdge nextEdge)) continue;

                Vector3 pA = transform.position;
                Vector3 pB = edge.Neighbor.transform.position;
                Vector3 pC = nextEdge.Neighbor.transform.position;

                Vector3 center = (pA + pB + pC) / 3f;
                center.y = pA.y + _tileHeight * 0.5f + ConnectorSurfaceOffset;

                // コーナー隙間の幅 = タイル中心〜重心の水平距離 − 内接円半径
                float dx = pA.x - center.x, dz = pA.z - center.z;
                float distToCenter = Mathf.Sqrt(dx * dx + dz * dz);
                float gapRadius    = distToCenter - _inRadius;

                _fillers.Add(CreateFillerGO(center, color, gapRadius * 1.6f));
            }
        }

        private GameObject CreateFillerGO(Vector3 worldPos, Color color, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TriFill_" + gameObject.name;
            Destroy(go.GetComponent<Collider>());

            go.transform.position   = worldPos;
            go.transform.localScale = new Vector3(size, ConnectorThickness, size);

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.material.color    = color;
            return go;
        }

        // ── B: 接続フラッシュ ─────────────────────────────────────────

        private void PlayFlash()
        {
            if (gameObject.activeInHierarchy)
                StartCoroutine(FlashCoroutine());
        }

        private IEnumerator FlashCoroutine()
        {
            if (_tileRenderer == null) yield break;
            yield return new WaitForSeconds(0.36f);

            Color baseColor  = _tileRenderer.material.color;
            Color flashColor = Color.Lerp(baseColor, Color.white, 0.60f);
            float duration   = 0.28f;
            float elapsed    = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Sin((elapsed / duration) * Mathf.PI);
                _tileRenderer.material.color = Color.Lerp(baseColor, flashColor, t);
                yield return null;
            }
            _tileRenderer.material.color = baseColor;
        }
    }
}
