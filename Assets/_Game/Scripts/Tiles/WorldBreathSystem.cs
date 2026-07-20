// 役割: 世界の息吹（WorldBreath）システム。
//       3枚クラスター → 穏やかな葉の舞い（クラスターごとに独立したエフェクト）
//       5枚クラスター → 風に運ばれる横流れ（5s待機 → 3s吹く → 20s止む → 繰り返し）
//       クラスターが別の場所に存在する場合、それぞれ独立してエフェクトが維持される。

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ElfVillage.Core;

namespace ElfVillage.Tiles
{
    public class WorldBreathSystem : MonoBehaviour
    {
        [Header("しきい値")]
        [SerializeField] private int _gentleThreshold = 3;   // 穏やかな葉の舞い
        [SerializeField] private int _windThreshold   = 5;   // 横流れの風

        [Header("風サイクル（秒）")]
        [SerializeField] private float _windDelay    = 5f;
        [SerializeField] private float _windDuration = 3f;
        [SerializeField] private float _windInterval = 20f;

        // 葉っぱVFX専用の基準色。地面色統一（TileType.tileColorの白化）の影響を受けないよう、
        // TileType.tileColor / EffectivePreviewColorには依存せずここで独立して保持する。
        // 初期値は旧TileType_Forest.tileColorと同じ値（地面色統一前の見た目を維持）。
        [Header("葉っぱVFX色")]
        [SerializeField] private Color _forestLeafBaseColor = new Color(0.13f, 0.55f, 0.13f, 1f);

        // クラスターごとにエフェクトを管理する。TileTypeでは区切らず、実際のタイル集合の
        // 重なりだけで同一物理クラスターかどうかを判定する（FlowerPetalSystem._clustersと同じ設計）。
        // 以前はTileTypeをキーにしたDictionary<TileType, List<ClusterEntry>>だったため、
        // legacy単一タイル（TileType_Forest）と複合タイル（TileType_ForestFlower等）が
        // 物理的には同じクラスターでも別々のキーに分かれ、VFXが重複生成される不具合があった
        // （ForestGrowthEvaluatorがカテゴリベース判定になり、両者が同じクラスターとして
        // 扱われるようになったことで顕在化。Session 13）。
        private readonly List<ClusterEntry> _clusters = new();

        // タイル配置と同フレームに Shader.Find + new Material が走るとフリーズするため
        // Awake でシェーダーを事前コンパイルしてキャッシュする。
        private Material _cachedParticleMat;

        private void Awake()
        {
            _cachedParticleMat = ForestBreathEffect.BuildMaterial();
        }

        private void Start()
        {
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
            foreach (var e in _clusters)
                e.DestroyEffects();
            _clusters.Clear();
        }

        private void OnForestGrow(TerrainGrowthEvent<ForestGrowthMetrics> evt)
        {
            int size = evt.Metrics.LargestClusterSize;

            // AffectedTiles = 今回イベントが属するクラスターの全タイル（BFS結果）
            var currentTileSet = new HashSet<HexTile>(evt.AffectedTiles);

            // 既存クラスターとの重複チェック（タイルが重なる = 同じクラスター）。
            // TileTypeでは区切らず全クラスターを対象にするため、legacy単一タイルと
            // 複合タイルが混在するクラスターも正しく1つとして扱われる。
            var overlapping = new List<ClusterEntry>();
            foreach (var entry in _clusters)
                if (entry.Tiles.Overlaps(currentTileSet))
                    overlapping.Add(entry);

            ClusterEntry cluster;
            if (overlapping.Count == 0)
            {
                // 新規クラスター（全く別の場所）
                cluster = new ClusterEntry();
                _clusters.Add(cluster);
            }
            else
            {
                // 既存クラスターが成長 or 複数クラスターが合流
                cluster = overlapping[0];
                // 合流した余分なクラスターは破棄して統合
                for (int i = 1; i < overlapping.Count; i++)
                {
                    overlapping[i].DestroyEffects(this);
                    _clusters.Remove(overlapping[i]);
                }
            }

            cluster.Tiles = currentTileSet;

            // ── 穏やかな葉の舞い（閾値以上で常時再生） ──────────────
            if (size >= _gentleThreshold)
            {
                if (cluster.Gentle == null)
                    cluster.Gentle = new ForestBreathEffect(
                        _forestLeafBaseColor, isWind: false, transform, _cachedParticleMat);
                cluster.Gentle.UpdateBounds(evt.AffectedTiles);
                cluster.Gentle.Play();
            }

            // ── 風サイクル（閾値以上でコルーチンを1回だけ起動） ──────
            if (size >= _windThreshold)
            {
                if (cluster.Wind == null)
                    cluster.Wind = new ForestBreathEffect(
                        _forestLeafBaseColor, isWind: true, transform, _cachedParticleMat);
                cluster.Wind.UpdateBounds(evt.AffectedTiles);
                cluster.Wind.SetWindStrength(CalcWindStrength(size));

                if (cluster.WindCoroutine == null)
                    cluster.WindCoroutine = StartCoroutine(WindCycle(cluster.Wind));
            }
        }

        // 枚数 → 強度 0〜1（5枚=20%, 8枚=40%, 15枚=60%, 30枚=100%）
        private static float CalcWindStrength(int size)
        {
            if (size <  5)  return 0f;
            if (size <  8)  return Mathf.Lerp(0.20f, 0.40f, (size -  5f) /  3f);
            if (size < 15)  return Mathf.Lerp(0.40f, 0.60f, (size -  8f) /  7f);
            if (size < 30)  return Mathf.Lerp(0.60f, 1.00f, (size - 15f) / 15f);
            return 1.0f;
        }

        // 待機 → そよ風 → 止む → 繰り返し
        private IEnumerator WindCycle(ForestBreathEffect effect)
        {
            while (true)
            {
                yield return new WaitForSeconds(_windDelay);
                effect.Play();
                yield return new WaitForSeconds(_windDuration);
                effect.Stop();
                yield return new WaitForSeconds(_windInterval);
            }
        }

        // ── クラスター単位の管理エントリ ──────────────────────────────

        private sealed class ClusterEntry
        {
            public HashSet<HexTile>   Tiles = new();
            public ForestBreathEffect Gentle;
            public ForestBreathEffect Wind;
            public Coroutine          WindCoroutine;

            // コルーチンを止めてエフェクト GameObject を破棄する
            internal void DestroyEffects(WorldBreathSystem owner = null)
            {
                if (owner != null && WindCoroutine != null)
                    owner.StopCoroutine(WindCoroutine);
                WindCoroutine = null;
                Gentle?.Destroy();
                Wind?.Destroy();
                Gentle = null;
                Wind   = null;
            }
        }

        // ── パーティクルエフェクト本体 ────────────────────────────────

        private sealed class ForestBreathEffect
        {
            private readonly GameObject     _go;
            private readonly ParticleSystem _ps;

            internal ForestBreathEffect(Color tileColor, bool isWind,
                                         Transform parent, Material sharedMat)
            {
                _go = new GameObject(isWind ? "ForestWind" : "ForestGentle");
                // WorldBreathSystem の子にすることで hierarchy を整理し
                // ルートレベル GameObject 追加による URP 再登録の副作用を避ける
                if (parent != null) _go.transform.SetParent(parent);

                _ps = _go.AddComponent<ParticleSystem>();

                if (sharedMat != null)
                    _go.GetComponent<ParticleSystemRenderer>().material = sharedMat;

                Setup(tileColor, isWind);
            }

            // クラスター AABB を更新してパーティクル発生源を移動（再生は外部制御）
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

                _go.transform.position = new Vector3(center.x, center.y + 2.5f, center.z);

                var shape = _ps.shape;
                shape.scale = new Vector3(extent.x + 1.0f, 0.3f, extent.z + 1.0f);
            }

            // 風強度 t（0〜1）で速度・重力・放出レートをスケールする
            // Setup の 100% 値を基準として乗算する
            internal void SetWindStrength(float t)
            {
                t = Mathf.Clamp01(t);

                var vel = _ps.velocityOverLifetime;
                vel.x = new ParticleSystem.MinMaxCurve(1.8f * t, 3.5f * t);
                vel.z = new ParticleSystem.MinMaxCurve(-0.9f * t, 0.9f * t);

                var main = _ps.main;
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0.07f * t);

                var em = _ps.emission;
                em.rateOverTime = 16f * t;
            }

            internal void Play() { if (!_ps.isPlaying) _ps.Play(); }

            // StopEmitting → 既存パーティクルは落下させて自然消滅
            internal void Stop() => _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            internal void Destroy()
            {
                if (_go != null) Object.Destroy(_go);
            }

            private void Setup(Color tileColor, bool isWind)
            {
                _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = _ps.main;
                main.loop            = true;
                main.duration        = 4f;
                // 目立ちすぎないようサイズだけ控えめにしている（元は0.10〜0.28）。
                // パーティクル数・発生頻度は元の値に戻した（ユーザー指定）。
                main.maxParticles    = isWind ? 80 : 30;
                main.startLifetime   = new ParticleSystem.MinMaxCurve(4.0f, 7.0f);
                main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);  // shape 方向を無効化
                main.startSize       = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
                main.startRotation   = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor      = LeafColorGradient(tileColor);
                // 穏やかは velocityOverLifetime で下方向を明示するので重力は 0
                main.gravityModifier = new ParticleSystem.MinMaxCurve(isWind ? 0.07f : 0f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var em = _ps.emission;
                em.rateOverTime = isWind ? 16f : 3f;

                var sh = _ps.shape;
                sh.shapeType             = ParticleSystemShapeType.Box;
                sh.scale                 = Vector3.one;
                sh.randomDirectionAmount = 0f;  // ランダム方向を完全に無効化

                // velocityOverLifetime で全軸を明示 → shape 由来の偶発的横移動を排除
                // 全軸 TwoConstants モードで統一（モード不一致エラーを防ぐ）
                var vel = _ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space   = ParticleSystemSimulationSpace.World;
                if (isWind)
                {
                    // 一方向に強く流れる（横風）、Y は重力に任せる
                    vel.x = new ParticleSystem.MinMaxCurve(1.8f, 3.5f);
                    vel.y = new ParticleSystem.MinMaxCurve(0.0f, 0.0f);
                    vel.z = new ParticleSystem.MinMaxCurve(-0.9f, 0.9f);
                }
                else
                {
                    // X/Z を厳密に 0 固定、Y で下方向を明示（横移動ゼロ保証）
                    vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                    vel.y = new ParticleSystem.MinMaxCurve(-0.8f, -0.4f);
                    vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                }

                var rot = _ps.rotationOverLifetime;
                rot.enabled = true;
                // 風の時は速く回転してひらひら感を強調
                float rotSpeed = isWind ? 360f : 180f;
                rot.z = new ParticleSystem.MinMaxCurve(
                    -rotSpeed * Mathf.Deg2Rad,
                     rotSpeed * Mathf.Deg2Rad
                );

                var col = _ps.colorOverLifetime;
                col.enabled = true;
                col.color   = FadeGradient();

                var sz = _ps.sizeOverLifetime;
                sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f,    0f),
                    new Keyframe(0.05f, 1f),
                    new Keyframe(0.80f, 0.9f),
                    new Keyframe(1f,    0f)
                ));
            }

            private static ParticleSystem.MinMaxGradient FadeGradient()
            {
                var g = new Gradient();
                // 目立ちすぎないよう最大不透明度を抑えている（元はピーク時1f、完全不透明だった）。
                g.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f),
                            new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f,    0f),
                            new GradientAlphaKey(0.75f, 0.05f),
                            new GradientAlphaKey(0.75f, 0.80f),
                            new GradientAlphaKey(0f,    1f) }
                );
                return new ParticleSystem.MinMaxGradient(g);
            }

            internal static Material BuildMaterial()
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;

                var mat = new Material(shader) { name = "ForestBreath_Runtime" };
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

        // ForestBreathEffect（private nested class）から抽出した純粋関数。
        // ネストクラスは外側クラスのprivateメンバーにもアクセスできるため、
        // Setup()側の呼び出しは無修正のまま動作する。EditModeテストから直接
        // 検証できるようにするためだけにここへ配置している（挙動・計算式は変更なし）。
        public static ParticleSystem.MinMaxGradient LeafColorGradient(Color baseColor)
        {
            // 目立ちすぎないよう、鮮やかな黄緑（0.75,0.95,0.20等）への寄せ幅を抑え、
            // 基準色（森の深緑）に近い落ち着いた色にしている（元はブレンド比0.25f/0.30f）。
            var c1 = Color.Lerp(baseColor, new Color(0.75f, 0.95f, 0.20f, 1f), 0.15f);
            var c2 = Color.Lerp(baseColor, new Color(0.90f, 0.85f, 0.10f, 1f), 0.18f);
            return new ParticleSystem.MinMaxGradient(c1, c2);
        }
    }
}
