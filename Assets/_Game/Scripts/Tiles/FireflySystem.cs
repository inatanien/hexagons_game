// 役割: TerrainSynergyEvent を受け取り、森と川の境界に蛍のパーティクルを生成する。
//       SynergyId ごとにエフェクトを管理し、クラスターが成長しても位置を更新する。
//       蛍は境界エリアをふわふわ漂い、夜の森×川の雰囲気を演出する。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class FireflySystem : MonoBehaviour
    {
        [Header("対象シナジー")]
        [SerializeField] private string _targetSynergyId = "ForestRiver";

        // SynergyId ごとにエフェクトを1つ管理（クラスターが成長しても再生成しない）
        private readonly Dictionary<string, FireflyEffect> _activeEffects = new();
        private Material _mat;

        private void Awake()
        {
            _mat = BuildMaterial();
        }

        private void OnEnable()  => EventBus.Subscribe<TerrainSynergyEvent>(OnSynergy);
        private void OnDisable() => EventBus.Unsubscribe<TerrainSynergyEvent>(OnSynergy);

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
                effect = new FireflyEffect(transform, _mat);
                _activeEffects[evt.SynergyId] = effect;
            }

            effect.UpdateBounds(evt.TilesA, evt.TilesB);
            effect.Play();
        }

        // ── パーティクルエフェクト本体 ────────────────────────────────

        private sealed class FireflyEffect
        {
            private readonly GameObject     _go;
            private readonly ParticleSystem _ps;

            internal FireflyEffect(Transform parent, Material mat)
            {
                _go = new GameObject("FireflyEffect");
                if (parent != null) _go.transform.SetParent(parent);

                _ps = _go.AddComponent<ParticleSystem>();
                if (mat != null)
                    _go.GetComponent<ParticleSystemRenderer>().material = mat;

                Setup();
            }

            // 森クラスターと川クラスターの AABB を合成して境界エリアを算出
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

                // 蛍はタイルのすぐ上をふわふわ漂う
                _go.transform.position = new Vector3(center.x, center.y + 0.8f, center.z);

                var shape = _ps.shape;
                shape.scale = new Vector3(
                    Mathf.Max(extent.x * 0.6f, 1f),
                    0.5f,
                    Mathf.Max(extent.z * 0.6f, 1f)
                );
            }

            internal void Play() { if (!_ps.isPlaying) _ps.Play(); }

            internal void Destroy() { if (_go != null) Object.Destroy(_go); }

            private void Setup()
            {
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = _ps.main;
                main.loop            = true;
                main.duration        = 4f;
                main.maxParticles    = 40;
                main.startLifetime   = new ParticleSystem.MinMaxCurve(4f, 8f);
                main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
                main.startSize       = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                // 蛍の光：淡い黄緑〜白
                main.startColor      = new ParticleSystem.MinMaxGradient(
                    new Color(0.80f, 1.00f, 0.55f, 1f),
                    new Color(0.95f, 1.00f, 0.80f, 1f)
                );
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var em = _ps.emission;
                em.rateOverTime = 6f;

                var sh = _ps.shape;
                sh.shapeType             = ParticleSystemShapeType.Box;
                sh.scale                 = Vector3.one;
                sh.randomDirectionAmount = 0f;

                // ゆっくりふわふわ漂う（上下 + 横に微量）
                var vel = _ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space   = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
                vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

                // ぽわっと明滅するフェード
                var col = _ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f),
                            new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f,    0f),
                            new GradientAlphaKey(1f,    0.15f),
                            new GradientAlphaKey(0.3f,  0.45f),
                            new GradientAlphaKey(1f,    0.60f),
                            new GradientAlphaKey(0.3f,  0.80f),
                            new GradientAlphaKey(0f,    1f) }
                );
                col.color = new ParticleSystem.MinMaxGradient(g);

                // サイズでぽわっと感を強調
                var sz = _ps.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f,    0f),
                    new Keyframe(0.1f,  1f),
                    new Keyframe(0.5f,  0.7f),
                    new Keyframe(0.85f, 1f),
                    new Keyframe(1f,    0f)
                ));
            }
        }

        // ── マテリアル（加算合成で光らせる） ─────────────────────────

        private static Material BuildMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "Firefly_Runtime" };
            // 加算合成：暗い背景でより光って見える
            mat.SetFloat("_Surface",     1f);
            mat.SetFloat("_Blend",       3f);  // Additive
            mat.SetFloat("_SrcBlend",    5f);  // SrcAlpha
            mat.SetFloat("_DstBlend",    1f);  // One
            mat.SetFloat("_ZWrite",      0f);
            mat.SetFloat("_AlphaToMask", 0f);
            mat.SetColor("_BaseColor", Color.white);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 1;
            return mat;
        }
    }
}
