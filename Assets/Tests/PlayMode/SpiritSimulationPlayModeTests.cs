// 役割: Stage 15「Settings中の完全停止と自然な再開」を実ライフサイクルで検証する。
//       ★Spawnerに依存しない
//         止まるかどうかはForestSpirit個体の責務なので、生成条件（SpiritSpawnPolicy）とは
//         切り離し、精霊を直接Initializeして検証する。
//       ★Phase1_v002.unity は開かない（Scene差分が残る危険を避けるため）。

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
    public class SpiritSimulationPlayModeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _spawned = new();

        private static void ClearEventBus()
        {
            var f = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "EventBus._handlers が見つかりません");
            ((System.Collections.IDictionary)f.GetValue(null)).Clear();
        }

        [SetUp]
        public void SetUp()
        {
            ClearEventBus();
            GameInteractionStateController.SetState(GameInteractionState.Playing);
        }

        [TearDown]
        public void TearDown()
        {
            // 操作状態がテストを跨いで残ると、以降のテストが止まったまま動かなくなる。
            GameInteractionStateController.SetState(GameInteractionState.Playing);
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearEventBus();
        }

        // ── ヘルパー ──────────────────────────────────────────────────

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        private List<HexTile> MakeForest(Vector3 origin, int count)
        {
            var root = Track(new GameObject("Forest"));
            var tiles = new List<HexTile>();

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Tile" + i);
                go.transform.SetParent(root.transform, true);
                var tile = go.AddComponent<HexTile>();
                tile.Initialize(new HexCoord(i, -i), 1f);
                go.transform.position = origin + new Vector3(i * 1.5f, 0f, (i % 2) * 0.866f);
                tiles.Add(tile);
            }
            return tiles;
        }

        /// <summary>Spawnerを介さず精霊を直接作る（生成条件はこのテストの関心事ではない）。</summary>
        private ForestSpirit MakeSpirit(SpiritPersonalityKind kind = SpiritPersonalityKind.Calm)
        {
            var home = MakeForest(Vector3.zero, 4);
            var go = Track(new GameObject("ForestSpirit"));
            var spirit = go.AddComponent<ForestSpirit>();   // 有効なGameObjectなのでOnEnableはUnityが発火する
            spirit.Initialize(home, Vector3.zero, 1.5f, 1.5f, 0.5f, kind);
            return spirit;
        }

        private static object GetField(object t, string n) => t.GetType().GetField(n, Priv).GetValue(t);

        /// <summary>
        /// 停止可能な個体時計。productionでは外部へ公開する用途が無いためprivateのままにし、
        /// 観測はここからリフレクションで行う（テスト専用のpublic APIをproductionへ足さない）。
        /// </summary>
        private static float SimulationTimeOf(ForestSpirit s) => (float)GetField(s, "_simulationTime");

        private static float FamiliarityOf(ForestSpirit s, SpiritStimulusKind kind)
        {
            var memory   = GetField(s, "_memory");
            var halfLife = (float)GetField(s, "_familiarityHalfLife");

            // 記憶の時刻基準は個体時計。Time.timeで問い合わせると余計に減衰した値が返る。
            return (float)memory.GetType().GetMethod("GetFamiliarity")
                .Invoke(memory, new object[] { kind, SimulationTimeOf(s), halfLife });
        }

        private static void PublishFlowerNear(ForestSpirit spirit)
            => EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(
                   SpiritStimulusKind.FlowerBloomed, spirit.transform.position + new Vector3(0.5f, 0f, 0f), null)));

        private static IEnumerator WaitUntil(System.Func<bool> cond, float timeout, string msg)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!cond())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, msg);
                yield return null;
            }
        }

        // ══ PauseMenu中は動き続ける ═════════════════════════════════════

        [UnityTest]
        public IEnumerator PauseMenu_KeepsSpiritRunning()
        {
            var spirit = MakeSpirit();
            yield return null;

            GameInteractionStateController.SetState(GameInteractionState.PauseMenu);

            float before = SimulationTimeOf(spirit);
            for (int i = 0; i < 10; i++) yield return null;

            Assert.Greater(SimulationTimeOf(spirit), before,
                "PauseMenu中は精霊が動き続けるべき（既存Critterと揃える）");
        }

        // ══ Settings中は完全停止し、解除後は停止地点から再開する ════════

        [UnityTest]
        public IEnumerator Settings_FreezesEverything_AndResumesFromWhereItStopped()
        {
            var spirit = MakeSpirit();
            yield return null;
            yield return null;

            var stateBefore     = spirit.CurrentState;
            float timeBefore    = SimulationTimeOf(spirit);
            float elapsedBefore = (float)GetField(spirit, "_stateElapsed");
            var posBefore       = spirit.transform.position;

            GameInteractionStateController.SetState(GameInteractionState.Settings);

            // 実フレームを十分進めても、精霊側は何も進んではいけない。
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(timeBefore, SimulationTimeOf(spirit), 0.00001f, "Settings中に個体時計が進んだ");
            Assert.AreEqual(elapsedBefore, (float)GetField(spirit, "_stateElapsed"), 0.00001f,
                "Settings中に状態の経過時間が進んだ");
            Assert.AreEqual(stateBefore, spirit.CurrentState, "Settings中に状態が遷移した");
            Assert.AreEqual(posBefore, spirit.transform.position, "Settings中に精霊が動いた");

            GameInteractionStateController.SetState(GameInteractionState.Playing);
            yield return null;
            yield return null;

            Assert.Greater(SimulationTimeOf(spirit), timeBefore, "解除後に再開しなかった");
            Assert.AreEqual(stateBefore, spirit.CurrentState,
                "Settingsからの復帰が状態のキャンセル扱いになっている");
        }

        [UnityTest]
        public IEnumerator Settings_DoesNotDecayFamiliarity()
        {
            var spirit = MakeSpirit();
            yield return null;

            PublishFlowerNear(spirit);
            yield return null;
            Assert.Greater(FamiliarityOf(spirit, SpiritStimulusKind.FlowerBloomed), 0f, "記憶されていない");

            float before = FamiliarityOf(spirit, SpiritStimulusKind.FlowerBloomed);

            GameInteractionStateController.SetState(GameInteractionState.Settings);
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(before, FamiliarityOf(spirit, SpiritStimulusKind.FlowerBloomed), 0.00001f,
                "Settings中にFamiliarityだけが薄れた（時計が止まっていない）");
        }

        [UnityTest]
        public IEnumerator Settings_DoesNotCompleteReact()
        {
            var spirit = MakeSpirit();
            yield return null;

            PublishFlowerNear(spirit);
            yield return null;
            Assert.AreEqual(SpiritState.React, spirit.CurrentState, "Reactへ入っていない");

            float elapsedBefore = (float)GetField(spirit, "_stateElapsed");

            GameInteractionStateController.SetState(GameInteractionState.Settings);
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(SpiritState.React, spirit.CurrentState, "Settings中にReactが勝手に完了した");
            Assert.AreEqual(elapsedBefore, (float)GetField(spirit, "_stateElapsed"), 0.00001f,
                "Settings中にReactの経過が進んだ");

            GameInteractionStateController.SetState(GameInteractionState.Playing);
            yield return WaitUntil(() => spirit.CurrentState != SpiritState.React, 10f,
                "解除後にReactが再開・完了しなかった");
        }

        [UnityTest]
        public IEnumerator Settings_DoesNotAdvanceGrowthFlourish()
        {
            var spirit = MakeSpirit(SpiritPersonalityKind.Curious);
            yield return null;

            // 短時間で成長させるため閾値を下げる（productionへテストモードは足さない）。
            typeof(ForestSpirit).GetField("_growthThresholdFluff", Priv).SetValue(spirit, 1f);
            typeof(ForestSpirit).GetField("_growthThresholdBloom", Priv).SetValue(spirit, 99f);

            PublishFlowerNear(spirit);
            yield return null;

            yield return WaitUntil(() => (bool)GetField(spirit, "_growthFlourishActive"), 30f,
                "成長演出が始まらなかった");

            float flourishBefore = (float)GetField(spirit, "_growthFlourishElapsed");
            var stageBefore = spirit.GrowthStage;

            GameInteractionStateController.SetState(GameInteractionState.Settings);
            for (int i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(flourishBefore, (float)GetField(spirit, "_growthFlourishElapsed"), 0.00001f,
                "Settings中に成長演出が進んだ");
            Assert.IsTrue((bool)GetField(spirit, "_growthFlourishActive"),
                "Settings中に成長演出がキャンセルされた");
            Assert.AreEqual(stageBefore, spirit.GrowthStage, "Settings中に成長段階が確定した");

            GameInteractionStateController.SetState(GameInteractionState.Playing);
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 15f,
                "解除後に成長演出が再開・完了しなかった");
        }
    }
}
