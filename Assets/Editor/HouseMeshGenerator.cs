// 役割: 参考画像の家を手続き的にメッシュ生成する。AI生成と違い形・ポリゴン数・向きを完全に制御でき、
//       色違いや大きさ違いのバリエーションを同じ形状から量産できる。
//       色は極小のパレットテクスチャ1枚をUVで参照させ、家1軒＝1マテリアル＝1ドローコールに収める。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ElfVillage.Editor
{
    /// <summary>家の寸法指定。ここを変えるだけでバリエーションが作れる。</summary>
    public struct HouseParams
    {
        public float width;        // X 幅（壁の外寸）
        public float depth;        // Z 奥行
        public float wallHeight;   // 壁の高さ
        public float roofRise;     // 壁上端から棟までの高さ
        public float eaveOut;      // 軒の張り出し（左右）
        public float gableOut;     // 破風の張り出し（前後）
        public float roofThick;    // 屋根板の厚み
        public float postSize;     // 隅柱の太さ

        public static HouseParams Default
        {
            get
            {
                HouseParams p;
                p.width = 0.62f;
                p.depth = 0.72f;
                p.wallHeight = 0.40f;
                p.roofRise = 0.30f;
                p.eaveOut = 0.075f;
                p.gableOut = 0.06f;
                p.roofThick = 0.035f;
                p.postSize = 0.055f;
                return p;
            }
        }
    }

    public static class HouseMeshGenerator
    {
        // パレット上の色番号。UVはこの番号のマスの中心を指す
        private const int SwWall = 0, SwPost = 1, SwRoof = 2, SwRoofSide = 3;
        private const int SwChimney = 4, SwDoor = 5, SwGlass = 6, SwFrame = 7;

        private static readonly Color[] Palette =
        {
            new Color32(0xEF, 0xE3, 0xC4, 0xFF), // 0 壁（クリーム色の漆喰）
            new Color32(0x9A, 0x63, 0x35, 0xFF), // 1 柱（温かみのある茶）
            new Color32(0x1F, 0x63, 0xD2, 0xFF), // 2 屋根（明るい青）
            new Color32(0x18, 0x4F, 0xA8, 0xFF), // 3 屋根の小口（陰になる面を一段暗く）
            new Color32(0xDC, 0xCF, 0xAC, 0xFF), // 4 煙突（明るい石材）
            new Color32(0x7B, 0x4A, 0x22, 0xFF), // 5 扉
            new Color32(0x6F, 0xA8, 0xDC, 0xFF), // 6 窓ガラス
            new Color32(0x8B, 0x55, 0x2A, 0xFF), // 7 窓枠
        };

        private const int SwatchPx = 16;   // 1色あたりのピクセル数
        private const int GridCols = 4;    // パレットの列数

        private static List<Vector3> _v;
        private static List<Vector3> _n;
        private static List<Vector2> _uv;
        private static List<int> _t;

        [MenuItem("Tools/精霊樹の森/家メッシュを生成")]
        public static void GenerateDefault()
        {
            Generate(HouseParams.Default, "House_Cream_Blue");
        }

        /// <summary>メッシュ・マテリアル・パレット・プレハブを一式書き出す。</summary>
        public static GameObject Generate(HouseParams p, string name,
            string folder = "Assets/_Game/Art/Models/House")
        {
            EnsureFolder(folder);

            Mesh built = BuildMesh(p);
            string meshPath = folder + "/" + name + "_Mesh.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = built;
                mesh.name = name + "_Mesh";
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                // 既存アセットの中身だけ差し替える。作り直すと GUID が変わり、
                // プレハブやシーンからの参照が切れてメッシュが消えるため
                mesh.Clear();
                mesh.vertices = built.vertices;
                mesh.normals = built.normals;
                mesh.uv = built.uv;
                mesh.triangles = built.triangles;
                mesh.RecalculateBounds();
                mesh.name = name + "_Mesh";
                Object.DestroyImmediate(built);
                EditorUtility.SetDirty(mesh);
            }

            string texPath = folder + "/HousePalette.png";
            string emiPath = folder + "/HouseEmission.png";
            WritePalette(texPath, false);
            WritePalette(emiPath, true);
            Texture2D texAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            Texture2D emiAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(emiPath);

            string matPath = folder + "/HouseFlat.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetFloat("_Smoothness", 0.05f);
            mat.mainTexture = texAsset;
            // 窓だけが光る発光マップ。UV0 を共用するのでマテリアルは1枚のまま
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetTexture("_EmissionMap", emiAsset);
            mat.SetColor("_EmissionColor", Color.black); // 初期値は消灯。点灯は実行時に制御する
            EditorUtility.SetDirty(mat);

            GameObject go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            string prefabPath = folder + "/" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();

            // 保存直後は Unity 内部のキャッシュが古く、プレハブから見た sharedMesh が
            // null のままになる。強制再インポートして参照を確定させる
            AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            Debug.Log("[HouseMeshGenerator] " + name + ": " + (mesh.triangles.Length / 3)
                      + " tris, " + mesh.vertexCount + " verts -> " + prefabPath);
            return prefab;
        }

        private static Mesh BuildMesh(HouseParams p)
        {
            _v = new List<Vector3>();
            _n = new List<Vector3>();
            _uv = new List<Vector2>();
            _t = new List<int>();

            float hw = p.width * 0.5f;
            float hd = p.depth * 0.5f;
            float wt = p.wallHeight;                // 壁の上端
            float peak = p.wallHeight + p.roofRise; // 棟の高さ

            BuildWalls(hw, hd, wt, peak);
            BuildRoof(p, hw, hd, wt, peak);
            BuildPosts(p, hw, hd, wt);
            BuildChimney(p, hd, peak);
            BuildDoor(hd, wt);
            BuildWindows(hw, hd, wt);

            Mesh mesh = new Mesh();
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- 面の追加ヘルパ ----
        // 面ごとに頂点を持つことでフラットな塗り分けを保つ。
        // outward に「その面が向くべき方向」を渡すと、頂点順が裏返っていても自動で直す。
        // 手で並び順を組むと必ずどこかで裏返るため、ここで機械的に担保する。

        private static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int swatch, Vector3 outward)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(nrm, outward) < 0f)
            {
                Vector3 tmp = b; b = d; d = tmp;   // 巻き順を反転
                nrm = Vector3.Cross(b - a, c - a);
            }
            nrm = nrm.normalized;
            int i = _v.Count;
            Vector2 uv = SwatchUV(swatch);
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) { _n.Add(nrm); _uv.Add(uv); }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        private static void Tri(Vector3 a, Vector3 b, Vector3 c, int swatch, Vector3 outward)
        {
            Vector3 nrm = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(nrm, outward) < 0f)
            {
                Vector3 tmp = b; b = c; c = tmp;
                nrm = Vector3.Cross(b - a, c - a);
            }
            nrm = nrm.normalized;
            int i = _v.Count;
            Vector2 uv = SwatchUV(swatch);
            _v.Add(a); _v.Add(b); _v.Add(c);
            for (int k = 0; k < 3; k++) { _n.Add(nrm); _uv.Add(uv); }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        /// <summary>底面は接地して見えないので既定では作らない（ポリゴン節約）。</summary>
        private static void Box(Vector3 center, Vector3 size, int swatch, bool withBottom)
        {
            Vector3 h = size * 0.5f;
            Vector3 c = center;
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = c + new Vector3(h.x, -h.y, -h.z);
            Vector3 p101 = c + new Vector3(h.x, -h.y, h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y, h.z);
            Vector3 p010 = c + new Vector3(-h.x, h.y, -h.z);
            Vector3 p110 = c + new Vector3(h.x, h.y, -h.z);
            Vector3 p111 = c + new Vector3(h.x, h.y, h.z);
            Vector3 p011 = c + new Vector3(-h.x, h.y, h.z);

            Quad(p001, p101, p111, p011, swatch, Vector3.forward);
            Quad(p100, p000, p010, p110, swatch, Vector3.back);
            Quad(p101, p100, p110, p111, swatch, Vector3.right);
            Quad(p000, p001, p011, p010, swatch, Vector3.left);
            Quad(p011, p111, p110, p010, swatch, Vector3.up);
            if (withBottom) Quad(p000, p100, p101, p001, swatch, Vector3.down);
        }

        // ---- 各パーツ ----

        private static void BuildWalls(float hw, float hd, float wt, float peak)
        {
            // 側面（左右）は矩形
            Quad(new Vector3(hw, 0, -hd), new Vector3(hw, 0, hd),
                 new Vector3(hw, wt, hd), new Vector3(hw, wt, -hd), SwWall, Vector3.right);
            Quad(new Vector3(-hw, 0, hd), new Vector3(-hw, 0, -hd),
                 new Vector3(-hw, wt, -hd), new Vector3(-hw, wt, hd), SwWall, Vector3.left);

            // 正面・背面は矩形＋切妻の三角
            Quad(new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd),
                 new Vector3(hw, wt, hd), new Vector3(-hw, wt, hd), SwWall, Vector3.forward);
            Tri(new Vector3(-hw, wt, hd), new Vector3(hw, wt, hd),
                new Vector3(0, peak, hd), SwWall, Vector3.forward);

            Quad(new Vector3(hw, 0, -hd), new Vector3(-hw, 0, -hd),
                 new Vector3(-hw, wt, -hd), new Vector3(hw, wt, -hd), SwWall, Vector3.back);
            Tri(new Vector3(hw, wt, -hd), new Vector3(-hw, wt, -hd),
                new Vector3(0, peak, -hd), SwWall, Vector3.back);
        }

        private static void BuildRoof(HouseParams p, float hw, float hd, float wt, float peak)
        {
            float ez = hd + p.gableOut;   // 破風側の端
            float ex = hw + p.eaveOut;    // 軒先の端
            float ey = wt - 0.02f;        // 軒先の高さ（壁より少し下げて庇らしく）
            float th = p.roofThick;
            Vector3 dn = new Vector3(0, -th, 0);

            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? 1f : -1f;
                Vector3 rF = new Vector3(0, peak, ez);
                Vector3 rB = new Vector3(0, peak, -ez);
                Vector3 eF = new Vector3(sx * ex, ey, ez);
                Vector3 eB = new Vector3(sx * ex, ey, -ez);
                Vector3 rF2 = rF + dn, rB2 = rB + dn, eF2 = eF + dn, eB2 = eB + dn;

                Vector3 upOut = new Vector3(sx, 1f, 0f);   // 勾配面は上＋外向き
                Vector3 dnOut = new Vector3(-sx, -1f, 0f);
                Vector3 sideOut = new Vector3(sx, 0f, 0f);

                Quad(rB, rF, eF, eB, SwRoof, upOut);          // 上面
                Quad(eB2, eF2, rF2, rB2, SwRoofSide, dnOut);  // 裏面
                Quad(eF, eF2, eB2, eB, SwRoofSide, sideOut);  // 軒先の小口
                Quad(rF, eF, eF2, rF2, SwRoofSide, Vector3.forward); // 破風（前）
                Quad(eB, eB2, rB2, rB, SwRoofSide, Vector3.back);    // 破風（後）
            }
        }

        private static void BuildPosts(HouseParams p, float hw, float hd, float wt)
        {
            float s = p.postSize;
            float y = wt * 0.5f;
            // 壁からわずかに外へ出して、柱が浮き出て見えるようにする
            float ox = hw - s * 0.25f;
            float oz = hd - s * 0.25f;
            Box(new Vector3(ox, y, oz), new Vector3(s, wt, s), SwPost, false);
            Box(new Vector3(-ox, y, oz), new Vector3(s, wt, s), SwPost, false);
            Box(new Vector3(ox, y, -oz), new Vector3(s, wt, s), SwPost, false);
            Box(new Vector3(-ox, y, -oz), new Vector3(s, wt, s), SwPost, false);
        }

        private static void BuildChimney(HouseParams p, float hd, float peak)
        {
            float w = 0.085f;
            float top = peak + 0.13f;
            float z = hd * 0.42f;
            // 屋根を貫くので、下端は壁の高さまで伸ばして隙間を作らない
            float bottom = p.wallHeight * 0.5f;
            Box(new Vector3(0.10f, (top + bottom) * 0.5f, -z),
                new Vector3(w, top - bottom, w), SwChimney, false);
        }

        private static void BuildDoor(float hd, float wt)
        {
            float dw = 0.058f;
            // アーチは付けず、以前カーブが始まっていた高さまでの矩形にする
            float dh = wt * 0.80f * 0.68f;
            float z = hd + 0.006f;   // 壁から少し手前に出して面が重ならないようにする

            Quad(new Vector3(-dw, 0, z), new Vector3(dw, 0, z),
                 new Vector3(dw, dh, z), new Vector3(-dw, dh, z),
                 SwDoor, Vector3.forward);
        }

        /// <summary>枠とガラスの2枚重ねで窓を1つ置く。right/up はその壁面上の向き。</summary>
        private static void AddWindow(Vector3 center, Vector3 right, Vector3 up,
                                      float halfW, float halfH, Vector3 outward)
        {
            Vector3 o = outward.normalized * 0.006f;
            Vector3 fc = center;
            Quad(fc - right * halfW - up * halfH, fc + right * halfW - up * halfH,
                 fc + right * halfW + up * halfH, fc - right * halfW + up * halfH,
                 SwFrame, outward);

            const float g = 0.68f;   // ガラスは枠より一回り小さく、さらに手前へ
            Vector3 gc = center + o;
            Quad(gc - right * halfW * g - up * halfH * g, gc + right * halfW * g - up * halfH * g,
                 gc + right * halfW * g + up * halfH * g, gc - right * halfW * g + up * halfH * g,
                 SwGlass, outward);
        }

        private static void BuildWindows(float hw, float hd, float wt)
        {
            float y = wt * 0.60f;
            float o = 0.006f;

            // 側面の窓。扉を正面として右側(+X)だけ大きくする
            float sq = 0.055f;
            float sqBig = 0.088f;
            AddWindow(new Vector3(hw + o, y, 0), Vector3.forward, Vector3.up, sqBig, sqBig, Vector3.right);
            AddWindow(new Vector3(-(hw + o), y, 0), Vector3.forward, Vector3.up, sq, sq, Vector3.left);

            // 正面は扉の横に1つ
            AddWindow(new Vector3(hw * 0.52f, y, hd + o), Vector3.right, Vector3.up, sq, sq, Vector3.forward);

            // 背面は何も無かったので、横長の窓を2つ追加する
            float bw = 0.095f, bh = 0.042f;
            float bx = hw * 0.45f;
            AddWindow(new Vector3(bx, y, -(hd + o)), Vector3.right, Vector3.up, bw, bh, Vector3.back);
            AddWindow(new Vector3(-bx, y, -(hd + o)), Vector3.right, Vector3.up, bw, bh, Vector3.back);
        }

        // ---- パレット ----

        private static Vector2 SwatchUV(int index)
        {
            int col = index % GridCols;
            int row = index / GridCols;
            int size = GridCols * SwatchPx;
            // マスの中心を指す。Point フィルタなので隣の色を拾わない
            float u = (col * SwatchPx + SwatchPx * 0.5f) / size;
            float v = 1f - (row * SwatchPx + SwatchPx * 0.5f) / size;
            return new Vector2(u, v);
        }

        /// <summary>パレットPNGを書き出す。emissive=true なら窓ガラス以外を黒にした発光用。</summary>
        private static void WritePalette(string path, bool emissive)
        {
            if (File.Exists(path)) return;
            Texture2D tex = BuildPalette(emissive);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
            // マスの境界で色が混ざらないよう、補間と圧縮とミップを切る
            ti.filterMode = FilterMode.Point;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 64;
            if (emissive) ti.sRGBTexture = true;
            ti.SaveAndReimport();
        }

        private static Texture2D BuildPalette(bool emissive)
        {
            int size = GridCols * SwatchPx;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int col = x / SwatchPx;
                    int row = (size - 1 - y) / SwatchPx;
                    int idx = row * GridCols + col;
                    Color c;
                    if (emissive)
                        // 窓ガラスのマスだけ灯りの色を置き、他は真っ黒にして光らせない
                        c = idx == SwGlass ? new Color(1.00f, 0.82f, 0.45f, 1f) : Color.black;
                    else
                        c = idx < Palette.Length ? Palette[idx] : Color.magenta;
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
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
