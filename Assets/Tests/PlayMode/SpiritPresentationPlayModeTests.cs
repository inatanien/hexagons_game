// 役割: Stage 16「誕生・成長演出」を実ライフサイクルで検証する。
//       ★演出の一回性・停止と再開・スケール合成・Collider/Raycastへの非干渉に絞る。
//       Phase1_v002は開かず、最小Hierarchyを構築する方針を維持する。

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
    public class SpiritPresentationPlayModeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _spawned = new();
        private readonly List<WorldNoticeEvent> _notices = new();
        private System.Action<WorldNoticeEvent> _noticeHandler;

        private static void ClearEventBus()
        {
            var f = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(f, "EventBus._handlers が見つかりません");
            ((System.Collections.IDictionary)f.GetValue(null)).Clear();
        }

        private static int SubscriberCount<T>()
        {
            var f = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            var dict = (System.Collections.IDictionary)f.GetValue(null);
            if (!dict.Contains(typeof(T))) return 0;
            return ((System.Delegate)dict[typeof(T)]).GetInvocationList().Length;
        }

        [SetUp]
        public void SetUp()
        {
            ClearEventBus();
            GameInteractionStateController.SetState(GameInteractionState.Playing);

            _notices.Clear();
            _noticeHandler = e => _notices.Add(e);
            EventBus.Subscribe(_noticeHandler);
        }

        [TearDown]
        public void TearDown()
        {
            if (_noticeHandler != null) { EventBus.Unsubscribe(_noticeHandler); _noticeHandler = null; }

            GameInteractionStateController.SetState(GameInteractionState.Playing);
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearEventBus();
        }

        // ── ヘルパー ──────────────────────────────────────────────────

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        private List<HexTile> MakeForest(string name, Vector3 origin, int count)
        {
            var root = Track(new GameObject(name));
            var tiles = new List<HexTile>();
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject(name + "_Tile" + i);
                go.transform.SetParent(root.transform, true);
                var tile = go.AddComponent<HexTile>();
                tile.Initialize(new HexCoord(i, -i), 1f);
                go.transform.position = origin + new Vector3(i * 1.5f, 0f, (i % 2) * 0.866f);
                tiles.Add(tile);
            }
            return tiles;
        }

        /// <summary>本編と同じ構成（Spirits GameObject 1つにSpawner・Relay・NoticePresenter）。</summary>
        private ForestSpiritSpawner MakeSpiritsSystem()
        {
            var go = Track(new GameObject("Spirits"));
            var spawner = go.AddComponent<ForestSpiritSpawner>();
            go.AddComponent<SpiritStimulusRelay>();
            go.AddComponent<SpiritNoticePresenter>();

            typeof(ForestSpiritSpawner).GetField("_minClusterSizeToSpawn", Priv).SetValue(spawner, 4);
            return spawner;
        }

        private static void PublishForestGrowth(IReadOnlyList<HexTile> tiles)
            => EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                   null, HexCoord.Zero, tiles, new ForestGrowthMetrics(tiles.Count, tiles.Count)));

        private static void PublishFlowerNear(ForestSpirit spirit)
            => EventBus.Publish(new SpiritStimulusEvent(new SpiritStimulus(
                   SpiritStimulusKind.FlowerBloomed, spirit.transform.position + new Vector3(0.5f, 0f, 0f), null)));

        private static ForestSpirit SpiritOf(ForestSpiritSpawner s) => s.GetComponentInChildren<ForestSpirit>(true);
        private static object GetField(object t, string n) => t.GetType().GetField(n, Priv).GetValue(t);
        private static Transform BodyRootOf(ForestSpirit s) => (Transform)GetField(s, "_bodyRoot");
        private static ForestSpiritPresentation PresentationOf(ForestSpirit s)
            => s.GetComponent<ForestSpiritPresentation>();

        private static IEnumerator WaitUntil(System.Func<bool> cond, float timeout, string msg)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!cond())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, msg);
                yield return null;
            }
        }

        // ══ 誕生演出 ════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator Birth_StartsSmall_AndSettlesBackToNormalSize()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);

            var spirit = SpiritOf(spawner);
            Assert.IsNotNull(spirit, "精霊が生成されなかった");

            var presentation = PresentationOf(spirit);
            Assert.IsNotNull(presentation, "Presentationが自動で付与されていない");
            Assert.IsTrue(presentation.IsPlayingBirth, "誕生演出が始まっていない");

            var bodyRoot = BodyRootOf(spirit);
            Assert.IsNotNull(bodyRoot);
            Assert.Less(bodyRoot.localScale.x, 0.5f,
                $"生まれた瞬間は小さく始まるべき（実測 {bodyRoot.localScale.x:F3}）");
            Assert.Greater(bodyRoot.localScale.x, 0f, "0スケールにしてはいけない");

            yield return WaitUntil(() => !presentation.IsPlayingBirth, 10f, "誕生演出が終わらなかった");
            yield return null;

            Assert.AreEqual(1f, presentation.BirthScaleMultiplier, 0.0001f, "終了後の倍率が1でない");
        }

        [UnityTest]
        public IEnumerator Birth_DoesNotReplay_WhenForestKeepsGrowing()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            var spirit = SpiritOf(spawner);
            var presentation = PresentationOf(spirit);

            yield return WaitUntil(() => !presentation.IsPlayingBirth, 10f, "誕生演出が終わらなかった");

            // 森が育っても誕生演出は二度と始まらない。
            for (int extra = 0; extra < 3; extra++)
            {
                var grown = new List<HexTile>(forest);
                grown.AddRange(MakeForest("Ext" + extra, new Vector3(-3f - extra * 1.5f, 0f, 0f), 2));
                PublishForestGrowth(grown);
                yield return null;

                Assert.IsFalse(presentation.IsPlayingBirth, $"{extra + 1}回目の成長で誕生演出が再生された");
            }
        }

        [UnityTest]
        public IEnumerator Birth_FreezesDuringSettings_AndResumesFromWhereItStopped()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            var spirit = SpiritOf(spawner);
            var presentation = PresentationOf(spirit);
            yield return null;

            Assert.IsTrue(presentation.IsPlayingBirth, "誕生演出が始まっていない");

            GameInteractionStateController.SetState(GameInteractionState.Settings);
            float before = presentation.BirthScaleMultiplier;

            for (int i = 0; i < 30; i++) yield return null;

            Assert.IsTrue(presentation.IsPlayingBirth, "Settings中に誕生演出がキャンセルされた");
            Assert.AreEqual(before, presentation.BirthScaleMultiplier, 0.00001f,
                "Settings中に誕生演出が進んだ");

            GameInteractionStateController.SetState(GameInteractionState.Playing);
            yield return null;
            yield return null;

            Assert.Greater(presentation.BirthScaleMultiplier, before, "解除後に再開しなかった");
            yield return WaitUntil(() => !presentation.IsPlayingBirth, 10f, "解除後に完了しなかった");
        }

        [UnityTest]
        public IEnumerator Birth_ContinuesDuringPauseMenu()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            var presentation = PresentationOf(SpiritOf(spawner));
            yield return null;

            GameInteractionStateController.SetState(GameInteractionState.PauseMenu);
            float before = presentation.BirthScaleMultiplier;

            for (int i = 0; i < 10; i++) yield return null;

            Assert.Greater(presentation.BirthScaleMultiplier, before,
                "PauseMenu中は誕生演出も進むべき");
        }

        [UnityTest]
        public IEnumerator Birth_DoesNotBreakReact_WhenStimulusArrivesDuringPresentation()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            var spirit = SpiritOf(spawner);
            var presentation = PresentationOf(spirit);
            yield return null;

            // 生成直後はReact中（生成時刺激）。誕生演出と同時進行しても壊れないこと。
            Assert.AreEqual(SpiritState.React, spirit.CurrentState, "生成時刺激でReactへ入っていない");
            Assert.IsTrue(presentation.IsPlayingBirth, "誕生演出が動いていない");

            var bodyRoot = BodyRootOf(spirit);
            for (int i = 0; i < 20; i++)
            {
                Assert.IsTrue(float.IsFinite(bodyRoot.localScale.x), "スケールが壊れた");
                Assert.Greater(bodyRoot.localScale.x, 0f, "スケールが0以下になった");
                yield return null;
            }

            // 誕生が終わった後、状態演出のスケールが1へ収まる。
            yield return WaitUntil(() => !presentation.IsPlayingBirth, 10f, "誕生演出が終わらなかった");
            yield return WaitUntil(() => spirit.CurrentState != SpiritState.React, 10f, "Reactが終わらなかった");
            yield return null;

            Assert.AreEqual(1f, bodyRoot.localScale.x, 0.05f,
                "誕生後に通常の大きさへ戻っていない");
        }

        // ══ 通知の一回性 ════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator BirthNotice_IsPublishedExactlyOnce()
        {
            var spawner = MakeSpiritsSystem();
            yield return null;

            // 3枚までは通知も出ない。
            for (int size = 1; size <= 3; size++)
            {
                PublishForestGrowth(MakeForest("F" + size, new Vector3(size * 20f, 0f, 0f), size));
                yield return null;
                Assert.AreEqual(0, _notices.Count, $"クラスタ{size}枚で通知が出た");
            }

            PublishForestGrowth(MakeForest("Home", Vector3.zero, 4));
            yield return null;

            Assert.AreEqual(1, _notices.Count, "誕生通知がちょうど1回出るべき");
            Assert.AreEqual(SpiritNoticeText.BirthBody, _notices[0].Body);
            Assert.AreEqual(WorldNoticeKind.Spirit, _notices[0].Kind);
        }

        [UnityTest]
        public IEnumerator GrowthNotice_OnlyOnBloom()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.AreEqual(1, _notices.Count, "誕生通知が出ていない");

            // 閾値を同値にして、1回の刺激でSprout→Bloomまで予約させる。
            typeof(ForestSpirit).GetField("_growthThresholdFluff", Priv).SetValue(spirit, 2f);
            typeof(ForestSpirit).GetField("_growthThresholdBloom", Priv).SetValue(spirit, 2f);

            yield return WaitUntil(() => spirit.CurrentState == SpiritState.Idle, 15f, "Idleへ戻らなかった");
            PublishFlowerNear(spirit);
            yield return null;

            // Fluffへの成長では通知が増えない。
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Fluff, 30f, "Fluffへ成長しなかった");
            Assert.AreEqual(1, _notices.Count, "Sprout→Fluffで通知が出てしまった");

            // Bloom到達でちょうど1回増える。
            yield return WaitUntil(() => spirit.GrowthStage == SpiritGrowthStage.Bloom, 45f, "Bloomへ成長しなかった");
            yield return null;

            Assert.AreEqual(2, _notices.Count, "Bloom到達の通知が1回でない");
            Assert.AreEqual(SpiritNoticeText.BloomBody, _notices[1].Body);
        }

        [UnityTest]
        public IEnumerator GrowthNotice_NotPublished_WhenInterruptedBeforeMidpoint()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;

            var spirit = SpiritOf(spawner);
            typeof(ForestSpirit).GetField("_growthThresholdFluff", Priv).SetValue(spirit, 2f);
            typeof(ForestSpirit).GetField("_growthThresholdBloom", Priv).SetValue(spirit, 2f);

            yield return WaitUntil(() => spirit.CurrentState == SpiritState.Idle, 15f, "Idleへ戻らなかった");
            PublishFlowerNear(spirit);
            yield return null;

            yield return WaitUntil(() => (bool)GetField(spirit, "_growthFlourishActive"), 30f,
                "成長演出が始まらなかった");
            Assert.IsFalse((bool)GetField(spirit, "_growthAppliedThisFlourish"),
                "既に頂点を越えていた（テストが成立していない）");

            int before = _notices.Count;
            PublishFlowerNear(spirit);   // 頂点前にReactで割り込む
            yield return null;

            Assert.AreEqual(before, _notices.Count, "頂点前の中断で通知が出た");
        }

        // ══ Collider / Raycast への非干渉 ═══════════════════════════════

        [UnityTest]
        public IEnumerator Presentation_AddsNoColliders()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            var spirit = SpiritOf(spawner);

            // VFXを実際に出してからも確認する。
            var presentation = PresentationOf(spirit);
            typeof(ForestSpiritPresentation).GetMethod("PlayGrowth", Priv)
                .Invoke(presentation, new object[] { SpiritGrowthStage.Bloom });
            yield return null;
            yield return null;

            var colliders = spirit.GetComponentsInChildren<Collider>(true);
            Assert.AreEqual(0, colliders.Length,
                $"演出でColliderが{colliders.Length}個増えた（タイル操作を妨げる）");
        }

        [UnityTest]
        public IEnumerator TileRaycast_StillReachesTile_WithVfxPlaying()
        {
            var tileGo = Track(new GameObject("RaycastTargetTile"));
            var tile   = tileGo.AddComponent<HexTile>();
            tile.Initialize(HexCoord.Zero, 1f);
            tileGo.transform.position = Vector3.zero;
            var box = tileGo.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 0.2f, 2f);

            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", new Vector3(10f, 0f, 10f), 4);
            yield return null;

            PublishForestGrowth(forest);
            var spirit = SpiritOf(spawner);
            spirit.transform.position = new Vector3(0f, 1f, 0f);
            yield return null;
            yield return null;

            var ray = new Ray(new Vector3(0f, 5f, 0f), Vector3.down);
            Assert.IsTrue(Physics.Raycast(ray, out RaycastHit hit), "Raycastが当たらなかった");
            Assert.AreSame(tile, hit.collider.GetComponentInParent<HexTile>(),
                "演出中の精霊がRaycastを遮った");
        }

        // ══ 後始末 ══════════════════════════════════════════════════════

        [UnityTest]
        public IEnumerator DestroyingSpiritsSystem_LeavesNoSubscriptionsOrLeftovers()
        {
            var spawner = MakeSpiritsSystem();
            var forest  = MakeForest("Home", Vector3.zero, 4);
            yield return null;

            PublishForestGrowth(forest);
            yield return null;

            Assert.AreEqual(1, SubscriberCount<ForestSpiritSpawnedEvent>(), "NoticePresenterが購読していない");
            Assert.AreEqual(1, SubscriberCount<ForestSpiritGrowthCommittedEvent>(), "NoticePresenterが購読していない");

            var spiritGo = SpiritOf(spawner).gameObject;
            Object.DestroyImmediate(spawner.gameObject);
            yield return null;

            Assert.AreEqual(0, SubscriberCount<ForestSpiritSpawnedEvent>(), "破棄後も購読が残っている");
            Assert.AreEqual(0, SubscriberCount<ForestSpiritGrowthCommittedEvent>(), "破棄後も購読が残っている");
            Assert.AreEqual(0, SubscriberCount<SpiritStimulusEvent>(), "破棄後も精霊の購読が残っている");
            Assert.IsTrue(spiritGo == null, "精霊のGameObjectが残骸として残っている");
        }
    }
}
