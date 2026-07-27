// 役割: 花畑タイルの花に「複数の絵柄」を使えるようにする。
//       画像を横並びのアトラスへ実行時合成し、粒ごとに別のコマ（＝別の花）を出す。
//
//       ★なぜアトラス1枚なのか
//         花はタイル1枚につきParticleSystem 1個・20粒という構成を維持したい。
//         1つのParticleSystemは1つのMaterialしか持てないので、
//         複数の絵柄を出すにはアトラスへまとめてコマで切り替えるしかない。
//
//       ★粒ごとのコマの決め方（実測して選んだ方式）
//         Emitのたびに textureSheetAnimation.startFrame を書き換える方法は使えない。
//         TSAはEmit時ではなくシミュレーション更新時にモジュール値を読むため、
//         同じフレーム内に撒いた粒が全部「最後に設定したコマ」になってしまう（実測確認済み）。
//         そこで startFrame をコマ番号の範囲抽選にしておき、
//         EmitParams.randomSeed へ「座標から作った決定論的な種」を渡す。
//         同じ種なら常に同じコマになることを実測で確認している。
//
//       ★決定論の保証範囲（重要・将来の担当者向け）
//         種を決めるのはこちらだが、種→コマ番号の対応を決めるのはUnityの粒ごと乱数。
//         したがって保証の範囲は次のとおり:
//
//           保証する   … 同じUnityバージョンであれば、同じタイルは常に同じ花畑になる。
//                        配置ゴーストと実配置も一致する（同じseed列を通すため）。
//           保証しない … Unity内部の乱数実装が変わった場合に、
//                        同じ種から同じコマが出続けること。
//                        出現比率の重み付け（この絵柄を多めに、等）。
//
//         つまり「セーブデータや通信で花の絵柄まで一致させたい」「絵柄ごとの比率を
//         設計したい」という要求が出た時点で、この方式では足りなくなる。
//
//       ★完全にコード管理された選択が必要になったときの置き換え先
//         TreeVariantWeights.Select(weights, seed) と同じ形の純粋関数、
//         例えば FlowerVariantWeights.Select() を用意して、
//         こちらでコマ番号（＝絵柄）を明示的に決めるようにする。
//         ただしその場合、1つのParticleSystemでは粒ごとにコマを指定できないため
//         （下の「粒ごとのコマの決め方」を参照）、
//         絵柄ごとにParticleSystemを分けるか、Shader側でUVをずらす必要がある。
//         「ParticleSystem 1個」という現在の構成とのトレードオフになる。
//
//       ★画像が未設定のとき
//         仮の花を _placeholderVariantCount 種類ぶん生成して同じ経路に流す。
//         こうしておくと、本物の絵が入る前でも複数絵柄の仕組みがそのまま動く。

using System.Collections.Generic;
using UnityEngine;

namespace ElfVillage.Tiles
{
    public class FlowerBillboardSystem : MonoBehaviour
    {
        [Header("花の絵")]
        [Tooltip("花に使う画像。複数入れると粒ごとに使い分けられる。\n" +
                  "未設定の場合は仮の花を自動生成する（仕組みは同じ経路を通る）")]
        [SerializeField] private Texture2D[] _flowerTextures;

        [Header("アトラス")]
        [Tooltip("アトラス1コマの解像度。元画像はここへ縮小・拡大される")]
        [SerializeField] private int _cellSize = 128;

        [Header("画像未設定時の仮の花")]
        [Tooltip("自動生成する仮の花の種類数")]
        [Range(1, 8)]
        [SerializeField] private int _placeholderVariantCount = 5;

        /// <summary>
        /// Sceneに置かれていなければnullのまま＝従来どおり単一の仮スプライトになる。
        /// 「やめたければコンポーネントを外すだけ」で戻せるようにするため。
        /// </summary>
        public static FlowerBillboardSystem Instance { get; private set; }

        private Material  _material;
        private Texture2D _atlas;
        private int       _shapeCount;

        /// <summary>アトラスに入っている絵柄の数。ParticleSystem側のnumTilesXに使う。</summary>
        public int ShapeCount => _shapeCount;

        /// <summary>全ての花タイルで共有するMaterial（タイルごとに複製しない）。</summary>
        public Material SharedMaterial => _material;

        public bool IsReady => _material != null && _shapeCount > 0;

        private void Awake()
        {
            Build();
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;

            DestroyRuntimeAsset(_material);
            DestroyRuntimeAsset(_atlas);
            _material   = null;
            _atlas      = null;
            _shapeCount = 0;
        }

        private static void DestroyRuntimeAsset(Object asset)
        {
            if (asset == null) return;
            if (Application.isPlaying) Destroy(asset);
            else                       DestroyImmediate(asset);
        }

        // ── 粒ごとの種 ────────────────────────────────────────────────

        /// <summary>
        /// 花1粒ぶんの位置seedから、Unityへ渡す粒ごとの乱数種を作る。
        /// ★花のseedは `q*31 + r*17 + i*7` という歩幅7の等差数列なので、
        ///   そのまま渡すと絵柄が規則的に並ぶ。必ずハッシュでかき混ぜてから使う。
        ///   0はUnity側で「種の指定なし」と解釈されうるため1へ寄せる。
        ///
        /// ★この関数が保証するのは「種が決定論的であること」まで。
        ///   種からどのコマ（絵柄）が出るかはUnityの粒ごと乱数が決めるため、
        ///   Unityバージョンをまたいだ一致までは保証しない（クラス冒頭の但し書きを参照）。
        /// </summary>
        public static uint ParticleSeed(int positionSeed)
        {
            uint s = TileVisualHash.Mix(positionSeed);
            return s == 0u ? 1u : s;
        }

        // ── ランタイム生成のリソース ──────────────────────────────────

        private void Build()
        {
            int cell = Mathf.Clamp(_cellSize, 16, 512);

            var sources = CollectSources();
            if (sources.Count == 0) return;

            _atlas      = BuildHorizontalAtlas(sources, cell);
            _shapeCount = sources.Count;

            // 元画像から作った一時テクスチャ（仮の花）はアトラスへ焼いた時点で不要。
            foreach (var t in _generatedSources) DestroyRuntimeAsset(t);
            _generatedSources.Clear();

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[FlowerBillboardSystem] URP Particles/Unlit shader が見つからないため花の絵柄は従来のままになります", this);
                DestroyRuntimeAsset(_atlas);
                _atlas      = null;
                _shapeCount = 0;
                return;
            }

            _material = new Material(shader) { name = "FlowerBillboard_Shared" };
            if (_material.HasProperty("_BaseMap")) _material.SetTexture("_BaseMap", _atlas);
            _material.mainTexture = _atlas;
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", Color.white);

            // ★URPのParticles/Unlitは既定がOpaqueで、明示しないとアルファが無視される。
            //   WorldBreathSystem / FlowerPetalSystem と同じ値を明示する。
            _material.SetFloat("_Surface",  1f);
            _material.SetFloat("_Blend",    0f);
            _material.SetFloat("_SrcBlend", 5f);   // SrcAlpha
            _material.SetFloat("_DstBlend", 10f);  // OneMinusSrcAlpha
            _material.SetFloat("_ZWrite",   0f);
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        // 仮の花は生成後にアトラスへ焼いて破棄するので、一時的に握っておく。
        private readonly List<Texture2D> _generatedSources = new();

        private List<Texture2D> CollectSources()
        {
            var list = new List<Texture2D>();

            if (_flowerTextures != null)
                foreach (var t in _flowerTextures)
                    if (t != null) list.Add(t);

            if (list.Count > 0) return list;

            // 画像が未設定なら仮の花を生成する。仕組みの経路は本番と同じ。
            int count = Mathf.Clamp(_placeholderVariantCount, 1, 8);
            for (int i = 0; i < count; i++)
            {
                var tex = BuildPlaceholderFlower(64, i);
                _generatedSources.Add(tex);
                list.Add(tex);
            }
            return list;
        }

        /// <summary>画像を横一列に並べたアトラスを作る（numTilesX = 枚数 / numTilesY = 1）。</summary>
        private static Texture2D BuildHorizontalAtlas(List<Texture2D> sources, int cell)
        {
            var atlas = new Texture2D(cell * sources.Count, cell, TextureFormat.RGBA32, false)
            {
                name       = "FlowerBillboard_Atlas",
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color[atlas.width * atlas.height];
            for (int s = 0; s < sources.Count; s++)
            {
                var src = sources[s];
                for (int y = 0; y < cell; y++)
                {
                    float v = (y + 0.5f) / cell;
                    for (int x = 0; x < cell; x++)
                    {
                        float u = (x + 0.5f) / cell;
                        pixels[y * atlas.width + s * cell + x] = SafeSample(src, u, v);
                    }
                }
            }

            atlas.SetPixels(pixels);
            atlas.Apply(false, false);
            return atlas;
        }

        /// <summary>
        /// 読み取り不可（Read/Write Disabled）な画像でもテストや実行を止めないための保険。
        /// 読めない場合は透明を返し、その絵柄だけが空になる。
        /// </summary>
        private static Color SafeSample(Texture2D src, float u, float v)
        {
            try   { return src.GetPixelBilinear(u, v); }
            catch { return new Color(0f, 0f, 0f, 0f); }
        }

        /// <summary>
        /// 仮の花を1枚作る。variantごとに花びらの枚数・大きさ・色をずらして、
        /// 「複数絵柄が粒ごとに切り替わっているか」を絵が無い段階でも確認できるようにする。
        /// </summary>
        private static Texture2D BuildPlaceholderFlower(int size, int variant)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name       = "FlowerBillboard_Placeholder_" + variant,
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            // variantごとの見た目の差（色相と花びら枚数）。乱数は使わない。
            int   petalCount  = 5 + (variant % 3);                    // 5〜7枚
            float hue         = (0.92f + variant * 0.07f) % 1f;       // ピンク〜黄〜白系へ少しずつ
            Color petalColor  = Color.HSVToRGB(hue, 0.45f, 1.0f);
            Color centerColor = new Color(1.0f, 0.85f, 0.15f);

            float petalRadius  = size * (0.26f + (variant % 2) * 0.03f);
            float petalDist    = size * 0.20f;
            float centerRadius = size * 0.14f;
            float startAngle   = variant * 13f * Mathf.Deg2Rad;       // 向きもずらす

            var center = new Vector2(size * 0.5f, size * 0.5f);
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);

                    float alpha = 0f;
                    for (int k = 0; k < petalCount; k++)
                    {
                        float ang = startAngle + k * (Mathf.PI * 2f / petalCount);
                        var petalCenter = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * petalDist;
                        float d = Vector2.Distance(p, petalCenter);
                        alpha = Mathf.Max(alpha, Mathf.Clamp01(1f - d / petalRadius));
                    }

                    var col = petalColor;
                    float dc = Vector2.Distance(p, center);
                    float centerA = Mathf.Clamp01(1f - dc / centerRadius);
                    if (centerA > 0f)
                    {
                        col   = Color.Lerp(petalColor, centerColor, centerA);
                        alpha = Mathf.Max(alpha, centerA);
                    }

                    pixels[y * size + x] = new Color(col.r, col.g, col.b, alpha);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
