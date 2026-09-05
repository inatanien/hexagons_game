# 精霊樹の森 — 引き継ぎドキュメント

> 他のAIに現状を伝えるためのまとめ。**このファイルには「今どうなっているか」を書く。**
> 変わらないルール・アーキテクチャ・価値基準は `AGENTS.md` / `CLAUDE.md` を参照。
> まだ手を付けていないアイデアは `IDEAS.md` へ。

**最終更新: 2026-09-05**（Unity実測値で更新）

---

## 基本情報

| 項目 | 内容 |
|------|------|
| Unity バージョン | 6000.0.77f1 (Unity 6) |
| レンダリング | URP 17.0.4 |
| 入力システム | New Input System 1.19.0 |
| アクティブシーン | `Assets/Scenes/Phase1_v002.unity`（ルート16個） |
| コンソール | **エラー0件・警告0件** |
| EditModeテスト | **791件中 791成功 / 0失敗 / 0スキップ**（約10秒・2回連続で確認） |
| スクリプト規模 | 122ファイル・約17,200行 |

※ Consoleに出る Burst compilation error (code 4551) は調査済み・無害。再調査不要。

---

## Design Pillars

① つながるのが気持ちいい
② 世界が広がるのを見るのが楽しい
③ プレイヤーが操作しなくても世界が生きている
④ 数値ではなく景色で成長を感じる
⑤ 置くゲームではなく育てるゲーム

## ゲームコンセプト

**精霊樹の森**（仮）— Dorfromantik風の癒し系六角形タイル配置シミュレーション。
戦闘なし。のんびりタイルを置いて森を育てる。「世界を好きになるゲーム」。

---

## 実装状況

### コア

| システム | ファイル | 概要 |
|----------|----------|------|
| Hex座標系 | `HexCoord.cs` | Cube Coordinates (q,r,s)、フラットトップ |
| グリッド管理 | `HexGridManager.cs` | 無限グリッド、配置・取得 |
| タイル本体 | `HexTile.cs` | メッシュ生成・プロップ配置・川フロー |
| エッジマッチング | `EdgeMatcher.cs` | 6方向接続判定 |
| カメラ | `CameraController.cs` | RTSピボット、パン/ズーム/回転 |
| デッキ・手札 | `TileDeck.cs` / `HandUI.cs` | 重み付き抽選・手札3枚 |
| 配置プレビュー | `TilePlacementPreview.cs` | ゴーストタイル表示 |
| EventBus | `EventBus.cs` | 型安全なグローバルイベントバス |
| 時間帯 | `TimeOfDaySystem.cs` | 朝・昼・夕方・夜のサイクル |
| 操作状態 | `GameInteractionStateController.cs` | Playing / PauseMenu / Settings |
| オーディオ | `AudioManager.cs` | BGM・SE・環境音・UI音（Singleton） |

### タイル種別（18種）

| 系統 | アセット |
|------|---------|
| 森 | `TileType_Forest` / `TileType_Forest_Edge` |
| 花畑 | `TileType_Field` |
| 複合 | `TileType_ForestFlower` / `TileType_FieldGrove` / `TileType_ForestFlower_Prototype` |
| 村 | `TileType_Village` |
| 道 | `TileType_Road_Straight` / `TileType_Road_Bend` |
| 川（素） | `TileType_River_Straight` / `_Bend` / `_Wide_Bend` |
| 景観川・森 | `TileType_RiverForest_Straight` / `_Bend` / `_WideBend` |
| 景観川・花 | `TileType_RiverFlower_Straight` / `_Bend` / `_WideBend` |

**景観川**は川タイルの陸地部分に木や花を生やした見た目違い。判定上は通常の川と同一で、
川クラスター・橋・森×川シナジーすべてに川として参加する（`LandDecorationLayout`）。

### 世界の演出（WorldBreath GO に集約）

| コンポーネント | トリガー | 演出 |
|--------------|---------|------|
| `WorldBreathSystem` | 森クラスター成長 | 葉が舞う |
| `TileShadeSystem` | 森タイル配置 | 地面に木陰を敷く |
| `TreeBillboardSystem` | 森タイル | 木を常にカメラを向く板で描画 |
| `FlowerBillboardSystem` | 花畑タイル | 複数絵柄の花を描画 |
| `FlowerPetalSystem` | FlowerClusterEvent | 花びらが舞う（枚数で色追加） |
| `FireflySystem` | 森×川シナジー | 境界に蛍 |
| `ButterflySystem` | 森×花シナジー | 境界に蝶 |
| `FishSystem` | RiverClusterEvent | 魚が泳ぐ・跳ねる |
| `BridgeSystem` | RiverBridgeEvent | アーチ橋をプロシージャル生成 |
| `RiverFlowSystem` | 川タイル配置・接続 | 水流方向の確定・伝播 |
| `HouseWindowLight` | TimeOfDayEvent | 家の窓の灯り |
| `ChimneySmokeTimeOfDay` | TimeOfDayEvent | 煙突の煙を昼だけ出す |
| `BirdRewardSpawner` | RewardUnlockedEvent | 鳥の出現 |

### 家（手続き生成）

- 生成器は `Assets/Editor/HouseMeshGenerator.cs`（**Editorアセンブリ**。ランタイム生成ではない）
- **6種の家バリアント**。全種1マテリアル・合計628三角形
- 村タイルには**4〜6軒**を配置（中央1軒＋円環）
- 窓の夜間発光・煙突の煙が時間帯と連動

### 精霊システム（Stage 9〜16）

| 要素 | ファイル | 内容 |
|------|---------|------|
| 本体 | `ForestSpirit.cs` | Idle / Wander / ObserveTree / Sleep の4状態で自律行動 |
| 生成 | `ForestSpiritSpawner.cs` / `SpiritSpawnPolicy.cs` | 森クラスターが一定以上で1体だけ誕生 |
| 成長 | `SpiritGrowthMath.cs` / `SpiritGrowthStage.cs` | 成長段階を管理 |
| 性格 | `SpiritPersonalityKind.cs` / `SpiritPersonalityProfile.cs` | 性格ごとの行動調整 |
| 記憶 | `SpiritMemory.cs` | 刺激種類ごとの「見慣れ度」 |
| 刺激 | `SpiritStimulusRelay.cs` | 世界イベントを精霊向け刺激へ翻訳 |
| 演出 | `ForestSpiritPresentation.cs` | 誕生・成長を目と耳へ伝える |
| 通知 | `SpiritNoticePresenter.cs` → `WorldNoticeUI` | 汎用通知として画面表示 |

行動計算は `SpiritBehaviorMath` / `SpiritGrowthMath` / `SpiritSpawnPolicy` /
`SpiritSimulationPolicy` に**副作用のない純粋関数**として分離済み（テスト可能）。

### クエストシステム

- `QuestDefinition`（SO）で条件をデータ駆動化。`QuestConditionKind` で条件種別を指定
- `QuestSequenceDefinition` / `QuestSequenceRunner` で出題順を管理
- 報酬は `QuestRewardSystem` → `RewardUnlockedEvent` でイベント駆動
- 定義済み: `Quest_ForestCluster5` / `Quest_RiverCluster3` / `Quest_FieldPlaced2` /
  `Quest_Bridge1` / `Quest_ForestRiverSynergy1` / `QuestSequence_Tutorial`

**達成演出**（3段構成）

1. `QuestTileFocusTracker` — どのタイルを祝うか解決
2. `QuestCelebrationOutlineSystem` — タイル群の外周を光の先端が**1.4秒**で一周
3. `QuestCelebrationRaySystem` — 一周後、外周から淡い光柱を一斉に立ち上げる

輪郭の抽出は `TileOutlineGeometry`（決定論的な閉ループ生成）、
点列の切り出しは `OutlineTraceSampler`、光柱位置は `CelebrationRayLayout`（いずれも純粋関数）。

### UI

`QuestPanelUI` / `QuestNotificationUI` / `WorldNoticeUI` /
`PauseMenuController` / `SettingsPanelController` / `DebugTilePanel`（F1で表示）

---

## 主なイベント

**Tiles内**: `TilePlacedEvent` / `TileConnectedEvent` / `TerrainGrowthEvent<T>` /
`RiverClusterEvent` / `RiverBridgeEvent` / `FlowerClusterEvent` / `TerrainSynergyEvent` /
`QuestTileSelectionResolvedEvent` / `QuestOutlineTraceCompletedEvent`

**Core（アセンブリ横断）**: `FirstTilePlacedEvent` / `TileCategoryPlacedEvent` /
`TerrainClusterProgressEvent` / `WorldEventOccurredEvent` / `WorldNoticeEvent` /
`QuestCelebrationEvent` / `QuestFocusStartedEvent` / `RewardUnlockedEvent` / `TimeOfDayEvent`

**Quest**: `QuestStartedEvent` / `QuestProgressChangedEvent` / `QuestCompletedEvent` /
`QuestSequenceCompletedEvent`

Tiles→Quest の翻訳は `WorldEventRelay` / `TerrainClusterProgressRelay` が担当
（アセンブリ依存の向きを守るため、Tiles側の詳細イベントをCoreの汎用イベントへ変換している）。

---

## 既知の課題

### 1. 時刻依存で不安定なテストがある（優先度：中）

`SpiritMemoryTests` / `SpiritPersonalityTests` の一部が、稀に失敗する。

`Assets/Tests/EditMode/SpiritMemoryTests.cs:72` のヘルパーが、見慣れ度を読むときの
「現在時刻」として `Time.time` をそのまま渡しているのが原因。
刺激を与えてから値を読むまでにエディタの時間が進むと、その分だけ減衰して期待値を下回る
（実測: 期待1.5に対し1.4766。半減期60秒なら約1.4秒ぶんの経過にあたる）。

`SpiritMemory` 本体は「時刻は呼び出し側から渡す」設計で正しく、**実装側の問題ではない**。
テストが固定時刻を渡すようにすれば解消する。

2026-09-05時点では連続実行で再現しておらず、常に落ちるわけではない。

### 2. `HexTile.cs` が 1841行（優先度：中）

200行ルールを大きく超過。分割の最有力候補。
他に500行超が3件 — `ForestSpirit.cs` 953 / `ForestSpiritPresentation.cs` 636 /
`WorldBreathSystem.cs` 539。

### 3. `Save` アセンブリが空（優先度：未定）

`_Game.Scripts.Save.asmdef` は存在するが `.cs` が0件。セーブ/ロードは未着手。

### 4. その他の未着手

BGM・SE（`AudioManager` は基盤のみ）／スコアリング／ゲーム終了条件

---

## 次の一手（最大3個・これ以上は `IDEAS.md` へ）

> ここは**ユーザーとの合意事項**を書く欄。以下は現状からの提案であり、未確定。

1. **クエストシステム Stage 2** — Stage 1（データ駆動化・Sequence・達成演出）は完了済み。
2. **タイル出現率（weight）の調整** — クエスト報酬によるタイル解放の実装時にまとめて行う方針。
   それまでは触らない。
3. （空き）

**完了済み（2026-09-05）**
- ~~スキップされているテストを実行可能にする~~ →
  `ScenicRivers_AreRegisteredAsTheRiverSideOfTheForestRiverSynergy` を修正。
  EditModeテストは Test Framework の「名前なし一時シーン」上で走るため
  `GetActiveScene()` では本編シーンを掴めない（name・path とも空）ことを実測で確認し、
  対象シーンを Additive で開いて読み finally で閉じる方式へ変更した。
  わざと壊して失敗することも確認済み（検出力あり）。

---

## 作業の進め方

このプロジェクトに**フェーズ型のロードマップは無い**（`AGENTS.md` 参照）。
`GameDesign.md.txt` のPhase 1〜4は初期の草案であり、現状と一致しない。
`Vision.md.txt` と `Roadmap.md.txt` は空ファイル。**これらを現状の根拠にしないこと。**

進捗の目安として、精霊・クエスト系のコードには `Stage N` の記述がある（現在 Stage 16 前後）。
