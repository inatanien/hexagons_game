// 役割: Stage 13「性格」を、実際のUnityライフサイクルを通して検証する。
//       ★EditModeテストとの役割分担
//         EditModeはリフレクションでOnEnable/EnterStateを手動発火させるため、
//         Stage 12では「AddComponentではOnEnableが走らない」ことが原因の
//         購読順依存バグを取りこぼした前例がある。
//         ここでは一切手動発火せず、GameObjectを実際に有効化し、Unity自身に
//         OnEnableを発火させ、実フレームを進め、EventBusの実経路だけを使う。
//         したがって内部状態の直接操作（EnterStateの手動呼び出し等）は行わない。

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
    public class SpiritPersonalityPlayModeTests
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly List<GameObject> _spawned = new();

        // ── EventBusのstate持ち越し防止 ────────────────────────────────
        //    EventBusはstaticなため、テストが失敗して破棄が漏れると次のテストへ
        //    購読が残る。production側にClear APIを足さず、テスト側でのみリセットする。
        private static void ClearEventBus()
        {
            var field = typeof(EventBus).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "EventBus._handlers が見つかりません（実装が変わった可能性）");
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
            // GameObjectを破棄することでOnDisableが実際に発火し、購読が解除される。
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ClearEventBus();
        }

        // ── ヘルパー（内部状態は読み取るだけで、状態遷移を手動で起こさない） ──

        private GameObject Track(GameObject go) { _spawned.Add(go); return go; }

        private List<HexTile> MakeForest(string name, Vector3 origin)
        {
            var root = Track(new GameObject(name));
            var tiles = new List<HexTile>();

            // 隣接した3枚で1つの森クラスターを作る。
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

        /// <summary>Spawnerを実際に有効なGameObjectとして生成する（OnEnableはUnityが発火する）。</summary>
        private ForestSpiritSpawner MakeSpawner(string name,
            ForestSpiritSpawner.PersonalitySelectionMode mode,
            SpiritPersonalityKind fixedKind = SpiritPersonalityKind.Calm)
        {
            var go = Track(new GameObject(name));
            var spawner = go.AddComponent<ForestSpiritSpawner>();
            typeof(ForestSpiritSpawner).GetField("_personalityMode", Priv).SetValue(spawner, mode);
            typeof(ForestSpiritSpawner).GetField("_fixedPersonality", Priv).SetValue(spawner, fixedKind);
            return spawner;
        }

        private SpiritStimulusRelay MakeRelay(string name)
            => Track(new GameObject(name)).AddComponent<SpiritStimulusRelay>();

        private static void PublishForestGrowth(IReadOnlyList<HexTile> tiles)
            => EventBus.Publish(new TerrainGrowthEvent<ForestGrowthMetrics>(
                   terrainType: null, anchor: HexCoord.Zero, affectedTiles: tiles,
                   metrics: new ForestGrowthMetrics(tiles.Count, tiles.Count)));

        private static ForestSpirit SpiritOf(ForestSpiritSpawner spawner)
            => spawner.GetComponentInChildren<ForestSpirit>(true);

        private static float FamiliarityOf(ForestSpirit spirit, SpiritStimulusKind kind)
        {
            var memory   = spirit.GetType().GetField("_memory", Priv).GetValue(spirit);
            var halfLife = (float)spirit.GetType().GetField("_familiarityHalfLife", Priv).GetValue(spirit);

            // ★Stage 15以降、記憶の時刻基準は精霊の個体時計（Settings中は止まる）。
            //   Time.timeで問い合わせると、セッション経過ぶんだけ余計に減衰した値が返る。
            //   個体時計はproductionで公開する用途が無いためprivateのままで、ここから観測する。
            float simulationTime = (float)spirit.GetType().GetField("_simulationTime", Priv).GetValue(spirit);

            return (float)memory.GetType().GetMethod("GetFamiliarity")
                .Invoke(memory, new object[] { kind, simulationTime, halfLife });
        }

        private static float StateDurationOf(ForestSpirit spirit)
            => (float)spirit.GetType().GetField("_stateDuration", Priv).GetValue(spirit);

        /// <summary>実フレームを進めながら指定状態になるのを待つ（手動遷移はしない）。</summary>
        private static IEnumerator WaitForState(ForestSpirit spirit, SpiritState state, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (spirit != null && spirit.CurrentState != state)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline,
                    $"{timeoutSeconds}秒以内に{state}へ遷移しなかった（現在 {spirit.CurrentState}）");
                yield return null;
            }
        }

        // ══ 1. Fixedモードで指定した性格が保持される ════════════════════

        [UnityTest]
        public IEnumerator FixedMode_SpawnedSpirit_KeepsConfiguredPersonality()
        {
            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
            {
                var spawner = MakeSpawner("Spawner_" + kind,
                    ForestSpiritSpawner.PersonalitySelectionMode.Fixed, kind);
                var forest = MakeForest("Forest_" + kind, new Vector3(0f, 0f, 0f));

                yield return null; // OnEnableをUnityに発火させてから発行する

                PublishForestGrowth(forest);
                yield return null;

                var spirit = SpiritOf(spawner);
                Assert.IsNotNull(spirit, $"{kind}: 精霊が生成されなかった");
                Assert.AreEqual(kind, spirit.Personality, $"{kind}: 指定した性格が保持されていない");

                // 実際にProfileが解決され、Visualまで構築されている（Initializeを通っている）。
                Assert.IsNotNull(spirit.GetType().GetField("_bodyRoot", Priv).GetValue(spirit),
                    $"{kind}: Visualが未構築");

                TearDown();
                SetUp();
            }
        }

        // ══ 2. DeterministicFromHomeは毎回同じ性格になる ════════════════

        [UnityTest]
        public IEnumerator DeterministicMode_SameHome_ProducesSamePersonalityEveryTime()
        {
            var origin = new Vector3(5.5f, 0f, -3.5f);
            var results = new List<SpiritPersonalityKind>();

            for (int run = 0; run < 4; run++)
            {
                var spawner = MakeSpawner("Spawner_Run" + run,
                    ForestSpiritSpawner.PersonalitySelectionMode.DeterministicFromHome);
                var forest = MakeForest("Forest_Run" + run, origin);

                yield return null;
                PublishForestGrowth(forest);
                yield return null;

                var spirit = SpiritOf(spawner);
                Assert.IsNotNull(spirit, $"run{run}: 精霊が生成されなかった");
                results.Add(spirit.Personality);

                TearDown();
                SetUp();
            }

            for (int i = 1; i < results.Count; i++)
                Assert.AreEqual(results[0], results[i],
                    $"同じhome森から生成した性格がぶれた: {string.Join(",", results)}");
        }

        // ══ 3. home成長で性格が再計算されない ═══════════════════════════

        [UnityTest]
        public IEnumerator Personality_IsNotRecalculated_WhenHomeForestGrows()
        {
            var spawner = MakeSpawner("Spawner_Growth",
                ForestSpiritSpawner.PersonalitySelectionMode.Fixed, SpiritPersonalityKind.Curious);
            var forest = MakeForest("Forest_Growth", Vector3.zero);

            yield return null;
            PublishForestGrowth(forest);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.IsNotNull(spirit);
            Assert.AreEqual(SpiritPersonalityKind.Curious, spirit.Personality);

            var profileField = spirit.GetType().GetField("_profile", Priv);
            var before = (SpiritPersonalityProfile)profileField.GetValue(spirit);

            // home森が育つ（元のタイルを含むので自分の森として受理される）。
            // 代表座標が変わる位置へ伸ばしても、性格は再決定されてはならない。
            var grown = new List<HexTile>(forest);
            grown.AddRange(MakeForest("Forest_Growth_Ext", new Vector3(-12.5f, 0f, 7.5f)));

            PublishForestGrowth(grown);
            yield return null;
            yield return null;

            Assert.AreEqual(SpiritPersonalityKind.Curious, spirit.Personality,
                "home成長で性格が変わってしまった");

            var after = (SpiritPersonalityProfile)profileField.GetValue(spirit);
            Assert.AreEqual(before.SleepWeight,       after.SleepWeight,       0.0001f, "Profileが差し替わった");
            Assert.AreEqual(before.IdleDurationScale, after.IdleDurationScale, 0.0001f, "Profileが差し替わった");
            Assert.AreEqual(before.FamiliarityGain,   after.FamiliarityGain,   0.0001f, "Profileが差し替わった");
        }

        // ══ 4. 購読順で生成時刺激も性格も変わらない ═════════════════════

        [UnityTest]
        public IEnumerator SubscriptionOrder_DoesNotChange_SpawnStimulusOrPersonality()
        {
            var personalities = new List<SpiritPersonalityKind>();
            var familiarities = new List<float>();
            var states        = new List<SpiritState>();

            // AddComponentの順＝EventBusの購読順。両方の順序を実際に作る。
            foreach (var relayFirst in new[] { false, true })
            {
                ForestSpiritSpawner spawner;

                if (relayFirst)
                {
                    MakeRelay("Relay_" + relayFirst);
                    spawner = MakeSpawner("Spawner_" + relayFirst,
                        ForestSpiritSpawner.PersonalitySelectionMode.DeterministicFromHome);
                }
                else
                {
                    spawner = MakeSpawner("Spawner_" + relayFirst,
                        ForestSpiritSpawner.PersonalitySelectionMode.DeterministicFromHome);
                    MakeRelay("Relay_" + relayFirst);
                }

                var forest = MakeForest("Forest_" + relayFirst, new Vector3(2.5f, 0f, 4.5f));

                yield return null;

                // RelayとSpawnerの両方が実際に購読していること（順序検証の前提）。
                Assert.AreEqual(2, SubscriberCount<TerrainGrowthEvent<ForestGrowthMetrics>>(),
                    $"relayFirst={relayFirst}: 購読者が2つになっていない");

                PublishForestGrowth(forest);
                yield return null;

                var spirit = SpiritOf(spawner);
                Assert.IsNotNull(spirit, $"relayFirst={relayFirst}: 精霊が生成されなかった");

                personalities.Add(spirit.Personality);
                familiarities.Add(FamiliarityOf(spirit, SpiritStimulusKind.ForestGrew));
                states.Add(spirit.CurrentState);

                TearDown();
                SetUp();
            }

            Assert.AreEqual(personalities[0], personalities[1],
                $"購読順で性格が変わった: {personalities[0]} vs {personalities[1]}");

            Assert.AreEqual(SpiritState.React, states[0], "Spawner先: 生成時刺激でReactへ入っていない");
            Assert.AreEqual(SpiritState.React, states[1], "Relay先: 生成時刺激でReactへ入っていない");

            // 生成時の森は必ず1回ぶんだけ記憶される（二重にも0にもならない）。
            float expected = SpiritPersonalityProfile.For(personalities[0]).FamiliarityGain;
            Assert.AreEqual(expected, familiarities[0], 0.01f,
                $"Spawner先: 生成時刺激が1回ぶん記憶されていない（実測 {familiarities[0]:F3}）");
            Assert.AreEqual(expected, familiarities[1], 0.01f,
                $"Relay先: 生成時刺激が1回ぶん記憶されていない（実測 {familiarities[1]:F3}）");
        }

        // ══ 5. 2つの性格が実際に別のProfile値で動作する ═════════════════

        [UnityTest]
        public IEnumerator CalmAndCurious_ActuallyRunOnDifferentProfileValues()
        {
            // ★純粋関数を呼び直すのではなく、走行中のUpdateが決めたIdle継続時間を読む。
            //   Idleの基準は2〜4秒なので、倍率適用後の範囲は
            //     Calm    (×1.4) = 2.8〜5.6秒
            //     Curious (×0.7) = 1.4〜2.8秒
            //   と重ならない。どちらの帯に入るかで、実行時に各自のProfileが
            //   使われていることが判別できる。
            var measured = new Dictionary<SpiritPersonalityKind, float>();

            foreach (var kind in new[] { SpiritPersonalityKind.Calm, SpiritPersonalityKind.Curious })
            {
                var spawner = MakeSpawner("Spawner_" + kind,
                    ForestSpiritSpawner.PersonalitySelectionMode.Fixed, kind);
                var forest = MakeForest("Forest_" + kind, Vector3.zero);

                yield return null;
                PublishForestGrowth(forest);
                yield return null;

                var spirit = SpiritOf(spawner);
                Assert.IsNotNull(spirit, $"{kind}: 精霊が生成されなかった");

                // 生成時刺激のReactが自然に終わってIdleへ戻るのを、実フレームを進めて待つ。
                yield return WaitForState(spirit, SpiritState.Idle, 10f);

                measured[kind] = StateDurationOf(spirit);

                TearDown();
                SetUp();
            }

            const float min = SpiritBehaviorMath.IdleMinDuration; // 2
            const float max = SpiritBehaviorMath.IdleMaxDuration; // 4

            float calm    = measured[SpiritPersonalityKind.Calm];
            float curious = measured[SpiritPersonalityKind.Curious];

            Assert.GreaterOrEqual(calm, min * 1.4f - 0.01f, $"CalmのIdleが伸びていない（{calm:F2}秒）");
            Assert.LessOrEqual(calm,    max * 1.4f + 0.01f, $"CalmのIdleが想定より長い（{calm:F2}秒）");

            Assert.GreaterOrEqual(curious, min * 0.7f - 0.01f, $"CuriousのIdleが想定より短い（{curious:F2}秒）");
            Assert.LessOrEqual(curious,    max * 0.7f + 0.01f, $"CuriousのIdleが縮んでいない（{curious:F2}秒）");
        }

        // ══ 6. 破棄でEventBusの購読が実際に解除される ═══════════════════

        [UnityTest]
        public IEnumerator DestroyingSpawnedSpirit_UnsubscribesFromEventBus()
        {
            // 実ライフサイクルでのみ検証できる項目（OnDisableの実発火）。
            Assert.AreEqual(0, SubscriberCount<SpiritStimulusEvent>(), "開始時に購読が残っている");

            var spawner = MakeSpawner("Spawner_Lifecycle",
                ForestSpiritSpawner.PersonalitySelectionMode.Fixed, SpiritPersonalityKind.Calm);
            var forest = MakeForest("Forest_Lifecycle", Vector3.zero);

            yield return null;
            PublishForestGrowth(forest);
            yield return null;

            var spirit = SpiritOf(spawner);
            Assert.IsNotNull(spirit);
            Assert.AreEqual(1, SubscriberCount<SpiritStimulusEvent>(),
                "生成された精霊が刺激を購読していない");

            Object.DestroyImmediate(spirit.gameObject);
            yield return null;

            Assert.AreEqual(0, SubscriberCount<SpiritStimulusEvent>(),
                "破棄後も購読が残っている（OnDisableで解除されていない）");
        }
    }
}
