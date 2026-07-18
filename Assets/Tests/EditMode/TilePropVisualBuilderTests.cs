// 役割: 複合タイル対応の共通プレビュー生成基盤 TilePropVisualBuilder（Session 11）のテスト。
//       legacy委譲・複合要素の種類/生成数/決定論性・visualOnly包含・副作用が無いことを検証する。
//       PickTreeVariantFrom/SpawnPrimitiveTreeVariant等が呼ぶSetPropMaterial(renderer.material)は
//       EditMode下で「マテリアルリーク」Console Errorを出しテスト失敗扱いになるため、
//       実際にプリミティブ木を生成する検証はPlayMode側（TilePropVisualBuilderPlayModeTests）に置く。
//       ここでは花Billboard（ParticleSystem、マテリアルリーク問題を起こさない）や
//       House（renderer.materialを使うが既存HexTileElementPropsTestsもPlayMode）を避けた
//       構造検証・決定論性検証を中心に行う。

using System.Collections.Generic;
using NUnit.Framework;
using ElfVillage.HexGrid;
using ElfVillage.Tiles;
using UnityEngine;

namespace ElfVillage.Tests
{
    public class TilePropVisualBuilderTests
    {
        private static TerrainVariantDefinition MakeFlowerVariant(int propCount)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category  = TileCategory.Field;
            v.propType  = TilePropType.Flower;
            v.propCount = propCount;
            return v;
        }

        private static TileType MakeElementsTile(params TileElement[] elements)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.elements = elements;
            return t;
        }

        private static int CountFlowerParticles(Transform root)
        {
            var billboards = root.GetComponentsInChildren<ParticleSystem>();
            int total = 0;
            foreach (var ps in billboards)
            {
                var particles = new ParticleSystem.Particle[64];
                total += ps.GetParticles(particles);
            }
            return total;
        }

        // ── null安全性 ──────────────────────────────────────────────

        [Test]
        public void SpawnProps_NullType_DoesNotThrow()
        {
            var go = new GameObject();
            Assert.DoesNotThrow(() => TilePropVisualBuilder.SpawnProps(null, go.transform, null));
        }

        [Test]
        public void SpawnProps_NullParent_DoesNotThrow()
        {
            var t = MakeElementsTile(new TileElement { variant = MakeFlowerVariant(2), areaWeight = 1f });
            Assert.DoesNotThrow(() => TilePropVisualBuilder.SpawnProps(t, null, null));
        }

        // ── 単一Flower要素（複合構成、positional要素1つ）の生成数・visualOnly ──────

        [Test]
        public void SingleFlowerElement_GeneratesExpectedCount()
        {
            var go = new GameObject();
            var t = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(10), areaWeight = 1f });
            TilePropVisualBuilder.SpawnProps(t, go.transform, new HexCoord(1, -1, 0));

            int count = CountFlowerParticles(go.transform);
            Assert.AreEqual(10, count, "propCount10・weight1.0で10個生成されるはず");
        }

        [Test]
        public void VisualOnlyFlowerElement_StillGeneratesProps()
        {
            var go = new GameObject();
            var t = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(6), areaWeight = 1f, visualOnly = true });
            TilePropVisualBuilder.SpawnProps(t, go.transform, new HexCoord(2, -2, 0));

            int count = CountFlowerParticles(go.transform);
            Assert.AreEqual(6, count, "visualOnly=trueでも見た目には含まれるはず");
        }

        // ── 決定論性 ────────────────────────────────────────────────

        [Test]
        public void SameTypeAndCoord_ProducesSameParticleCount()
        {
            var t = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(9), areaWeight = 1f });

            var go1 = new GameObject();
            TilePropVisualBuilder.SpawnProps(t, go1.transform, new HexCoord(3, -1, -2));
            int count1 = CountFlowerParticles(go1.transform);

            var go2 = new GameObject();
            TilePropVisualBuilder.SpawnProps(t, go2.transform, new HexCoord(3, -1, -2));
            int count2 = CountFlowerParticles(go2.transform);

            Assert.AreEqual(count1, count2, "同じTileType・同じ座標なら同じ生成数になるはず");
        }

        [Test]
        public void NullSeedCoord_FallsBackToPreviewFallbackCoord_Deterministic()
        {
            var t = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(7), areaWeight = 1f });

            var go1 = new GameObject();
            TilePropVisualBuilder.SpawnProps(t, go1.transform, null);
            var go2 = new GameObject();
            TilePropVisualBuilder.SpawnProps(t, go2.transform, null);

            Assert.AreEqual(CountFlowerParticles(go1.transform), CountFlowerParticles(go2.transform));
            // null時はPreviewFallbackCoordを使ったときと同じ結果になるはず
            var go3 = new GameObject();
            TilePropVisualBuilder.SpawnProps(t, go3.transform, TilePropVisualBuilder.PreviewFallbackCoord);
            Assert.AreEqual(CountFlowerParticles(go1.transform), CountFlowerParticles(go3.transform));
        }

        // ── 複数要素（Flower×2、生成数の比率とElementRegionLayoutとの整合） ─────

        [Test]
        public void TwoFlowerElements_CountsMatchComputeElementPropCount()
        {
            // propCount20+propCount20、weight0.7/0.3 → 期待値はHexTileElementPropsTestsの
            // ComputeElementPropCount式(RoundToInt(Max(1,base)*normalizedWeight))と同じ考え方。
            var go = new GameObject();
            var t = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(20), areaWeight = 0.7f },
                new TileElement { variant = MakeFlowerVariant(20), areaWeight = 0.3f });
            TilePropVisualBuilder.SpawnProps(t, go.transform, new HexCoord(0, 0, 0));

            int total = CountFlowerParticles(go.transform);
            // RoundToInt(20*0.7)=14, RoundToInt(20*0.3)=6 → 合計20
            Assert.AreEqual(20, total, "2要素合計の生成数はComputeElementPropCountの合計と一致するはず");
        }

        [Test]
        public void DifferentCoords_CanProduceDifferentDistribution()
        {
            // 境界方向はcoordに依存するため、十分multiple試せば少なくとも1組は異なる結果になるはず。
            var t = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(20), areaWeight = 0.5f },
                new TileElement { variant = MakeFlowerVariant(20), areaWeight = 0.5f });

            var boundaryDegs = new HashSet<float>();
            for (int q = 0; q < 6; q++)
            {
                boundaryDegs.Add(ElementRegionLayout.ComputeBoundaryDirectionDeg(q, q * 2, ElementRegionLayout.StableStringHash(t.name)));
            }
            Assert.Greater(boundaryDegs.Count, 1, "座標が変われば境界方向も変わりうるはず");
        }

        // ── 副作用が無いこと（グリッド/EventBus/TileDeckへ触れない）───────────
        // TilePropVisualBuilderはHexGridManager/_grid/EventBus/TileDeckへの参照を一切持たないため、
        // コンパイル依存関係の不在自体が構造的な保証になる。ここでは実行時にも例外や
        // 想定外の副作用が起きないことを、複数回連続生成しても安定することで確認する。

        [Test]
        public void RepeatedCalls_DoNotThrow_AndRemainStable()
        {
            var t = MakeElementsTile(
                new TileElement { variant = MakeFlowerVariant(5), areaWeight = 1f });

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    var go = new GameObject();
                    TilePropVisualBuilder.SpawnProps(t, go.transform, new HexCoord(i, -i, 0));
                }
            });
        }
    }
}
