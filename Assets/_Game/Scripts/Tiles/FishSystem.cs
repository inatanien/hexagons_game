// 役割: RiverClusterEvent を受け取り、川クラスター内に魚の泳ぎ演出を生成する。
//       WaterPS 子 GO の座標・向きを収集し、その位置で手動 Emit することで
//       流れエフェクトと完全に重なった位置に魚を出現させる。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class FishSystem : MonoBehaviour
    {
        [Header("泳ぎ間隔（秒）")]
        [SerializeField] private float _swimIntervalMin = 0.5f;
        [SerializeField] private float _swimIntervalMax = 1.0f;

        [Header("ジャンプ間隔（秒）")]
        [SerializeField] private float _jumpIntervalMin = 20f;
        [SerializeField] private float _jumpIntervalMax = 40f;

        private GameObject     _swimGO;
        private ParticleSystem _swimPS;
        private GameObject     _jumpGO;
        private ParticleSystem _jumpPS;
        private Material       _mat;
        private Coroutine      _swimCoroutine;
        private Coroutine      _jumpCoroutine;
        private bool           _initialized;

        // WaterPS の位置と流れ方向（WaterPS は川の曲線接線方向を向いている）
        private struct WaterPoint
        {
            public Vector3 Position;
            public Vector3 FlowForward; // WaterPS のワールド forward = 曲線接線方向
        }
        private readonly List<WaterPoint> _waterPoints = new();

        private void Awake() => _mat = BuildFishMaterial();

        private void OnEnable()  => EventBus.Subscribe<RiverClusterEvent>(OnRiverCluster);
        private void OnDisable() => EventBus.Unsubscribe<RiverClusterEvent>(OnRiverCluster);

        private void OnDestroy()
        {
            if (_swimGO != null) Destroy(_swimGO);
            if (_jumpGO != null) Destroy(_jumpGO);
        }

        private void OnRiverCluster(RiverClusterEvent evt)
        {
            if (!_initialized)
            {
                InitParticleSystems();
                _initialized = true;
            }

            CollectWaterPoints(evt.Tiles);

            if (_swimCoroutine == null)
                _swimCoroutine = StartCoroutine(SwimRoutine());

            if (_jumpCoroutine == null)
                _jumpCoroutine = StartCoroutine(JumpRoutine());
        }

        // ── パーティクルシステム初期化 ────────────────────────────────

        private void InitParticleSystems()
        {
            _swimGO = new GameObject("FishSwim");
            _swimGO.transform.SetParent(transform);
            _swimPS = _swimGO.AddComponent<ParticleSystem>();
            SetupSwimRenderer(_swimGO.GetComponent<ParticleSystemRenderer>());
            SetupSwimPS();

            _jumpGO = new GameObject("FishJump");
            _jumpGO.transform.SetParent(transform);
            _jumpPS = _jumpGO.AddComponent<ParticleSystem>();
            SetupJumpRenderer(_jumpGO.GetComponent<ParticleSystemRenderer>());
            SetupJumpPS();
        }

        private void SetupSwimRenderer(ParticleSystemRenderer r)
        {
            if (_mat != null) r.material = _mat;
            r.renderMode    = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 2.5f;
            r.lengthScale   = 1.5f;
        }

        private void SetupJumpRenderer(ParticleSystemRenderer r)
        {
            if (_mat != null) r.material = _mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
        }

        // ── 泳ぎ PS 設定 ──────────────────────────────────────────────

        private void SetupSwimPS()
        {
            _swimPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _swimPS.main;
            main.loop            = true;
            main.duration        = 5f;
            main.maxParticles    = 15;
            // 短い寿命でドリフトを抑制（0.4 × 2.5s × √2 ≈ 1.4 タイル以内）
            main.startLifetime   = new ParticleSystem.MinMaxCurve(2.0f, 3.5f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(0.12f, 0.32f, 0.62f, 0.88f),
                new Color(0.50f, 0.70f, 0.88f, 0.92f));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = _swimPS.emission;
            em.rateOverTime = 0f; // SwimRoutine から手動 Emit する

            // Shape 無効 → _swimGO.transform.position が放出位置になる
            var sh = _swimPS.shape;
            sh.enabled = false;

            // 川の流れ方向（EmitParams.velocity）に小さな横ぶれを加える程度
            var vel = _swimPS.velocityOverLifetime;
            vel.enabled = true;
            vel.space   = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);

            var col = _swimPS.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f,    0f),
                        new GradientAlphaKey(0.90f, 0.12f),
                        new GradientAlphaKey(0.90f, 0.80f),
                        new GradientAlphaKey(0f,    1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            _swimPS.Play();
        }

        // ── ジャンプ PS 設定 ──────────────────────────────────────────

        private void SetupJumpPS()
        {
            _jumpPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _jumpPS.main;
            main.loop            = true;   // Emit(EmitParams) のために再生状態を維持
            main.duration        = 10f;
            main.maxParticles    = 5;
            // 速度は JumpRoutine で EmitParams 経由で設定するため startSpeed = 0
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.07f, 0.12f);
            main.startColor      = new ParticleSystem.MinMaxGradient(
                new Color(0.12f, 0.32f, 0.62f, 0.90f),
                new Color(0.65f, 0.82f, 0.96f, 0.95f));
            // 寿命を短く・重力を強くして弧軌道を寿命内に収める
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0.9f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var em = _jumpPS.emission;
            em.rateOverTime = 0f; // JumpRoutine から手動 Emit する

            var sh = _jumpPS.shape;
            sh.enabled = false;

            // VoL 無効（速度は EmitParams で完全制御）
            var vel = _jumpPS.velocityOverLifetime;
            vel.enabled = false;

            var col = _jumpPS.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f,    0f),
                        new GradientAlphaKey(1f,    0.05f),
                        new GradientAlphaKey(1f,    0.80f),
                        new GradientAlphaKey(0f,    1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            _jumpPS.Play();
        }

        // ── WaterPS 座標・向き収集 ────────────────────────────────────────
        // HexTile.SpawnWater が生成する WaterPS はベジェ曲線の接線方向を向いており、
        // その forward が川の流れ方向を表す。魚をこの向きに沿って泳がせる。

        private void CollectWaterPoints(IReadOnlyList<HexTile> tiles)
        {
            if (tiles == null || tiles.Count == 0) return;

            _waterPoints.Clear();

            foreach (var tile in tiles)
            {
                // GetComponentsInChildren で非アクティブ子も含めて検索
                foreach (Transform child in tile.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "WaterPS")
                    {
                        _waterPoints.Add(new WaterPoint
                        {
                            Position    = child.position,
                            FlowForward = child.forward
                        });
                    }
                }
            }

            // フォールバック: WaterPS が見つからない場合はタイル中心を使う
            if (_waterPoints.Count == 0)
            {
                foreach (var t in tiles)
                {
                    _waterPoints.Add(new WaterPoint
                    {
                        Position    = t.transform.position + Vector3.up * 0.30f,
                        FlowForward = t.transform.forward
                    });
                }
            }
        }

        // ── 泳ぎコルーチン ────────────────────────────────────────────────
        // WaterPS 位置に GO を移動してから Emit → 流れと同じ場所に出現。
        // velocity = 流れ方向 ± 上流/下流（StretchedBillboard が向きに伸びる）。

        private IEnumerator SwimRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(_swimIntervalMin, _swimIntervalMax));

                if (_swimPS == null || _waterPoints.Count == 0) yield break;

                var wp = _waterPoints[Random.Range(0, _waterPoints.Count)];
                _swimGO.transform.position = wp.Position;

                // 流れ方向（順流または逆流）に泳ぐ
                float speed = Random.Range(0.12f, 0.30f);
                float sign  = Random.value > 0.5f ? 1f : -1f;

                var ep = new ParticleSystem.EmitParams();
                ep.velocity = wp.FlowForward * speed * sign;
                _swimPS.Emit(ep, 1);
            }
        }

        // ── ジャンプコルーチン ────────────────────────────────────────────
        // EmitParams で上向き初速を設定 → 重力で放物線弾道（シェイプ無効でも確実に上へ飛ぶ）

        private IEnumerator JumpRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(_jumpIntervalMin, _jumpIntervalMax));

                if (_jumpPS == null || _waterPoints.Count == 0) yield break;

                var wp = _waterPoints[Random.Range(0, _waterPoints.Count)];
                _jumpGO.transform.position = wp.Position;

                int count = Random.Range(1, 3);
                for (int i = 0; i < count; i++)
                {
                    // 各粒子: 上向き + 少し横にぶれる → アーチ弾道で跳ねる
                    var ep = new ParticleSystem.EmitParams();
                    ep.velocity = new Vector3(
                        Random.Range(-0.25f, 0.25f),
                        Random.Range(2.0f,  3.0f),   // 上向き初速
                        Random.Range(-0.25f, 0.25f));
                    _jumpPS.Emit(ep, 1);
                }
            }
        }

        // ── マテリアル ─────────────────────────────────────────────────

        private static Material BuildFishMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "Fish_Runtime" };
            mat.SetFloat("_Surface", 1f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetColor("_BaseColor", Color.white);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }
    }
}
