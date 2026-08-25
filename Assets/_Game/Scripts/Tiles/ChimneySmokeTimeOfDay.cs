// 役割: TimeOfDayEvent を購読し、煙突の煙を昼だけ出す。
//       煙はタイル配置時に家ごと生成されるため、中央から管理するのではなく
//       煙プレハブ自身に持たせて自分で時間帯を見に行く形にしている。
//       夜は Stop(StopEmitting) にするので、既に出ている煙は自然に薄れて消える。

using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    [RequireComponent(typeof(ParticleSystem))]
    public class ChimneySmokeTimeOfDay : MonoBehaviour
    {
        private ParticleSystem _ps;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<TimeOfDayEvent>(OnTimeOfDay);
            // 生成された時点の時間帯に合わせる。イベントは切り替わりの瞬間にしか来ないため
            Apply(TimeOfDaySystem.Current);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<TimeOfDayEvent>(OnTimeOfDay);
        }

        private void OnTimeOfDay(TimeOfDayEvent evt) => Apply(evt.Current);

        private void Apply(TimeOfDayEvent.Phase phase)
        {
            if (_ps == null) return;

            // 夕方までは竈を使っている想定で煙を出し、夜は落とす
            bool smoking = phase != TimeOfDayEvent.Phase.Night;

            if (smoking)
            {
                if (!_ps.isEmitting) _ps.Play(true);
            }
            else
            {
                // Clear せずに止める。既に空にある煙はそのまま薄れて消えるので、
                // 夜になった瞬間に煙が消失する不自然さが出ない
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }
}
