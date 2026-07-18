// 役割: TileTypeMigrationUtility（Session 5）の単体テスト。
//       移行ツールはAssetDatabase経由で実アセットを操作するため、専用の一時フォルダに
//       テスト用アセットを作成し、TearDownで確実に削除する。

using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ElfVillage.Tiles;
using ElfVillage.Editor;

namespace ElfVillage.Tests
{
    public class TileTypeMigrationUtilityTests
    {
        private const string TestRoot    = "Assets/_Game/ScriptableObjects/_TestMigration";
        private const string VariantRoot = TestRoot + "/Variants";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.CreateFolder("Assets/_Game/ScriptableObjects", "_TestMigration");
            if (!AssetDatabase.IsValidFolder(VariantRoot))
                AssetDatabase.CreateFolder(TestRoot, "Variants");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
                AssetDatabase.DeleteAsset(TestRoot);
        }

        // ── テストヘルパー ────────────────────────────────────────────

        private static TileType CreateLegacyTile(string name, string category, TilePropType propType, int propCount,
                                                   GameObject[] treePrefabs = null, Texture2D billboardSprite = null)
        {
            var t = ScriptableObject.CreateInstance<TileType>();
            t.tileName            = name;
            t.tileCategory        = category;
            t.propType             = propType;
            t.propCount            = propCount;
            t.treeVariantPrefabs   = treePrefabs;
            t.billboardSprite      = billboardSprite;
            t.edges                = new[] { EdgeType.Forest, EdgeType.Forest, EdgeType.Forest,
                                              EdgeType.Field,  EdgeType.Field,  EdgeType.Field };
            AssetDatabase.CreateAsset(t, $"{TestRoot}/{name}.asset");
            return t;
        }

        private static TerrainVariantDefinition CreateExistingVariant(string name, TileCategory category,
                                                                        TilePropType propType, int propCount)
        {
            var v = ScriptableObject.CreateInstance<TerrainVariantDefinition>();
            v.category  = category;
            v.propType  = propType;
            v.propCount = propCount;
            AssetDatabase.CreateAsset(v, $"{VariantRoot}/{name}.asset");
            return v;
        }

        private static int CountAssetsUnder(string folder, string filter)
            => AssetDatabase.FindAssets(filter, new[] { folder }).Length;

        // ── Dry Run ───────────────────────────────────────────────────

        [Test]
        public void DryRun_DoesNotModifyAssets()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);
            var edgesBefore = (EdgeType[])tile.edges.Clone();

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(MigrationDecision.Migratable, results[0].Decision);
            Assert.IsNull(tile.elements, "Dry Runではelementsを変更してはならない");
            CollectionAssert.AreEqual(edgesBefore, tile.edges, "Dry Runではedgesを変更してはならない");
            Assert.AreEqual(0, CountAssetsUnder(TestRoot, "t:TerrainVariantDefinition"), "Dry Runでは新規Variantを作成してはならない");
        }

        // ── 基本の移行 ────────────────────────────────────────────────

        [Test]
        public void Execute_LegacyTile_MigratesWithAreaWeightOne()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);

            var summary = TileTypeMigrationUtility.Execute(new[] { tile }, VariantRoot);

            Assert.AreEqual(1, summary.Succeeded);
            Assert.AreEqual(1, tile.elements.Length);
            Assert.AreEqual(1f, tile.elements[0].areaWeight);
            Assert.IsNotNull(tile.elements[0].variant);
            Assert.AreEqual(TileCategory.Forest, tile.elements[0].variant.category);
            Assert.IsFalse(tile.elements[0].visualOnly, "移行ツールが作成する要素は必ずGameplay（visualOnly=false）であるべき");
        }

        [Test]
        public void Execute_PreservesLegacyFields()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 7);
            string categoryBefore = tile.tileCategory;
            var propTypeBefore = tile.propType;
            int propCountBefore = tile.propCount;

            TileTypeMigrationUtility.Execute(new[] { tile }, VariantRoot);

            Assert.AreEqual(categoryBefore, tile.tileCategory, "legacyのtileCategoryは維持されるべき");
            Assert.AreEqual(propTypeBefore, tile.propType, "legacyのpropTypeは維持されるべき");
            Assert.AreEqual(propCountBefore, tile.propCount, "legacyのpropCountは維持されるべき");
        }

        [Test]
        public void Execute_DoesNotChangeEdges()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 3);
            var edgesBefore = (EdgeType[])tile.edges.Clone();

            TileTypeMigrationUtility.Execute(new[] { tile }, VariantRoot);

            CollectionAssert.AreEqual(edgesBefore, tile.edges, "edgesは変更されないべき");
        }

        // ── スキップ条件 ──────────────────────────────────────────────

        [Test]
        public void AlreadyHasElements_IsSkipped()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 3);
            var variant = CreateExistingVariant("V_Existing", TileCategory.Forest, TilePropType.Tree, 3);
            tile.elements = new[] { new TileElement { variant = variant, areaWeight = 1f } };
            EditorUtility.SetDirty(tile);

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(MigrationDecision.AlreadyMigrated, results[0].Decision);
        }

        [Test]
        public void UnconvertibleLegacyCategory_IsSkipped()
        {
            var tile = CreateLegacyTile("T_Bad", "NotARealCategory", TilePropType.Tree, 3);

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(MigrationDecision.LegacyCategoryUnconvertible, results[0].Decision);
        }

        [Test]
        public void EmptyLegacyCategory_IsSkipped()
        {
            var tile = CreateLegacyTile("T_Empty", "", TilePropType.Tree, 3);

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(MigrationDecision.LegacyCategoryUnconvertible, results[0].Decision);
        }

        [Test]
        public void UnknownPropType_IsSkippedAsInsufficientInfo()
        {
            var tile = CreateLegacyTile("T_Unknown", "Forest", (TilePropType)999, 3);

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(MigrationDecision.InsufficientInfo, results[0].Decision);
        }

        // ── Variantの再利用・新規作成・曖昧判定 ──────────────────────

        [Test]
        public void ExactlyOneMatchingVariant_IsReused()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);
            var variant = CreateExistingVariant("V_Match", TileCategory.Forest, TilePropType.Tree, 5);

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(MigrationDecision.Migratable, results[0].Decision);
            Assert.AreEqual(VariantPlan.ReuseExisting, results[0].VariantPlan);
            Assert.AreEqual(variant, results[0].ExistingVariantCandidate);
        }

        [Test]
        public void NoMatchingVariant_PlansCreateNew()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(MigrationDecision.Migratable, results[0].Decision);
            Assert.AreEqual(VariantPlan.CreateNew, results[0].VariantPlan);
        }

        [Test]
        public void MultipleMatchingVariants_IsAmbiguousAndSkipped()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);
            CreateExistingVariant("V_MatchA", TileCategory.Forest, TilePropType.Tree, 5);
            CreateExistingVariant("V_MatchB", TileCategory.Forest, TilePropType.Tree, 5);

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(MigrationDecision.AmbiguousVariantMatch, results[0].Decision);
        }

        [Test]
        public void Execute_ReusesExistingVariant_DoesNotCreateNewAsset()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);
            var variant = CreateExistingVariant("V_Match", TileCategory.Forest, TilePropType.Tree, 5);
            int variantCountBefore = CountAssetsUnder(TestRoot, "t:TerrainVariantDefinition");

            var summary = TileTypeMigrationUtility.Execute(new[] { tile }, VariantRoot);

            Assert.AreEqual(1, summary.VariantsReused);
            Assert.AreEqual(0, summary.VariantsCreated);
            Assert.AreEqual(variantCountBefore, CountAssetsUnder(TestRoot, "t:TerrainVariantDefinition"));
            Assert.AreEqual(variant, tile.elements[0].variant);
        }

        [Test]
        public void Execute_CreatesNewVariant_WhenNoMatchExists()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);

            var summary = TileTypeMigrationUtility.Execute(new[] { tile }, VariantRoot);

            Assert.AreEqual(1, summary.VariantsCreated);
            Assert.AreEqual(0, summary.VariantsReused);
            Assert.IsNotNull(tile.elements[0].variant);
            Assert.IsTrue(AssetDatabase.Contains(tile.elements[0].variant), "新規Variantはアセットとして保存されているべき");
        }

        // ── 冪等性 ────────────────────────────────────────────────────

        [Test]
        public void ExecuteTwice_DoesNotDuplicateVariantOrElements()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);

            var summary1 = TileTypeMigrationUtility.Execute(new[] { tile }, VariantRoot);
            int variantCountAfterFirst = CountAssetsUnder(TestRoot, "t:TerrainVariantDefinition");

            var summary2 = TileTypeMigrationUtility.Execute(new[] { tile }, VariantRoot);
            int variantCountAfterSecond = CountAssetsUnder(TestRoot, "t:TerrainVariantDefinition");

            Assert.AreEqual(1, summary1.Succeeded);
            Assert.AreEqual(1, summary2.AlreadyMigrated);
            Assert.AreEqual(0, summary2.Succeeded);
            Assert.AreEqual(variantCountAfterFirst, variantCountAfterSecond, "2回目の実行でVariantが重複作成されてはならない");
            Assert.AreEqual(1, tile.elements.Length, "elementsが重複追加されてはならない");
        }

        [Test]
        public void SecondDryRunAfterExecute_ReportsAlreadyMigrated()
        {
            var tile = CreateLegacyTile("T_Forest", "Forest", TilePropType.Tree, 5);
            TileTypeMigrationUtility.Execute(new[] { tile }, VariantRoot);

            var results = TileTypeMigrationUtility.AnalyzeDryRun(new[] { tile });

            Assert.AreEqual(MigrationDecision.AlreadyMigrated, results[0].Decision);
        }

        // ── 対象0件 ───────────────────────────────────────────────────

        [Test]
        public void Execute_EmptyTargetList_DoesNothing()
        {
            var summary = TileTypeMigrationUtility.Execute(new System.Collections.Generic.List<TileType>(), VariantRoot);

            Assert.AreEqual(0, summary.Total);
            Assert.AreEqual(0, summary.Succeeded);
            Assert.AreEqual(0, summary.Results.Count);
        }

        [Test]
        public void DryRun_EmptyTargetList_ReturnsEmpty()
        {
            var results = TileTypeMigrationUtility.AnalyzeDryRun(new System.Collections.Generic.List<TileType>());
            Assert.AreEqual(0, results.Count);
        }
    }
}
