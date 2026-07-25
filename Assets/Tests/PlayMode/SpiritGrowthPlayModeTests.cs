// 役割: Stage 14「成長」を、実際のUnityライフサイクルを通して検証する。
//       ★EditModeでは検証できない部分に絞る
//         ・生成直後のVisualが最初のUpdateを待たずに正しい段階になっているか
//         ・成長演出が「安全なIdle」でしか始まらないか
//         ・演出が頂点の前後どちらで中断されても矛盾が残らないか
//         ・成長でGameObjectやMaterialが作り直されていないか
//       内部状態は読み取るだけで、状態遷移を手動で起こさない
//       （EnterStateの手動呼び出しはEditMode側の役割）。

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ElfVillage.Core;
using ElfVillage.HexGrid;
using ElfVillage.Spirits;
using ElfVillage.Tiles;

namespace ElfVillage.Tests
{
    public class SpiritGrowthPlayModeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _spawned = new();

        private static void ClearEventBus()
        {
            var field = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "EventBus._handlers が見つかりません");
            ((System.Collections.IDictionary)field.GetValue(null)).Clear();
        }

        private static int SubscriberCount<T>()
        {
            var field = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            var dict  = (System.Collections.IDictionary)field.GetValue(null);
            if (!dict.Contains(typeof(T))) return 0;
            return ((System.Delegate)dict[typeof(T)]).GetInvocationList().Length;
        }

        [SetUp]
        public void SetUp() => ClearEventBus();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearEventBus();
        }

        // ── ヘルパー ──────────────────────────────────────────────────

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        private List<HexTile> MakeForest(string name, Vector3 origin)
        {
            var root = Track(new GameObject(name));
            var tiles = new List<HexTile>();
            var offsets = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1.5f, 0f, 0.866f),
                new Vector3(1.5f, 0f, -0.866f),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                var go = new GameObject(name + "_Tile" + i);
                go.transform.SetParent(root.transform, true);
                var tile = go.AddComponent<HexTile>();
                tile.Initialize(new HexCoord(i, -i), 1f);
                go.transform.position = origin + offsets[i];
                tiles.Add(tile);
            }
            return tiles;
        }

        private ForestSpiritSpawner MakeSpawner(SpiritPersonalityKind kind)
        {
            var go = Track(new GameObject("GrowthSpawner"));
            var spawner = go.AddComponent<ForestSpiritSpawner>();
            typeof(ForestSpiritSpawner).GetField("_personalityMode", Priv)
                .SetValue(spawner, ForestSpiritSpawner.PersonalitySelectionMode.Fixed);
            typeof(ForestSpiritSpawner).GetField("_fixedPersonality", Priv).SetValue(spawner, kind);
            return spawner;
        }

        private static void PublishForestGrowth(IReadOnlyList<HexTile> tiles)
            => EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                   null, HexCoord.Zero, tiles, new ForestGrowthMetrics(tiles.Count, tiles.Count)));

        private static void PublishFlowerNear(ForestSpirit spirit)
            => EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(
                   SpiritStimulusKind.FlowerBloomed, spirit.transform.position + new Vector3(0.5f, 0f, 0.5f), null)));

        private static object GetField(object t, string n) => t.GetType().GetField(n, Priv).GetValue(t);
        private static void SetField(object t, string n, object v) => t.GetType().GetField(n, Priv).SetValue(t, v);

        private static Transform[] FluffOf(ForestSpirit s) => (Transform[])GetField(s, "_fluffTransforms");

        private static int ActiveFluffCount(ForestSpirit s)
        {
            var fluff = FluffOf(s);
            int n = 0;
            foreach (var f in fluff) if (f != null && f.gameObject.activeSelf) n++;
            return n;
        }

        private static float ActiveFluffScale(ForestSpirit s)
        {
            foreach (var f in FluffOf(s))
                if (f != null && f.gameObject.activeSelf) return f.localScale.x;
            return -1f;
        }

        private static bool FlourishActive(ForestSpirit s)  => (bool)GetField(s, "_growthFlourishActive");
        private static bool FlourishApplied(ForestSpirit s) => (bool)GetField(s, "_growthAppliedThisFlourish");
        private static SpiritGrowthStage Pending(ForestSpirit s) => (SpiritGrowthStage)GetField(s, "_pendingGrowthStage");

        /// <summary>Spawner経由で精霊を1体作り、閾値を短時間検証向けへ下げる。</summary>
        private IEnumerator SpawnSpirit(SpiritPersonalityKind kind, float tFluff, float tBloom,
                                         System.Action<ForestSpirit> onReady)
        {
            var spawner = MakeSpawner(kind);
            var forest  = MakeForest("GrowthForest", Vector3.zero);

            yield return null;
            PublishForestGrowth(forest);   // 生成 ＋ 生成時刺激（累積体験=1）
            yield return null;

            var spirit = spawner.GetComponentInChildren<ForestSpirit>(true);
            Assert.IsNotNull(spirit, "精霊が生成されなかった");

            // ★productionへテストモードを足さず、既存SerializeFieldを下げるだけ。
            SetField(spirit, "_growthThresholdFluff", tFluff);
            SetField(spirit, "_growthThresholdBloom", tBloom);

            // ★生成時の森刺激でReact中になっている。
            //   React中の同優先度の刺激はStage 11の仕様どおり拒否されるため、
            //   ここでIdleへ戻るまで待たないと、以降の花の刺激が受理されない。
            yield return WaitUntil(() => spirit.CurrentState == SpiritState.Idle, 10f,
                "生成時のReactが終わってIdleへ戻らなかった");

            onReady(spirit);
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutSeconds, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, message);
                yield return null;
            }
        }

        // ══ 1. 生成直後のVisual ═════════════════════════════════════════

        [UnityTest]
        public IEnumerator NewSpirit_IsBornWithSproutVisual_BeforeFirstUpdate()
        {
            var spawner = MakeSpawner(SpiritPersonalityKind.Calm);
            var forest  = MakeForest("BornForest", Vector3.zero);

            yield return null;
            PublishForestGrowth(forest);

            // ★yieldを挟まずに直後を見る。Initialize内でApplyGrowthVisualまで済んでいなければ
            //   ここで別段階の姿が観測される。
            var spirit = spawner.GetComponentInChildren<ForestSpirit>(true);
            Assert.IsNotNull(spirit);

            var expected = SpiritGrowthMath.ComputeGrowthVisual(SpiritGrowthStage.Sprout);

            Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage);
            Assert.AreEqual(expected.FluffLayers, ActiveFluffCount(spirit),
                "生成直後の有効な毛玉の数がSproutと一致しない");
            Assert.AreEqual(0.10f * expected.FluffScale, ActiveFluffScale(spirit), 0.0001f,
                "生成直後の毛玉サイズがSproutと一致しない");

            // 生成時の森刺激で累積体験は1になるが、閾値8未満なのでSproutのまま。
            var memory = (SpiritMemory)GetField(spirit, "_memory");
            Assert.AreEqual(1f, memory.GetLifetimeExperience(), 0.0001f);
            Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage);
        }

        [UnityTest]
        public IEnumerator NewSpirit_PreallocatesMaxFluff_AndHidesUnusedOnes()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Calm, 8f, 20f, s => spirit = s);

            var fluff = FluffOf(spirit);
            Assert.IsNotNull(fluff, "毛玉の配列が保持されていない");
            Assert.AreEqual(SpiritGrowthMath.MaxFluffLayers, fluff.Length,
                "最大段階ぶんの毛玉が事前生成されていない");

            Assert.AreEqual(4, ActiveFluffCount(spirit), "Sproutで4個だけ有効になっていない");

            for (int i = 4; i < fluff.Length; i++)
                Assert.IsFalse(fluff[i].gameObject.activeSelf, $"余りの毛玉{i}が表示されている");
        }

        // ══ 2〜3. 成長でVisualが変わり、作り直しは起きない ═══════════════

        [UnityTest]
        public IEnumerator Growth_UpdatesFluffCountScaleAndLayout_WithoutRebuilding()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 99f, s => spirit = s);

            var bodyRootBefore = (Transform)GetField(spirit, "_bodyRoot");
            var fluffBefore    = FluffOf(spirit);
            int childCountBefore = bodyRootBefore.childCount;
            var idsBefore = new int[fluffBefore.Length];
            for (int i = 0; i < fluffBefore.Length; i++) idsBefore[i] = fluffBefore[i].GetInstanceID();

            var materialsBefore = (List<Material>)GetField(spirit, "_runtimeMaterials");
            int materialCountBefore = materialsBefore.Count;

            // 累積体験を1→2にしてFluffへ到達させる。
            PublishFlowerNear(spirit);
            yield return null;

            Assert.AreEqual(SpiritGrowthStage.Fluff, Pending(spirit), "Fluffが予約されていない");

            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f,
                "30秒以内に成長段階がFluffへ進まなかった");

            // 見た目が実際に変わっている
            Assert.AreEqual(6, ActiveFluffCount(spirit), "Fluffで6個になっていない");
            Assert.AreEqual(0.10f * 1.00f, ActiveFluffScale(spirit), 0.0001f, "Fluffのサイズになっていない");

            // 有効な毛玉が円周上へ均等に配置し直されている
            var fluffAfter = FluffOf(spirit);
            for (int i = 0; i < 6; i++)
            {
                float angle = i * (Mathf.PI * 2f / 6f);
                Assert.AreEqual(Mathf.Cos(angle) * 0.075f, fluffAfter[i].localPosition.x, 0.0001f,
                    $"毛玉{i}が6個ぶんの均等配置になっていない");
                Assert.AreEqual(Mathf.Sin(angle) * 0.075f, fluffAfter[i].localPosition.z, 0.0001f,
                    $"毛玉{i}が6個ぶんの均等配置になっていない");
            }

            // ★作り直しが起きていない
            Assert.AreSame(bodyRootBefore, (Transform)GetField(spirit, "_bodyRoot"), "Visualルートが作り直された");
            Assert.AreEqual(childCountBefore, bodyRootBefore.childCount, "GameObjectが増減した");
            for (int i = 0; i < fluffAfter.Length; i++)
                Assert.AreEqual(idsBefore[i], fluffAfter[i].GetInstanceID(), $"毛玉{i}が別インスタンスへ差し替わった");
            Assert.AreEqual(materialCountBefore, materialsBefore.Count, "Materialが再生成された");
        }

        // ══ 4. 安全でない状態では演出を開始しない ════════════════════════

        [UnityTest]
        public IEnumerator Flourish_DoesNotStart_WhileNotInIdle()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 99f, s => spirit = s);

            PublishFlowerNear(spirit);   // 受理されてReactへ入る＋Fluffを予約
            yield return null;

            Assert.AreEqual(SpiritState.React, spirit.CurrentState, "刺激でReactへ入っていない");
            Assert.AreEqual(SpiritGrowthStage.Fluff, Pending(spirit));

            // React中は演出も段階確定も起きない。
            float deadline = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < deadline && spirit.CurrentState == SpiritState.React)
            {
                Assert.IsFalse(FlourishActive(spirit), "React中に成長演出が始まった");
                Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage, "React中に段階が確定した");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator Flourish_DoesNotStart_WhileMovingOrSleeping()
        {
            ForestSpirit spirit = null;
            // 閾値を同値にして2段階ぶん予約させる。こうすると観察中ずっと pending > current が続き、
            // 「Idle以外では始まらない」ことが空振りせずに検証できる。
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 2f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;

            // 15秒ぶん観察し、Idle以外・移動中に演出が動いていないことを確認する。
            float deadline = Time.realtimeSinceStartup + 15f;
            bool sawNonIdle = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                bool isMoving = (bool)GetField(spirit, "_isMoving");
                var state = spirit.CurrentState;

                if (state != SpiritState.Idle)
                {
                    sawNonIdle = true;
                    Assert.IsFalse(FlourishActive(spirit), $"{state} 中に成長演出が動いている");
                }
                if (isMoving)
                    Assert.IsFalse(FlourishActive(spirit), "移動中に成長演出が動いている");

                yield return null;
            }

            Assert.IsTrue(sawNonIdle, "観察中に一度もIdle以外へ遷移しなかった（検証が成立していない）");
        }

        // ══ 5〜6. 安全なIdleで1回だけ／1回のIdleにつき1段階 ═════════════

        [UnityTest]
        public IEnumerator Flourish_AdvancesOnlyOneStagePerIdle()
        {
            ForestSpirit spirit = null;
            // ★閾値を同値にすると、1回の刺激でSproutからBloomまで一気に到達する
            //   （ComputeGrowthStageは重複閾値でも単調性を保つ）。
            //   段階の途中でIdleを跨いでしまう競合を避けて、確実に2段階跨ぎを作れる。
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 2f, s => spirit = s);

            PublishFlowerNear(spirit);   // 累積体験 1→2
            yield return null;

            Assert.AreEqual(SpiritGrowthStage.Bloom, Pending(spirit), "Bloomが予約されていない");
            Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage, "予約時点で段階が確定してしまった");

            // 最初の安全なIdleでは1段階（Sprout→Fluff）だけ進む。
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f,
                "最初のIdleでFluffへ進まなかった");

            Assert.AreEqual(SpiritGrowthStage.Bloom, Pending(spirit), "残りの予約が失われた");
            Assert.IsTrue((bool)GetField(spirit, "_growthFlourishConsumedThisIdle"),
                "このIdleで演出済みのフラグが立っていない");

            // 同じIdle滞在中は2段階目へ進まない。
            while (spirit.CurrentState == SpiritState.Idle)
            {
                Assert.AreEqual(SpiritGrowthStage.Fluff, spirit.GrowthStage,
                    "同じIdle滞在中に2段階目まで進んでしまった");
                yield return null;
            }

            // 次にIdleへ入ったとき、残りのpendingが消化される。
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Bloom, 45f,
                "次のIdleでBloomへ進まなかった");

            Assert.AreEqual(9, ActiveFluffCount(spirit), "Bloomの毛玉数になっていない");
            Assert.AreEqual(0.10f * 1.20f, ActiveFluffScale(spirit), 0.0001f, "Bloomのサイズになっていない");
        }

        [UnityTest]
        public IEnumerator Flourish_DoesNotRefire_AtSameStage()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 99f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f, "Fluffへ進まなかった");
            yield return WaitUntil(() => !FlourishActive(spirit), 5f, "演出が終わらなかった");

            // pendingが追いついている以上、以後は何度Idleを経ても演出は起きない。
            float deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Assert.IsFalse(FlourishActive(spirit), "同じ段階で成長演出が再発火した");
                Assert.AreEqual(SpiritGrowthStage.Fluff, spirit.GrowthStage, "段階が勝手に変わった");
                yield return null;
            }
        }

        // ══ 7〜8. 頂点の前後での中断 ════════════════════════════════════

        [UnityTest]
        public IEnumerator Flourish_InterruptedBeforeApex_LeavesStageUncommitted()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 99f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;
            yield return WaitUntil(() => FlourishActive(spirit), 30f, "成長演出が始まらなかった");

            // 頂点(p>=0.5)より前であることを確認してから割り込む。
            Assert.IsFalse(FlourishApplied(spirit), "既に頂点を越えていた（テストが成立していない）");
            Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage);

            PublishFlowerNear(spirit);   // Reactで割り込む
            yield return null;

            Assert.AreEqual(SpiritState.React, spirit.CurrentState, "Reactへ割り込めなかった");
            Assert.IsFalse(FlourishActive(spirit), "演出フラグが残っている");
            Assert.AreEqual(SpiritGrowthStage.Sprout, spirit.GrowthStage, "頂点前なのに段階が確定した");
            Assert.AreEqual(4, ActiveFluffCount(spirit), "頂点前なのに見た目が変わった");
            Assert.AreEqual(SpiritGrowthStage.Fluff, Pending(spirit), "予約が失われた");

            // 次の安全なIdleで最初からやり直され、最終的に成長する。
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f,
                "中断後に再演出されなかった");
            Assert.AreEqual(6, ActiveFluffCount(spirit));
        }

        [UnityTest]
        public IEnumerator Flourish_InterruptedAfterApex_KeepsCommittedStageAndVisual()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 99f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;
            yield return WaitUntil(() => FlourishActive(spirit), 30f, "成長演出が始まらなかった");

            // 頂点を越えるまで待つ（段階と見た目がここで確定する）。
            yield return WaitUntil(() => FlourishApplied(spirit) || !FlourishActive(spirit), 5f,
                "頂点に到達しなかった");

            if (!FlourishActive(spirit))
            {
                // 演出が最後まで終わっていた場合も、結果は確定しているはず。
                Assert.AreEqual(SpiritGrowthStage.Fluff, spirit.GrowthStage);
                Assert.AreEqual(6, ActiveFluffCount(spirit));
                yield break;
            }

            Assert.AreEqual(SpiritGrowthStage.Fluff, spirit.GrowthStage, "頂点で段階が確定していない");
            Assert.AreEqual(6, ActiveFluffCount(spirit), "頂点で見た目が適用されていない");

            PublishFlowerNear(spirit);   // 頂点後に割り込む
            yield return null;

            Assert.IsFalse(FlourishActive(spirit), "演出フラグが残っている");
            Assert.AreEqual(SpiritGrowthStage.Fluff, spirit.GrowthStage, "確定済みの段階が巻き戻った");
            Assert.AreEqual(6, ActiveFluffCount(spirit), "適用済みの見た目が失われた");
            Assert.AreEqual(0.10f * 1.00f, ActiveFluffScale(spirit), 0.0001f);
        }

        // ══ 9. 演出後に一時姿勢が完全に戻る ═════════════════════════════

        [UnityTest]
        public IEnumerator Flourish_LeavesNoTemporaryPose_AndGrowthVisualSurvivesReset()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 99f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f, "成長しなかった");
            yield return WaitUntil(() => !FlourishActive(spirit), 5f, "演出が終わらなかった");
            yield return null;

            var bodyRoot = (Transform)GetField(spirit, "_bodyRoot");
            Assert.AreEqual(Vector3.one,       bodyRoot.localScale,    "一時的な変形が残っている");
            Assert.AreEqual(Quaternion.identity, bodyRoot.localRotation, "一時的な回転が残っている");
            Assert.AreEqual(Vector3.zero,      bodyRoot.localPosition, "一時的な位置が残っている");

            // ★ResetVisualPoseはVisualルートだけを戻すため、成長した毛玉は元に戻らない。
            typeof(ForestSpirit).GetMethod("ResetVisualPose", Priv).Invoke(spirit, null);

            Assert.AreEqual(6, ActiveFluffCount(spirit), "ResetVisualPoseが毛玉の数を戻してしまった");
            Assert.AreEqual(0.10f * 1.00f, ActiveFluffScale(spirit), 0.0001f,
                "ResetVisualPoseが毛玉のサイズを戻してしまった");
        }

        // ══ 10. Stage 11〜13の保証が維持されている ══════════════════════

        [UnityTest]
        public IEnumerator Growth_DoesNotBreakStimulusMemoryOrPersonalityGuarantees()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 3f, s => spirit = s);

            var memory = (SpiritMemory)GetField(spirit, "_memory");

            // Stage 13: 性格は固定されたまま
            Assert.AreEqual(SpiritPersonalityKind.Curious, spirit.Personality);

            // Stage 12: 生成時刺激が1回ぶん記憶されている（Curiousのgain=0.6）。
            // ★Familiarityは実時間で減衰するため厳密一致では見ない。
            //   1回ぶん(0.6)を超えず、かつ薄れきってもいないことを確認する。
            //   一方、累積体験は減衰しないので厳密に1のままでなければならない。
            float familiarity = memory.GetFamiliarity(SpiritStimulusKind.ForestGrew, Time.time, 60f);
            Assert.Greater(familiarity, 0.4f,      $"生成時刺激が記憶されていない（{familiarity:F3}）");
            Assert.LessOrEqual(familiarity, 0.61f, $"1回ぶん(0.6)を超えて記憶された（{familiarity:F3}）");
            Assert.AreEqual(1f, memory.GetLifetimeExperience(), 0.0001f,
                "累積体験は減衰せず1のままであるべき");

            // Stage 11: 遠方の刺激は受理されない → Familiarityも累積体験も増えない
            EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(
                SpiritStimulusKind.FlowerBloomed, spirit.transform.position + new Vector3(60f, 0f, 60f), null)));
            yield return null;

            Assert.AreEqual(0f, memory.GetFamiliarity(SpiritStimulusKind.FlowerBloomed, Time.time, 60f), 0.001f,
                "遠方の刺激が受理された");
            Assert.AreEqual(1f, memory.GetLifetimeExperience(), 0.0001f,
                "遠方の刺激で累積体験が増えた");

            // 成長しても性格は変わらない
            PublishFlowerNear(spirit);
            yield return null;
            yield return WaitUntil(() => spirit.GrowthStage != SpiritGrowthStage.Sprout, 30f, "成長しなかった");

            Assert.AreEqual(SpiritPersonalityKind.Curious, spirit.Personality, "成長で性格が変わった");
        }

        [UnityTest]
        public IEnumerator DestroyingGrownSpirit_LeavesNoSubscription()
        {
            ForestSpirit spirit = null;
            yield return SpawnSpirit(SpiritPersonalityKind.Curious, 2f, 99f, s => spirit = s);

            PublishFlowerNear(spirit);
            yield return null;
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f, "成長しなかった");

            Assert.AreEqual(1, SubscriberCount<SpiritStimulusEvent>(), "購読されていない");

            Object.DestroyImmediate(spirit.gameObject);
            yield return null;

            Assert.AreEqual(0, SubscriberCount<SpiritStimulusEvent>(), "破棄後も購読が残っている");
        }
    }
}
