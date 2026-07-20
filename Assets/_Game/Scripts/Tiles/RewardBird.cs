// 役割: 1羽の鳥の飛行を担当する最小限のコンポーネント。群れAI・巣・餌・成長・
//       プレイヤー操作との連動は持たない。中心点の周りをゆっくり円を描きながら飛び、
//       わずかに上下し、常に指定した半径・振幅の範囲内に収まる。
//       経過時間→座標の変換はComputePosition()に純粋関数として切り出してあり、
//       EditModeからコルーチン/Update実行なしで直接検証できる
//       （QuestNotificationUI.ComputeFrameと同じ設計方針）。
//       最大羽数がごく少数（Stage 5時点で3羽まで）であるため、鳥ごとに素朴な
//       Updateを持たせても負荷は問題にならない。

using UnityEngine;

namespace ElfVillage.Tiles
{
    public class RewardBird : MonoBehaviour
    {
        private Vector3 _center;
        private float   _radius;
        private float   _angularSpeed;
        private float   _bobAmplitude;
        private float   _bobFrequency;
        private float   _phase;
        private float   _time;

        public void Init(Vector3 center, float radius, float angularSpeed, float bobAmplitude, float bobFrequency, float phase)
        {
            _center       = center;
            _radius       = radius;
            _angularSpeed = angularSpeed;
            _bobAmplitude = bobAmplitude;
            _bobFrequency = bobFrequency;
            _phase        = phase;
            _time         = 0f;

            // 最初のUpdateが回るまでの1フレーム、原点に一瞬表示されてしまうのを防ぐため、
            // 生成直後にt=0の位置へ即座にスナップしておく。
            transform.position = ComputePosition(_center, _radius, _angularSpeed, _bobAmplitude, _bobFrequency, _phase, _time);
        }

        // 経過時間から見た座標を求める純粋関数（副作用なし）。
        // 水平方向は中心から常にradius、垂直方向は中心からbobAmplitude以内に収まる。
        public static Vector3 ComputePosition(Vector3 center, float radius, float angularSpeed, float bobAmplitude, float bobFrequency, float phase, float time)
        {
            float angle = angularSpeed * time + phase;
            float x = center.x + radius * Mathf.Cos(angle);
            float z = center.z + radius * Mathf.Sin(angle);
            float y = center.y + bobAmplitude * Mathf.Sin(bobFrequency * time + phase);
            return new Vector3(x, y, z);
        }

        private void Update()
        {
            _time += Time.deltaTime;

            transform.position = ComputePosition(_center, _radius, _angularSpeed, _bobAmplitude, _bobFrequency, _phase, _time);

            // 円運動の接線方向へ向ける（進行方向を向いて飛んでいるように見せる）。
            float angle = _angularSpeed * _time + _phase;
            float dir = _angularSpeed >= 0f ? 1f : -1f;
            var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * dir;
            if (tangent.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(tangent);
        }
    }
}
