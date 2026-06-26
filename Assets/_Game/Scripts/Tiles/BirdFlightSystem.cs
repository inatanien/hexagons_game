// 役割: 森クラスターが10枚以上に育ったとき、鳥が飛び立つ演出を提供する。
//       TerrainGrowthEvent<ForestGrowthMetrics> を購読し、
//       LargestClusterSize >= birdThreshold で ParticleSystem による鳥シルエットを再生する。
//       クラスターごとに独立して管理し、飛び立ちサイクルを繰り返す。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class BirdFlightSystem : MonoBehaviour
    {
        [Header("しきい値")]
        [SerializeField] private int _birdThreshold = 10;

        [Header("飛び立ちサイクル（秒）")]
        [SerializeField] private float _flightInterval = 20f;
        [SerializeField] private float _flightDuration = 6f;

        private readonly Dictionary<TileType, List<BirdClusterEntry>> _clusterMap = new();
        private Material _birdMat;

        private void Awake()
        {
            _birdMat = BuildMaterial();
        }

        private void OnEnable()  => EventBus.Subscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);
        private void OnDisable() => EventBus.Unsubscribe<TerrainGrowthEvent<ForestGrowthMetrics>>(OnForestGrow);

        private void OnDestroy()
        {
            foreach (var entries in _clusterMap.Values)
                foreach (var e in entries)
                    e.DestroyEffect(this);
            _clusterMap.Clear();
        }

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            if (evt.Metrics.LargestClusterSize < _birdThreshold) return;

            if (!_clusterMap.TryGetValue(evt.TerrainType, out var entries))
            {
                entries = new List<BirdClusterEntry>();
                _clusterMap[evt.TerrainType] = entries;
            }

            var currentTileSet = new HashSet<HexTile>(evt.AffectedTiles);

            var overlapping = new List<BirdClusterEntry>();
            foreach (var entry in entries)
                if (entry.Tiles.Overlaps(currentTileSet))
                    overlapping.Add(entry);

            BirdClusterEntry cluster;
            if (overlapping.Count == 0)
            {
                cluster = new BirdClusterEntry();
                entries.Add(cluster);
            }
            else
            {
                cluster = overlapping[0];
                for (int i = 1; i < overlapping.Count; i++)
                {
                    overlapping[i].DestroyEffect(this);
                    entries.Remove(overlapping[i]);
                }
            }

            cluster.Tiles = currentTileSet;

            if (cluster.Effect == null)
                cluster.Effect = new BirdFlightEffect(transform, _birdMat);

            cluster.Effect.UpdateBounds(evt.AffectedTiles);

            if (cluster.Coroutine == null)
                cluster.Coroutine = StartCoroutine(FlightCycle(cluster.Effect));
        }

        private IEnumerator FlightCycle(BirdFlightEffect effect)
        {
            while (true)
            {
                yield return new WaitForSeconds(_flightInterval);
                effect.Play();
                yield return new WaitForSeconds(_flightDuration);
                effect.Stop();
            }
        }

        // ── クラスター管理エントリ ────────────────────────────────────

        private sealed class BirdClusterEntry
        {
            public HashSet<HexTile> Tiles = new();
            public BirdFlightEffect Effect;
            public Coroutine        Coroutine;

            internal void DestroyEffect(BirdFlightSystem owner)
            {
                if (owner != null && Coroutine != null)
                    owner.StopCoroutine(Coroutine);
                Coroutine = null;
                Effect?.Destroy();
                Effect = null;
            }
        }

        // ── パーティクルエフェクト本体 ────────────────────────────────

        private sealed class BirdFlightEffect
        {
            private readonly GameObject     _go;
            private readonly ParticleSystem _ps;

            internal BirdFlightEffect(Transform parent, Material mat)
            {
                _go = new GameObject("BirdFlight");
                if (parent != null) _go.transform.SetParent(parent);

                _ps = _go.AddComponent<ParticleSystem>();
                if (mat != null)
                    _go.GetComponent<ParticleSystemRenderer>().material = mat;

                Setup();
            }

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
                _go.transform.position = new Vector3(center.x, center.y + 1f, center.z);

                var shape = _ps.shape;
                shape.scale = new Vector3(
                    Mathf.Max(extent.x * 0.5f, 1f),
                    0.2f,
                    Mathf.Max(extent.z * 0.5f, 1f)
                );
            }

            internal void Play() => _ps.Play();
            internal void Stop() => _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            internal void Destroy() { if (_go != null) Object.Destroy(_go); }

            private void Setup()
            {
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = _ps.main;
                main.loop            = false;
                main.duration        = 1f;
                main.maxParticles    = 12;
                main.startLifetime   = new ParticleSystem.MinMaxCurve(4f, 7f);
                main.startSpeed      = new ParticleSystem.MinMaxCurve(1.5f, 3.0f);
                main.startSize       = new ParticleSystem.MinMaxCurve(0.12f, 0.22f);
                main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor      = new ParticleSystem.MinMaxGradient(
                    new Color(0.15f, 0.12f, 0.10f, 1f),
                    new Color(0.28f, 0.22f, 0.18f, 1f)
                );
                // 鳥は落ちないので弱い逆重力
                main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.04f, -0.01f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                // バーストで5〜8羽まとめて飛び立つ
                var em = _ps.emission;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, 5, 8, 1, 0f) });

                var sh = _ps.shape;
                sh.shapeType = ParticleSystemShapeType.Box;
                sh.scale     = Vector3.one;

                // 上方向 + 前後左右にランダムに飛び散る
                var vel = _ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space   = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
                vel.y = new ParticleSystem.MinMaxCurve(0.8f, 2.0f);
                vel.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

                // ゆっくりした回転（翼のはばたき感）
                var rot = _ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(
                    -60f * Mathf.Deg2Rad,
                     60f * Mathf.Deg2Rad
                );

                var col = _ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f),
                            new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f,   0f),
                            new GradientAlphaKey(1f,   0.1f),
                            new GradientAlphaKey(0.9f, 0.7f),
                            new GradientAlphaKey(0f,   1f) }
                );
                col.color = new ParticleSystem.MinMaxGradient(g);

                var sz = _ps.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f,   0.3f),
                    new Keyframe(0.1f, 1f),
                    new Keyframe(0.8f, 0.8f),
                    new Keyframe(1f,   0f)
                ));
            }
        }

        // ── マテリアル生成（WorldBreathSystem と同じ透過設定） ─────────

        private static Material BuildMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "BirdFlight_Runtime" };
            mat.SetFloat("_Surface",     1f);
            mat.SetFloat("_Blend",       0f);
            mat.SetFloat("_SrcBlend",    5f);
            mat.SetFloat("_DstBlend",   10f);
            mat.SetFloat("_ZWrite",      0f);
            mat.SetFloat("_AlphaToMask", 0f);
            mat.SetColor("_BaseColor", Color.white);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }
    }
}
