// 役割: RTS視点のカメラ操作を提供する MonoBehaviour。
//       右ドラッグでパン、ホイールでズーム、中ドラッグ・Q/Eで回転。

using UnityEngine;
using UnityEngine.InputSystem;

namespace ElfVillage.Core
{
    public class CameraController : MonoBehaviour
    {
        [Header("パン設定")]
        [SerializeField] private float panSpeed   = 0.015f;

        [Header("ズーム設定")]
        [SerializeField] private float zoomSpeed  = 3f;
        [SerializeField] private float minDistance = 5f;
        [SerializeField] private float maxDistance = 45f;

        [Header("回転設定")]
        [SerializeField] private float orbitSpeed = 0.35f;
        [SerializeField] private float pitchAngle = 55f; // 俯角（固定）

        // カメラが注視する地面上の点
        private Vector3 _pivot;
        private float   _yaw;
        private float   _distance;
        private Vector2 _prevMousePos;
        private bool    _prevMouseInitialized;

        private void Start()
        {
            _yaw      = transform.eulerAngles.y;
            // 現在位置からピボットを逆算
            _distance = Mathf.Clamp(transform.position.y / Mathf.Sin(Mathf.Deg2Rad * pitchAngle), minDistance, maxDistance);
            _pivot    = transform.position - GetOffset();
            _prevMouseInitialized = false;
        }

private void Update()
        {
            var mouse    = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null) return;

            Vector2 mousePos = mouse.position.ReadValue();

            // 初回フレームはdeltaを使わない
            if (!_prevMouseInitialized)
            {
                _prevMousePos        = mousePos;
                _prevMouseInitialized = true;
                return;
            }

            Vector2 delta   = mousePos - _prevMousePos;
            _prevMousePos   = mousePos;

            HandleZoom(mouse);
            HandlePan(mouse, delta);
            HandleOrbitMouse(mouse, delta);
            HandleOrbitKeyboard(keyboard);

            ApplyTransform();
        }

        // ── ズーム ────────────────────────────────────────────────
private void HandleZoom(Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;
            _distance = Mathf.Clamp(_distance - scroll * zoomSpeed, minDistance, maxDistance);
        }

        // ── 右ドラッグ：パン ──────────────────────────────────────
private void HandlePan(Mouse mouse, Vector2 delta)
        {
            if (!mouse.rightButton.isPressed) return;
            float scale = _distance * panSpeed;
            // ヨー からXZ平面の前後左右ベクトルを算出
            float yawRad = Mathf.Deg2Rad * _yaw;
            var right   = new Vector3( Mathf.Cos(yawRad), 0,  Mathf.Sin(yawRad));
            var forward = new Vector3(-Mathf.Sin(yawRad), 0,  Mathf.Cos(yawRad));
            _pivot -= (right * delta.x - forward * delta.y) * scale;
        }

        // ── 中ドラッグ：オービット回転 ────────────────────────────
private void HandleOrbitMouse(Mouse mouse, Vector2 delta)
        {
            if (!mouse.middleButton.isPressed) return;
            _yaw += delta.x * orbitSpeed;
        }

        // ── Q/E キー：60°スナップ回転 ────────────────────────────
private void HandleOrbitKeyboard(Keyboard keyboard)
        {
            if (keyboard == null) return;
            if (keyboard.qKey.wasPressedThisFrame) _yaw -= 60f;
            if (keyboard.eKey.wasPressedThisFrame) _yaw += 60f;
        }

private Vector3 GetOffset()
        {
            return Quaternion.Euler(pitchAngle, _yaw, 0f) * new Vector3(0, 0, -_distance);
        }

        private void ApplyTransform()
        {
            transform.position = _pivot + GetOffset();
            transform.rotation = Quaternion.Euler(pitchAngle, _yaw, 0f);
        }
    }
}
