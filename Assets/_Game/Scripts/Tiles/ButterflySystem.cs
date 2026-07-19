// 役割: TerrainSynergyEvent (ForestFlower) を受け取り、森と花畑の境界に蝶のパーティクルを生成する。
//       TimeOfDayEvent を購読し、昼（Afternoon）の時間帯のみ蝶を出現させる。
//       森8枚 + 花畑8枚以上でシナジー成立。FireflySystem と同じ構造で昼夜を逆にした設計。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class ButterflySystem : MonoBehaviour
    {
        [Header("対象シナジー")]
        [SerializeField] private string _targetSynergyId = "ForestFlower";

        private readonly Dictionary<string, ButterflyEffect> _activeEffects = new();
        private Material _mat;
        private bool     _isDaytime = false;

        private void Awake() => _mat = BuildMaterial();

        private void OnEnable()
        {
            EventBus.Subscribe<TerrainSynergyEvent>(OnSynergy);
            EventBus.Subscribe<TimeOfDayEvent>(OnTimeOfDay);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<TerrainSynergyEvent>(OnSynergy);
            EventBus.Unsubscribe<TimeOfDayEvent>(OnTimeOfDay);
        }

        private void OnDestroy()
        {
            foreach (var e in _activeEffects.Values)
                e.Destroy();
            _activeEffects.Clear();
        }

        private void OnSynergy(TerrainSynergyEvent evt)
        {
            if (evt.SynergyId != _targetSynergyId) return;

            if (!_activeEffects.TryGetValue(evt.SynergyId, out var effect))
            {
                effect = new ButterflyEffect(transform, _mat);
                _activeEffects[evt.SynergyId] = effect;
            }

            effect.UpdateBounds(evt.TilesA, evt.TilesB);

            // 昼のみ再生
            if (_isDaytime) effect.Play();
        }

        private void OnTimeOfDay(TimeOfDayEvent evt)
        {
            _isDaytime = evt.Current == TimeOfDayEvent.Phase.Afternoon;

            foreach (var effect in _activeEffects.Values)
            {
                if (_isDaytime)
                    effect.Play();
                else
                    effect.Stop();
            }
        }

        // ── パーティクルエフェクト本体 ────────────────────────────────────

        private sealed class ButterflyEffect
        {
            private readonly GameObject     _go;
            private readonly ParticleSystem _ps;

            internal ButterflyEffect(Transform parent, Material mat)
            {
                _go = new GameObject("ButterflyEffect");
                if (parent != null) _go.transform.SetParent(parent);

                _ps = _go.AddComponent<ParticleSystem>();
                if (mat != null)
                    _go.GetComponent<ParticleSystemRenderer>().material = mat;

                Setup();
            }

            // 森クラスターと花畑クラスターの AABB を合成してエリアを算出
            internal void UpdateBounds(
                IReadOnlyList<HexTile> tilesA,
                IReadOnlyList<HexTile> tilesB)
            {
                var min = new Vector3(float.MaxValue,  float.MaxValue,  float.MaxValue);
                var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

                foreach (var t in tilesA) { var p = t.transform.position; min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                foreach (var t in tilesB) { var p = t.transform.position; min = Vector3.Min(min, p); max = Vector3.Max(max, p); }

                var center = (min + max) * 0.5f;
                var extent = max - min;

                // 蝶はタイルより少し高めをひらひら漂う
                _go.transform.position = new Vector3(center.x, center.y + 0.4f, center.z);

                // 花畑タイル全体を覆うように広めに設定（自由に飛び回れるよう余白追加）
                var shape = _ps.shape;
                shape.scale = new Vector3(
                    Mathf.Max(extent.x * 1.0f + 1.0f, 2.0f),
                    0.6f,
                    Mathf.Max(extent.z * 1.0f + 1.0f, 2.0f)
                );
            }

            internal void Play()  { if (!_ps.isPlaying) _ps.Play(); }
            internal void Stop()  => _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            internal void Destroy() { if (_go != null) Object.Destroy(_go); }

            private void Setup()
            {
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = _ps.main;
                main.loop            = true;
                main.duration        = 5f;
                main.maxParticles    = 10;
                main.startLifetime   = new ParticleSystem.MinMaxCurve(10f, 16f);
                main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
                main.startSize       = new ParticleSystem.MinMaxCurve(0.10f, 0.18f);
                main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                // 昼の蝶：ピンク〜黄の暖かい色
                main.startColor      = new ParticleSystem.MinMaxGradient(
                    new Color(1.00f, 0.78f, 0.85f, 0.95f),  // ピンク
                    new Color(1.00f, 0.95f, 0.55f, 0.95f)   // 黄
                );
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var em = _ps.emission;
                em.rateOverTime = 0.7f;

                var sh = _ps.shape;
                sh.shapeType             = ParticleSystemShapeType.Box;
                sh.scale                 = Vector3.one;
                sh.randomDirectionAmount = 0f;

                // 花畑の上を自由に飛び回る：タイル間を移動できる速度
                var vel = _ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space   = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.55f, 0.55f);
                vel.y = new ParticleSystem.MinMaxCurve(-0.04f, 0.10f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.55f, 0.55f);

                // ノイズで方向転換しながら飛び回る（低周波で大きく蛇行）
                var noise = _ps.noise;
                noise.enabled     = true;
                noise.strength    = new ParticleSystem.MinMaxCurve(0.55f);
                noise.frequency   = 0.22f;
                noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.18f);
                noise.damping     = true;
                noise.quality     = ParticleSystemNoiseQuality.Medium;

                // フェードイン・アウト
                var col = _ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f),
                            new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f,    0f),
                            new GradientAlphaKey(0.95f, 0.08f),
                            new GradientAlphaKey(0.95f, 0.85f),
                            new GradientAlphaKey(0f,    1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                // 羽ばたきのサイズ波動
                var sz = _ps.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0.00f, 0.65f, 0f, 0f),
                    new Keyframe(0.08f, 1.00f, 0f, 0f),
                    new Keyframe(0.16f, 0.65f, 0f, 0f),
                    new Keyframe(0.24f, 1.00f, 0f, 0f),
                    new Keyframe(0.32f, 0.65f, 0f, 0f),
                    new Keyframe(0.40f, 1.00f, 0f, 0f),
                    new Keyframe(0.48f, 0.65f, 0f, 0f),
                    new Keyframe(0.56f, 1.00f, 0f, 0f),
                    new Keyframe(0.64f, 0.65f, 0f, 0f),
                    new Keyframe(0.72f, 1.00f, 0f, 0f),
                    new Keyframe(0.80f, 0.65f, 0f, 0f),
                    new Keyframe(0.88f, 1.00f, 0f, 0f),
                    new Keyframe(1.00f, 0.65f, 0f, 0f)
                ));

                // 軽い回転で翅の向きが変わる様子
                var rot = _ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-25f * Mathf.Deg2Rad, 25f * Mathf.Deg2Rad);
            }
        }

        // ── マテリアル（半透明・通常ブレンド） ───────────────────────────

        private static Material BuildMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "Butterfly_Runtime" };
            mat.SetFloat("_Surface", 1f);
            // _Surface=1だけではGPUのブレンド式が不透明のまま残り、colorOverLifetime/
            // sizeOverLifetimeのフェードが効かない（花のBillboard・花びらVFXと同じ不具合）。
            // WorldBreathSystem.ForestBreathEffect.BuildMaterialと同じ値を明示する。
            mat.SetFloat("_Blend",    0f);
            mat.SetFloat("_SrcBlend", 5f);  // SrcAlpha
            mat.SetFloat("_DstBlend", 10f); // OneMinusSrcAlpha
            mat.SetFloat("_ZWrite",   0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetColor("_BaseColor", Color.white);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }
    }
}
