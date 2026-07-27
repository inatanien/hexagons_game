// 役割: FlowerClusterEvent を受け取り、花畑クラスター上に花びらが舞う演出を生成する。
//       複数の独立したクラスターをそれぞれ追跡し、すべての場所から花びらを放出する。
//       最大クラスターサイズに応じて段階的に花びらの色が追加される。
//       デフォルト: 3=黄, 4=青, 5=紫, 6=赤, 7=ピンク
//       色ティアが打ち止めになる7枚以降も、25枚まではクラスターが大きいほど
//       1バーストの発生数が最大4倍まで増える（それ以降は頭打ち）。

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

        [Header("大規模クラスターでの発生数増加（上限あり）")]
        [Tooltip("この枚数を超えたクラスターサイズから発生数が増え始める（色ティアが打ち止めになる枚数と合わせる）")]
        [SerializeField] private int _boostStartSize = 7;
        [Tooltip("この枚数で発生数が最大倍率に到達し、以降は頭打ちになる")]
        [SerializeField] private int _boostMaxSize = 25;
        [Tooltip("最大倍率（_emitCountMin/_emitCountMaxにこの倍率を掛けた値が上限になる）")]
        [SerializeField] private float _boostMaxMultiplier = 4f;

        // 直近のFlowerClusterEventで計算した最大クラスターサイズ（EmitRoutineの発生数スケーリングに使う）。
        // Stage 8以降は重み付き（複合タイルはareaWeightで按分）なのでfloat。
        private float _currentMaxWeightedSize;

        [Header("花びらの絵柄")]
        [Tooltip("花びらに使う画像。ファイル名の末尾の色（Yellow/Blue/Purple/Red/Pink…）で" +
                  "ティアへ割り当てられ、同じ色の複数形状が1粒ごとにランダムで選ばれる。\n" +
                  "未設定の場合は従来どおり色だけの粒になる")]
        [SerializeField] private Texture2D[] _petalTextures;
        [Tooltip("絵柄をティアの色でどれだけ染めるか。0=絵の色そのまま / 1=従来どおりティアの色。\n" +
                  "画像は既にティアと同じ色を持っているため、強く染めると陰影が潰れる")]
        [Range(0f, 1f)]
        [SerializeField] private float _petalTextureTint = 0.15f;

        // 閾値ごとの色定義
        private readonly struct PetalTier
        {
            public readonly int    Threshold;
            public readonly Color  ColorA;
            public readonly Color  ColorB;
            /// <summary>この段階で使う花びら画像の色名（ファイル名末尾と一致させる）。</summary>
            public readonly string TextureColorName;

            public PetalTier(int threshold, Color a, Color b, string textureColorName)
            { Threshold = threshold; ColorA = a; ColorB = b; TextureColorName = textureColorName; }
        }

        private static readonly PetalTier[] s_Tiers =
        {
            new PetalTier(3, new Color(1.00f, 0.92f, 0.20f, 0.85f), new Color(1.00f, 0.75f, 0.10f, 0.90f), "Yellow"), // 黄
            new PetalTier(4, new Color(0.45f, 0.72f, 1.00f, 0.85f), new Color(0.60f, 0.85f, 1.00f, 0.90f), "Blue"),   // 青
            new PetalTier(5, new Color(0.72f, 0.40f, 1.00f, 0.85f), new Color(0.85f, 0.60f, 1.00f, 0.90f), "Purple"), // 紫
            new PetalTier(6, new Color(1.00f, 0.25f, 0.25f, 0.85f), new Color(1.00f, 0.50f, 0.40f, 0.90f), "Red"),    // 赤
            new PetalTier(7, new Color(1.00f, 0.55f, 0.75f, 0.85f), new Color(1.00f, 0.75f, 0.88f, 0.90f), "Pink"),   // ピンク
        };

        private readonly List<(GameObject go, ParticleSystem ps, int threshold)> _tiers = new();

        private Material  _mat;
        private Coroutine _emitCoroutine;
        private bool      _initialized;

        // ティアごとの絵柄。★ティア単位でアトラスを分ける理由
        //   花びらは「クラスターが育つと色が増える」ことが見た目の進行になっている。
        //   全色を1枚のアトラスにまとめると、最初の段階から全色が出てしまい進行が消える。
        //   ティアごとに自分の色だけのアトラスを持たせることで、既存の進行を保ったまま絵柄を使える。
        private readonly List<Texture2D> _tierAtlases   = new();
        private readonly List<Material>  _tierMaterials = new();

        // クラスターごとにタイルセットを保持（重複検出で同一クラスターを更新する）
        private readonly List<HashSet<HexTile>> _clusters    = new();
        // 全クラスターから集めた放出位置（毎回再構築）
        private readonly List<Vector3>          _tilePositions = new();

        private void Awake()
        {
            _mat = BuildPetalMaterial();

            // ★タイル配置と同フレームにShader.Find/new Material/アトラス生成が走ると重いため、
            //   WorldBreathSystemと同じくAwakeで先に用意しておく。
            BuildTierAtlases();
        }

        private static void DestroyRuntimeAsset(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset);
            else                       DestroyImmediate(asset);
        }

        /// <summary>
        /// ティアごとに「その色の花びら画像」だけを集めたアトラスとMaterialを作る。
        /// 画像が無い色は null のままで、そのティアは従来どおり色だけの粒になる。
        /// </summary>
        private void BuildTierAtlases()
        {
            _tierAtlases.Clear();
            _tierMaterials.Clear();

            foreach (var tier in s_Tiers)
            {
                var shapes = CollectTexturesFor(tier.TextureColorName);
                if (shapes.Count == 0) { _tierAtlases.Add(null); _tierMaterials.Add(null); continue; }

                var atlas = BuildHorizontalAtlas(shapes);
                if (atlas == null) { _tierAtlases.Add(null); _tierMaterials.Add(null); continue; }

                var mat = BuildPetalMaterial();
                if (mat != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", atlas);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", atlas);
                    mat.mainTexture = atlas;
                    mat.name = "FlowerPetal_" + tier.TextureColorName;
                }

                _tierAtlases.Add(atlas);
                _tierMaterials.Add(mat);
            }
        }

        /// <summary>ファイル名の末尾が指定色と一致する画像を集める（例: Petal_Shape_02_Pink → "Pink"）。</summary>
        private List<Texture2D> CollectTexturesFor(string colorName)
        {
            var result = new List<Texture2D>();
            if (_petalTextures == null || string.IsNullOrEmpty(colorName)) return result;

            foreach (var tex in _petalTextures)
            {
                if (tex == null) continue;

                int underscore = tex.name.LastIndexOf('_');
                if (underscore < 0 || underscore + 1 >= tex.name.Length) continue;

                string suffix = tex.name.Substring(underscore + 1);
                if (!string.Equals(suffix, colorName, System.StringComparison.OrdinalIgnoreCase)) continue;

                if (!tex.isReadable)
                {
                    Debug.LogWarning($"[FlowerPetalSystem] {tex.name} が Read/Write 無効のため使用できません。");
                    continue;
                }
                result.Add(tex);
            }
            return result;
        }

        /// <summary>
        /// 同じ色の形状を横一列に並べたアトラス。1色あたり数枚しか無いため、
        /// 格子計算を単純に保てる横1行で十分（TextureSheetAnimationは numTilesX×1 で拾う）。
        /// 寸法が揃っていない場合はアトラスを作らない。
        /// </summary>
        private static Texture2D BuildHorizontalAtlas(List<Texture2D> shapes)
        {
            int cell = shapes[0].width;
            foreach (var t in shapes)
            {
                if (t.width == cell && t.height == cell) continue;
                Debug.LogWarning($"[FlowerPetalSystem] 花びら画像の寸法が揃っていません（{t.name}）。絵柄は無効になります。");
                return null;
            }

            var atlas = new Texture2D(cell * shapes.Count, cell, TextureFormat.RGBA32, false)
            {
                name       = "FlowerPetalAtlas_Runtime",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            for (int i = 0; i < shapes.Count; i++)
                atlas.SetPixels(i * cell, 0, cell, cell, shapes[i].GetPixels());

            atlas.Apply(false, false);
            return atlas;
        }

        /// <summary>
        /// 絵柄を使うときの粒の色。テクスチャへ掛け算されるため、
        /// ティアの色をそのまま使うと絵の陰影が沈む。RGBだけ白へ寄せ、透明度はそのまま活かす。
        /// 純粋関数なのでEditModeから直接検証できる。
        /// </summary>
        public static Color TintForTexturedPetal(Color tierColor, float tint)
        {
            float t = float.IsFinite(tint) ? Mathf.Clamp01(tint) : 0f;
            var rgb = Color.Lerp(Color.white, tierColor, t);
            return new Color(rgb.r, rgb.g, rgb.b, tierColor.a);   // アルファはティアの値を維持する
        }

        private void OnEnable()  => EventBus.Subscribe<FlowerClusterEvent>(OnFlowerCluster);
        private void OnDisable() => EventBus.Unsubscribe<FlowerClusterEvent>(OnFlowerCluster);

        private void OnDestroy()
        {
            foreach (var t in _tiers)
                if (t.go != null) Destroy(t.go);

            // ランタイム生成したMaterialとアトラスは自動では解放されないため明示的に破棄する。
            foreach (var m in _tierMaterials) DestroyRuntimeAsset(m);
            foreach (var a in _tierAtlases)   DestroyRuntimeAsset(a);
            _tierMaterials.Clear();
            _tierAtlases.Clear();

            DestroyRuntimeAsset(_mat);
            _mat = null;
        }

        private void OnFlowerCluster(FlowerClusterEvent evt)
        {
            if (!_initialized)
            {
                InitParticleSystems();
                _initialized = true;
            }

            UpdateClusters(evt.Tiles);

            // 最大クラスターサイズでティアを切り替える。
            // Stage 8以降は重み付きで評価するため、複合タイル（花0.3＋森0.7等）だけの
            // クラスターでは色ティアが増えにくくなる。単一属性の花畑は1.0なので挙動不変。
            float maxSize = 0f;
            foreach (var c in _clusters)
            {
                float w = TerrainEffectWeight.SumFor(c, TileCategory.Field);
                if (w > maxSize) maxSize = w;
            }
            _currentMaxWeightedSize = maxSize;

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
            for (int i = 0; i < s_Tiers.Length; i++)
            {
                var tier = s_Tiers[i];

                // 絵柄が用意できたティアは、その色専用のMaterialとアトラスを使う。
                Material  tierMat   = i < _tierMaterials.Count ? _tierMaterials[i] : null;
                Texture2D tierAtlas = i < _tierAtlases.Count   ? _tierAtlases[i]   : null;
                int shapeCount = (tierAtlas != null) ? tierAtlas.width / tierAtlas.height : 0;

                var go = new GameObject($"FlowerPetal_T{tier.Threshold}");
                go.transform.SetParent(transform);
                var ps = go.AddComponent<ParticleSystem>();
                SetupRenderer(go.GetComponent<ParticleSystemRenderer>(), tierMat);
                SetupPS(ps, tier.ColorA, tier.ColorB, shapeCount, _petalTextureTint);
                go.SetActive(false);
                _tiers.Add((go, ps, tier.Threshold));
            }
        }

        private void SetupRenderer(ParticleSystemRenderer r, Material tierMaterial)
        {
            var mat = tierMaterial != null ? tierMaterial : _mat;
            if (mat != null) r.material = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
        }

        private void SetupPS(ParticleSystem ps, Color colorA, Color colorB,
                              int shapeCount, float textureTint)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop            = true;
            main.duration        = 5f;
            main.maxParticles    = 60;
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0f);
            main.startLifetime   = new ParticleSystem.MinMaxCurve(3.0f, 5.0f);
            // ★色だけの粒だった頃は0.04〜0.09で足りていたが、絵柄を貼った今はこの大きさだと
            //   通常のカメラ距離で花びらの形が潰れ、地面テクスチャの模様と見分けがつかなくなる。
            //   実測して「近距離で花びらの形が読めて、最大ズームアウトでもうるさくならない」
            //   範囲に広げた。地面の花（0.40〜0.60）より十分小さいので主役は入れ替わらない。
            main.startSize       = new ParticleSystem.MinMaxCurve(0.11f, 0.20f);
            // 絵柄を使う場合、色はテクスチャへ掛け算されるので白へ寄せて絵を活かす。
            bool hasSprites = shapeCount > 0;
            main.startColor = hasSprites
                ? new ParticleSystem.MinMaxGradient(TintForTexturedPetal(colorA, textureTint),
                                                     TintForTexturedPetal(colorB, textureTint))
                : new ParticleSystem.MinMaxGradient(colorA, colorB);

            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            // 粒ごとにアトラスから1つの形状を選ばせる（アニメーションはさせない）。
            var tsa = ps.textureSheetAnimation;
            if (shapeCount > 1)
            {
                tsa.enabled       = true;
                tsa.mode          = ParticleSystemAnimationMode.Grid;
                tsa.numTilesX     = shapeCount;
                tsa.numTilesY     = 1;
                tsa.animation     = ParticleSystemAnimationType.WholeSheet;
                tsa.timeMode      = ParticleSystemAnimationTimeMode.Lifetime;
                tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);        // 寿命中は同じ絵柄のまま
                tsa.startFrame    = new ParticleSystem.MinMaxCurve(0f, shapeCount); // コマ番号でランダム
            }
            else
            {
                tsa.enabled = false;   // 形状が1つ以下なら格子で切る必要がない
            }

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

                // クラスターが大きいほど1バーストの発生数を増やす（_boostMaxSizeで頭打ち）。
                // WorldBreathSystem.CalcWindStrengthと同じ「Lerpで段階的に強度を上げ上限で頭打ち」設計。
                // CalcCountMultiplierは既存の公開API（int）のまま維持し、重み付きサイズは切り捨てて渡す。
                float mult    = CalcCountMultiplier(Mathf.FloorToInt(_currentMaxWeightedSize), _boostStartSize, _boostMaxSize, _boostMaxMultiplier);
                int minCount  = Mathf.Max(_emitCountMin, Mathf.RoundToInt(_emitCountMin * mult));
                int maxCount  = Mathf.Max(minCount,      Mathf.RoundToInt(_emitCountMax * mult));

                int count = Random.Range(minCount, maxCount + 1);
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

        /// <summary>
        /// クラスターサイズに応じた発生数倍率。boostStartSize以下は1倍、boostMaxSize以上でmaxMultiplierに
        /// 頭打ちになる（間は線形補間）。EditModeテストから直接検証できるようpublic staticにしている
        /// （ElementRegionLayout/TilePropVisualBuilderと同じ、本プロジェクトの純粋関数テスト規約）。
        /// </summary>
        public static float CalcCountMultiplier(int clusterSize, int boostStartSize, int boostMaxSize, float maxMultiplier)
        {
            if (clusterSize <= boostStartSize) return 1f;
            if (clusterSize >= boostMaxSize)    return maxMultiplier;

            float t = (float)(clusterSize - boostStartSize) / (boostMaxSize - boostStartSize);
            return Mathf.Lerp(1f, maxMultiplier, t);
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
