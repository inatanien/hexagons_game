// 役割: 家を手続き的にメッシュ生成する。形・ポリゴン数・向きを完全に制御でき、
//       屋根の色や寸法を変えたバリエーションを同じ仕組みから量産できる。
//       色は1枚のパレットテクスチャをUVで参照させるため、何種類作ってもマテリアルは1枚のまま。
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ElfVillage.Editor
{
    public enum RoofType
    {
        Gable,    // 切妻（棟がZ軸に走る三角屋根）
        Pyramid,  // 四角錐（塔向け）
    }

    /// <summary>家1種類ぶんの指定。寸法・色・付属物をここでまとめて決める。</summary>
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

        public RoofType roofType;
        public bool hasChimney;
        public int windowRows;     // 側面と背面の窓を何段置くか
        public int sideWindows;    // 側面1枚あたりの窓の数
        public int windowSeed;     // 窓の位置と大きさのばらつきを決める種

        public int swRoof;         // 屋根の上面
        public int swRoofSide;     // 屋根の小口・裏面（一段暗い色）
        public int swWall;
        public int swPost;

        /// <summary>標準の家。他のバリエーションはこれを基準に差分だけ変える。</summary>
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
                p.roofType = RoofType.Gable;
                p.hasChimney = true;
                p.windowRows = 1;
                p.sideWindows = 1;
                p.windowSeed = 1;
                p.swRoof = HouseMeshGenerator.SwRoofBlue;
                p.swRoofSide = HouseMeshGenerator.SwRoofBlueSide;
                p.swWall = HouseMeshGenerator.SwWallCream;
                p.swPost = HouseMeshGenerator.SwPostBrown;
                return p;
            }
        }
    }

    public static class HouseMeshGenerator
    {
        // パレット上の色番号。UVはこの番号のマスの中心を指す
        public const int SwWallCream = 0, SwPostBrown = 1, SwRoofBlue = 2, SwRoofBlueSide = 3;
        public const int SwChimney = 4, SwDoor = 5, SwGlass = 6, SwFrame = 7;
        public const int SwRoofRed = 8, SwRoofRedSide = 9, SwRoofMoss = 10, SwRoofMossSide = 11;
        public const int SwRoofOchre = 12, SwRoofOchreSide = 13, SwRoofWood = 14, SwRoofWoodSide = 15;
        public const int SwRoofSlate = 16, SwRoofSlateSide = 17, SwWallWood = 18, SwWallStone = 19;

        private static readonly Color[] Palette =
        {
            new Color32(0xEF, 0xE3, 0xC4, 0xFF), //  0 壁（クリーム色の漆喰）
            new Color32(0x9A, 0x63, 0x35, 0xFF), //  1 柱（温かみのある茶）
            new Color32(0x1F, 0x63, 0xD2, 0xFF), //  2 屋根 青
            new Color32(0x18, 0x4F, 0xA8, 0xFF), //  3 屋根 青（小口）
            new Color32(0xDC, 0xCF, 0xAC, 0xFF), //  4 煙突（明るい石材）
            new Color32(0x7B, 0x4A, 0x22, 0xFF), //  5 扉
            new Color32(0x6F, 0xA8, 0xDC, 0xFF), //  6 窓ガラス
            new Color32(0x8B, 0x55, 0x2A, 0xFF), //  7 窓枠
            new Color32(0xC8, 0x3E, 0x36, 0xFF), //  8 屋根 赤
            new Color32(0xA0, 0x2E, 0x28, 0xFF), //  9 屋根 赤（小口）
            new Color32(0x5E, 0x8C, 0x42, 0xFF), // 10 屋根 苔緑
            new Color32(0x47, 0x6D, 0x32, 0xFF), // 11 屋根 苔緑（小口）
            new Color32(0xE0, 0x9B, 0x35, 0xFF), // 12 屋根 黄土
            new Color32(0xB5, 0x7A, 0x26, 0xFF), // 13 屋根 黄土（小口）
            new Color32(0x6B, 0x4A, 0x30, 0xFF), // 14 屋根 焦茶（納屋）
            new Color32(0x53, 0x39, 0x25, 0xFF), // 15 屋根 焦茶（小口）
            new Color32(0x74, 0x7C, 0x88, 0xFF), // 16 屋根 石板（塔）
            new Color32(0x5A, 0x61, 0x6B, 0xFF), // 17 屋根 石板（小口）
            new Color32(0xB8, 0x8A, 0x55, 0xFF), // 18 壁（板張り・納屋）
            new Color32(0xC9, 0xC4, 0xB6, 0xFF), // 19 壁（石積み・塔）
        };

        private const string SmokePrefabPath = "Assets/_Game/VFX/Common/ChimneySmoke.prefab";

        /// <summary>煙突の先端。煙エフェクトの取り付け位置に使う。BuildChimney と同じ式。</summary>
        public static Vector3 ChimneyTop(HouseParams p)
        {
            float hw = p.width * 0.5f;
            float hd = p.depth * 0.5f;
            float top = p.wallHeight + p.roofRise + 0.13f;
            return new Vector3(hw * 0.32f, top, -hd * 0.42f);
        }

        private const int SwatchPx = 16;   // 1色あたりのピクセル数
        private const int GridCols = 8;    // パレットの列数（8×8＝64マス）

        private static List<Vector3> _v;
        private static List<Vector3> _n;
        private static List<Vector2> _uv;
        private static List<int> _t;

        [MenuItem("Tools/精霊樹の森/家メッシュを生成（全6種）")]
        public static void GenerateAll()
        {
            // 煙を家の子として組み込むので、先に用意しておく
            if (AssetDatabase.LoadAssetAtPath<GameObject>(SmokePrefabPath) == null)
                ChimneySmokeGenerator.Generate();

            foreach (var kv in Variants())
                Generate(kv.Value, kv.Key);
            AssetDatabase.SaveAssets();
            Debug.Log("[HouseMeshGenerator] 全6種の生成が完了しました。");
        }

        /// <summary>6種類の指定。標準からの差分だけ書く。</summary>
        public static Dictionary<string, HouseParams> Variants()
        {
            var list = new Dictionary<string, HouseParams>();

            // 1) 標準：青屋根の家
            list["House_Blue"] = HouseParams.Default;

            // 2) 赤屋根の家（形は標準と同じ、色だけ差し替え）
            var red = HouseParams.Default;
            red.swRoof = SwRoofRed; red.swRoofSide = SwRoofRedSide;
            red.sideWindows = 2; red.windowSeed = 2;   // 青屋根と形が同じなので窓で差を付ける
            list["House_Red"] = red;

            // 3) 苔屋根の小屋：小さく低く、煙突なし。盤面の隙間を埋める役
            var hut = HouseParams.Default;
            hut.width = 0.44f; hut.depth = 0.50f;
            hut.wallHeight = 0.30f; hut.roofRise = 0.20f;
            hut.eaveOut = 0.06f; hut.gableOut = 0.05f;
            hut.postSize = 0.045f;
            hut.hasChimney = false;
            hut.swRoof = SwRoofMoss; hut.swRoofSide = SwRoofMossSide;
            hut.windowSeed = 3;
            list["Hut_Moss"] = hut;

            // 4) 背の高い家：間口を狭め壁を高く。窓を上下2段にして二階建てに見せる
            var tall = HouseParams.Default;
            tall.width = 0.50f; tall.depth = 0.58f;
            tall.wallHeight = 0.64f; tall.roofRise = 0.32f;
            tall.eaveOut = 0.06f;
            tall.windowRows = 2;
            tall.swRoof = SwRoofOchre; tall.swRoofSide = SwRoofOchreSide;
            tall.windowSeed = 4;
            list["House_Tall"] = tall;

            // 5) 横長の納屋：幅広で低く、屋根が大きい。壁は板張り
            var barn = HouseParams.Default;
            barn.width = 0.92f; barn.depth = 0.60f;
            barn.wallHeight = 0.34f; barn.roofRise = 0.30f;
            barn.eaveOut = 0.10f; barn.gableOut = 0.07f;
            barn.postSize = 0.065f;
            barn.hasChimney = false;
            barn.swRoof = SwRoofWood; barn.swRoofSide = SwRoofWoodSide;
            barn.swWall = SwWallWood;
            barn.sideWindows = 2; barn.windowSeed = 5;  // 側面が長いので2枚並べる
            list["Barn_Wide"] = barn;

            // 6) 塔：細く高く、四角錐の屋根。窓は3段。盤面の目印になる強いシルエット
            var tower = HouseParams.Default;
            tower.width = 0.36f; tower.depth = 0.36f;
            tower.wallHeight = 0.92f; tower.roofRise = 0.26f;
            tower.eaveOut = 0.055f; tower.gableOut = 0.055f;
            tower.postSize = 0.05f;
            tower.roofType = RoofType.Pyramid;
            tower.hasChimney = false;
            tower.windowRows = 3;
            tower.swRoof = SwRoofSlate; tower.swRoofSide = SwRoofSlateSide;
            tower.swWall = SwWallStone;
            tower.windowSeed = 6;
            list["Tower_Stone"] = tower;

            return list;
        }

        /// <summary>メッシュ・マテリアル・パレット・プレハブを書き出す。</summary>
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
            EditorUtility.SetDirty(mat);

            GameObject go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            // 煙は家プレハブの子として持たせる。ここで組み込んでおけば、
            // 家を再生成しても煙が消えない
            if (p.hasChimney)
            {
                var smokePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SmokePrefabPath);
                if (smokePrefab != null)
                {
                    var smoke = (GameObject)PrefabUtility.InstantiatePrefab(smokePrefab, go.transform);
                    smoke.transform.localPosition = ChimneyTop(p) - new Vector3(0f, 0.01f, 0f);
                }
                else
                {
                    Debug.LogWarning("[HouseMeshGenerator] 煙プレハブが無いため " + name
                                     + " に煙を付けませんでした: " + SmokePrefabPath);
                }
            }

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
                      + " tris, " + mesh.vertexCount + " verts");
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
            float wt = p.wallHeight;
            float peak = p.wallHeight + p.roofRise;

            BuildWalls(p, hw, hd, wt, peak);
            if (p.roofType == RoofType.Gable) BuildGableRoof(p, hw, hd, wt, peak);
            else BuildPyramidRoof(p, hw, hd, wt, peak);
            BuildPosts(p, hw, hd, wt);
            if (p.hasChimney) BuildChimney(p, hw, hd, peak);
            BuildDoor(p, hd, wt);
            BuildWindows(p, hw, hd, wt);

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

        private static void BuildWalls(HouseParams p, float hw, float hd, float wt, float peak)
        {
            int w = p.swWall;

            Quad(new Vector3(hw, 0, -hd), new Vector3(hw, 0, hd),
                 new Vector3(hw, wt, hd), new Vector3(hw, wt, -hd), w, Vector3.right);
            Quad(new Vector3(-hw, 0, hd), new Vector3(-hw, 0, -hd),
                 new Vector3(-hw, wt, -hd), new Vector3(-hw, wt, hd), w, Vector3.left);
            Quad(new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd),
                 new Vector3(hw, wt, hd), new Vector3(-hw, wt, hd), w, Vector3.forward);
            Quad(new Vector3(hw, 0, -hd), new Vector3(-hw, 0, -hd),
                 new Vector3(-hw, wt, -hd), new Vector3(hw, wt, -hd), w, Vector3.back);

            // 切妻のときだけ、前後の壁に三角を足して棟まで塞ぐ
            if (p.roofType == RoofType.Gable)
            {
                Tri(new Vector3(-hw, wt, hd), new Vector3(hw, wt, hd),
                    new Vector3(0, peak, hd), w, Vector3.forward);
                Tri(new Vector3(hw, wt, -hd), new Vector3(-hw, wt, -hd),
                    new Vector3(0, peak, -hd), w, Vector3.back);
            }
        }

        private static void BuildGableRoof(HouseParams p, float hw, float hd, float wt, float peak)
        {
            float ez = hd + p.gableOut;
            float ex = hw + p.eaveOut;
            float ey = wt - 0.02f;
            Vector3 dn = new Vector3(0, -p.roofThick, 0);

            for (int s = 0; s < 2; s++)
            {
                float sx = s == 0 ? 1f : -1f;
                Vector3 rF = new Vector3(0, peak, ez);
                Vector3 rB = new Vector3(0, peak, -ez);
                Vector3 eF = new Vector3(sx * ex, ey, ez);
                Vector3 eB = new Vector3(sx * ex, ey, -ez);
                Vector3 rF2 = rF + dn, rB2 = rB + dn, eF2 = eF + dn, eB2 = eB + dn;

                Vector3 upOut = new Vector3(sx, 1f, 0f);
                Vector3 dnOut = new Vector3(-sx, -1f, 0f);
                Vector3 sideOut = new Vector3(sx, 0f, 0f);

                Quad(rB, rF, eF, eB, p.swRoof, upOut);
                Quad(eB2, eF2, rF2, rB2, p.swRoofSide, dnOut);
                Quad(eF, eF2, eB2, eB, p.swRoofSide, sideOut);
                Quad(rF, eF, eF2, rF2, p.swRoofSide, Vector3.forward);
                Quad(eB, eB2, rB2, rB, p.swRoofSide, Vector3.back);
            }
        }

        /// <summary>四角錐の屋根。塔のように四方が同じ形の建物に使う。</summary>
        private static void BuildPyramidRoof(HouseParams p, float hw, float hd, float wt, float peak)
        {
            float ex = hw + p.eaveOut;
            float ez = hd + p.gableOut;
            float ey = wt - 0.02f;
            float th = p.roofThick;

            Vector3 apex = new Vector3(0, peak, 0);
            Vector3 apex2 = apex + new Vector3(0, -th, 0);
            Vector3[] e =
            {
                new Vector3( ex, ey,  ez),
                new Vector3(-ex, ey,  ez),
                new Vector3(-ex, ey, -ez),
                new Vector3( ex, ey, -ez),
            };
            Vector3[] e2 = new Vector3[4];
            for (int i = 0; i < 4; i++) e2[i] = e[i] + new Vector3(0, -th, 0);

            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vector3 mid = (e[i] + e[j]) * 0.5f;
                Vector3 outward = new Vector3(mid.x, 0.6f, mid.z).normalized;
                Tri(e[i], e[j], apex, p.swRoof, outward);          // 上面
                Tri(e2[j], e2[i], apex2, p.swRoofSide, -outward);  // 裏面
                Quad(e[i], e2[i], e2[j], e[j], p.swRoofSide,
                     new Vector3(mid.x, 0f, mid.z).normalized);    // 軒先の小口
            }
        }

        private static void BuildPosts(HouseParams p, float hw, float hd, float wt)
        {
            float s = p.postSize;
            float y = wt * 0.5f;
            float ox = hw - s * 0.25f;
            float oz = hd - s * 0.25f;
            Box(new Vector3(ox, y, oz), new Vector3(s, wt, s), p.swPost, false);
            Box(new Vector3(-ox, y, oz), new Vector3(s, wt, s), p.swPost, false);
            Box(new Vector3(ox, y, -oz), new Vector3(s, wt, s), p.swPost, false);
            Box(new Vector3(-ox, y, -oz), new Vector3(s, wt, s), p.swPost, false);
        }

        private static void BuildChimney(HouseParams p, float hw, float hd, float peak)
        {
            float w = Mathf.Min(0.085f, p.width * 0.16f);
            float top = peak + 0.13f;
            float bottom = p.wallHeight * 0.5f;
            // 屋根を貫くので、下端は壁の途中まで伸ばして隙間を作らない
            Box(new Vector3(hw * 0.32f, (top + bottom) * 0.5f, -hd * 0.42f),
                new Vector3(w, top - bottom, w), SwChimney, false);
        }

        private static void BuildDoor(HouseParams p, float hd, float wt)
        {
            // 扉は建物の大きさに関係なく全種で共通。人が通る寸法は変わらないため
            const float dw = 0.058f;
            const float dh = 0.218f;
            float z = hd + 0.006f;
            Quad(new Vector3(-dw, 0, z), new Vector3(dw, 0, z),
                 new Vector3(dw, dh, z), new Vector3(-dw, dh, z),
                 SwDoor, Vector3.forward);
        }

        /// <summary>枠とガラスの2枚重ねで窓を1つ置く。right/up はその壁面上の向き。</summary>
        private static void AddWindow(Vector3 center, Vector3 right, Vector3 up,
                                      float halfW, float halfH, Vector3 outward)
        {
            Vector3 o = outward.normalized * 0.006f;
            Quad(center - right * halfW - up * halfH, center + right * halfW - up * halfH,
                 center + right * halfW + up * halfH, center - right * halfW + up * halfH,
                 SwFrame, outward);

            const float g = 0.68f;   // ガラスは枠より一回り小さく、さらに手前へ
            Vector3 gc = center + o;
            Quad(gc - right * halfW * g - up * halfH * g, gc + right * halfW * g - up * halfH * g,
                 gc + right * halfW * g + up * halfH * g, gc - right * halfW * g + up * halfH * g,
                 SwGlass, outward);
        }

        /// <summary>種から決まる決定的な擬似乱数。再生成しても毎回同じ配置になる。</summary>
        private static float Rand(int seed, int salt, float min, float max)
        {
            unchecked
            {
                uint h = (uint)(seed * 73856093) ^ (uint)(salt * 19349663);
                h ^= h >> 13; h *= 0x85ebca6b; h ^= h >> 16;
                return min + (max - min) * ((h & 0xFFFFFF) / (float)0xFFFFFF);
            }
        }

        /// <summary>壁の上に置く窓1枚ぶん。u は壁に沿った位置、v は高さ。</summary>
        private struct WinRect
        {
            public float u, v, hu, hv;
            public WinRect(float u, float v, float hu, float hv)
            { this.u = u; this.v = v; this.hu = hu; this.hv = hv; }
        }

        /// <summary>枠が重なった窓は、大きいほうだけを残して1枚にまとめる。</summary>
        private static List<WinRect> MergeOverlaps(List<WinRect> list)
        {
            // 面積の大きい順に確定させ、既に置いた窓と重なるものは捨てる
            list.Sort((a, b) => (b.hu * b.hv).CompareTo(a.hu * a.hv));
            var kept = new List<WinRect>();
            foreach (var w in list)
            {
                bool hit = false;
                for (int i = 0; i < kept.Count; i++)
                {
                    var k = kept[i];
                    if (Mathf.Abs(w.u - k.u) < w.hu + k.hu && Mathf.Abs(w.v - k.v) < w.hv + k.hv)
                    { hit = true; break; }
                }
                if (!hit) kept.Add(w);
            }
            return kept;
        }

        private static void EmitWall(List<WinRect> list, Vector3 planePoint,
                                     Vector3 rightAxis, Vector3 outward)
        {
            foreach (var w in MergeOverlaps(list))
                AddWindow(planePoint + rightAxis * w.u + Vector3.up * w.v,
                          rightAxis, Vector3.up, w.hu, w.hv, outward);
        }

        private static void BuildWindows(HouseParams p, float hw, float hd, float wt)
        {
            const float o = 0.006f;
            int rows = Mathf.Max(1, p.windowRows);
            int count = Mathf.Max(1, p.sideWindows);
            float baseSq = Mathf.Min(0.055f, wt / (rows * 2.6f));
            int seed = p.windowSeed;
            int salt = 0;

            // 隅柱に食い込まないよう、窓を置ける範囲を先に決めておく
            float zRoom = hd - p.postSize * 0.9f;
            float xRoom = hw - p.postSize * 0.9f;

            var right = new List<WinRect>();
            var left  = new List<WinRect>();
            var back  = new List<WinRect>();
            var front = new List<WinRect>();

            for (int r = 0; r < rows; r++)
            {
                float y = wt * (r + 0.5f) / rows;

                // 側面（左右）。左右で位置も大きさも別々にずらす
                for (int side = 0; side < 2; side++)
                {
                    // 扉を正面として右(+X)は一回り大きめに保つ
                    float bias = side == 0 ? 1.30f : 1.0f;
                    var target = side == 0 ? right : left;
                    for (int k = 0; k < count; k++)
                    {
                        float anchor = count == 1 ? 0f : (k == 0 ? -zRoom * 0.45f : zRoom * 0.45f);
                        float z = anchor + Rand(seed, ++salt, -0.16f, 0.16f) * hd;
                        float half = baseSq * bias * Rand(seed, ++salt, 0.80f, 1.20f);
                        // 壁からはみ出さないよう、位置に応じて大きさを詰める
                        half = Mathf.Min(half, Mathf.Max(0.018f, zRoom - Mathf.Abs(z)));
                        z = Mathf.Clamp(z, -(zRoom - half), zRoom - half);
                        target.Add(new WinRect(z, y, half, half * Rand(seed, ++salt, 0.85f, 1.15f)));
                    }
                }

                // 背面の横長窓2つ。左右で位置も寸法も変える
                for (int k = 0; k < 2; k++)
                {
                    float anchor = (k == 0 ? -1f : 1f) * xRoom * 0.45f;
                    float x = anchor + Rand(seed, ++salt, -0.18f, 0.18f) * hw;
                    float halfW = Mathf.Min(0.095f, hw * 0.40f) * Rand(seed, ++salt, 0.65f, 1.15f);
                    halfW = Mathf.Min(halfW, Mathf.Max(0.018f, xRoom - Mathf.Abs(x)));
                    x = Mathf.Clamp(x, -(xRoom - halfW), xRoom - halfW);
                    float halfH = baseSq * 0.72f * Rand(seed, ++salt, 0.80f, 1.25f);
                    back.Add(new WinRect(x, y, halfW, halfH));
                }

                // 正面は扉があるので、2段目以降にだけ窓を置く
                if (r > 0)
                    front.Add(new WinRect(Rand(seed, ++salt, -0.2f, 0.2f) * hw, y, baseSq, baseSq));
            }

            // 1段のときは扉の横にも1つ添える
            if (rows == 1)
                front.Add(new WinRect(hw * 0.52f, wt * 0.60f, baseSq, baseSq));

            EmitWall(right, new Vector3(hw + o, 0, 0), Vector3.forward, Vector3.right);
            EmitWall(left, new Vector3(-(hw + o), 0, 0), Vector3.forward, Vector3.left);
            EmitWall(back, new Vector3(0, 0, -(hd + o)), Vector3.right, Vector3.back);
            EmitWall(front, new Vector3(0, 0, hd + o), Vector3.right, Vector3.forward);
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
            ti.maxTextureSize = 128;
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
                        c = idx < Palette.Length ? Palette[idx] : Color.black;
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
