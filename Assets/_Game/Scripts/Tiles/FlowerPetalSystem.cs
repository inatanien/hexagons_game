// 役割: FlowerClusterEvent を受け取り、花畑クラスター上に花びらが舞う演出を生成する。
//       複数の独立したクラスターをそれぞれ追跡し、すべての場所から花びらを放出する。
//       最大クラスターサイズに応じて段階的に花びらの色が追加される。
//       デフォルト: 3=黄, 4=青, 5=紫, 6=赤, 7=ピンク

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class FlowerPetalSystem : MonoBehaviour
    {
        [Header("花びら放出間隔（秒）")]
        [SerializeField] private float _emitIntervalMin = 0.3f;
        [SerializeField] private float _emitIntervalMax = 0.7f;

        [Header("1回の放出数")]
        [SerializeField] private int _emitCountMin = 1;
        [SerializeField] private int _emitCountMax = 3;

        // 閾値ごとの色定義
        private readonly struct PetalTier
        {
            public readonly int   Threshold;
            public readonly Color ColorA;
            public readonly Color ColorB;
            public PetalTier(int threshold, Color a, Color b)
            { Threshold = threshold; ColorA = a; ColorB = b; }
        }

        private static readonly PetalTier[] s_Tiers =
        {
            new PetalTier(3, new Color(1.00f, 0.92f, 0.20f, 0.85f), new Color(1.00f, 0.75f, 0.10f, 0.90f)), // 黄
            new PetalTier(4, new Color(0.45f, 0.72f, 1.00f, 0.85f), new Color(0.60f, 0.85f, 1.00f, 0.90f)), // 青
            new PetalTier(5, new Color(0.72f, 0.40f, 1.00f, 0.85f), new Color(0.85f, 0.60f, 1.00f, 0.90f)), // 紫
            new PetalTier(6, new Color(1.00f, 0.25f, 0.25f, 0.85f), new Color(1.00f, 0.50f, 0.40f, 0.90f)), // 赤
            new PetalTier(7, new Color(1.00f, 0.55f, 0.75f, 0.85f), new Color(1.00f, 0.75f, 0.88f, 0.90f)), // ピンク
        };

        private readonly List<(GameObject go, ParticleSystem ps, int threshold)> _tiers = new();

        private Material  _mat;
        private Coroutine _emitCoroutine;
        private bool      _initialized;

        // クラスターごとにタイルセットを保持（重複検出で同一クラスターを更新する）
        private readonly List<HashSet<HexTile>> _clusters    = new();
        // 全クラスターから集めた放出位置（毎回再構築）
        private readonly List<Vector3>          _tilePositions = new();

        private void Awake() => _mat = BuildPetalMaterial();

        private void OnEnable()  => EventBus.Subscribe<FlowerClusterEvent>(OnFlowerCluster);
        private void OnDisable() => EventBus.Unsubscribe<FlowerClusterEvent>(OnFlowerCluster);

        private void OnDestroy()
        {
            foreach (var t in _tiers)
                if (t.go != null) Destroy(t.go);
        }

        private void OnFlowerCluster(FlowerClusterEvent evt)
        {
            if (!_initialized)
            {
                InitParticleSystems();
                _initialized = true;
            }

            UpdateClusters(evt.Tiles);

            // 最大クラスターサイズでティアを切り替える
            int maxSize = 0;
            foreach (var c in _clusters)
                if (c.Count > maxSize) maxSize = c.Count;

            foreach (var t in _tiers)
                t.go.SetActive(maxSize >= t.threshold);

            if (_emitCoroutine == null)
                _emitCoroutine = StartCoroutine(EmitRoutine());
        }

        // ── クラスター管理 ────────────────────────────────────────────
        // タイルの重複を見てクラスターを更新 or 新規追加する

        private void UpdateClusters(IReadOnlyList<HexTile> newTiles)
        {
            var newSet = new HashSet<HexTile>(newTiles);

            // 重なるクラスターを探す
            int matchIndex = -1;
            for (int i = 0; i < _clusters.Count; i++)
            {
                if (_clusters[i].Overlaps(newSet))
                {
                    matchIndex = i;
                    break;
                }
            }

            if (matchIndex < 0)
                _clusters.Add(newSet);        // 新規クラスター
            else
                _clusters[matchIndex] = newSet; // 既存クラスターが成長

            // 全クラスターから放出位置を再構築
            _tilePositions.Clear();
            foreach (var cluster in _clusters)
                foreach (var tile in cluster)
                    _tilePositions.Add(tile.transform.position);
        }

        // ── パーティクルシステム初期化 ────────────────────────────────

        private void InitParticleSystems()
        {
            foreach (var tier in s_Tiers)
            {
                var go = new GameObject($"FlowerPetal_T{tier.Threshold}");
                go.transform.SetParent(transform);
                var ps = go.AddComponent<ParticleSystem>();
                SetupRenderer(go.GetComponent<ParticleSystemRenderer>());
                SetupPS(ps, tier.ColorA, tier.ColorB);
                go.SetActive(false);
                _tiers.Add((go, ps, tier.Threshold));
            }
        }

        private void SetupRenderer(ParticleSystemRenderer r)
        {
            if (_mat != null) r.material = _mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
        }

        private void SetupPS(ParticleSystem ps, Color colorA, Color colorB)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop            = true;
            main.duration        = 5f;
            main.maxParticles    = 60;
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
            main.startLifetime   = new ParticleSystem.MinMaxCurve(3.0f, 5.0f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor      = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = ps.emission;
            em.rateOverTime = 0f;

            var sh = ps.shape;
            sh.enabled = false;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            vel.y = new ParticleSystem.MinMaxCurve(0.08f,  0.20f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            var noise = ps.noise;
            noise.enabled     = true;
            noise.strength    = new ParticleSystem.MinMaxCurve(0.12f);
            noise.frequency   = 0.5f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.3f);
            noise.damping     = true;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f,    0f),
                        new GradientAlphaKey(0.90f, 0.10f),
                        new GradientAlphaKey(0.90f, 0.75f),
                        new GradientAlphaKey(0f,    1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(
                -90f * Mathf.Deg2Rad, 90f * Mathf.Deg2Rad);

            ps.Play();
        }

        // ── 放出コルーチン ────────────────────────────────────────────

        private IEnumerator EmitRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(
                    Random.Range(_emitIntervalMin, _emitIntervalMax));

                if (_tilePositions.Count == 0) yield break;

                int count = Random.Range(_emitCountMin, _emitCountMax + 1);
                for (int i = 0; i < count; i++)
                {
                    foreach (var t in _tiers)
                    {
                        if (!t.go.activeSelf) continue;

                        var basePos = _tilePositions[Random.Range(0, _tilePositions.Count)];
                        var offset  = new Vector3(
                            Random.Range(-0.5f, 0.5f),
                            0.18f,
                            Random.Range(-0.5f, 0.5f));
                        t.go.transform.position = basePos + offset;
                        t.ps.Emit(new ParticleSystem.EmitParams(), 1);
                    }
                }
            }
        }

        // ── マテリアル ─────────────────────────────────────────────────

        private static Material BuildPetalMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "FlowerPetal_Runtime" };
            mat.SetFloat("_Surface", 1f);
            // _Surface=1だけではGPUのブレンド式が不透明のまま残り、colorOverLifetimeの
            // アルファフェードが effectively 無視されてしまう。WorldBreathSystem.ForestBreathEffect.
            // BuildMaterialと同じ値を明示する。
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
