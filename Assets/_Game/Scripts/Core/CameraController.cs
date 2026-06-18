// 役割: RTS視点のカメラ操作を提供する MonoBehaviour。
//       右ドラッグでパン、ホイールでズーム、中ドラッグ・Q/Eで回転。

using UnityEngine;
using UnityEngine.InputSystem;

namespace ElfVillage.Core
{
    public class CameraController : MonoBehaviour
    {
        [Header("パン設定")]
        [SerializeField] private float panSpeed    = 0.006f;

        [Header("ズーム設定")]
        [SerializeField] private float zoomSpeed   = 3f;
        [SerializeField] private float minDistance = 5f;
        [SerializeField] private float maxDistance = 45f;

        [Header("回転設定")]
        [SerializeField] private float orbitSpeed  = 0.25f;
        [SerializeField] private float pitchAngle  = 55f; // 俯角（固定）

        [Header("スムージング")]
        [SerializeField] private float smoothSpeed = 12f; // 大きいほど即レスポンス

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

        private void Start()
        {
            _yaw      = transform.eulerAngles.y;
            _distance = Mathf.Clamp(transform.position.y / Mathf.Sin(Mathf.Deg2Rad * pitchAngle), minDistance, maxDistance);
            _pivot    = transform.position - GetOffset(_distance, _yaw);

            // 目標値を現在値で初期化
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

            SmoothAndApply();
        }

        // ── ズーム ────────────────────────────────────────────────
        private void HandleZoom(Mouse mouse)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f)) return;
            _targetDistance = Mathf.Clamp(_targetDistance - scroll * zoomSpeed, minDistance, maxDistance);
        }

        // ── 右ドラッグ：パン ──────────────────────────────────────
        private void HandlePan(Mouse mouse, Vector2 delta)
        {
            if (!mouse.rightButton.isPressed) return;
            float scale  = _distance * panSpeed;
            float yawRad = Mathf.Deg2Rad * _yaw;
            var right    = new Vector3( Mathf.Cos(yawRad), 0,  Mathf.Sin(yawRad));
            var forward  = new Vector3(-Mathf.Sin(yawRad), 0,  Mathf.Cos(yawRad));
            _targetPivot -= (right * delta.x - forward * delta.y) * scale;
        }

        // ── 中ドラッグ：オービット回転 ────────────────────────────
        private void HandleOrbitMouse(Mouse mouse, Vector2 delta)
        {
            if (!mouse.middleButton.isPressed) return;
            _targetYaw += delta.x * orbitSpeed;
        }

        // ── Q/E キー：60°スナップ回転 ────────────────────────────
        private void HandleOrbitKeyboard(Keyboard keyboard)
        {
            if (keyboard == null) return;
            if (keyboard.qKey.wasPressedThisFrame) _targetYaw -= 60f;
            if (keyboard.eKey.wasPressedThisFrame) _targetYaw += 60f;
        }

        // ── スムージングして適用 ──────────────────────────────────
        private void SmoothAndApply()
        {
            float t    = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
            _pivot     = Vector3.Lerp(_pivot, _targetPivot, t);
            _yaw       = Mathf.LerpAngle(_yaw, _targetYaw, t);
            _distance  = Mathf.Lerp(_distance, _targetDistance, t);

            transform.position = _pivot + GetOffset(_distance, _yaw);
            transform.rotation = Quaternion.Euler(pitchAngle, _yaw, 0f);
        }

        private Vector3 GetOffset(float dist, float yaw)
        {
            return Quaternion.Euler(pitchAngle, yaw, 0f) * new Vector3(0, 0, -dist);
        }
    }
}
