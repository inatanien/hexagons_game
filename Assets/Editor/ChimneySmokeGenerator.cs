// 役割: 煙突から立ちのぼる煙のエフェクトを、テクスチャ・マテリアル・プレハブごと生成する。
//       家のプレハブに子として入れると HouseMeshGenerator の再生成で消えるため、
//       独立したプレハブにして、配置時に家へ取り付ける形にしている。
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ElfVillage.Editor
{
    public static class ChimneySmokeGenerator
    {
        private const string Folder = "Assets/_Game/VFX/Common";
        private const string TexPath = Folder + "/SmokePuff.png";
        private const string MatPath = Folder + "/ChimneySmoke.mat";
        private const string PrefabPath = Folder + "/ChimneySmoke.prefab";

        [MenuItem("Tools/精霊樹の森/煙エフェクトを生成")]
        public static GameObject Generate()
        {
            EnsureFolder(Folder);
            WritePuffTexture();

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
            Material mat = BuildMaterial(tex);

            GameObject go = new GameObject("ChimneySmoke");
            var ps = go.AddComponent<ParticleSystem>();
            Configure(ps);

            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            pr.sortMode = ParticleSystemSortMode.Distance;
            // 屋根と交差したときに面がちらつかないよう、少し手前に寄せる
            pr.sortingFudge = -2f;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Debug.Log("[ChimneySmokeGenerator] 生成しました -> " + PrefabPath);
            return prefab;
        }

        private static void Configure(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            // 立ちのぼって薄れるまでの時間。長いほどゆったり見える
            main.startLifetime = new ParticleSystem.MinMaxCurve(4.0f, 6.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.86f, 0.86f, 0.84f, 1f),
                new Color(0.70f, 0.71f, 0.72f, 1f));
            // わずかな負の重力で、煙が浮き上がる感じを出す
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.015f);
            // 家が動いても煙は空間に取り残される。World にしないと家に貼り付く
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 180;
            main.playOnAwake = true;

            var em = ps.emission;
            em.enabled = true;
            // 粒が重なって初めて煙の塊に見える。少ないと点々に見えてしまう
            em.rateOverTime = new ParticleSystem.MinMaxCurve(13f);

            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 7f;
            sh.radius = 0.018f;
            sh.rotation = new Vector3(-90f, 0f, 0f);  // 上向きに噴き出させる

            // 上昇しながら風で流される
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = Curve(0f, 0.075f);
            vel.y = Curve(0.018f, 0.042f);
            vel.z = Curve(0f, 0.026f);

            // 上がるにつれて膨らむ。煙らしさの大半はこれで決まる
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            var sc = new AnimationCurve();
            sc.AddKey(0f, 0.5f);
            sc.AddKey(0.25f, 1.3f);
            sc.AddKey(1f, 3.2f);
            size.size = new ParticleSystem.MinMaxCurve(1f, sc);

            // ふわっと出て、ゆっくり消える
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.90f, 0.90f, 0.89f), 0f),
                    new GradientColorKey(new Color(0.74f, 0.75f, 0.77f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.60f, 0.18f),
                    new GradientAlphaKey(0.45f, 0.55f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // ゆっくり回してのっぺり感を消す
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = false;
            rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            // 乱流。これがないと一直線に上がって不自然になる
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.05f);
            noise.frequency = 0.35f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.07f);
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.High;
            noise.octaveCount = 2;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private static ParticleSystem.MinMaxCurve Curve(float min, float max)
        {
            return new ParticleSystem.MinMaxCurve(min, max);
        }

        private static Material BuildMaterial(Texture2D tex)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, MatPath);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            // 半透明・加算ではなくアルファ合成。煙は光らせない
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>中心が濃く縁が透ける、ムラのある煙の粒を描く。</summary>
        private static void WritePuffTexture()
        {
            if (File.Exists(TexPath)) return;

            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            // 正円だと粒が揃いすぎるので、低周波のムラを掛けて崩す
            var seedX = Random.Range(0f, 100f);
            var seedY = Random.Range(0f, 100f);

            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f;
                    float v = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);

                    float fall = 1f - Mathf.SmoothStep(0.15f, 1.0f, r);
                    float n = Mathf.PerlinNoise(seedX + u * 2.2f, seedY + v * 2.2f);
                    float a = Mathf.Clamp01(fall * (0.65f + n * 0.7f));

                    px[y * N + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();

            File.WriteAllBytes(TexPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexPath);

            var ti = (TextureImporter)AssetImporter.GetAtPath(TexPath);
            ti.textureType = TextureImporterType.Default;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 64;
            ti.SaveAndReimport();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
