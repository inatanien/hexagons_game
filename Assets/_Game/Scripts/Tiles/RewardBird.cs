// 役割: 1羽の鳥の飛行を担当する最小限のコンポーネント。群れAI・巣・餌・成長・
//       プレイヤー操作との連動は持たない。中心点の周りをX/Zで異なる周波数の
//       sin/cosカーブ（リサージュ曲線）で飛び回り、わずかに上下し、常に指定した
//       中心＋半幅（extentX/extentZ）・振幅の範囲内に収まる。単純な円運動と違い、
//       X/Zの周波数比を無関係な値にすることで、森クラスターの矩形範囲全体を
//       ゆらぎながら自由に飛び回っているように見える。
//       経過時間→座標の変換はComputePosition()に純粋関数として切り出してあり、
//       EditModeからコルーチン/Update実行なしで直接検証できる
//       （QuestNotificationUI.ComputeFrameと同じ設計方針）。
//       森クラスターが後から成長した場合はUpdateBounds()で中心・範囲だけを
//       差し替える（周波数・位相・経過時間は据え置くため、範囲が広がっても
//       飛行が不自然に飛躍しない）。
//
//       夜間はHide(hidePoint)で現在位置から指定地点（呼び出し元が「一番近い森タイル」を
//       渡す想定）へ一直線に飛んで消え、朝Show()で同じ地点から通常の飛行中心へ
//       一直線に飛んで再び現れる（Hideの単純な逆再生）。移動中はDOTween等を使わず
//       Vector3.Lerp + SmoothStepのみで実装している。パトロール中の経過時間(_time)は
//       隠れている・移動中は進めず、Patrolling復帰時に不自然な位置へ飛ばないようにする。
//
//       最大羽数がごく少数（Stage 5時点で3羽まで）であるため、鳥ごとに素朴な
//       Updateを持たせても負荷は問題にならない。

using UnityEngine;

namespace ElfVillage.Tiles
{
    public class RewardBird : MonoBehaviour
    {
        private const float TransitionDuration = 1.4f; // 隠れる/現れる移動にかける秒数

        private enum FlightState { Patrolling, FlyingToHide, Hidden, FlyingToShow }

        private Vector3 _centerOffset; // 個体差用のオフセット（Initで1回だけ決定、以後は据え置き）
        private Vector3 _baseCenter;
        private float   _extentX;
        private float   _extentZ;
        private float   _freqX;
        private float   _freqZ;
        private float   _bobAmplitude;
        private float   _bobFrequency;
        private float   _phaseX;
        private float   _phaseZ;
        private float   _time;

        private FlightState _state = FlightState.Patrolling;
        private Vector3      _transitionStart;
        private Vector3      _transitionTarget;
        private float         _transitionTime;
        private MeshRenderer[] _renderers;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<MeshRenderer>(true);
        }

        public void Init(Vector3 baseCenter, Vector3 centerOffset, float extentX, float extentZ,
                          float freqX, float freqZ, float bobAmplitude, float bobFrequency,
                          float phaseX, float phaseZ)
        {
            _baseCenter   = baseCenter;
            _centerOffset = centerOffset;
            _extentX      = extentX;
            _extentZ      = extentZ;
            _freqX        = freqX;
            _freqZ        = freqZ;
            _bobAmplitude = bobAmplitude;
            _bobFrequency = bobFrequency;
            _phaseX       = phaseX;
            _phaseZ       = phaseZ;
            _time         = 0f;
            _state        = FlightState.Patrolling;

            // 最初のUpdateが回るまでの1フレーム、原点に一瞬表示されてしまうのを防ぐため、
            // 生成直後にt=0の位置へ即座にスナップしておく。
            transform.position = ComputePosition(Center, _extentX, _extentZ, _freqX, _freqZ, _bobAmplitude, _bobFrequency, _phaseX, _phaseZ, _time);
        }

        // 森クラスターの成長に合わせて中心・飛行範囲だけを更新する（周波数・位相・経過時間は変えない）。
        public void UpdateBounds(Vector3 baseCenter, float extentX, float extentZ)
        {
            _baseCenter = baseCenter;
            _extentX    = extentX;
            _extentZ    = extentZ;
        }

        // 現在位置からhidePointへ一直線に飛び、到着したら姿を消す。
        public void Hide(Vector3 hidePoint)
        {
            if (_state == FlightState.FlyingToHide || _state == FlightState.Hidden) return;
            BeginTransition(hidePoint, FlightState.FlyingToHide);
        }

        // 現在位置（Hideで消えた地点）から通常の飛行中心へ一直線に飛び、再び姿を見せる。
        public void Show()
        {
            if (_state == FlightState.Patrolling || _state == FlightState.FlyingToShow) return;
            SetVisible(true);
            BeginTransition(Center, FlightState.FlyingToShow);
        }

        private void BeginTransition(Vector3 target, FlightState state)
        {
            _transitionStart = transform.position;
            _transitionTarget = target;
            _transitionTime   = 0f;
            _state            = state;

            var dir = _transitionTarget - _transitionStart;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized);
        }

        private Vector3 Center => _baseCenter + _centerOffset;

        // 経過時間から見た座標を求める純粋関数（副作用なし）。
        // 水平方向は中心からX方向±extentX、Z方向±extentZ以内、垂直方向はbobAmplitude以内に収まる。
        // freqX と freqZ を異なる値にすることでリサージュ曲線になり、単純な円軌道より
        // 範囲全体をゆらぎながら飛び回っているように見える。
        public static Vector3 ComputePosition(Vector3 center, float extentX, float extentZ,
                                                float freqX, float freqZ,
                                                float bobAmplitude, float bobFrequency,
                                                float phaseX, float phaseZ, float time)
        {
            float x = center.x + extentX * Mathf.Cos(freqX * time + phaseX);
            float z = center.z + extentZ * Mathf.Sin(freqZ * time + phaseZ);
            float y = center.y + bobAmplitude * Mathf.Sin(bobFrequency * time + phaseX);
            return new Vector3(x, y, z);
        }

        // 0〜1の進行度から補間係数を求める純粋関数（イーズイン・アウト）。
        public static float EaseInOut(float t) => t * t * (3f - 2f * Mathf.Clamp01(t));

        private void Update()
        {
            switch (_state)
            {
                case FlightState.Patrolling:
                    UpdatePatrol();
                    break;
                case FlightState.FlyingToHide:
                    UpdateTransition(onArrived: () => { _state = FlightState.Hidden; SetVisible(false); });
                    break;
                case FlightState.Hidden:
                    break; // 静止して非表示のまま待機
                case FlightState.FlyingToShow:
                    UpdateTransition(onArrived: () => { _state = FlightState.Patrolling; });
                    break;
            }
        }

        private void UpdatePatrol()
        {
            _time += Time.deltaTime;

            transform.position = ComputePosition(Center, _extentX, _extentZ, _freqX, _freqZ, _bobAmplitude, _bobFrequency, _phaseX, _phaseZ, _time);

            // 進行方向（位置の時間微分）へ向ける。
            float dx = -extentXVelocity(_extentX, _freqX, _phaseX, _time);
            float dz =  extentZVelocity(_extentZ, _freqZ, _phaseZ, _time);
            var tangent = new Vector3(dx, 0f, dz);
            if (tangent.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(tangent.normalized);
        }

        private void UpdateTransition(System.Action onArrived)
        {
            _transitionTime += Time.deltaTime;
            float t = EaseInOut(Mathf.Clamp01(_transitionTime / TransitionDuration));
            transform.position = Vector3.Lerp(_transitionStart, _transitionTarget, t);

            if (_transitionTime >= TransitionDuration)
            {
                transform.position = _transitionTarget;
                onArrived();
            }
        }

        private void SetVisible(bool visible)
        {
            if (_renderers == null) return;
            foreach (var r in _renderers)
                if (r != null) r.enabled = visible;
        }

        private static float extentXVelocity(float extentX, float freqX, float phaseX, float time)
            => extentX * freqX * Mathf.Sin(freqX * time + phaseX);

        private static float extentZVelocity(float extentZ, float freqZ, float phaseZ, float time)
            => extentZ * freqZ * Mathf.Cos(freqZ * time + phaseZ);
    }
}
