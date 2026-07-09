// 役割: RiverBridgeEvent を受け取り、対象タイル上に川を渡るアーチ状の橋をプロシージャル生成する。
//       HexTile.CreateWaterFlow の岸辺と同じ手法（Cube を並べる低ポリ表現）を流用する。

using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class BridgeSystem : MonoBehaviour
    {
        [Header("橋の形状")]
        [SerializeField] private int   _deckSegments  = 8;
        [SerializeField] private float _deckWidth     = 0.45f;
        [SerializeField] private float _deckThickness = 0.035f;
        [SerializeField] private float _archHeight    = 0.18f;
        [SerializeField] private float _bankOverhang  = 0.08f;
        [SerializeField] private float _railHeight    = 0.07f;
        [SerializeField] private float _railThickness = 0.025f;

        [Header("色")]
        [SerializeField] private Color _deckColor = new Color(0.62f, 0.58f, 0.52f);
        [SerializeField] private Color _railColor = new Color(0.42f, 0.36f, 0.30f);

        private void OnEnable()  => EventBus.Subscribe<RiverBridgeEvent>(OnRiverBridge);
        private void OnDisable() => EventBus.Unsubscribe<RiverBridgeEvent>(OnRiverBridge);

        private void OnRiverBridge(RiverBridgeEvent evt)
        {
            if (evt.Tile == null) return;
            SpawnBridge(evt.Tile);
        }

        // ── 橋の生成 ─────────────────────────────────────────────────────
        // タイルの流路中央（t=0.5）を通り、流れに垂直な向きへアーチを架ける。
        // アーチ形状は正弦カーブで近似し、区間ごとに Cube を並べて橋桁・欄干とする。

        private void SpawnBridge(HexTile tile)
        {
            if (!tile.TryGetRiverBridgeAnchor(out Vector3 localCenter, out Vector3 localTangent, out float riverWidth))
                return;

            Vector3 crossDir = new Vector3(-localTangent.z, 0f, localTangent.x);
            float   halfSpan = riverWidth * 0.5f + _bankOverhang;
            float   baseY    = tile.TileHeight * 0.5f;

            var root = new GameObject("RiverBridge");
            // worldPositionStays を false にし、タイルの縮小スケール（プレハブ既定の約0.31倍）を
            // そのまま継承させる。true（既定）だとワールドスケール維持のため逆補正がかかり、
            // タイルより橋が大きく描画されてしまう。
            root.transform.SetParent(tile.transform, false);
            root.transform.localPosition = new Vector3(localCenter.x, baseY, localCenter.z);
            root.transform.localRotation = Quaternion.LookRotation(crossDir, Vector3.up);

            Vector3 prevPos = ArchPoint(0, _deckSegments, halfSpan, _archHeight);
            for (int i = 1; i <= _deckSegments; i++)
            {
                Vector3 curPos = ArchPoint(i, _deckSegments, halfSpan, _archHeight);
                Vector3 seg    = curPos - prevPos;
                Vector3 mid    = (curPos + prevPos) * 0.5f;
                float   len    = seg.magnitude;
                Quaternion rot = Quaternion.LookRotation(seg.normalized, Vector3.up);

                CreateBeam(root.transform, mid, rot, len, _deckWidth, _deckThickness, _deckColor);

                // 横方向オフセットのみ符号反転して左右に振り分け、上方向オフセットは両側とも
                // 常に同じ向き（+）で加える。両方まとめて rot * (±w, railY, 0) にすると、
                // 反対側は railY も反転してデッキの下に沈んでしまう（既知の不具合）。
                float   railY     = _railHeight * 0.5f + _deckThickness * 0.5f;
                Vector3 sideOffset = rot * new Vector3(_deckWidth * 0.5f, 0f, 0f);
                Vector3 upOffset   = rot * new Vector3(0f, railY, 0f);
                CreateBeam(root.transform, mid + sideOffset + upOffset, rot, len, _railThickness, _railHeight, _railColor);
                CreateBeam(root.transform, mid - sideOffset + upOffset, rot, len, _railThickness, _railHeight, _railColor);

                prevPos = curPos;
            }
        }

        // 弧の中心線: 中央(u=0.5)が最も高くなる正弦カーブ
        private static Vector3 ArchPoint(int i, int segments, float halfSpan, float archHeight)
        {
            float u = (float)i / segments;
            float z = Mathf.Lerp(-halfSpan, halfSpan, u);
            float y = archHeight * Mathf.Sin(Mathf.PI * u);
            return new Vector3(0f, y, z);
        }

        private static void CreateBeam(Transform parent, Vector3 localPos, Quaternion localRot,
                                        float length, float width, float thickness, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale    = new Vector3(width, thickness, length + 0.01f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.color = color;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
        }
    }
}
