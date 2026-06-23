// 役割: 世界の息吹（WorldBreath）システム。
//       TerrainGrowthEvent<ForestGrowthMetrics> を購読し、
//       クラスターサイズに応じた環境演出を起動・更新する。
//       将来の演出（鳥・精霊・花・環境音）は OnForestGrow 内に追加する。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class WorldBreathSystem : MonoBehaviour
    {
        [Header("森の息吹しきい値")]
        [SerializeField] private int _windThreshold = 3;
        // 将来: [SerializeField] private int _birdThreshold   = 8;
        // 将来: [SerializeField] private int _spiritThreshold = 20;

        // TileType ごとに 1 つの ForestBreathEffect を管理（複数の森タイプに対応）
        private readonly Dictionary<TileType, ForestBreathEffect> _effects = new();

        private void Start()
        {
            // ForestGrowthEvaluator がシーンにないとイベントが届かないため起動時にチェック
            if (FindObjectOfType<ForestGrowthEvaluator>() == null)
            {
                Debug.LogError(
                    "[WorldBreathSystem] ForestGrowthEvaluator がシーンに見つかりません！\n" +
                    "Hierarchy の WorldBreath GameObject に ForestGrowthEvaluator コンポーネントを追加し、\n" +
                    "・Grid Manager → HexGridManager をアサイン\n" +
                    "・Forest Tile Types → 森の TileType SO をアサイン\n" +
                    "してください。");
            }
        }

        private void OnEnable()  => EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
        private void OnDisable() => EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);

        private void OnDestroy()
        {
            foreach (var e in _effects.Values) e.Destroy();
            _effects.Clear();
        }

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            int size = evt.Metrics.LargestClusterSize;

            if (size >= _windThreshold)
                GetOrCreate(evt.TerrainType).UpdateBounds(evt.AffectedTiles);

            // 将来: if (size >= _birdThreshold)   ActivateBirds(evt);
            // 将来: if (size >= _spiritThreshold)  ActivateSpirits(evt);
        }

        private ForestBreathEffect GetOrCreate(TileType type)
        {
            if (!_effects.TryGetValue(type, out var e))
            {
                e = new ForestBreathEffect(type.tileColor);
                _effects[type] = e;
            }
            return e;
        }

        // ── 内部クラス: 葉っぱパーティクルの生成・管理 ──────────────────────

        private sealed class ForestBreathEffect
        {
            private readonly GameObject     _go;
            private readonly ParticleSystem _ps;

            internal ForestBreathEffect(Color tileColor)
            {
                _go = new GameObject("ForestBreath");
                _ps = _go.AddComponent<ParticleSystem>();

                var mat = BuildMaterial();
                if (mat != null)
                    _go.GetComponent<ParticleSystemRenderer>().material = mat;

                Setup(tileColor);
            }

            // クラスター全体のAABBを計算してパーティクル発生範囲を更新する
            internal void UpdateBounds(IReadOnlyList<HexTile> tiles)
            {
                if (tiles == null || tiles.Count == 0) return;

                var min = new Vector3(float.MaxValue,  float.MaxValue,  float.MaxValue);
                var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
                foreach (var t in tiles)
                {
                    var p = t.transform.position;
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }

                var center = (min + max) * 0.5f;
                var extent = max - min;

                // 樹冠の高さから葉が落ちるよう、タイル上面 +2.5f に発生源を設置
                _go.transform.position = new Vector3(center.x, center.y + 2.5f, center.z);

                // クラスター全域をカバーする薄い Box（Y は薄く = 樹冠の一層だけから放出）
                var shape = _ps.shape;
                shape.scale = new Vector3(extent.x + 1.0f, 0.3f, extent.z + 1.0f);

                if (!_ps.isPlaying) _ps.Play();
            }

            internal void Destroy()
            {
                if (_go != null) Object.Destroy(_go);
            }

            // ── ParticleSystem 各モジュールの設定 ──────────────────────────

            private void Setup(Color tileColor)
            {
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                // Main
                var main = _ps.main;
                main.loop            = true;
                main.duration        = 4f;
                main.maxParticles    = 60;
                main.startLifetime   = new ParticleSystem.MinMaxCurve(4.0f, 7.0f);
                // 初速はほぼゼロ — 落下は重力に任せる
                main.startSpeed      = new ParticleSystem.MinMaxCurve(0f, 0.15f);
                main.startSize       = new ParticleSystem.MinMaxCurve(0.10f, 0.28f);
                main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor      = LeafColorGradient(tileColor);
                // 重力の 6% → ゆっくりひらひら落ちる（9.81 * 0.06 ≈ 0.59 m/s²）
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0.05f, 0.08f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                // Emission
                var em = _ps.emission;
                em.rateOverTime = 6f;

                // Shape（UpdateBounds で上書きされる）
                var sh = _ps.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale     = Vector3.one;

                // Velocity over lifetime: 水平にゆらゆら揺れる（Y は重力に任せる）
                var vel = _ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space   = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                vel.y = new ParticleSystem.MinMaxCurve( 0.0f, 0.0f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

                // Rotation over lifetime: 葉がくるくる舞う
                var rot = _ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(
                    -180f * Mathf.Deg2Rad,
                     180f * Mathf.Deg2Rad
                );

                // Color over lifetime: 素早くフェードイン→長く見せてフェードアウト
                var col = _ps.colorOverLifetime;
                col.enabled = true;
                col.color   = FadeGradient();

                // Size over lifetime: ふわっと現れてゆっくり消える
                var sz = _ps.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f,    0f),
                    new Keyframe(0.05f, 1f),
                    new Keyframe(0.80f, 0.9f),
                    new Keyframe(1f,    0f)
                ));
            }

            // 葉らしく黄緑〜黄色の 2 色ランダム（tileColor ベース）
            private static ParticleSystem.MinMaxGradient LeafColorGradient(Color baseColor)
            {
                var c1 = Color.Lerp(baseColor, new Color(0.75f, 0.95f, 0.20f, 1f), 0.25f);
                var c2 = Color.Lerp(baseColor, new Color(0.90f, 0.85f, 0.10f, 1f), 0.30f);
                return new ParticleSystem.MinMaxGradient(c1, c2);
            }

            // 0→不透明（素早く）→不透明→0 のフェードグラデーション
            private static ParticleSystem.MinMaxGradient FadeGradient()
            {
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f),
                            new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f,    0f),
                            new GradientAlphaKey(1f,    0.05f),
                            new GradientAlphaKey(1f,    0.80f),
                            new GradientAlphaKey(0f,    1f) }
                );
                return new ParticleSystem.MinMaxGradient(g);
            }

            // URP Particles/Unlit（半透明アルファブレンド）マテリアルを手続き生成
            private static Material BuildMaterial()
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;

                var mat = new Material(shader) { name = "ForestBreath_Runtime" };
                mat.SetFloat("_Surface",      1f);   // Transparent
                mat.SetFloat("_Blend",        0f);   // Alpha blend
                mat.SetFloat("_SrcBlend",     5f);   // SrcAlpha
                mat.SetFloat("_DstBlend",    10f);   // OneMinusSrcAlpha
                mat.SetFloat("_ZWrite",       0f);
                mat.SetFloat("_AlphaToMask",  0f);
                mat.SetColor("_BaseColor", Color.white);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return mat;
            }
        }
    }
}
