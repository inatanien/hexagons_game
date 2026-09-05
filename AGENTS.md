# AGENTS.md

このリポジトリで作業するAIエージェント向けの指示書。

**このファイルには「変わらないもの」だけを書くこと。**
現在の進捗・ファイル数・既知の課題など、時間で腐る情報は `HANDOVER.md` に書く。

---

## このプロジェクトは何か

**精霊樹の森**（仮） — Dorfromantik風の癒し系六角形タイル配置シミュレーション。
可愛い精霊たちが暮らす森を育てる。**戦闘なし、のんびり発展。**

参考作品: Dorfromantik / Islanders / Tiny Glade

## 最優先の価値基準

このゲームは「数字を増やすゲーム」ではなく **「世界を好きになるゲーム」** である。

機能を提案・実装するときは、必ず最初にこの3つを考えること。

1. 癒されるか
2. 気持ちいいか
3. 世界観に合うか

パフォーマンスやコードの綺麗さより、**プレイヤーが「もう一枚置きたい」と思える気持ち良さ**が優先される。
プレイヤーを急かさない。ストレスを与えない。眺めているだけでも楽しいことを目指す。

### Design Pillars

- つながるのが気持ちいい
- 世界が広がるのを見るのが楽しい
- プレイヤーが操作しなくても世界が生きている
- 数値ではなく景色で成長を感じる

---

## 計画の立て方について

**このプロジェクトにフェーズ型のロードマップは存在しない。意図的に持っていない。**

良かった機能の多くは当初の計画になく、作っているうちに湧いたものである。
癒し系のゲームで「眺めて気持ちいい」を積み上げる以上、その進み方が正しいと判断している。

- `HANDOVER.md` の **「次の一手」は最大3個**。それ以上は書かない。
- 4個目を思いついたら `IDEAS.md` に送る。**IDEAS.md に順序と実施の約束はない。**
- 「Phase 2 が終わったから次は Phase 3」といった順序を前提にした提案をしないこと。

古い計画を根拠に「予定と違う」と指摘することは不要。**脱線は仕様である。**

---

## 環境

| 項目 | 内容 |
|------|------|
| Unity | 6000.0.77f1 (Unity 6) |
| レンダリング | Universal Render Pipeline (URP) 17.0.4 |
| 入力 | New Input System 1.19.0 |
| メインシーン | `Assets/Scenes/Phase1_v002.unity` |

## アーキテクチャ

### レイヤー構造

```
View Layer       TileView / SpiritView / UIView（MonoBehaviour）
     ↕ EventBus
Presenter Layer  GameManager / TilePlacer / QuestController
     ↕ read/write
Domain Model     HexGrid / TileModel / QuestModel（純粋C#・テスト可能）
     ↕ load
Data Layer       ScriptableObject / JSON SaveData
```

**EventBus** が各システムを疎結合に繋ぐ。システム間の直接参照は禁止。

### アセンブリ依存（asmdef実測値）

```
_Game.Scripts.Core     → Unity.InputSystem
_Game.Scripts.HexGrid  → Core
_Game.Scripts.Tiles    → Core, HexGrid, Unity.InputSystem
_Game.Scripts.Quest    → Core
_Game.Scripts.Spirits  → Core, Tiles
_Game.Scripts.UI       → Core, Tiles, Quest, Unity.InputSystem, TextMeshPro
_Game.Scripts.Save     → Core, Tiles, Quest
_Game.Editor           → 全参照（Editor専用）
```

namespace は `ElfVillage.<AssemblyName>`。**この依存方向を越えるコードを書かないこと。**

## Hex座標系

`HexCoord`（`ElfVillage.HexGrid`）は **Cube Coordinates**（q, r, s / 常に `q+r+s=0`）。
**フラットトップ**配置。`ToWorldPosition(size)` / `FromWorldPosition(pos, size)` でUnityワールド座標と相互変換。

---

## コーディング規約

- **1クラス1責務。巨大クラスは禁止。**
- 200行を超えたら分割を検討する。
- コメントは「何をしているか」ではなく **「なぜそうしたか」** を書く。
- 新規スクリプトは `using` より前、ファイル冒頭に `// 役割: ...` コメントを必ず入れる。
- コミットメッセージは英語のConventional Commits（`feat:` `fix:` `refactor:` `docs:` `test:` `chore:`）。
- テストは Unity Test Runner の EditMode（`Assets/Tests/EditMode/`）。純粋C#のドメインロジックが対象。

## 実装するときのルール

- **着手前に実装内容を説明し、承認を得ること。** 提案を求められた段階で勝手に実装しない。
- **1回の作業は1機能だけ。** 気になった箇所をついでに直さない。
- 既存機能を壊さない。変更後はリグレッションを確認する。
- 完了時は以下を必ずまとめて報告する。
  - 追加したファイル
  - 変更したファイル
  - 作成したGameObject
  - **Unity Editor上で手動設定が必要なもの**（Prefab割り当て、シーン配置、Inspector値など）

最後の項目は特に重要。エディタを直接操作できない場合、この報告がないと作業が完結しない。

## 禁止事項

ユーザーの承認なしに以下を行わないこと。

- ファイルの削除
- 名前変更・フォルダ移動
- 依頼されていないリファクタリング
- asmdefの変更
- Packageの追加

`開発メモ.txt` はユーザーの個人ファイル。**編集もコミットもしない。**

---

## Unity Editorの操作について

このリポジトリはUnity MCPブリッジ（`http://127.0.0.1:8080/mcp`）に接続できる。

- **既定では接続しない。** レビュー・調査・提案はコードとスクリーンショットから行う。
- ユーザーが明示的に指示した場合のみ接続してよい。
- **Claude Code と同時にUnityへ書き込まないこと。** ブリッジは1本であり、競合する。

接続しない場合の代替手段:

- 現在の見た目 → `Assets/Screenshots/` の画像を見る
- コンソールエラー数・テスト結果 → ユーザーに取得を依頼する

## 現状把握の入り口

1. `HANDOVER.md` — 現在地のまとめ。**まずこれを読む。**
2. `Assets/Screenshots/` — 実際の画面。
3. `git log --oneline -20` — 直近の作業内容。

※ `Vision.md.txt` と `Roadmap.md.txt` は空ファイル、`GameDesign.md.txt` は
初期の草案であり現状を反映していない。**これらを現状の根拠にしないこと。**

## 役割分担

- ゲームデザイン・世界観・システム設計・実装方針・レビュー → ChatGPT / Codex
- Unity実装・コード生成・リファクタリング・UnityMCP操作 → Claude Code

いずれも固定ではない。ユーザーの指示が優先される。
**仕様が曖昧な場合は、勝手に決めずユーザーに確認すること。**
