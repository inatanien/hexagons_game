// 役割: HexTile.ApplyRiverChannelMesh（川底専用マテリアルの生成）のテスト。
//       renderer.material呼び出しがEditMode下ではマテリアルリークConsole Errorで
//       テスト失敗扱いになるため、既存のHexTileElementPropsTests等と同じくPlayModeで実施する。
//       川タイルの地面にgroundTextureが設定されている状態で、隣接接続による
//       RefreshRiverChannelMesh後も水路スロットへ地面テクスチャが混入しないことを検証する。

using System.Reflection;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class HexTileRiverChannelMaterialTests
    {
        // ── テストヘルパー ────────────────────────────────────────────
        // meshFilter/meshRenderer/meshCollider はprefab上でInspector配線される前提の
        // private SerializeFieldのため、prefabを使わないテストではreflectionで直接注入する
        // （既存のHexTileElementPropsTests等と同じ手法）。

        private static HexTile MakeRiverCapableTile(HexCoord coord)
        {
            var go = new GameObject("RiverTestTile");
            var meshFilter   = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var meshCollider = go.AddComponent<MeshCollider>();

            var tile = go.AddComponent<HexTile>();
            var type = typeof(HexTile);
            SetField(type, tile, "meshFilter",   meshFilter);
            SetField(type, tile, "meshRenderer", meshRenderer);
            SetField(type, tile, "meshCollider", meshCollider);
            SetField(type, tile, "tileRenderer", meshRenderer);

            tile.Initialize(coord, 1f);
            return tile;
        }

        private static void SetField(System.Type type, object target, string fieldName, object value)
        {
            var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(target, value);
        }

        private static TileType MakeRiverType(Texture2D groundTexture)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.propType      = TilePropType.Water;
            t.tileColor      = Color.white; // 地面色統一後の実際の値と揃える
            t.groundTexture  = groundTexture;
            return t;
        }

        private static Texture2D MakeGroundTexture() => new Texture2D(2, 2);

        // ── テスト ────────────────────────────────────────────────────

        [Test]
        public void InitialPlace_ChannelSlot_HasNoGroundTexture()
        {
            var riverType = MakeRiverType(MakeGroundTexture());
            var tile      = MakeRiverCapableTile(HexCoord.Zero);

            tile.Place(riverType, 0);

            var mr = tile.GetComponent<MeshRenderer>();
            Assert.AreEqual(2, mr.materials.Length, "川タイルはsubMesh2枚（陸地+水路）になるはず");
            Assert.IsNull(mr.materials[1].mainTexture, "初回配置時点で水路スロットに地面テクスチャが付いてはいけない");
        }

        [Test]
        public void RefreshAfterConnection_ChannelSlot_StillHasNoGroundTexture()
        {
            var groundTex = MakeGroundTexture();
            var riverType = MakeRiverType(groundTex);
            var tile      = MakeRiverCapableTile(HexCoord.Zero);

            tile.Place(riverType, 0);
            var mr = tile.GetComponent<MeshRenderer>();

            // 前提条件確認: ApplyVisual()によりスロット0にはgroundTextureが反映済み。
            Assert.AreEqual(groundTex, mr.materials[0].mainTexture, "前提条件: スロット0にはgroundTextureが反映されているはず");

            Color channelColorBefore = mr.materials[1].color;

            // 隣接川タイル接続時にHexGridManager.CheckAndApplyConnectionsが呼ぶのと同じ経路。
            tile.RefreshRiverChannelMesh();

            var mrAfter = tile.GetComponent<MeshRenderer>();
            Assert.IsNull(mrAfter.materials[1].mainTexture, "接続後の再生成でも水路スロットへ地面テクスチャが混入してはいけない（回帰）");
            Assert.AreEqual(channelColorBefore, mrAfter.materials[1].color, "接続前後で水路の色は変わらないはず");
            Assert.AreEqual(groundTex, mrAfter.materials[0].mainTexture, "スロット0のgroundTextureは維持されるはず");
        }

        [Test]
        public void NonRiverTile_SingleSubMesh_Unaffected()
        {
            var treeType = ScriptableObject.CreateInstance<TileType>();
            treeType.propType = TilePropType.Tree;
            var tile = MakeRiverCapableTile(HexCoord.Zero);

            tile.Place(treeType, 0);

            var mr = tile.GetComponent<MeshRenderer>();
            Assert.AreEqual(1, mr.materials.Length, "非川タイルはsubMesh1枚のまま（水路スロットは追加されない）");
        }
    }
}
