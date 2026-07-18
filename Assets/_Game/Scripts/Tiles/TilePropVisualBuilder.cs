// 役割: 複合タイル（TileType.elements）の見た目（Tree/Flower/House/Water）を、
//       実配置用HexTileインスタンスへ依存せずに生成するための静的ビルダー（Session 11）。
//       TilePlacementPreview（配置ゴースト）から呼ばれる。グリッド登録・EventBus通知・
//       TileDeck消費・接続判定等は一切行わない「見た目だけ」の生成経路。
//       legacy単一要素タイル（elements未設定）は、既存のHexTile.SpawnPropsPreviewへそのまま
//       委譲する（コード変更なし・挙動完全維持）。

using System.Collections.Generic;
using UnityEngine;
using ElfVillage.HexGrid;

namespace ElfVillage.Tiles
{
    public static class TilePropVisualBuilder
    {
        /// <summary>座標を取得できない呼び出し元向けの固定シード座標。
        /// 同じTileType・このフォールバックなら常に同じ見た目になる。</summary>
        public static readonly HexCoord PreviewFallbackCoord = new HexCoord(0, 0, 0);

        /// <summary>
        /// TileTypeの見た目（分割線は含まない。プロップのみ）を生成する。
        /// elements[]が無効（legacy単一要素）な場合はHexTile.SpawnPropsPreviewへそのまま委譲する。
        /// </summary>
        /// <param name="type">対象TileType</param>
        /// <param name="parent">生成先の親Transform</param>
        /// <param name="seedCoord">見た目生成シードに使う座標。null時はPreviewFallbackCoordを使用</param>
        /// <param name="tileHeight">タイル高さ（メッシュと合わせる）</param>
        /// <param name="outerRadius">タイル外接半径（メッシュと合わせる）</param>
        public static void SpawnProps(TileType type, Transform parent, HexCoord? seedCoord,
                                       float tileHeight = 0.30f, float outerRadius = 2.0f)
        {
            if (type == null || parent == null) return;

            // legacy/elements分岐はTileType.HasVisualElementsで判定する。
            // HexTile.SpawnPropsForと完全に同じ判定源のため、実配置とプレビューの
            // 分岐条件は常に一致する。
            if (!type.HasVisualElements)
            {
                HexTile.SpawnPropsPreview(type, parent, outerRadius, tileHeight);
                return;
            }

            var elements = new List<TileElement>(type.EffectiveElements);
            HexCoord coord = seedCoord ?? PreviewFallbackCoord;
            SpawnElementProps(elements, type, parent, coord, tileHeight);
        }

        private static void SpawnElementProps(List<TileElement> elements, TileType type,
                                               Transform parent, HexCoord coord, float tileHeight)
        {
            // areaWeightの正規化（HexTile.SpawnPropsForElementsと同じ考え方・同じヘルパー再利用）。
            float weightSum = 0f;
            foreach (var e in elements) weightSum += HexTile.SafeWeight(e.areaWeight);
            bool useEqualSplit = weightSum <= 0f;

            var counts       = new int[elements.Count];
            var isPositional = new bool[elements.Count];
            int positionalCount = 0;
            int totalPositional = 0;
            for (int i = 0; i < elements.Count; i++)
            {
                var v = elements[i].variant;
                if (v == null) continue;
                bool positional = v.propType == TilePropType.Tree || v.propType == TilePropType.Flower;
                isPositional[i] = positional;
                if (!positional) continue;

                float normalizedWeight = useEqualSplit
                    ? 1f / elements.Count
                    : HexTile.SafeWeight(elements[i].areaWeight) / weightSum;
                counts[i] = HexTile.ComputeElementPropCount(v.propCount, normalizedWeight);
                totalPositional += counts[i];
                positionalCount++;
            }

            // 要素ごとの事前割当（ElementRegionLayoutを再利用。Tree/Flowerが2つ以上ある場合のみ）。
            // それ以外（positional要素が1つ以下）はnullのままとし、下流で単一要素フォールバック式を使う
            // （HexTile側のComputeRegionAssignmentと同じ方針）。
            var assignedOffsets = new Vector3[elements.Count][];
            var assignedSeeds   = new int[elements.Count][];
            if (positionalCount >= 2 && totalPositional > 0)
            {
                ComputeAssignment(elements, isPositional, counts, positionalCount, totalPositional,
                                   type, coord, assignedOffsets, assignedSeeds);
            }

            var root = new GameObject("PreviewElementProps");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;

            for (int i = 0; i < elements.Count; i++)
            {
                var variant = elements[i].variant;
                if (variant == null) continue;

                switch (variant.propType)
                {
                    case TilePropType.Tree:
                        SpawnTreeElement(variant, counts[i], coord, i, elements.Count,
                                          assignedOffsets[i], assignedSeeds[i], root.transform, tileHeight);
                        break;
                    case TilePropType.House:
                        // SpawnHouseはpropCountを使わない単一形状のため要素ごとに1棟だけ生成する
                        // （実配置のSpawnSingleElementPropsと同じ考え方）。
                        HexTile.SpawnHouseStatic(root.transform, tileHeight);
                        break;
                    case TilePropType.Flower:
                        SpawnFlowerElement(variant, counts[i], coord, i, elements.Count,
                                            assignedOffsets[i], assignedSeeds[i], root.transform, tileHeight);
                        break;
                    case TilePropType.Water:
                        // Waterはelements/領域割当を無視しtypeのedgesから辺を選ぶ点が実配置と同じ。
                        // プレビューでは実配置の動くParticleSystemではなく、既存のlegacy previewと
                        // 同じ軽量な岸ラインのみ表示に統一する（ゴーストは頻繁に生成破棄されるため）。
                        HexTile.SpawnWaterPreview(type, parent, 2.0f, tileHeight);
                        break;
                }
            }
        }

        // Tree/Flower要素が2つ以上ある場合の領域割当（ElementRegionLayoutを再利用）。
        // HexTile.ComputeRegionAssignmentと同じ手順（候補生成→スコア→ソート→区間割当）。
        private static void ComputeAssignment(
            List<TileElement> elements, bool[] isPositional, int[] counts,
            int positionalCount, int totalPositional, TileType type, HexCoord coord,
            Vector3[][] assignedOffsets, int[][] assignedSeeds)
        {
            int   typeHash     = ElementRegionLayout.StableStringHash(type.name);
            float boundaryDeg  = ElementRegionLayout.ComputeBoundaryDirectionDeg(coord.q, coord.r, typeHash);
            float baseRotation = coord.q * 23f + coord.r * 37f;

            var candidates = ElementRegionLayout.GenerateNormalizedCandidates(
                totalPositional, coord.q, coord.r, typeHash, HexTile.TreeGoldenAngleDeg, baseRotation);
            var scores = ElementRegionLayout.ComputeScores(candidates, boundaryDeg, coord.q, coord.r, typeHash);
            var sortedIndices = ElementRegionLayout.SortIndicesByScore(scores);

            var positionalCounts  = new int[positionalCount];
            var positionalIndices = new int[positionalCount];
            int pi = 0;
            for (int i = 0; i < elements.Count; i++)
            {
                if (!isPositional[i]) continue;
                positionalCounts[pi]  = counts[i];
                positionalIndices[pi] = i;
                pi++;
            }

            var partitioned = ElementRegionLayout.PartitionByCounts(sortedIndices, positionalCounts);

            for (int p = 0; p < positionalIndices.Length; p++)
            {
                int elementIndex = positionalIndices[p];
                var variant      = elements[elementIndex].variant;
                float maxRadius  = variant.propType == TilePropType.Tree ? HexTile.TreeMaxRadius : HexTile.FlowerMaxRadius;

                var chunk   = partitioned[p];
                var offsets = new Vector3[chunk.Length];
                var seeds   = new int[chunk.Length];
                for (int k = 0; k < chunk.Length; k++)
                {
                    var cand   = candidates[chunk[k]];
                    offsets[k] = new Vector3(cand.NormX * maxRadius, 0f, cand.NormZ * maxRadius);
                    seeds[k]   = cand.Seed;
                }
                assignedOffsets[elementIndex] = offsets;
                assignedSeeds[elementIndex]   = seeds;
            }
        }

        private static void SpawnTreeElement(TerrainVariantDefinition variant, int count, HexCoord coord,
            int elementIndex, int elementTotal, Vector3[] offsets, int[] seeds, Transform parent, float tileHeight)
        {
            if (offsets != null && offsets.Length > 0)
            {
                for (int k = 0; k < offsets.Length; k++)
                    HexTile.SpawnSingleTreeForVariantStatic(variant, parent, offsets[k], seeds[k], tileHeight);
                return;
            }

            // positional要素が1つしかない場合のフォールバック（実配置のSpawnTreesForVariantの
            // フォールバック式と同じ）。
            float angleOffsetDeg = elementIndex * (360f / elementTotal);
            int   n              = Mathf.Max(1, count);
            float baseRotation   = coord.q * 23f + coord.r * 37f + angleOffsetDeg;
            for (int i = 0; i < n; i++)
            {
                int seed = Mathf.Abs(coord.q * 92821 + coord.r * 68917 + i * 40361
                                      + Mathf.RoundToInt(angleOffsetDeg) * 131);
                var offset = HexTile.ComputeSpiralOffset(i, n, seed, HexTile.TreeGoldenAngleDeg, HexTile.TreeMaxRadius, baseRotation);
                HexTile.SpawnSingleTreeForVariantStatic(variant, parent, offset, seed, tileHeight);
            }
        }

        private static void SpawnFlowerElement(TerrainVariantDefinition variant, int count, HexCoord coord,
            int elementIndex, int elementTotal, Vector3[] offsets, int[] seeds, Transform parent, float tileHeight)
        {
            Vector3 yOffset = new Vector3(0f, tileHeight + 0.02f, 0f);

            if (offsets != null && offsets.Length > 0)
            {
                var positions = new Vector3[offsets.Length];
                for (int k = 0; k < offsets.Length; k++)
                    positions[k] = offsets[k] + yOffset;
                HexTile.SpawnFlowerBillboards(parent, variant.billboardSprite, positions, seeds);
                return;
            }

            // positional要素が1つしかない場合のフォールバック（実配置のSpawnFlowersForVariantの
            // フォールバック式と同じ）。
            float angleOffsetDeg = elementIndex * (360f / elementTotal);
            int   n              = Mathf.Max(1, count);
            float baseRotation   = coord.q * 23f + coord.r * 37f + angleOffsetDeg;

            var fallbackPositions = new Vector3[n];
            var fallbackSeeds     = new int[n];
            for (int i = 0; i < n; i++)
            {
                int seed = coord.q * 31 + coord.r * 17 + i * 7 + Mathf.RoundToInt(angleOffsetDeg) * 13;
                fallbackPositions[i] = HexTile.ComputeSpiralOffset(i, n, seed, HexTile.FlowerGoldenAngleDeg, HexTile.FlowerMaxRadius, baseRotation) + yOffset;
                fallbackSeeds[i]     = seed;
            }
            HexTile.SpawnFlowerBillboards(parent, variant.billboardSprite, fallbackPositions, fallbackSeeds);
        }
    }
}
