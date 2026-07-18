// 役割: legacy TileTypeアセットをTileType.elements形式へ移行するEditorWindow。
//       実際の判定・書き込みロジックはTileTypeMigrationUtilityに委譲し、
//       このクラスは対象選択・Dry Run結果表示・実行確認ダイアログのみを担当する。

using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ElfVillage.Tiles;

namespace ElfVillage.Editor
{
    public class TileTypeMigrationWindow : EditorWindow
    {
        private enum TargetMode { SelectionOnly, Folder }

        private TargetMode _targetMode = TargetMode.SelectionOnly;
        private DefaultAsset _folderTarget;

        private List<MigrationEntry> _dryRunResults;
        private MigrationSummary _executionSummary;
        private Vector2 _scroll;

        [MenuItem("ElfVillage/Tile Type Migration...")]
        private static void Open()
        {
            var window = GetWindow<TileTypeMigrationWindow>(utility: true, title: "TileType Migration");
            window.minSize = new Vector2(480f, 360f);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "legacy TileType（tileCategory/propType等）を TileType.elements 形式へ移行します。\n" +
                "必ず Dry Run で内容を確認してから実行してください。全アセット一括実行は行われません。",
                MessageType.Info);

            EditorGUILayout.Space();
            DrawTargetSelection();

            var targets = CollectTargets();
            EditorGUILayout.LabelField("対象件数", targets.Count.ToString());

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(targets.Count == 0))
            {
                if (GUILayout.Button("Dry Run（変更なし）"))
                {
                    _dryRunResults = TileTypeMigrationUtility.AnalyzeDryRun(targets);
                    _executionSummary = null;
                }
            }

            if (targets.Count == 0)
                EditorGUILayout.HelpBox("対象が0件です。TileTypeアセットを選択するか、フォルダを指定してください。", MessageType.Warning);

            if (_dryRunResults != null)
                DrawDryRunResults(targets);

            if (_executionSummary != null)
                DrawExecutionSummary();
        }

        private void DrawTargetSelection()
        {
            _targetMode = (TargetMode)EditorGUILayout.EnumPopup("対象選択", _targetMode);
            if (_targetMode == TargetMode.Folder)
            {
                _folderTarget = (DefaultAsset)EditorGUILayout.ObjectField(
                    "対象フォルダ", _folderTarget, typeof(DefaultAsset), false);
            }
        }

        private List<TileType> CollectTargets()
        {
            if (_targetMode == TargetMode.SelectionOnly)
                return Selection.GetFiltered<TileType>(SelectionMode.Assets).ToList();

            if (_folderTarget == null) return new List<TileType>();
            string folderPath = AssetDatabase.GetAssetPath(_folderTarget);
            if (!AssetDatabase.IsValidFolder(folderPath)) return new List<TileType>();

            var result = new List<TileType>();
            foreach (var guid in AssetDatabase.FindAssets("t:TileType", new[] { folderPath }))
            {
                var t = AssetDatabase.LoadAssetAtPath<TileType>(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null) result.Add(t);
            }
            return result;
        }

        private void DrawDryRunResults(List<TileType> targets)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dry Run 結果", EditorStyles.boldLabel);

            int migratable = _dryRunResults.Count(e => e.Decision == MigrationDecision.Migratable);
            int newVariants = _dryRunResults.Count(e =>
                e.Decision == MigrationDecision.Migratable && e.VariantPlan == VariantPlan.CreateNew);
            int reuseVariants = _dryRunResults.Count(e =>
                e.Decision == MigrationDecision.Migratable && e.VariantPlan == VariantPlan.ReuseExisting);
            int alreadyMigrated = _dryRunResults.Count(e => e.Decision == MigrationDecision.AlreadyMigrated);
            int insufficient = _dryRunResults.Count(e => e.Decision == MigrationDecision.InsufficientInfo);
            int unconvertible = _dryRunResults.Count(e => e.Decision == MigrationDecision.LegacyCategoryUnconvertible);
            int ambiguous = _dryRunResults.Count(e => e.Decision == MigrationDecision.AmbiguousVariantMatch);

            EditorGUILayout.LabelField($"移行可能: {migratable}（新規Variant: {newVariants} / 再利用: {reuseVariants}）");
            EditorGUILayout.LabelField($"移行済みのためスキップ: {alreadyMigrated}");
            EditorGUILayout.LabelField($"情報不足のためスキップ: {insufficient}");
            EditorGUILayout.LabelField($"legacyカテゴリ変換不能: {unconvertible}");
            EditorGUILayout.LabelField($"Variant候補が複数のためスキップ: {ambiguous}");

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(200f));
            foreach (var e in _dryRunResults)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(e.AssetPath);
                EditorGUILayout.LabelField(
                    $"legacyカテゴリ: {e.LegacyCategoryRaw}　→　変換先: {(e.TargetCategory.HasValue ? e.TargetCategory.Value.ToString() : "-")}");
                EditorGUILayout.LabelField($"判定: {DecisionLabel(e.Decision)}" +
                    (e.Decision == MigrationDecision.Migratable ? $"（{VariantPlanLabel(e.VariantPlan)}）" : ""));
                foreach (var w in e.Warnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(migratable == 0))
            {
                if (GUILayout.Button("実行..."))
                    TryExecute(targets, migratable, newVariants, reuseVariants);
            }
        }

        private void TryExecute(List<TileType> targets, int migratable, int newVariants, int reuseVariants)
        {
            int skipped = targets.Count - migratable;
            bool confirmed = EditorUtility.DisplayDialog(
                "TileType移行の実行確認",
                $"対象件数: {targets.Count}\n" +
                $"移行実行: {migratable}\n" +
                $"新規Variant作成: {newVariants}\n" +
                $"既存Variant再利用: {reuseVariants}\n" +
                $"スキップ: {skipped}\n\n" +
                "この操作はUndo（Ctrl+Z）で戻せますが、実行前にGit等でバックアップを取ることを推奨します。\n\n" +
                "実行してよろしいですか？",
                "実行", "キャンセル");

            if (!confirmed) return;

            var toMigrate = _dryRunResults
                .Where(e => e.Decision == MigrationDecision.Migratable)
                .Select(e => e.Tile)
                .ToList();

            _executionSummary = TileTypeMigrationUtility.Execute(toMigrate);
            AssetDatabase.SaveAssets();

            // 実行後の最新状態を反映するためDry Runを取り直す
            _dryRunResults = TileTypeMigrationUtility.AnalyzeDryRun(targets);
        }

        private void DrawExecutionSummary()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("実行結果", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"対象総数: {_executionSummary.Total}");
            EditorGUILayout.LabelField($"移行成功: {_executionSummary.Succeeded}");
            EditorGUILayout.LabelField($"Variant新規作成: {_executionSummary.VariantsCreated}");
            EditorGUILayout.LabelField($"Variant再利用: {_executionSummary.VariantsReused}");
            EditorGUILayout.LabelField($"既に移行済み: {_executionSummary.AlreadyMigrated}");
            EditorGUILayout.LabelField($"情報不足: {_executionSummary.InsufficientInfo}");
            EditorGUILayout.LabelField($"legacy変換失敗: {_executionSummary.LegacyCategoryUnconvertible}");
            EditorGUILayout.LabelField($"Variant候補複数: {_executionSummary.AmbiguousVariantMatch}");
            EditorGUILayout.LabelField($"エラー: {_executionSummary.Errors}");

            if (_executionSummary.Errors > 0)
            {
                foreach (var e in _executionSummary.Results.Where(r => r.Executed && !r.Succeeded))
                    EditorGUILayout.HelpBox($"{e.AssetPath}: {e.ErrorMessage}", MessageType.Error);
            }
        }

        private static string DecisionLabel(MigrationDecision decision) => decision switch
        {
            MigrationDecision.Migratable                    => "移行可能",
            MigrationDecision.AlreadyMigrated                => "移行済みのためスキップ",
            MigrationDecision.InsufficientInfo               => "情報不足のためスキップ",
            MigrationDecision.LegacyCategoryUnconvertible    => "legacyカテゴリ変換不能",
            MigrationDecision.AmbiguousVariantMatch          => "Variant候補が複数のためスキップ",
            _ => decision.ToString(),
        };

        private static string VariantPlanLabel(VariantPlan plan) => plan switch
        {
            VariantPlan.CreateNew      => "新規Variant作成",
            VariantPlan.ReuseExisting  => "既存Variant再利用",
            _ => "-",
        };
    }
}
