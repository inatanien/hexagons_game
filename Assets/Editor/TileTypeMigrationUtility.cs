// 役割: legacy TileTypeアセット（tileCategory/propType等の単一要素形式）を
//       TileType.elements（複数要素形式）へ移行するための純粋なロジック層。
//       Dry Run分析と実移行の両方をEditorWindow（UI）から独立して提供する。
//       このファイルはEditor専用（_Game.Editor asmdef、includePlatforms=Editor）。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Editor
{
    public enum MigrationDecision
    {
        Migratable,
        AlreadyMigrated,
        InsufficientInfo,
        LegacyCategoryUnconvertible,
        AmbiguousVariantMatch,
    }

    public enum VariantPlan
    {
        None,
        ReuseExisting,
        CreateNew,
    }

    /// <summary>1件のTileTypeに対するDry Run（または実行）結果。</summary>
    public class MigrationEntry
    {
        public TileType Tile;
        public string AssetPath;
        public string LegacyCategoryRaw;
        public TileCategory? TargetCategory;
        public MigrationDecision Decision;
        public VariantPlan VariantPlan;
        public TerrainVariantDefinition ExistingVariantCandidate;
        public readonly List<string> Warnings = new List<string>();

        // 実行後にのみ設定される
        public bool Executed;
        public bool Succeeded;
        public string ErrorMessage;
    }

    public class MigrationSummary
    {
        public int Total;
        public int AlreadyMigrated;
        public int InsufficientInfo;
        public int LegacyCategoryUnconvertible;
        public int AmbiguousVariantMatch;
        public int VariantsCreated;
        public int VariantsReused;
        public int Succeeded;
        public int Errors;
        public List<MigrationEntry> Results = new List<MigrationEntry>();
    }

    public static class TileTypeMigrationUtility
    {
        public const string DefaultVariantFolder = "Assets/_Game/ScriptableObjects/TerrainVariants";

        // ── Dry Run（読み取り専用） ────────────────────────────────────

        public static List<MigrationEntry> AnalyzeDryRun(IReadOnlyList<TileType> targets)
        {
            var known = FindAllVariants();
            var results = new List<MigrationEntry>(targets.Count);
            foreach (var tile in targets)
                results.Add(Analyze(tile, known));
            return results;
        }

        /// <summary>1件のTileTypeを判定する。Dry Run・実行の両方から呼ばれる唯一の判定ロジック。</summary>
        public static MigrationEntry Analyze(TileType tile, List<TerrainVariantDefinition> knownVariants)
        {
            var entry = new MigrationEntry
            {
                Tile = tile,
                AssetPath = AssetDatabase.GetAssetPath(tile),
                LegacyCategoryRaw = tile.tileCategory,
            };

            // 1. 既に有効なelementsがあれば移行済みとしてスキップする
            if (tile.EffectiveElements.Any())
            {
                entry.Decision = MigrationDecision.AlreadyMigrated;
                return entry;
            }

            // 2. legacyカテゴリが変換できるか（空文字列・不明な文字列はfalse）
            if (!tile.TryGetLegacyCategory(out var category))
            {
                entry.Decision = MigrationDecision.LegacyCategoryUnconvertible;
                return entry;
            }
            entry.TargetCategory = category;

            // 3. propTypeが既知の値か（推測補正はせず、未知の値は情報不足として弾く）
            if (!Enum.IsDefined(typeof(TilePropType), tile.propType))
            {
                entry.Decision = MigrationDecision.InsufficientInfo;
                entry.Warnings.Add($"未知のpropType値です: {tile.propType}");
                return entry;
            }

            // 4. 既存Variantとの一致検索（完全一致のみ。曖昧な場合は自動選択しない）
            var matches = knownVariants.Where(v => VariantMatchesLegacy(v, tile, category)).ToList();
            if (matches.Count > 1)
            {
                entry.Decision = MigrationDecision.AmbiguousVariantMatch;
                entry.Warnings.Add($"一致するTerrainVariantDefinition候補が{matches.Count}件あり、一意に選べません。");
                return entry;
            }

            entry.Decision = MigrationDecision.Migratable;
            if (matches.Count == 1)
            {
                entry.VariantPlan = VariantPlan.ReuseExisting;
                entry.ExistingVariantCandidate = matches[0];
            }
            else
            {
                entry.VariantPlan = VariantPlan.CreateNew;
            }
            return entry;
        }

        public static List<TerrainVariantDefinition> FindAllVariants()
        {
            var list = new List<TerrainVariantDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:TerrainVariantDefinition"))
            {
                var v = AssetDatabase.LoadAssetAtPath<TerrainVariantDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (v != null) list.Add(v);
            }
            return list;
        }

        private static bool VariantMatchesLegacy(TerrainVariantDefinition v, TileType legacy, TileCategory category)
        {
            if (v.category != category) return false;
            if (v.propType != legacy.propType) return false;
            if (v.propCount != legacy.propCount) return false;
            if (!PrefabArraysEquivalent(v.propPrefabs, legacy.treeVariantPrefabs)) return false;
            if (v.billboardSprite != legacy.billboardSprite) return false;
            return true;
        }

        private static bool PrefabArraysEquivalent(GameObject[] a, GameObject[] b)
            => NonNull(a).SequenceEqual(NonNull(b));

        private static IEnumerable<GameObject> NonNull(GameObject[] arr)
            => arr == null ? Enumerable.Empty<GameObject>() : arr.Where(x => x != null);

        // ── 実行（書き込みあり。AssetDatabase.SaveAssetsは呼び出し側の責務） ──────

        /// <summary>
        /// 対象を実際に移行する。Migratable以外のエントリはスキップとしてカウントするのみ。
        /// AssetDatabase.SaveAssetsはここでは呼ばない（呼び出し側が確定タイミングで一度だけ行う）。
        /// </summary>
        public static MigrationSummary Execute(IReadOnlyList<TileType> targets, string variantFolder = DefaultVariantFolder)
        {
            var summary = new MigrationSummary { Total = targets.Count };
            var known = FindAllVariants();
            EnsureFolder(variantFolder);

            foreach (var tile in targets)
            {
                var entry = Analyze(tile, known);
                summary.Results.Add(entry);

                switch (entry.Decision)
                {
                    case MigrationDecision.AlreadyMigrated:
                        summary.AlreadyMigrated++;
                        continue;
                    case MigrationDecision.InsufficientInfo:
                        summary.InsufficientInfo++;
                        continue;
                    case MigrationDecision.LegacyCategoryUnconvertible:
                        summary.LegacyCategoryUnconvertible++;
                        continue;
                    case MigrationDecision.AmbiguousVariantMatch:
                        summary.AmbiguousVariantMatch++;
                        continue;
                }

                try
                {
                    TerrainVariantDefinition variant;
                    if (entry.VariantPlan == VariantPlan.ReuseExisting)
                    {
                        variant = entry.ExistingVariantCandidate;
                        summary.VariantsReused++;
                    }
                    else
                    {
                        variant = CreateVariantAsset(tile, entry.TargetCategory.Value, variantFolder);
                        known.Add(variant); // 同一バッチ内の後続エントリが重複作成しないよう、既知リストへ追加する
                        summary.VariantsCreated++;
                    }

                    Undo.RecordObject(tile, "Migrate TileType to Elements");
                    tile.elements = new[] { new TileElement { variant = variant, areaWeight = 1f } };
                    EditorUtility.SetDirty(tile);

                    entry.Executed  = true;
                    entry.Succeeded = true;
                    summary.Succeeded++;
                }
                catch (Exception ex)
                {
                    entry.Executed     = true;
                    entry.Succeeded    = false;
                    entry.ErrorMessage = ex.Message;
                    summary.Errors++;
                }
            }

            return summary;
        }

        private static TerrainVariantDefinition CreateVariantAsset(TileType legacy, TileCategory category, string folder)
        {
            var variant = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            variant.category        = category;
            variant.variantName     = legacy.tileName;
            variant.propType        = legacy.propType;
            variant.propCount       = legacy.propCount;
            variant.propPrefabs     = legacy.treeVariantPrefabs;
            variant.billboardSprite = legacy.billboardSprite;

            string fileName = $"TerrainVariant_{category}_{SanitizeFileName(legacy.tileName)}.asset";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}");

            AssetDatabase.CreateAsset(variant, path);
            Undo.RegisterCreatedObjectUndo(variant, "Create TerrainVariantDefinition");
            return variant;
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unnamed";
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
