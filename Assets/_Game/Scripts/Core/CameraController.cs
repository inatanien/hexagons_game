// 役割: RTS視点のカメラ操作を提供する MonoBehaviour。
//       右ドラッグでパン、ホイールでズーム、中ドラッグ・Q/Eで回転。

using UnityEngine;
using UnityEngine.InputSystem;

namespace ElfVillage.Core
{
    public class CameraController : MonoBehaviour
    {
        [Header("パン設定")]
        [SerializeField] private float panSpeed = 0.02f;

        [Header("ズーム設定")]
        [SerializeField] private float zoomSpeed = 2f;
        [SerializeField] private float minZoom = 5f;
        [SerializeField] private float maxZoom = 40f;

        [Header("回転設定")]
        [SerializeField] private float orbitSpeed = 0.3f;

        private Vector2 _prevMousePos;
        private float   _yaw;   // 水平回転角
        private float   _pitch; // 垂直角（固定）

        private void Start()
        {
            // 初期角度を現在の回転から取得
            _yaw   = transform.eulerAngles.y;
            _pitch = transform.eulerAngles.x;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null) return;

            Vector2 mousePos = mouse.position.ReadValue();
            Vector2 delta    = mousePos - _prevMousePos;
            _prevMousePos    = mousePos;

            HandleZoom(mouse);
            HandlePan(mouse, delta);
            HandleOrbitMouse(mouse, delta);
            HandleOrbitKeyboard(keyboard);
        }

        // ── ズーム ────────────────────────────────────────────────
        private void HandleZoom(Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;

            Vector3 forward = transform.forward;
            Vector3 pos     = transform.position + forward * scroll * zoomSpeed;

            // Y座標でズーム制限
            pos.y = Mathf.Clamp(pos.y, minZoom, maxZoom);
            transform.position = pos;
        }

        // ── 右ドラッグ：パン ──────────────────────────────────────
        private void HandlePan(Mouse mouse, Vector2 delta)
        {
            if (!mouse.rightButton.isPressed) return;

            Vector3 right   = transform.right;
            Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;

            transform.position -= (right * delta.x + forward * delta.y) * panSpeed * transform.position.y;
        }

        // ── 中ドラッグ：オービット回転 ────────────────────────────
        private void HandleOrbitMouse(Mouse mouse, Vector2 delta)
        {
            if (!mouse.middleButton.isPressed) return;
            _yaw += delta.x * orbitSpeed;
            ApplyRotation();
        }

        // ── Q/E キー：60°スナップ回転 ────────────────────────────
        private void HandleOrbitKeyboard(Keyboard keyboard)
        {
            if (keyboard == null) return;
            if (keyboard.qKey.wasPressedThisFrame) { _yaw -= 60f; ApplyRotation(); }
            if (keyboard.eKey.wasPressedThisFrame) { _yaw += 60f; ApplyRotation(); }
        }

        private void ApplyRotation()
        {
            // グリッド中心（原点）を軸にオービット
            float   dist      = Vector3.Distance(transform.position, Vector3.zero);
            Quaternion rot    = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3  offset   = rot * new Vector3(0, 0, -dist);
            transform.position = Vector3.zero + offset;
            transform.rotation = rot;
        }
    }
}
