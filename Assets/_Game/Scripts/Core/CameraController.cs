// 役割: RTS視点のカメラ操作を提供する MonoBehaviour。
//       左ドラッグ（タイル外）でパン、ホイールでズーム、中ドラッグ・Q/Eで回転。

using UnityEngine;
using UnityEngine.InputSystem;

namespace ElfVillage.Core
{
    public class CameraController : MonoBehaviour
    {
        [Header("パン設定")]
        [SerializeField] private float panSpeed    = 0.001f;

        [Header("ズーム設定")]
        [SerializeField] private float zoomSpeed       = 3f;
        [SerializeField] private float initialDistance = 4f;
        [SerializeField] private float minDistance     = 4f;
        [SerializeField] private float maxDistance     = 35f;

        [Header("回転設定")]
        [SerializeField] private float orbitSpeed  = 0.25f;
        [SerializeField] private float pitchAngle  = 40f;
        [Tooltip("HexGridManager の tileSize と合わせること")]
        [SerializeField] private float hexSize     = 2.0f;

        [Header("スムージング")]
        [SerializeField] private float smoothSpeed = 12f;

        // 目標値（入力で即時更新）
        private Vector3 _targetPivot;
        private float   _targetYaw;
        private float   _targetDistance;

        // 実際の描画値（目標値に向かって補間）
        private Vector3 _pivot;
        private float   _yaw;
        private float   _distance;

        private Vector2 _prevMousePos;
        private bool    _prevMouseInitialized;

        // パン用
        private bool  _isPanning;
        private float _panPressTime = -1f;

        // タップ vs 長押し判定しきい値（HexGridManager と合わせること）
        private const float PanHoldThreshold = 0.2f;

        // オービット回転用（押した瞬間の基準点を固定）
        private Vector3 _orbitCenter;
        private bool    _hasOrbitCenter;

        private void Start()
        {
            _yaw      = transform.eulerAngles.y;
            _distance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
            _pivot    = transform.position - GetOffset(_distance, _yaw);

            _targetPivot    = _pivot;
            _targetYaw      = _yaw;
            _targetDistance = _distance;

            _prevMouseInitialized = false;
        }

        private void Update()
        {
            var mouse    = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null) return;

            Vector2 mousePos = mouse.position.ReadValue();

            if (!_prevMouseInitialized)
            {
                _prevMousePos         = mousePos;
                _prevMouseInitialized = true;
                return;
            }

            Vector2 delta = mousePos - _prevMousePos;
            _prevMousePos = mousePos;

            HandleZoom(mouse);
            HandlePan(mouse, delta);
            HandleOrbitMouse(mouse, delta);
            HandleOrbitKeyboard(keyboard);

            SmoothAndApply();
        }

        // ── ズーム（カーソル基準） ────────────────────────────────
        private void HandleZoom(Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;

            float oldDist = _targetDistance;
            _targetDistance = Mathf.Clamp(_targetDistance - scroll * zoomSpeed, minDistance, maxDistance);

            if (TryGetGroundPoint(mouse, out Vector3 cursorGround))
            {
                float ratio = 1f - _targetDistance / oldDist;
                _targetPivot = Vector3.Lerp(_targetPivot, cursorGround, ratio * 0.6f);
            }
        }

        // ── 左ドラッグ：パン ──────────────────────────────────────
        // スクリーンデルタ × ズーム距離 × panSpeed でクリック位置に依存しない一定移動量を実現。
        // どこをクリックしても 0.2秒長押しでパン開始（素早いタップはタイル配置）。
        // 配置済みタイルはコライダーが無効のため、即パンが発生して「カメラが飛ぶ」バグを防ぐ。
        private void HandlePan(Mouse mouse, Vector2 screenDelta)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _panPressTime = Time.time;
                _isPanning    = false;
            }

            if (!mouse.leftButton.isPressed)
            {
                _isPanning    = false;
                _panPressTime = -1f;
                return;
            }

            if (!_isPanning && _panPressTime >= 0f && Time.time - _panPressTime >= PanHoldThreshold)
                _isPanning = true;

            if (!_isPanning) return;

            // カメラのXZ方向ベクトルにデルタを乗せて移動（クリック位置・距離に依存しない）
            var cam = Camera.main;
            float scale = _distance * panSpeed;
            Vector3 right   = cam.transform.right;   right.y   = 0f; right.Normalize();
            Vector3 forward = cam.transform.forward; forward.y = 0f; forward.Normalize();
            Vector3 move = (-right * screenDelta.x + -forward * screenDelta.y) * scale;
            _targetPivot += move;
            _pivot       += move;
        }

        // ── 中ドラッグ：ヨー回転（画面中央基準・押した瞬間に固定） ──
        private void HandleOrbitMouse(Mouse mouse, Vector2 delta)
        {
            // 押した瞬間だけ基準点を記録
            if (mouse.middleButton.wasPressedThisFrame)
                _hasOrbitCenter = TryGetScreenCenterGroundPoint(out _orbitCenter);

            if (!mouse.middleButton.isPressed) { _hasOrbitCenter = false; return; }
            if (!_hasOrbitCenter) return;

            float deltaYaw = delta.x * orbitSpeed;
            if (Mathf.Approximately(deltaYaw, 0f)) return;

            _targetYaw += deltaYaw;
            Vector3 offset = _targetPivot - _orbitCenter;
            offset = Quaternion.Euler(0f, deltaYaw, 0f) * offset;
            _targetPivot = _orbitCenter + offset;
        }

        // ── Q/E キー：60°スナップ回転（画面中央基準） ──────────
        private void HandleOrbitKeyboard(Keyboard keyboard)
        {
            if (keyboard == null) return;
            float deltaYaw = 0f;
            if (keyboard.qKey.wasPressedThisFrame) deltaYaw = -60f;
            if (keyboard.eKey.wasPressedThisFrame) deltaYaw = +60f;
            if (Mathf.Approximately(deltaYaw, 0f)) return;

            _targetYaw += deltaYaw;

            if (TryGetScreenCenterGroundPoint(out Vector3 center))
            {
                Vector3 offset = _targetPivot - center;
                offset = Quaternion.Euler(0f, deltaYaw, 0f) * offset;
                _targetPivot = center + offset;
            }
        }

        // ── スムージングして適用 ──────────────────────────────────
        private void SmoothAndApply()
        {
            float t   = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
            _pivot    = Vector3.Lerp(_pivot, _targetPivot, t);
            _yaw      = Mathf.LerpAngle(_yaw, _targetYaw, t);
            _distance = Mathf.Lerp(_distance, _targetDistance, t);

            transform.position = _pivot + GetOffset(_distance, _yaw);
            transform.rotation = Quaternion.Euler(pitchAngle, _yaw, 0f);
        }

        // ── マウス下の Y=0 平面上の点を取得 ──────────────────────
        private bool TryGetGroundPoint(Mouse mouse, out Vector3 result)
        {
            result = Vector3.zero;
            Ray ray = Camera.main.ScreenPointToRay(mouse.position.ReadValue());
            if (Mathf.Approximately(ray.direction.y, 0f)) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            result = ray.origin + ray.direction * t;
            return true;
        }

        // ── 画面中央の Y=0 平面上の点を取得 ──────────────────────
        private bool TryGetScreenCenterGroundPoint(out Vector3 result)
        {
            result = Vector3.zero;
            var cam = Camera.main;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            if (Mathf.Approximately(ray.direction.y, 0f)) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0f) return false;
            result = ray.origin + ray.direction * t;
            return true;
        }

        // ── 画面中央に最も近いグリッド（六角形）の中心点を取得 ──
        // HexCoord と同じ Cube Coordinates の計算式をインライン実装。
        // Core は Tiles/HexGrid を参照できないためここに持つ。
        private bool TryGetScreenCenterHexPoint(out Vector3 result)
        {
            if (!TryGetScreenCenterGroundPoint(out result)) return false;
            result = SnapToNearestHex(result);
            return true;
        }

        private Vector3 SnapToNearestHex(Vector3 worldPos)
        {
            float fq = (2f / 3f * worldPos.x) / hexSize;
            float fr = (-1f / 3f * worldPos.x + Mathf.Sqrt(3f) / 3f * worldPos.z) / hexSize;
            float fs = -fq - fr;

            int rq = Mathf.RoundToInt(fq);
            int rr = Mathf.RoundToInt(fr);
            int rs = Mathf.RoundToInt(fs);
            float dq = Mathf.Abs(rq - fq), dr = Mathf.Abs(rr - fr), ds = Mathf.Abs(rs - fs);
            if      (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds)            rr = -rq - rs;

            float x = hexSize * (1.5f * rq);
            float z = hexSize * (Mathf.Sqrt(3f) * (rr + rq * 0.5f));
            return new Vector3(x, 0f, z);
        }

        private Vector3 GetOffset(float dist, float yaw)
        {
            return Quaternion.Euler(pitchAngle, yaw, 0f) * new Vector3(0, 0, -dist);
        }
    }
}
