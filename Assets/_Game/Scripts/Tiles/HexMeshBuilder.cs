// 役割: 六角柱メッシュを頂点から生成する静的ユーティリティ。
//       MonoBehaviour に依存せず単体テスト可能。

using UnityEngine;

namespace ElfVillage.Tiles
{
    public static class HexMeshBuilder
    {
        /// <summary>
        /// フラットトップの六角柱メッシュを生成する。
        /// </summary>
        /// <param name="outerRadius">外接円半径（頂点-中心距離）</param>
        /// <param name="height">柱の高さ</param>
        public static Mesh Build(float outerRadius = 0.95f, float height = 0.15f)
        {
            var mesh = new Mesh { name = "HexTile" };

            float h = height * 0.5f;
            // 頂点配列: 上面中心(1) + 上面外周(6) + 下面中心(1) + 下面外周(6) + 側面用上下(12) = 26
            var verts = new Vector3[26];
            var uvs   = new Vector2[26];

            // 上面・下面の頂点生成
            verts[0] = new Vector3(0, h, 0);   // 上面中心
            verts[7] = new Vector3(0, -h, 0);  // 下面中心

            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i); // フラットトップ: 0°スタート
                float x = outerRadius * Mathf.Cos(angle);
                float z = outerRadius * Mathf.Sin(angle);
                verts[1 + i] = new Vector3(x,  h, z); // 上面外周
                verts[8 + i] = new Vector3(x, -h, z); // 下面外周

                // 側面用: 上下それぞれコピー（UV独立のため）
                verts[14 + i]      = new Vector3(x,  h, z); // 側面上
                verts[14 + 6 + i]  = new Vector3(x, -h, z); // 側面下
            }

            // UV
            uvs[0] = new Vector2(0.5f, 0.5f);
            uvs[7] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < 6; i++)
            {
                float angle = Mathf.Deg2Rad * (60f * i);
                uvs[1 + i] = new Vector2(0.5f + 0.5f * Mathf.Cos(angle), 0.5f + 0.5f * Mathf.Sin(angle));
                uvs[8 + i] = uvs[1 + i];
                uvs[14 + i]     = new Vector2((float)i / 6f, 1f);
                uvs[14 + 6 + i] = new Vector2((float)i / 6f, 0f);
            }

            // 三角形インデックス: 上面(6) + 下面(6) + 側面(12) = 24三角形 = 72インデックス
            var tris = new int[72];
            int t = 0;

            // 上面（時計回り）
            for (int i = 0; i < 6; i++)
            {
                tris[t++] = 0;
                tris[t++] = 1 + (i + 1) % 6;
                tris[t++] = 1 + i;
            }

            // 下面（反時計回り）
            for (int i = 0; i < 6; i++)
            {
                tris[t++] = 7;
                tris[t++] = 8 + i;
                tris[t++] = 8 + (i + 1) % 6;
            }

            // 側面（各辺2三角形）
            for (int i = 0; i < 6; i++)
            {
                int cur  = 14 + i;
                int next = 14 + (i + 1) % 6;
                int curB  = 20 + i;
                int nextB = 20 + (i + 1) % 6;

                tris[t++] = cur;
                tris[t++] = curB;
                tris[t++] = next;

                tris[t++] = next;
                tris[t++] = curB;
                tris[t++] = nextB;
            }

            mesh.vertices  = verts;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
