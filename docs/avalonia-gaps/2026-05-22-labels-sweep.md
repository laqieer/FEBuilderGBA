---
generated: "2026-05-21T16:32:05Z"
git-sha: c7db152fd
sweep-type: labels
---

# Avalonia vs WinForms — Field Label Diff Sweep

This report extracts label literals from paired WinForms ↔ Avalonia
editors and lists, per pair, the labels present in the WinForms designer
but missing from the Avalonia counterpart. These are strong candidates
for **missing fields in the Avalonia migration** — qualitative follow-up
to the Phase 1 control-density sweep.

WinForms side: Roslyn extracts `.Text = "..."` assignments on
`Label`, `GroupBox`, `Button`, `CheckBox`, `RadioButton`, `TabPage`
controls (plus property-initialiser syntax for hand-coded forms).
Avalonia side: `XDocument` parses every view, harvests literal values from
`Text` / `Content` / `Header` / `ToolTip` / `Watermark` attributes,
skipping markup-extension values (`{Binding ...}`, `{StaticResource ...}`)
and elements nested inside template containers (`Style`, `DataTemplate`, ...).

Normalisation collapses whitespace, strips trailing colons, removes mnemonic
markers (`&` for WF, `_` for AV), and lowercases — so `Name:` / `&Name` /
`_Name` / `Name` all collide to the same set key. Original casing is preserved
in the report's WF-only / AV-only / Common lists.

Methodology lives in `FEBuilderGBA.Avalonia/GapSweep/LabelDiffScanner.cs`.
Regenerate with `FEBuilderGBA.Avalonia --gap-sweep-labels --out=<path>`.

## Summary

| Metric | Count |
|---|---:|
| Pairs scanned (both files exist) | 298 |
| Pairs with ≥1 WF-only label | 293 |
| Total WF-only labels | 4645 |
| Total AV-only labels | 2463 |
| Total common labels | 52 |

## Top 20 Forms by WF-only Label Count

Each row's WF-only count is the upper bound on missing fields in the AV view.
Cross-link to the [density sweep](2026-05-21-density-sweep.md) for quantitative context.

| Rank | WF Form | AV View | WF-only | AV-only | Common |
|---:|---|---|---:|---:|---:|
| 1 | `EmulatorMemoryForm` | `EmulatorMemoryView` | 174 | 4 | 1 |
| 2 | `MapSettingFE7UForm` | `MapSettingFE7UView` | 90 | 78 | 0 |
| 3 | `MapSettingFE7Form` | `MapSettingFE7View` | 87 | 78 | 0 |
| 4 | `SkillConfigFE8NSkillForm` | `SkillConfigFE8NSkillView` | 84 | 18 | 0 |
| 5 | `EventCondForm` | `EventCondView` | 81 | 21 | 0 |
| 6 | `MapSettingForm` | `MapSettingView` | 78 | 116 | 0 |
| 7 | `MapSettingFE6Form` | `MapSettingFE6View` | 65 | 2 | 0 |
| 8 | `ClassForm` | `ClassEditorView` | 57 | 78 | 1 |
| 9 | `EventUnitForm` | `EventUnitView` | 50 | 24 | 0 |
| 10 | `SongInstrumentForm` | `SongInstrumentView` | 50 | 21 | 1 |
| 11 | `TextForm` | `TextViewerView` | 48 | 11 | 0 |
| 12 | `WorldMapImageForm` | `WorldMapImageView` | 47 | 2 | 0 |
| 13 | `ImageUnitPaletteForm` | `ImageUnitPaletteView` | 45 | 17 | 0 |
| 14 | `ItemForm` | `ItemEditorView` | 45 | 47 | 0 |
| 15 | `MapStyleEditorForm` | `MapStyleEditorView` | 45 | 5 | 0 |
| 16 | `UnitForm` | `UnitEditorView` | 45 | 50 | 4 |
| 17 | `ItemFE6Form` | `ItemFE6View` | 44 | 27 | 0 |
| 18 | `ToolInitWizardForm` | `ToolInitWizardView` | 44 | 8 | 0 |
| 19 | `ClassFE6Form` | `ClassFE6View` | 43 | 5 | 0 |
| 20 | `ImageBattleScreenForm` | `ImageBattleScreenView` | 42 | 2 | 0 |

## Per-pair WF-only Labels (gaps)

Sections sorted by WF-only count descending. Each label is rendered as a
backticked literal preserving the original casing/punctuation. Use these as
the per-form backlog for follow-up gap-fix PRs.

### EmulatorMemoryForm
WF labels: **175** · AV labels: **5** · WF-only: **174** · AV-only: **4** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `0で霧なし。1が視界1マスの最大の霧です。`
- `100(0x64)`
- `104(0x68)`
- `41(0x29)`
- `44(0x2C)`
- `48(0x30)`
- `52(0x34)`
- `56(0x38)`
- `60(0x3C)`
- `64(0x40)`
- `68(0x44)`
- `72(0x48)`
- `76(0x4C)`
- `80(0x50)`
- `84(0x54)`
- `88(0x58)`
- `92(0x5C)`
- `96(0x60)`
- `??? 1`
- `??? 2`
- `??? 3`
- `??? 8`
- `??? 9`
- `ActionData`
- `AI1`
- `AI1 Count`
- `AI2`
- `AI2 Count`
- `AI3`
- `AI4`
- `AIData`
- `BattleActor`
- `BattleRound`
- `BattleRounds`
- `BattleSome`
- `BattleTarget`
- `BGM`
- `Block Counter`
- `BWL`
- `ChapterData`
- `Code cursor`
- `Destructor Routine`
- `Dungeon`
- `ERROR`
- `Etc`
- `Exp`
- `First Child Struct`
- `Loop Routine`
- `Lv`
- `MapSprite`
- `Mark`
- `Name`
- `Next Struct`
- `Parent Procs`
- `PartyCount`
- `Previous Struct`
- `Procs`
- `ROM Class`
- `ROM Unit`
- `Sleep Timer`
- `Some kind of bitfield`
- `Start of code`
- `Text`
- `UserSpace`
- `UserSpace1`
- `UserSpace2`
- `UserSpace3`
- `Worldmap`
- `Worldmap FE8`
- `X`
- `Y`
- `↑最新`
- `このユニットに以下のアイテムをもたせる`
- `このユニットのHPを1にする。`
- `このユニットのパラメータをカンストさせる。`
- `このユニットの座標を変更しワープさせる。`
- `この章へワープする`
- `すべての味方ユニットを最強にします。(Hotkey Ctrl + G)`
- `すべての敵ユニットAIを「移動しない」に設定にします。`
- `すべての敵ユニットのHPを1にします。`
- `より古い↓`
- `アイテムID1`
- `アイテムID2`
- `アイテムID3`
- `アイテムID4`
- `アイテムID5`
- `アイテム数1`
- `アイテム数2`
- `アイテム数3`
- `アイテム数4`
- `アイテム数5`
- `アドレス`
- `イベント`
- `イベント履歴`
- `イベント履歴(上が最新)
時刻,イベント開始アドレス,実行中のアドレス,イベント`
- `クリアターン数`
- `クリアフラグ以外の、他のフラグを変更したい場合は、イベント画面から、変更したいフラグをダブルクリックしてください。`
- `ターン数`
- `ターン数を変更`
- `チート`
- `デバッグ用に便利なチート機能です`
- `トラップデータ`
- `パレット`
- `パーティー`
- `フラグ`
- `マップID`
- `メモリをダンプしてファイルに書き込む 0x02000000`
- `メモリをダンプしてファイルに書き込む 0x03000000`
- `メモリスロット`
- `ユニットの行動で設定される項目`
- `ワープする章`
- `ワールドマップ拠点`
- `体格＋`
- `個数`
- `光 EXP`
- `再生されている音楽`
- `剣 EXP`
- `力と魔力`
- `同行者ID`
- `回復モード`
- `塔と遺跡のデータ`
- `天気`
- `天気を変更(次のターンから適用されます。)`
- `守備`
- `実行しているイベント`
- `弓 EXP`
- `戦歴データ`
- `戦闘に関係する諸データ`
- `戦闘データ gBattleActor`
- `戦闘データ gBattleTarget`
- `所持金`
- `所持金を以下の値に変更する`
- `技`
- `持たせるアイテム`
- `操作中のユニット`
- `支援1`
- `支援2`
- `支援3`
- `支援4`
- `支援5`
- `支援6`
- `支援7`
- `支援フラグ`
- `敵を含めて、すべてのユニットを最強にします`
- `斧 EXP`
- `最大HP`
- `杖 EXP`
- `検索`
- `槍 EXP`
- `汎用`
- `状態`
- `状態とターン`
- `現在HP`
- `現在、操作しているユニット`
- `現在の章をクリアします。(Hotkey: Ctrl + U)`
- `理 EXP`
- `移動＋`
- `章データ`
- `編`
- `聖水松明`
- `自動的に更新する`
- `解析者向けの機能`
- `輸送隊`
- `輸送隊の内容`
- `速さ`
- `運`
- `選択アドレス:`
- `部隊表ID`
- `闇 EXP`
- `闘技場`
- `闘技場の相手選出に利用するデータ`
- `霧レベル`
- `霧レベルを変更(次のターンから適用されます。)`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Emulator Memory`
- `Emulator memory reading requires Windows P/Invoke and is not available in the cross-platform Avalonia version.`
- `Platform Notice`
- `This feature uses Windows-specific APIs to read the memory of a running GBA emulator process for live debugging. Please use the Windows (WinForms) version of FEBuilderGBA for this functionality.`

### MapSettingFE7UForm
WF labels: **90** · AV labels: **78** · WF-only: **90** · AV-only: **78** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Aエリウッドノーマル`
- `Aエリウッドハード`
- `Aヘクトルノーマル`
- `Aヘクトルハード`
- `Bエリウッドノーマル`
- `Bエリウッドハード`
- `Bヘクトルノーマル`
- `Bヘクトルハード`
- `CP`
- `Cエリウッドノーマル`
- `Cエリウッドハード`
- `Cヘクトルノーマル`
- `Cヘクトルハード`
- `Dエリウッドノーマル`
- `Dエリウッドハード`
- `Dヘクトルノーマル`
- `Dヘクトルハード`
- `PreparationScreenCh No.`
- `Size:`
- `X:`
- `Y:`
- `♪`
- `アドレス`
- `イベントID(Plist)`
- `エリウッドノーマル`
- `エリウッドハード`
- `オブジェクトタイプ(Plist)`
- `クリア条件(表示のみ)`
- `タイルアニメーション1`
- `タイルアニメーション2`
- `ターン数表示用`
- `チップセットクタイプ(Plist)`
- `パレット(Plist)`
- `ヘクトルノーマル`
- `ヘクトルハード`
- `マップエディタへJump`
- `マップスタイルの変更`
- `マップポインタ(Plist)`
- `マップ部分変更(Plist)`
- `リストの拡張`
- `ワールドマップ自動イベント`
- `先頭アドレス`
- `再取得`
- `初期座標`
- `勝利BGMに変わる敵数`
- `占い会話(エリウッド)`
- `占い会話(ヘクトル)`
- `占い会話(冒頭)`
- `占い会話(終了確認)`
- `占い師の顔`
- `占い料`
- `友軍BGM(エリウッド編)`
- `友軍BGM(ヘクトル編)`
- `名前`
- `味方フェーズBGM(エリウッド編)`
- `味方フェーズBGM(ヘクトル編)`
- `味方フェーズBGMフラグ4`
- `壊れる壁HP`
- `天気`
- `戦闘準備の有無`
- `戦闘背景`
- `攻略評価`
- `敵フェーズBGM(エリウッド編)`
- `敵フェーズBGM(ヘクトル編)`
- `敵フェーズBGMフラグ4`
- `書き込み`
- `特殊表示`
- `章タイトル(エリウッド)`
- `章タイトル(ヘクトル)`
- `章タイトル文字(エリウッド)`
- `章タイトル文字(ヘクトル)`
- `章タイトル画像`
- `章タイトル画像2`
- `章プロローグBGM(エリウッド編)`
- `章プロローグBGM(ヘクトル編)`
- `章プロローグBGM(共通)`
- `経験評価`
- `詳細クリア条件(表示のみ)`
- `読込数`
- `資産評価`
- `輸送隊 エリウッド編`
- `輸送隊 ヘクトル編`
- `選択アドレス:`
- `開始イベント前に暗転`
- `防衛ユニットの◇マーク`
- `離脱▲マーク`
- `離脱ポイントへJump`
- `難易度補正`
- `霧レベル`

AV-only labels (usually fine — layout polish or rewording):

- `B10: Tile animation 2 (PLIST)`
- `B11: Map change (PLIST)`
- `B120: Event ID (PLIST)`
- `B121: World map auto event`
- `B12: Fog level`
- `B130: Fortune portrait`
- `B131: Fortune fee`
- `B132: Prep screen Ch no. 1`
- `B133: Prep screen Ch no. 2`
- `B134: Transporter Eliwood X`
- `B135: Transporter Hector X`
- `B136: Transporter Eliwood Y`
- `B137: Transporter Hector Y`
- `B138: Victory BGM enemy count`
- `B139: Darken before start`
- `B13: Battle preparation`
- `B144: Special display`
- `B145: Turn count display`
- `B146: Defense unit mark`
- `B147: Escape marker X`
- `B148: Escape marker Y`
- `B149: ??`
- `B14: Chapter title image`
- `B150: ??`
- `B151: ??`
- `B15: Chapter title image 2`
- `B16: Padding`
- `B17: Padding`
- `B18: Weather`
- `B19: Battle BG`
- `B44: Breakable wall HP`
- `B61: ??`
- `B6: Palette (PLIST)`
- `B7: Chipset config (PLIST)`
- `B8: Map pointer (PLIST)`
- `B9: Tile animation 1 (PLIST)`
- `BGM / Music`
- `Breakable Wall HP / Unknowns`
- `Chapter / Transporter / Victory`
- `Chapter Title Text IDs`
- `Clear Conditions`
- `CP / Pointer`
- `D0: CP`
- `Difficulty Pointers (D96-D108)`
- `Display Flags (B144-B151)`
- `Eliwood Hard (D100):`
- `Eliwood Normal (D96):`
- `Event / Fortune Teller`
- `Hector Hard (D108):`
- `Hector Normal (D104):`
- `Map Properties`
- `Map Settings (FE7U)`
- `Map Style / PLIST`
- `W112: Eliwood title text`
- `W114: Hector title text`
- `W116: Eliwood title text 2`
- `W118: Hector title text 2`
- `W122: Fortune text (opening)`
- `W124: Fortune text (Eliwood)`
- `W126: Fortune text (Hector)`
- `W128: Fortune text (confirm)`
- `W140: Clear condition text`
- `W142: Detail clear condition`
- `W20: Difficulty adjustment`
- `W22: Player phase BGM`
- `W24: Enemy phase BGM`
- `W26: NPC phase BGM`
- `W28: Player phase BGM 2`
- `W30: Enemy phase BGM 2`
- `W32: NPC phase BGM 2`
- `W34: Player phase BGM flag 4`
- `W36: Enemy phase BGM flag 4`
- `W38: Prologue BGM (Common)`
- `W40: Prologue BGM (Eliwood)`
- `W42: Prologue BGM (Hector)`
- `W4: Object type (PLIST)`
- `W94: ??`
- `Write`

### MapSettingFE7Form
WF labels: **87** · AV labels: **78** · WF-only: **87** · AV-only: **78** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Aエリウッドノーマル`
- `Aエリウッドハード`
- `Aヘクトルノーマル`
- `Aヘクトルハード`
- `Bエリウッドノーマル`
- `Bエリウッドハード`
- `Bヘクトルノーマル`
- `Bヘクトルハード`
- `CP`
- `Cエリウッドノーマル`
- `Cエリウッドハード`
- `Cヘクトルノーマル`
- `Cヘクトルハード`
- `Dエリウッドノーマル`
- `Dエリウッドハード`
- `Dヘクトルノーマル`
- `Dヘクトルハード`
- `PreparationScreenCh No.`
- `Size:`
- `X:`
- `Y:`
- `♪`
- `アドレス`
- `イベントID(Plist)`
- `エリウッドノーマル`
- `エリウッドハード`
- `オブジェクトタイプ(Plist)`
- `クリア条件(表示のみ)`
- `タイルアニメーション1`
- `タイルアニメーション2`
- `ターン数表示用`
- `チップセットクタイプ(Plist)`
- `パレット(Plist)`
- `ヘクトルノーマル`
- `ヘクトルハード`
- `マップエディタへJump`
- `マップスタイルの変更`
- `マップポインタ(Plist)`
- `マップ部分変更(Plist)`
- `リストの拡張`
- `ワールドマップ自動イベント`
- `先頭アドレス`
- `再取得`
- `初期座標`
- `勝利BGMに変わる敵数`
- `占い会話(エリウッド)`
- `占い会話(ヘクトル)`
- `占い会話(冒頭)`
- `占い会話(終了確認)`
- `占い師の顔`
- `占い料`
- `友軍BGM(エリウッド編)`
- `友軍BGM(ヘクトル編)`
- `名前`
- `味方フェーズBGM(エリウッド編)`
- `味方フェーズBGM(ヘクトル編)`
- `味方フェーズBGMフラグ4`
- `壊れる壁HP`
- `天気`
- `戦闘準備の有無`
- `戦闘背景`
- `攻略評価`
- `敵フェーズBGM(エリウッド編)`
- `敵フェーズBGM(ヘクトル編)`
- `敵フェーズBGMフラグ4`
- `書き込み`
- `特殊表示`
- `章タイトル(エリウッド)`
- `章タイトル(ヘクトル)`
- `章タイトル画像`
- `章タイトル画像2`
- `章プロローグBGM(エリウッド編)`
- `章プロローグBGM(ヘクトル編)`
- `章プロローグBGM(共通)`
- `経験評価`
- `詳細クリア条件(表示のみ)`
- `読込数`
- `資産評価`
- `輸送隊 エリウッド編`
- `輸送隊 ヘクトル編`
- `選択アドレス:`
- `開始イベント前に暗転`
- `離脱▲マーク`
- `離脱ポイントへJump`
- `難易度補正`
- `霧レベル`

AV-only labels (usually fine — layout polish or rewording):

- `B10: Tile animation 2 (PLIST)`
- `B116: Event ID (PLIST)`
- `B117: World map auto event`
- `B11: Map change (PLIST)`
- `B126: Fortune portrait`
- `B127: Fortune fee`
- `B128: Chapter number`
- `B129: Prep screen Ch no. 1`
- `B12: Fog level`
- `B130: Transporter Eliwood X`
- `B131: Transporter Hector X`
- `B132: Transporter Eliwood Y`
- `B133: Transporter Hector Y`
- `B134: Victory BGM enemy count`
- `B135: Blackout before start`
- `B13: Battle preparation`
- `B140: Special display`
- `B141: Turn count display`
- `B142: Defense unit mark`
- `B143: Escape marker X`
- `B144: Escape marker Y`
- `B145: ??`
- `B146: ??`
- `B147: ??`
- `B14: Chapter title image`
- `B15: Chapter title image 2`
- `B16: Initial X coordinate`
- `B17: Initial Y coordinate`
- `B18: Weather`
- `B19: Battle BG lookup`
- `B44: Breakable wall HP`
- `B61: ??`
- `B61: Unknown`
- `B6: Palette (PLIST)`
- `B7: Chipset config (PLIST)`
- `B8: Map pointer (PLIST)`
- `B9: Tile animation 1 (PLIST)`
- `BGM / Music`
- `Breakable Wall HP / Difficulty Ratings`
- `Chapter / Transporter / Victory`
- `Clear Conditions`
- `CP / Pointer`
- `D0: CP`
- `Difficulty Pointers (D96-D108)`
- `Display Flags (B140-B147)`
- `Eliwood Hard (D100):`
- `Eliwood Normal (D96):`
- `Event / Fortune Teller`
- `Hector Hard (D108):`
- `Hector Normal (D104):`
- `Map Name Text IDs`
- `Map Properties`
- `Map Settings (FE7JP)`
- `Map Style / PLIST`
- `W112: Map name 1`
- `W114: Map name 2`
- `W118: Fortune text (opening)`
- `W120: Fortune text (Eliwood)`
- `W122: Fortune text (Hector)`
- `W124: Fortune text (confirm)`
- `W136: Clear condition text`
- `W138: Detail clear condition`
- `W20: Difficulty adjustment`
- `W22: Player phase BGM`
- `W24: Enemy phase BGM`
- `W26: NPC phase BGM`
- `W28: Player phase BGM 2`
- `W30: Enemy phase BGM 2`
- `W32: NPC phase BGM 2`
- `W34: Player phase BGM flag 4`
- `W36: Enemy phase BGM flag 4`
- `W38: ???`
- `W40: ???`
- `W42: ???`
- `W4: Object type (PLIST)`
- `W94: ??`
- `W94: Unknown`
- `Write`

### SkillConfigFE8NSkillForm
WF labels: **84** · AV labels: **18** · WF-only: **84** · AV-only: **18** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `↓文字列内訳`
- `その他`
- `その他1`
- `その他10`
- `その他11`
- `その他12`
- `その他13`
- `その他14`
- `その他15`
- `その他16`
- `その他2`
- `その他3`
- `その他4`
- `その他5`
- `その他6`
- `その他7`
- `その他8`
- `その他9`
- `アイコン`
- `アイコン表示条件`
- `アイテム`
- `アイテム1`
- `アイテム10`
- `アイテム11`
- `アイテム12`
- `アイテム13`
- `アイテム14`
- `アイテム15`
- `アイテム16`
- `アイテム2`
- `アイテム3`
- `アイテム4`
- `アイテム5`
- `アイテム6`
- `アイテム7`
- `アイテム8`
- `アイテム9`
- `アドレス`
- `クラス`
- `クラス1`
- `クラス10`
- `クラス11`
- `クラス12`
- `クラス13`
- `クラス14`
- `クラス15`
- `クラス16`
- `クラス2`
- `クラス3`
- `クラス4`
- `クラス5`
- `クラス6`
- `クラス7`
- `クラス8`
- `クラス9`
- `スキル名`
- `ユニット`
- `ユニット1`
- `ユニット10`
- `ユニット11`
- `ユニット12`
- `ユニット13`
- `ユニット14`
- `ユニット15`
- `ユニット16`
- `ユニット2`
- `ユニット3`
- `ユニット4`
- `ユニット5`
- `ユニット6`
- `ユニット7`
- `ユニット8`
- `ユニット9`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `条件:`
- `説明`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Condition Class 1:`
- `Condition Class 2:`
- `Condition Class 3:`
- `Condition Class 4:`
- `Condition Item 1:`
- `Condition Item 2:`
- `Condition Item 3:`
- `Condition Item 4:`
- `Condition Unit 1:`
- `Condition Unit 2:`
- `Condition Unit 3:`
- `Condition Unit 4:`
- `Description:`
- `Icon:`
- `Skill Configuration (FE8N)`
- `Skill system editors require a compatible skill patch to be installed.
Use the Patch Manager to install a skill system patch first.

Supported skill systems: CSkillSys, FE8N Skill System`
- `Write`

### EventCondForm
WF labels: **81** · AV labels: **21** · WF-only: **81** · AV-only: **21** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `0:`
- `00`
- `??`
- `ASM会話条件`
- `ASM条件`
- `ASM条件FE6`
- `LV`
- `NOP`
- `Size:`
- `TextID`
- `Vein`
- `VeinEffectID`
- `X:`
- `Y:`
- `アドレス`
- `アーチの種類`
- `アーチ配置`
- `イベント`
- `イベントポインタ`
- `イベントポインタ書き込み`
- `イベント種類`
- `ガスの方向`
- `コメント`
- `ゴーゴンの卵`
- `ゴールド`
- `ターン前指定`
- `ターン条件`
- `ターン条件FE7`
- `チュートリアル`
- `トラップ`
- `トラップ床`
- `マップオブジェクト`
- `マップ名`
- `リストの拡張`
- `リピートタイマー`
- `会話元`
- `会話先`
- `会話条件`
- `先頭アドレス`
- `再取得`
- `初期タイマー`
- `判定ASM関数`
- `判定フラグ`
- `制圧ポイントと民家`
- `名前`
- `地雷`
- `宝箱`
- `宝箱の中身`
- `常時条件`
- `店`
- `店の売り物`
- `店の種類`
- `座標`
- `扉`
- `新規イベント`
- `新規確保`
- `書き込み`
- `毒ガス`
- `炎`
- `発生タイプ`
- `神の矢`
- `種類`
- `範囲条件`
- `範囲終了`
- `範囲開始`
- `終了ターン`
- `編`
- `羽化開始`
- `耐久`
- `訪問村`
- `話す条件`
- `話す条件FE6`
- `読込数`
- `追加判定`
- `達成フラグ`
- `選択されている配置情報を、ユニット配置画面で開く`
- `選択されている開始/終了イベントを、イベント編集画面で開く`
- `選択アドレス:`
- `配置`
- `配置座標`
- `開始ターン`

AV-only labels (usually fine — layout polish or rewording):

- `B10:`
- `B11:`
- `B12:`
- `B13:`
- `B14:`
- `B15:`
- `B8:`
- `B9:`
- `Condition Slot`
- `Event Condition Editor`
- `Event Pointer:`
- `Flag ID:`
- `Maps`
- `Raw Hex:`
- `Record Address:`
- `Record Size:`
- `Records`
- `Sub-type:`
- `Type:`
- `Type Name:`
- `Write`

### MapSettingForm
WF labels: **78** · AV labels: **116** · WF-only: **78** · AV-only: **116** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `???`
- `Aエリウッドノーマル`
- `Aエリウッドハード`
- `Aヘクトルノーマル`
- `Aヘクトルハード`
- `Bエリウッドノーマル`
- `Bエリウッドハード`
- `Bヘクトルノーマル`
- `Bヘクトルハード`
- `Chapter Number`
- `CP`
- `Cエリウッドノーマル`
- `Cエリウッドハード`
- `Cヘクトルノーマル`
- `Cヘクトルハード`
- `Dエリウッドノーマル`
- `Dエリウッドハード`
- `Dヘクトルノーマル`
- `Dヘクトルハード`
- `Size:`
- `X:`
- `Y:`
- `♪`
- `アドレス`
- `イベントID(Plist)`
- `エリウッドノーマル`
- `エリウッドハード`
- `オブジェクトタイプ(Plist)`
- `クリア条件(表示のみ)`
- `タイルアニメーション1`
- `タイルアニメーション2`
- `ターン数表示用`
- `チップセットクタイプ(Plist)`
- `パレット(Plist)`
- `ヘクトルノーマル`
- `ヘクトルハード`
- `マップエディタへJump`
- `マップスタイルの変更`
- `マップポインタ(Plist)`
- `マップ名1`
- `マップ名2(X)`
- `マップ部分変更(Plist)`
- `リストの拡張`
- `ワールドマップ自動イベント`
- `先頭アドレス`
- `再取得`
- `初期座標`
- `勝利BGMに変わる敵数`
- `友軍BGM`
- `友軍BGM2(X)`
- `名前`
- `味方フェーズBGM`
- `味方フェーズBGM2(X)`
- `味方フェーズBGMフラグ4`
- `壊れる壁HP`
- `戦闘準備の有無(X)`
- `戦闘背景`
- `指南へJump`
- `攻略評価`
- `敵フェーズBGM`
- `敵フェーズBGM2(X)`
- `敵フェーズBGMフラグ4`
- `書き込み`
- `特殊表示`
- `章タイトル画像`
- `章タイトル画像2(X)`
- `経験評価`
- `詳細クリア条件(表示のみ)`
- `読込数`
- `資産評価`
- `選択アドレス:`
- `開始イベント前に暗転`
- `防衛ユニットの◇マーク`
- `離脱▲マーク`
- `離脱ポイントへJump`
- `難易度補正`
- `霧レベル`

AV-only labels (usually fine — layout polish or rewording):

- `?? (B119):`
- `?? (B120):`
- `?? (B121):`
- `?? (B122):`
- `?? (B123):`
- `?? (B124):`
- `?? (B125):`
- `?? (B126):`
- `?? (B127):`
- `?? (B129):`
- `?? (B130):`
- `?? (B131):`
- `?? (B132):`
- `?? (B133):`
- `Asset Rating`
- `B10: Tile animation 2 (PLIST)`
- `B116: Event ID (PLIST)`
- `B117: World map auto event`
- `B118: ??`
- `B11: Map change (PLIST)`
- `B12: Fog level`
- `B134: Enemies for victory BGM`
- `B135: Blackout before start`
- `B13: Battle preparation (X)`
- `B140: Special display`
- `B141: Turn count display`
- `B142: Defense unit mark`
- `B143: Escape marker X`
- `B144: Escape marker Y`
- `B145: ??`
- `B146: ??`
- `B147: ??`
- `B14: Chapter title image`
- `B15: Chapter title image 2 (X)`
- `B16: Initial X coordinate`
- `B17: Initial Y coordinate`
- `B18: Weather`
- `B19: Battle BG lookup`
- `B44: Breakable wall HP`
- `B45: A Eliwood Normal`
- `B46: A Eliwood Hard`
- `B47: A Hector Normal`
- `B48: A Hector Hard`
- `B49: B Eliwood Normal`
- `B50: B Eliwood Hard`
- `B51: B Hector Normal`
- `B52: B Hector Hard`
- `B53: C Eliwood Normal`
- `B54: C Eliwood Hard`
- `B55: C Hector Normal`
- `B56: C Hector Hard`
- `B57: D Eliwood Normal`
- `B58: D Eliwood Hard`
- `B59: D Hector Normal`
- `B60: D Hector Hard`
- `B61: ??`
- `B6: Palette (PLIST)`
- `B7: Chipset config (PLIST)`
- `B8: Map pointer (PLIST)`
- `B9: Tile animation 1 (PLIST)`
- `BGM / Music`
- `Breakable Wall HP / Difficulty Ratings`
- `Chapter number (B128):`
- `Clear Condition (display only)`
- `CP / Pointer`
- `D Rating`
- `D0: CP`
- `Difficulty Ratings`
- `Display Flags (B140-B147)`
- `Eliwood Hard (D100):`
- `Eliwood Normal (D96):`
- `Event / World Map (B116-B135)`
- `Experience Rating`
- `Hector Hard (D108):`
- `Hector Normal (D104):`
- `Map Name Text IDs`
- `Map Properties`
- `Map Settings`
- `Map Style / PLIST`
- `Pointers (D96-D108)`
- `Strategy Rating`
- `W112: Map name 1`
- `W114: Map name 2 (X)`
- `W136: Clear condition (display only)`
- `W138: Detail clear condition (display only)`
- `W20: Difficulty adjustment`
- `W22: Player phase BGM`
- `W24: Enemy phase BGM`
- `W26: NPC phase BGM`
- `W28: Player phase BGM 2 (X)`
- `W30: Enemy phase BGM 2 (X)`
- `W32: NPC phase BGM 2 (X)`
- `W34: Player phase BGM flag 4`
- `W36: Enemy phase BGM flag 4`
- `W38: ???`
- `W40: ???`
- `W42: ???`
- `W4: Object type (PLIST)`
- `W62: A Eliwood Normal`
- `W64: A Eliwood Hard`
- `W66: A Hector Normal`
- `W68: A Hector Hard`
- `W70: B Eliwood Normal`
- `W72: B Eliwood Hard`
- `W74: B Hector Normal`
- `W76: B Hector Hard`
- `W78: C Eliwood Normal`
- `W80: C Eliwood Hard`
- `W82: C Hector Normal`
- `W84: C Hector Hard`
- `W86: D Eliwood Normal`
- `W88: D Eliwood Hard`
- `W90: D Hector Normal`
- `W92: D Hector Hard`
- `W94: ??`
- `Write`

### MapSettingFE6Form
WF labels: **65** · AV labels: **2** · WF-only: **65** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Chapter Number`
- `CP`
- `Size:`
- `X:`
- `Y:`
- `♪`
- `アドレス`
- `イベントID(Plist)`
- `オブジェクトタイプ(Plist)`
- `クリア条件(表示のみ)`
- `タイルアニメーション1`
- `タイルアニメーション2`
- `チップセットクタイプ(Plist)`
- `ハードブースト`
- `パレット(Plist)`
- `マップエディタへJump`
- `マップスタイルの変更`
- `マップポインタ(Plist)`
- `マップ部分変更(Plist)`
- `リストの拡張`
- `ワールドマップBGM`
- `ワールドマップX`
- `ワールドマップY`
- `ワールドマップポイントX`
- `ワールドマップポイントY`
- `ワールドマップ地名`
- `ワールドマップ自動イベント`
- `上の軍`
- `下の軍`
- `先頭アドレス`
- `再取得`
- `初期座標`
- `勝利BGMに変わる敵数`
- `友軍BGM`
- `名前`
- `味方フェーズBGM`
- `壊れる壁HP`
- `天気`
- `戦力評価A`
- `戦力評価B`
- `戦力評価C`
- `戦力評価D`
- `戦闘準備の有無`
- `戦闘背景`
- `攻略評価`
- `攻略評価A`
- `攻略評価B`
- `攻略評価C`
- `攻略評価D`
- `敵の軍旗`
- `敵フェーズBGM`
- `書き込み`
- `章オープニングBGM`
- `章タイトル`
- `章タイトル画像`
- `経験評価`
- `経験評価A`
- `経験評価B`
- `経験評価C`
- `経験評価D`
- `読込数`
- `選択アドレス:`
- `離脱ポイントへJump`
- `霧レベル`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Map Settings (FE6)`

### ClassForm
WF labels: **58** · AV labels: **79** · WF-only: **57** · AV-only: **78** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `???`
- `[HardCoding]`
- `CCボーナス`
- `Export All`
- `Export Selected`
- `Growths as Decimal`
- `HP`
- `ID`
- `Import All`
- `Import Selected`
- `Include Base Stats`
- `Include Growths`
- `Include Header`
- `Include Name`
- `Include UID`
- `Include Wep Level`
- `LV`
- `Size:`
- `Use Clipboard`
- `アドレス`
- `クラスチェンジクラス`
- `クラス基礎能力`
- `クラス能力最大値`
- `シミュレーション`
- `スキル`
- `ソート順`
- `リストの拡張`
- `一般兵顔`
- `体格`
- `先頭アドレス`
- `再取得`
- `合計%`
- `名前`
- `地形回避`
- `地形防御`
- `地形魔防`
- `守備`
- `幸運`
- `待機アイコン`
- `戦闘時アニメ`
- ` 技 `
- `攻撃`
- `敵成長率(%)`
- `書き込み`
- `武器LV`
- `移動`
- `移動コスト`
- `移動コスト(雨)`
- `移動コスト(雪)`
- `移動速度`
- `経験値補正値`
- `詳細`
- `読込数`
- `速さ`
- `選択アドレス:`
- `魔力`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `??? (D80):`
- `Ability 1 (B40):`
- `Ability 2 (B41):`
- `Ability 3 (B42):`
- `Ability 4 (B43):`
- `Ability Flags`
- `Address:`
- `Anima (B49):`
- `Axe (B46):`
- `Base Stats`
- `Battle Anime (P52):`
- `Bow (B47):`
- `Calculate Growth`
- `Class # (B4):`
- `Class Editor`
- `Con (B17):`
- `Dark (B51):`
- `Def (B15):`
- `Def (B31):`
- `Def (b38):`
- `Desc`
- `Desc ID (W2):`
- `Edit Skills`
- `Export TSV`
- `Growth Rates`
- `Growth Simulator`
- `HP (B11):`
- `HP (B27):`
- `HP (b34):`
- `Identity / Misc`
- `Import TSV`
- `Jump`
- `Lance (B45):`
- `Lck (B33):`
- `Light (B50):`
- `Max Con (B25):`
- `Max Def (B23):`
- `Max HP (B19):`
- `Max Res (B24):`
- `Max Skl (B21):`
- `Max Spd (B22):`
- `Max Str (B20):`
- `Mov (B18):`
- `Move Cost (P56):`
- `Move Cost Rain (P60):`
- `Move Cost Snow (P64):`
- `Name:`
- `Name ID (W0):`
- `Pointers / Movement / Terrain`
- `Portrait (W8):`
- `Power (B26):`
- `Promo Lv (B5):`
- `Promotion Gains (CC Bonus)`
- `Res (B16):`
- `Res (B32):`
- `Res (b39):`
- `Sim Level:`
- `Skl (B13):`
- `Skl (B29):`
- `Skl (b36):`
- `Sort Order (B10):`
- `Spd (B14):`
- `Spd (B30):`
- `Spd (b37):`
- `Staff (B48):`
- `Stat Caps (Max Values)`
- `Str (B12):`
- `Str (B28):`
- `Str (b35):`
- `Sword (B44):`
- `Terrain Avoid (P68):`
- `Terrain Def (P72):`
- `Terrain Res (P76):`
- `Wait Icon (B6):`
- `Walk Spd (B7):`
- `Warnings`
- `Weapon Rank Levels (B44-B51)`
- `Write`

### EventUnitForm
WF labels: **50** · AV labels: **24** · WF-only: **50** · AV-only: **24** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `/60秒`
- `1次AI`
- `2次AI`
- `??`
- `Close`
- `FF`
- `LV:`
- `Size:`
- `X:`
- `Y:`
- `↑`
- `↓`
- `アイテムドロップ`
- `アドレス`
- `イベント名前`
- `クラス`
- `コメント`
- `マップ名前`
- `ユニット情報`
- `ユニット番号`
- `リストの拡張`
- `交戦時BGMへJump`
- `交戦時セリフへJump`
- `先頭アドレス`
- `再取得`
- `削除`
- `名前`
- `変更`
- `座標`
- `待機`
- `成長率:`
- `所属:`
- `所持品1`
- `所持品2`
- `所持品3`
- `所持品4`
- `指揮官`
- `新規挿入`
- `新規領域の確保`
- `書き込み`
- `標的と回復AI`
- `死亡時セリフへJump`
- `特殊`
- `移動後座標`
- `移動速度`
- `読込数`
- `追従`
- `退避AI`
- `選択アドレス:`
- `配置後座標格納アドレス`

AV-only labels (usually fine — layout polish or rewording):

- `0x Address`
- `Address:`
- `AI1 Primary:`
- `AI2 Secondary:`
- `AI3 Target/Recovery:`
- `AI4 Retreat:`
- `Class ID:`
- `Coord Count:`
- `Coord Pointer:`
- `Event Unit Placement (FE8)`
- `Item 1:`
- `Item 2:`
- `Item 3:`
- `Item 4:`
- `Leader Unit ID:`
- `Load`
- `Maps`
- `Reserved (0x06):`
- `Unit Groups`
- `Unit Growth:`
- `Unit ID:`
- `Unit Info:`
- `Units`
- `Write`

### SongInstrumentForm
WF labels: **51** · AV labels: **22** · WF-only: **50** · AV-only: **21** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `00`
- `= c_v - 64`
- `??`
- `atk`
- `dec`
- `DirectSound`
- `DirectSound Fixed Freq`
- `DirectSound Reverse`
- `DrumPart`
- `Info`
- `Multi Sample`
- `Noise`
- `Noise2`
- `noisepattern`
- `PAN強制`
- `Preview`
- `rel`
- `s`
- `Size:`
- `SongData FingerPrint`
- `squarepattern`
- `SquareWave1`
- `SquareWave2`
- `SquareWave3`
- `SquareWave4`
- `sus`
- `sweepshift`
- `sweeptime`
- `Wave Memory`
- `Wave Memory2`
- `アドレス`
- `キー割り当て`
- `ドラムセット`
- `パンポット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `基準ノート値`
- `書き込み`
- `楽器セット`
- `楽器データ 書出`
- `楽器データ 読込`
- `波形データ`
- `種類`
- `読込数`
- `選択アドレス:`
- `音データ`
- `音データ 書出`
- `音データ 読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Attack:`
- `Decay:`
- `Drum Part Pointer`
- `Duty / Length:`
- `Envelope Step:`
- `Header Byte:`
- `Instrument Editor`
- `Key Map Pointer:`
- `Multi Sample Pointers`
- `Noise Parameters + ADSR`
- `Period:`
- `Release:`
- `Square Wave Parameters + ADSR`
- `Sub-Instrument Ptr:`
- `Sustain:`
- `Type:`
- `Unknown instrument type. Raw 12-byte data is shown in the header byte field.`
- `Wave Pointer:`
- `Wave Pointer + ADSR`
- `Write`

### TextForm
WF labels: **48** · AV labels: **11** · WF-only: **48** · AV-only: **11** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `AI用に登場人物のヒントを追加`
- `from:`
- `google translateに投げて、翻訳しますw
負荷をかけすぎないように、忖度してください。`
- `ID テキスト`
- `Import/Export`
- `REDO`
- `Size:`
- `to:`
- `UNDO`
- `このテキストを利用している箇所`
- `すべてのテキストをファイルに書きだす`
- `アドレス`
- `エクスポート制限`
- `キャラ消去`
- `キャラ登場`
- `キャラ移動`
- `ジャンプ`
- `ジャンプする場所:`
- `セリフ`
- `ソーステキスト`
- `テキスト`
- `リストの拡張`
- `先頭アドレス`
- `全てのテキストをファイルから読みこむ`
- `再取得`
- `削除`
- `参照`
- `参照情報の追加`
- `参照箇所`
- `名前`
- `変更`
- `変更したい行をダブルクリック or Enterキーを押してください。 右クリックでメニューが出ます。`
- `新規追加`
- `書き込み`
- `未参照の空き領域の探索`
- `検索`
- `検索ワード`
- `消去する場所:`
- `登場する人:`
- `登場する場所:`
- `移動元場所:`
- `移動先場所:`
- `簡易`
- `翻訳`
- `翻訳する`
- `話す人:`
- `読込数`
- `警告:
セリフが
3行以上に
なっています`

AV-only labels (usually fine — layout polish or rewording):

- `Edit Text:`
- `Export All Texts (TSV)`
- `ID:`
- `Import Texts (TSV)`
- `Other reference sources (maps, events, supports, …) not yet ported. Track full parity in a follow-up issue.`
- `References (units, classes, items):`
- `Search Content`
- `Search in text content...`
- `Show All`
- `Text Editor`
- `Write Text`

### WorldMapImageForm
WF labels: **47** · AV labels: **2** · WF-only: **47** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00 Padding`
- `??`
- `AP`
- `Center X`
- `Center Y`
- `Height`
- `OAMTable entry`
- `Size:`
- `tcs params??`
- `TSA`
- `Width`
- `X`
- `Y`
- `アイコン用のデータ`
- `アドレス`
- `イベント用`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレット`
- `パレットマップ`
- `ポインタを書き込む`
- `ミニマップ`
- `メインフィールドマップ`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `国境`
- `拠点アイコン`
- `拠点画像1`
- `拠点画像2`
- `描画例`
- `書き込み`
- `減色ツール`
- `画像`
- `画像シート番号`
- `画像取出`
- `画像取出し`
- `画像読込`
- `読込数`
- `通常時パレット`
- `道画像`
- `選択アドレス:`
- `闇パレット`
- `闇マップ`
- `闇マップ画像取出`
- `闇マップ画像読込(パレット)`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `World Map Image`

### ImageUnitPaletteForm
WF labels: **45** · AV labels: **17** · WF-only: **45** · AV-only: **17** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1`
- `10`
- `11`
- `12`
- `13`
- `14`
- `15`
- `16`
- `2`
- `3`
- `4`
- `5`
- `6`
- `7`
- `8`
- `9`
- `B`
- `G`
- `R`
- `REDO`
- `Size:`
- `UNDO`
- `↓文字列内訳`
- `アドレス`
- `クリップボード`
- `コメント`
- `パレットアドレス`
- `パレット書き込み`
- `パレット種類`
- `ポインタ`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `利用クラスとアニメ`
- `名前`
- `戦闘アニメ`
- `拡大`
- `敵とNPC、グレーも同じ色に設定する`
- `新規パレット割り当て`
- `書き込み`
- `画像取出`
- `画像読込`
- `読込数`
- `識別子`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Id 0:`
- `Id 1:`
- `Id 10:`
- `Id 11:`
- `Id 2:`
- `Id 3:`
- `Id 4:`
- `Id 5:`
- `Id 6:`
- `Id 7:`
- `Id 8:`
- `Id 9:`
- `Identifier:`
- `Palette Ptr (P12):`
- `Unit Palette Editor`
- `Write`

### ItemForm
WF labels: **45** · AV labels: **47** · WF-only: **45** · AV-only: **47** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `[HardCoding]`
- `HP`
- `ID`
- `Size:`
- `アイコン`
- `アドレス`
- `ダメージ追加効果`
- `リストの拡張`
- `レベル`
- `体格`
- `使った場合`
- `使用画面`
- `先頭アドレス`
- `再取得`
- `単価`
- `名前`
- `命中`
- `売却価格`
- `守備`
- `幸運`
- `店での買値`
- `必殺`
- `性能`
- ` 技 `
- `攻撃`
- `書き込み`
- `武器LV熟練度`
- `特効`
- `特効効果
新規割当`
- `移動`
- `種別`
- `耐久`
- `能力補正`
- `能力補正
新規割当`
- `能力補正値`
- `説明`
- `読込数`
- `速さ`
- `進撃準備店`
- `選択アドレス:`
- `重さ`
- `間接エフェクト Jump`
- `魔力`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Basic Info`
- `Buy Price:`
- `Crit (B24):`
- `Desc`
- `Desc ID (W2):`
- `Dmg Effect (B31):`
- `Edit Skill Config`
- `Effective (P16):`
- `Effective Against`
- `Export TSV`
- `Forge Price:`
- `Hit (B22):`
- `Icon (B29):`
- `Import TSV`
- `Item # (B6):`
- `Item Editor`
- `Jump`
- `Might (B21):`
- `Name:`
- `Name ID (W0):`
- `Price (W26):`
- `Range (B25):`
- `Rank (B28):`
- `Sell Price:`
- `Stat Bonus (P12):`
- `Stat Bonuses Preview`
- `Stats / Bonuses`
- `Trait 1 (B8):`
- `Trait 2 (B9):`
- `Trait 3 (B10):`
- `Trait 4 (B11):`
- `Trait Flags`
- `Type (B7):`
- `Unk33 (B33):`
- `Unk34 (B34):`
- `Unk35 (B35):`
- `Use Desc (W4):`
- `Use Effect (B30):`
- `Uses (B20):`
- `Warning: Effectiveness pointer is null (P16=0). Consider allocating.`
- `Warning: Stat Bonuses pointer is null (P12=0). Consider allocating.`
- `Warnings`
- `Weapon Properties`
- `Weight (B23):`
- `Wep Exp (B32):`
- `Write`

### MapStyleEditorForm
WF labels: **45** · AV labels: **5** · WF-only: **45** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1`
- `10`
- `11`
- `12`
- `13`
- `14`
- `15`
- `16`
- `2`
- `3`
- `4`
- `5`
- `6`
- `7`
- `8`
- `9`
- `B`
- `G`
- `No`
- `R`
- `REDO`
- `UNDO`
- `X`
- `Y`
- `アドレス`
- `オブジェクト`
- `クリップボード`
- `タイルのコピー(Alt + T) `
- `パレット`
- `パレットNo`
- `パレット取出`
- `パレット読込`
- `マップスタイル`
- `マップチップ割り当ての保存`
- `マップチップ割り当ての読込`
- `右上`
- `右下`
- `左上`
- `左下`
- `張り付け(Alt + V)`
- `書き込み`
- `画像取出`
- `画像読込`
- `種類`
- `種類のコピー(Alt + C)`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Config Pointer:`
- `Map Style Editor`
- `OBJ Tile Pointer:`
- `Write`

### UnitForm
WF labels: **49** · AV labels: **54** · WF-only: **45** · AV-only: **50** · Common: **4**

WF-only labels (candidates for missing fields in AV):

- `[HardCoding]`
- `Export All`
- `Export Selected`
- `Growths as Decimal`
- `ID`
- `Import All`
- `Import Selected`
- `Include Base Stats`
- `Include Growths`
- `Include Header`
- `Include Name`
- `Include UID`
- `Include Wep Level`
- `Size:`
- `Use Clipboard`
- `アドレス`
- `シミュレーション`
- `スキル`
- `マップ顔`
- `ユニットソート順`
- `ユニット別パレットへジャンプ`
- `ユニット別能力`
- `会話グループ`
- `体格`
- `先頭アドレス`
- `再取得`
- `合計%`
- `名前`
- `守備`
- `属性:-`
- `幸運`
- `成長率(%)`
- ` 技 `
- `支援クラス`
- `支援データ`
- `攻撃`
- `書き込み`
- `武器LV`
- `詳細`
- `読込数`
- `速さ`
- `選択アドレス:`
- `顔`
- `魔力`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Ability Flags`
- `Address:`
- `Affinity:`
- `Anima:`
- `Axe:`
- `Base Stats`
- `Bow:`
- `Byte 1:`
- `Byte 2:`
- `Byte 3:`
- `Byte 4:`
- `Calculate Growth`
- `Class ID:`
- `Con:`
- `Dark:`
- `Def:`
- `Desc`
- `Desc ID:`
- `Edit Skills`
- `Export TSV`
- `Growth Rates (%)`
- `Growth Simulator`
- `Identity`
- `Import TSV`
- `Jump`
- `Lance:`
- `Lck:`
- `Light:`
- `Map Face:`
- `Name:`
- `Name ID:`
- `Pick...`
- `Portrait:`
- `Res:`
- `Simulate to LV:`
- `Skl:`
- `Sort:`
- `Spd:`
- `Staff:`
- `Str:`
- `Support & Other`
- `Support Ptr:`
- `Sword:`
- `Talk:`
- `Undo`
- `Unit Editor`
- `Unit ID:`
- `Warnings`
- `Weapon Levels`
- `Write`

### ItemFE6Form
WF labels: **44** · AV labels: **27** · WF-only: **44** · AV-only: **27** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `[HardCoding]`
- `HP`
- `ID`
- `Size:`
- `アイコン`
- `アドレス`
- `ダメージ追加効果`
- `リストの拡張`
- `レベル`
- `体格`
- `使った場合`
- `使用画面`
- `先頭アドレス`
- `再取得`
- `単価`
- `名前`
- `命中`
- `売却価格`
- `守備`
- `射程`
- `幸運`
- `店での買値`
- `必殺`
- `性能`
- ` 技 `
- `攻撃`
- `書き込み`
- `特効`
- `特効効果
新規割当`
- `移動`
- `種別`
- `耐久`
- `能力補正`
- `能力補正
新規割当`
- `能力補正値`
- `説明`
- `読込数`
- `速さ`
- `進撃準備店`
- `選択アドレス:`
- `重さ`
- `間接エフェクト Jump`
- `魔力`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Crit (B24):`
- `Desc`
- `Desc ID (W2):`
- `Dmg Effect (B31):`
- `Effective (P16):`
- `Hit (B22):`
- `Icon (B29):`
- `Item # (B6):`
- `Item Editor (FE6)`
- `Might (B21):`
- `Name:`
- `Name ID (W0):`
- `Price (W26):`
- `Range (B25):`
- `Rank (B28):`
- `Stat Bonus (P12):`
- `Trait 1 (B8):`
- `Trait 2 (B9):`
- `Trait 3 (B10):`
- `Trait 4 (B11):`
- `Type (B7):`
- `Use Desc (W4):`
- `Use Effect (B30):`
- `Uses (B20):`
- `Weight (B23):`
- `Write`

### ToolInitWizardForm
WF labels: **44** · AV labels: **8** · WF-only: **44** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `BeginPage`
- `EA:`
- `EN`
- `EndPage`
- `gba mus riper`
- `git`
- `JP`
- `mGBAをダウンロードする`
- `midfix4agb`
- `Sappy:`
- `Sappyをダウンロードする`
- `Sappyを設定しない`
- `SettingNowPage`
- `sox`
- `Step1Page`
- `Step2Page`
- `Step3Page`
- `Step4Page`
- `Step5Page`
- `Step6Page`
- `VGMusicStudioをダウンロードする`
- `ZH`
- `しばらくお待ちください....`
- `すべての設定が完了しました。`
- `または、`
- `アセンブラ:`
- `エミュレータ:`
- `デバッガー:`
- `参照`
- `始める`
- `安定して動作するバージョンのVBA-Mをダウンロードする`
- `完了`
- `戻る`
- `最新版のEAをダウンロードする`
- `最新版のGitを自動でダウンロードしてインストールします。`
- `最新版を自動的にダウンロードします。`
- `白背景`
- `色`
- `言語`
- `設定して完了する`
- `設定して次へ`
- `設定しない`
- `黒背景`
- `黒背景2`

AV-only labels (usually fine — layout polish or rewording):

- `Initial Configuration`
- `Setup Steps`
- `Setup Wizard`
- `Step 1: Select your clean Fire Emblem GBA ROM file.`
- `Step 2: Choose your preferred language for the editor interface.`
- `Step 3: Configure paths for external tools (emulators, assemblers).`
- `Step 4: Review settings and begin editing.`
- `You can change these settings later from the Options menu.`

### ClassFE6Form
WF labels: **43** · AV labels: **5** · WF-only: **43** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `- `
- `??`
- `???`
- `[HardCoding]`
- `HP`
- `ID`
- `LV`
- `Size:`
- `アドレス`
- `クラスチェンジクラス`
- `クラス基礎能力`
- `クラス能力最大値`
- `シミュレーション`
- `ソート順`
- `リストの拡張`
- `一般兵顔`
- `体格`
- `先頭アドレス`
- `再取得`
- `合計%`
- `名前`
- `地形回避`
- `地形防御`
- `地形魔防`
- `守備`
- `幸運`
- `待機アイコン`
- `戦闘時アニメ`
- ` 技 `
- `攻撃`
- `敵成長率(%)`
- `書き込み`
- `武器LV`
- `移動`
- `移動コスト`
- `移動速度`
- `経験値補正値`
- `詳細`
- `読込数`
- `速さ`
- `選択アドレス:`
- `魔力`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `--- Growth Simulator ---`
- `Address:`
- `Calculate Growth`
- `Class Editor (FE6)`
- `Sim Level:`

### ImageBattleScreenForm
WF labels: **42** · AV labels: **2** · WF-only: **42** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1`
- `10`
- `11`
- `12`
- `13`
- `14`
- `15`
- `16`
- `2`
- `3`
- `4`
- `5`
- `6`
- `7`
- `8`
- `9`
- `B`
- `G`
- `Import/Export`
- `R`
- `REDO`
- `Tile1`
- `Tile2`
- `Tile3`
- `Tile4`
- `Tile5`
- `UNDO`
- `アイテム`
- `クリップボード`
- `パレット`
- `パレットアドレス`
- `パレット書き込み`
- `パレット種類`
- `メイン画像`
- `右側`
- `名前`
- `左側`
- `戦闘画面を一括でインポートします。
TSAがあるので、共通タイルは1つにまとめられるという制約があります。`
- `書き込み`
- `画像`
- `画像取出`
- `画像読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Battle Screen Layout`

### UnitFE7Form
WF labels: **39** · AV labels: **58** · WF-only: **38** · AV-only: **57** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `- `
- `??`
- `[HardCoding]`
- `ID`
- `LV`
- `Size:`
- `アドレス`
- `シミュレーション`
- `マップ顔`
- `ユニットソート順`
- `ユニット別能力`
- `上位クラス戦闘アニメ色`
- `上級専用アニメ`
- `下位クラス戦闘アニメ色`
- `下級専用アニメ`
- `会話グループ`
- `体格`
- `先頭アドレス`
- `再取得`
- `合計%`
- `名前`
- `守備`
- `属性:-`
- `幸運`
- `成長率(%)`
- ` 技 `
- `支援クラス`
- `支援データ`
- `攻撃`
- `書き込み`
- `武器LV`
- `詳細`
- `読込数`
- `速さ`
- `選択アドレス:`
- `顔`
- `魔力`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Ability 1:`
- `Ability 2:`
- `Ability 3:`
- `Ability 4:`
- `Ability Flags:`
- `Address:`
- `Affinity:`
- `Anima:`
- `Axe:`
- `Base Stats:`
- `Bow:`
- `CON:`
- `Dark:`
- `Decoded:`
- `DEF:`
- `DEF Growth:`
- `Desc`
- `Description:`
- `Growth Rates (%):`
- `HP Growth:`
- `Lance:`
- `LCK:`
- `LCK Growth:`
- `Level:`
- `Light:`
- `Lower Class Anime:`
- `Lower Class Palette:`
- `Map Face:`
- `Name:`
- `Palette / Custom Animation:`
- `Portrait:`
- `RES:`
- `RES Growth:`
- `SKL:`
- `SKL Growth:`
- `Sort Order:`
- `SPD:`
- `SPD Growth:`
- `Staff:`
- `STR:`
- `STR Growth:`
- `Support / Talk:`
- `Support Class:`
- `Support Data Ptr:`
- `Sword:`
- `Talk Group:`
- `Unit ID:`
- `Units (FE7) Editor`
- `Unknown 39:`
- `Unknown 49:`
- `Unknown 50:`
- `Unknown 51:`
- `Unknown Fields:`
- `Upper Class Anime:`
- `Upper Class Palette:`
- `Weapon Ranks:`
- `Write`

### MonsterItemForm
WF labels: **37** · AV labels: **8** · WF-only: **37** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `Size:`
- `アイテム1`
- `アイテム1確率`
- `アイテム2`
- `アイテム2確率`
- `アイテム3`
- `アイテム3確率`
- `アイテム4`
- `アイテム4確率`
- `アイテム5`
- `アイテム5確率`
- `アイテム確率`
- `アドレス`
- `クラス`
- `コメント`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `合計`
- `所持品1 候補1`
- `所持品1 候補2`
- `所持品1 候補3`
- `所持品1 候補4`
- `所持品1 候補5`
- `所持品2 候補1`
- `所持品2 候補2`
- `所持品2 候補3`
- `所持品2 候補4`
- `所持品2 候補5`
- `書き込み`
- `確率`
- `読込数`
- `選択アドレス:`
- `魔物アイテムテーブル`
- `魔物アイテム確率`
- `魔物所持品テーブル`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Drop Rate:`
- `Item ID:`
- `Monster Item Editor`
- `Unknown 1:`
- `Unknown 2:`
- `Unknown 3:`
- `Write`

### SkillConfigFE8NVer3SkillForm
WF labels: **37** · AV labels: **11** · WF-only: **37** · AV-only: **11** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `COMBAT_ARTへ移動`
- `Size:`
- `このアイテムを所持しているときに、このスキルを有効にします。`
- `このアイテムを武器として装備しているときに、このスキルを有効にします。`
- `アイコン`
- `アイテム`
- `アドレス`
- `アニメ`
- `アニメーション取出`
- `アニメーション読込`
- `エディタ`
- `クラス`
- `クラススキル`
- `スキル`
- `パレット`
- `フレーム`
- `ユニット`
- `ユニットクラス`
- `ユニットスキル`
- `リストの拡張`
- `レベルアップで取得するスキルの先頭アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `所持アイテムスキル`
- `拡大`
- `書き込み`
- `武器アイテムスキル`
- `現在のスキルに、指定した他のスキルの効果を追加します。`
- `画像取出`
- `画像読込`
- `表示例`
- `複合スキル`
- `詳細`
- `読込数`
- `選択アドレス:`
- `領域が確保されていません。
「リストの拡張ボタン」を押して領域を確保してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class Skill Pointer:`
- `Composite Skill Pointer:`
- `Held Item Skill Pointer:`
- `Palette:`
- `Skill Configuration (FE8N v3)`
- `Skill system editors require a compatible skill patch to be installed.
Use the Patch Manager to install a skill system patch first.

Supported skill systems: CSkillSys, FE8N Skill System`
- `Text Detail:`
- `Unit/Class Pointer:`
- `Weapon Item Skill Pointer:`
- `Write`

### EventUnitFE7Form
WF labels: **36** · AV labels: **24** · WF-only: **36** · AV-only: **24** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1次AI`
- `2次AI`
- `LV:`
- `Size:`
- `X:`
- `Y:`
- `アイテムドロップ`
- `アドレス`
- `イベント名前`
- `クラス`
- `コメント`
- `マップ名前`
- `ユニット情報`
- `ユニット番号`
- `リストの拡張`
- `交戦時BGMへJump`
- `交戦時セリフへJump`
- `先頭アドレス`
- `再取得`
- `名前`
- `成長率:`
- `所属:`
- `所持品1`
- `所持品2`
- `所持品3`
- `所持品4`
- `指揮官`
- `新規領域の確保`
- `書き込み`
- `標的と回復AI`
- `死亡時セリフへJump`
- `読込数`
- `退避AI`
- `選択アドレス:`
- `配置前座標`
- `配置後座標`

AV-only labels (usually fine — layout polish or rewording):

- `0x Address`
- `Address:`
- `AI1 Primary:`
- `AI2 Secondary:`
- `AI3 Target/Recovery:`
- `AI4 Retreat:`
- `Class ID:`
- `End X:`
- `End Y:`
- `Event Unit (FE7)`
- `Item 1:`
- `Item 2:`
- `Item 3:`
- `Item 4:`
- `Leader Unit ID:`
- `Load`
- `Maps`
- `Start X:`
- `Start Y:`
- `Unit Groups`
- `Unit ID:`
- `Unit Info:`
- `Units`
- `Write`

### ImageBattleAnimeForm
WF labels: **35** · AV labels: **29** · WF-only: **35** · AV-only: **29** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `FrameData`
- `LeftToRightOAM`
- `RightToLeftOAM`
- `SectionData`
- `Size:`
- `↓文字列内訳`
- `このテーブルは、複数のクラスで参照されています。`
- `アドレス`
- `アニメ番号`
- `インターネットから新しいリソースを探す`
- `エディタ`
- `コメント`
- `セクション`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレット`
- `フレーム`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメの書出し`
- `戦闘アニメの読込`
- `拡大`
- `方向`
- `書き込み`
- `武器種類`
- `汎用色`
- `特殊`
- `表示例`
- `読込数`
- `識別子`
- `選択アドレス:`
- `選択クラスの分離独立`
- `領域が確保されていません。
「リストの拡張ボタン」を押して領域を確保してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Animation # (W2):`
- `Animation Data Details`
- `Animation Frame Viewer`
- `Animation Table Summary`
- `Battle Animation Editor`
- `Data Address:`
- `Export Tile Sheet PNG...`
- `Frame:`
- `Frame Data:`
- `Frame Ptr:`
- `Name:`
- `Next`
- `No animation data found for this ID (ID=0 or invalid pointer chain).`
- `OAM (L→R) Ptr:`
- `OAM (R→L) Ptr:`
- `OAM Data:`
- `Palette Ptr:`
- `Play`
- `Prev`
- `Resolved:`
- `Section:`
- `Section Ptr:`
- `Special (B1):`
- `Speed:`
- `Sprite Tile Sheet`
- `Total animations: --`
- `Weapon Type (B0):`
- `Write`

### EventUnitFE6Form
WF labels: **34** · AV labels: **24** · WF-only: **34** · AV-only: **24** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1次AI`
- `2次AI`
- `LV:`
- `Size:`
- `X:`
- `Y:`
- `アドレス`
- `イベント名前`
- `クラス`
- `コメント`
- `マップ名前`
- `ユニット情報`
- `ユニット番号`
- `リストの拡張`
- `交戦時セリフへJump`
- `先頭アドレス`
- `再取得`
- `名前`
- `成長率:`
- `所属:`
- `所持品1`
- `所持品2`
- `所持品3`
- `所持品4`
- `指揮官`
- `新規領域の確保`
- `書き込み`
- `標的と回復AI`
- `死亡時セリフへJump`
- `読込数`
- `退避AI`
- `選択アドレス:`
- `配置前座標`
- `配置後座標`

AV-only labels (usually fine — layout polish or rewording):

- `0x Address`
- `Address:`
- `AI1 Primary:`
- `AI2 Secondary:`
- `AI3 Target/Recovery:`
- `AI4 Retreat:`
- `Class ID:`
- `End X:`
- `End Y:`
- `Event Unit (FE6)`
- `Item 1:`
- `Item 2:`
- `Item 3:`
- `Item 4:`
- `Leader Unit ID:`
- `Load`
- `Maps`
- `Start X:`
- `Start Y:`
- `Unit Groups`
- `Unit ID:`
- `Unit Info:`
- `Units`
- `Write`

### SongTrackForm
WF labels: **37** · AV labels: **11** · WF-only: **34** · AV-only: **8** · Common: **3**

WF-only labels (candidates for missing fields in AV):

- `Sappyで再生`
- `Size:`
- `アドレス`
- `インターネットから新しいリソースを探す`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `トラック1`
- `トラック10`
- `トラック11`
- `トラック12`
- `トラック13`
- `トラック14`
- `トラック15`
- `トラック16`
- `トラック2`
- `トラック3`
- `トラック4`
- `トラック5`
- `トラック6`
- `トラック7`
- `トラック8`
- `トラック9`
- `トラック数`
- `先頭アドレス`
- `再取得`
- `別ゲームからの曲移植`
- `名前`
- `書き込み`
- `楽器セット`
- `楽譜`
- `読込数`
- `選択アドレス:`
- `音楽をファイルから読み込む`
- `音楽をファイルへ書き出す`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Export MIDI`
- `Import MIDI`
- `Instrument Set:`
- `Song Track Editor`
- `Track Count:`
- `Tracks`
- `Write`

### UnitFE6Form
WF labels: **36** · AV labels: **45** · WF-only: **33** · AV-only: **42** · Common: **3**

WF-only labels (candidates for missing fields in AV):

- `- `
- `[HardCoding]`
- `ID`
- `Size:`
- `アドレス`
- `シミュレーション`
- `マップ顔`
- `ユニットソート順`
- `ユニット別能力`
- `上位クラス戦闘アニメ色`
- `下位クラス戦闘アニメ色`
- `体格`
- `先頭アドレス`
- `再取得`
- `合計%`
- `名前`
- `守備`
- `属性:-`
- `幸運`
- `成長率(%)`
- ` 技 `
- `支援クラス`
- `支援データ`
- `攻撃`
- `書き込み`
- `武器LV`
- `詳細`
- `読込数`
- `速さ`
- `選択アドレス:`
- `顔`
- `魔力`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Ability Flags`
- `Address:`
- `Affinity:`
- `Anima:`
- `Axe:`
- `Base Stats`
- `Bow:`
- `Byte1:`
- `Byte2:`
- `Byte3:`
- `Byte4:`
- `Class ID:`
- `Con:`
- `Dark:`
- `Def:`
- `Desc`
- `Desc ID:`
- `Growth Rates (%)`
- `Identity`
- `Lance:`
- `Lck:`
- `Light:`
- `Lower Class Anim Color`
- `Map Face:`
- `Name:`
- `Name ID:`
- `Portrait:`
- `Res:`
- `Skl:`
- `Sort:`
- `Spd:`
- `Staff:`
- `Str:`
- `Support & Other`
- `Support Ptr:`
- `Sword:`
- `Undo`
- `Unit Editor (FE6)`
- `Unit ID:`
- `Upper Class Anim Color`
- `Weapon Levels`
- `Write`

### ClassOPDemoForm
WF labels: **32** · AV labels: **2** · WF-only: **32** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `/60 (秒)`
- `00固定`
- `05固定`
- `??`
- `size:`
- `アドレス`
- `アニメの特殊指定`
- `アニメ指定
ポインタ書き込み`
- `アニメ指定のポインタ`
- `アニメ指定のポインタ先`
- `アニメ指定共有`
- `ウェイト`
- `パレットID`
- `リストの拡張`
- `使用可能表示武器`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメ`
- `敵味方カラー`
- `日本語名
ポインタ書き込み`
- `日本語名の長さ`
- `日本語名ポインタ`
- `日本語名ポインタ先`
- `書き込み`
- `英語ポインタ`
- `表示地形右半分`
- `表示地形左半分`
- `説明文ID`
- `読込数`
- `選択アドレス:`
- `魔法エフェクト`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class OP Demo`

### ImagePortraitForm
WF labels: **32** · AV labels: **22** · WF-only: **32** · AV-only: **22** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `mug_exceed用のタイルをどこに配置するか設定してください`
- `Size:`
- `X:`
- `Y:`
- `アドレス`
- `インターネットから新しいリソースを探す`
- `クラス顔`
- `コメント`
- `ステータス画面の背丈調整へJump`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `タイル1`
- `タイル2`
- `パレット`
- `フレーム`
- `マップ顔`
- `ユニット顔`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `口`
- `口座標`
- `名前`
- `書き込み`
- `状態`
- `画像取出`
- `画像読込`
- `目座標`
- `表示例`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class Card:`
- `Export PAL`
- `Export PNG`
- `Eye X:`
- `Eye Y:`
- `Image Pointer:`
- `Import PAL`
- `Import PNG`
- `Main Portrait`
- `Map Pointer:`
- `Mini Portrait`
- `Mouth:`
- `Mouth X:`
- `Mouth Y:`
- `Padding (B25):`
- `Padding (B26):`
- `Padding (B27):`
- `Palette Pointer:`
- `Portrait Editor`
- `State:`
- `Write`

### ImagePortraitForm
WF labels: **32** · AV labels: **28** · WF-only: **32** · AV-only: **28** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `mug_exceed用のタイルをどこに配置するか設定してください`
- `Size:`
- `X:`
- `Y:`
- `アドレス`
- `インターネットから新しいリソースを探す`
- `クラス顔`
- `コメント`
- `ステータス画面の背丈調整へJump`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `タイル1`
- `タイル2`
- `パレット`
- `フレーム`
- `マップ顔`
- `ユニット顔`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `口`
- `口座標`
- `名前`
- `書き込み`
- `状態`
- `画像取出`
- `画像読込`
- `目座標`
- `表示例`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class Card`
- `Export Face`
- `Export Mini`
- `Export PAL`
- `Export Sheet`
- `Eye Frames (32x16 x2)`
- `Eye X:`
- `Eye Y:`
- `FE-Repo`
- `Import PAL`
- `Import PNG`
- `Map Face:`
- `Map Face (32x32)`
- `Mouth Frames:`
- `Mouth Frames (32x16 x6)`
- `Mouth X:`
- `Mouth Y:`
- `Palette:`
- `Portrait Image Editor`
- `Show Frame:`
- `Status:`
- `Unit Face:`
- `Unit Face (96x80)`
- `Unused (B25):`
- `Unused (B26):`
- `Unused (B27):`
- `Write Positions`

### SkillConfigFE8NVer2SkillForm
WF labels: **31** · AV labels: **10** · WF-only: **31** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アイコン`
- `アイテム`
- `アドレス`
- `アニメ`
- `アニメーション取出`
- `アニメーション読込`
- `エディタ`
- `クラス`
- `クラススキル`
- `パレット`
- `フレーム`
- `ユニット`
- `ユニットスキル建設予定地`
- `リストの拡張`
- `レベルアップで取得するスキルの先頭アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `所持アイテムスキル`
- `拡大`
- `書き込み`
- `武器アイテムスキル`
- `画像取出`
- `画像読込`
- `表示例`
- `詳細`
- `読込数`
- `選択アドレス:`
- `領域が確保されていません。
「リストの拡張ボタン」を押して領域を確保してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class Skill Pointer:`
- `Held Item Skill Pointer:`
- `Palette:`
- `Skill Configuration (FE8N v2)`
- `Skill system editors require a compatible skill patch to be installed.
Use the Patch Manager to install a skill system patch first.

Supported skill systems: CSkillSys, FE8N Skill System`
- `Text Detail:`
- `Unit Skill Pointer:`
- `Weapon Item Skill Pointer:`
- `Write`

### ImageTSAEditorForm
WF labels: **30** · AV labels: **2** · WF-only: **30** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1`
- `10`
- `11`
- `12`
- `13`
- `14`
- `15`
- `16`
- `2`
- `3`
- `4`
- `5`
- `6`
- `7`
- `8`
- `9`
- `B`
- `G`
- `R`
- `REDO`
- `UNDO`
- `クリップボード`
- `パレット`
- `パレットアドレス`
- `パレット書き込み`
- `メイン画像`
- `書き込み`
- `画像`
- `画像取出`
- `画像読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `TSA Tile Editor`

### SummonsDemonKingForm
WF labels: **30** · AV labels: **19** · WF-only: **30** · AV-only: **19** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `1次AI`
- `2次AI`
- `??`
- `LV:`
- `Size:`
- `X:`
- `Y:`
- `アドレス`
- `クラス`
- `ユニット情報`
- `ユニット番号`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `座標`
- `成長率:`
- `所属:`
- `所持品1`
- `所持品2`
- `所持品3`
- `所持品4`
- `指揮官`
- `書き込み`
- `標的と回復AI`
- `特殊:`
- `読込数`
- `退避AI`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI 1:`
- `AI 2:`
- `AI Pointer:`
- `Class ID:`
- `Commander:`
- `Coordinates:`
- `Demon King Summon Editor`
- `Item 1:`
- `Item 2:`
- `Item 3:`
- `Item 4:`
- `Level/Growth:`
- `Padding (B7):`
- `Retreat AI:`
- `Special:`
- `Target/Recovery AI:`
- `Unit ID:`
- `Write`

### ImageBattleAnimePalletForm
WF labels: **29** · AV labels: **2** · WF-only: **29** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1`
- `10`
- `11`
- `12`
- `13`
- `14`
- `15`
- `16`
- `2`
- `3`
- `32ColorMode`
- `4`
- `5`
- `6`
- `7`
- `8`
- `9`
- `B`
- `G`
- `R`
- `REDO`
- `UNDO`
- `クリップボード`
- `パレットアドレス`
- `パレット書き込み`
- `パレット種類`
- `拡大`
- `画像取出`
- `画像読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Battle Animation Palette`

### ImagePalletForm
WF labels: **28** · AV labels: **2** · WF-only: **28** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1`
- `10`
- `11`
- `12`
- `13`
- `14`
- `15`
- `16`
- `2`
- `3`
- `4`
- `5`
- `6`
- `7`
- `8`
- `9`
- `B`
- `G`
- `R`
- `REDO`
- `UNDO`
- `クリップボード`
- `パレットアドレス`
- `パレット書き込み`
- `パレット種類`
- `拡大`
- `画像取出`
- `画像読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Palette Editor`

### OPClassDemoFE7Form
WF labels: **28** · AV labels: **16** · WF-only: **28** · AV-only: **16** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `/60 (秒)`
- `00`
- `??`
- `Commamd`
- `Size:`
- `アドレス`
- `アニメ指定
ポインタ書き込み`
- `アニメ指定のポインタ`
- `グラフィックツール`
- `パレットID`
- `リストの拡張`
- `使用可能表示武器`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメ`
- `敵味方カラー`
- `日本語名 開始位置`
- `日本語名の長さ`
- `日本語名アドレス`
- `書き込み`
- `英語ポインタ`
- `表示地形右半分`
- `表示地形左半分`
- `説明文ID`
- `読込数`
- `選択アドレス:`
- `魔法エフェクト`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Ally/Enemy Color:`
- `Anime Pointer:`
- `Battle Anime:`
- `Class ID:`
- `Description Text ID:`
- `Display Weapon:`
- `English Name Pointer:`
- `Japanese Name Length:`
- `Japanese Name Pointer:`
- `Magic Effect:`
- `OP Class Demo (FE7) Editor`
- `Palette ID:`
- `Terrain Left:`
- `Terrain Right:`
- `Write`

### StatusOptionForm
WF labels: **28** · AV labels: **22** · WF-only: **28** · AV-only: **22** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ASM関数`
- `Size:`
- `アイコン(value*8)`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`
- `選択子1テキスト`
- `選択子1ヘルプ`
- `選択子1不明2`
- `選択子1表示位地`
- `選択子2テキスト`
- `選択子2ヘルプ`
- `選択子2不明2`
- `選択子2表示位地`
- `選択子3テキスト`
- `選択子3ヘルプ`
- `選択子3不明2`
- `選択子3表示位地`
- `選択子4テキスト`
- `選択子4ヘルプ`
- `選択子4不明2`
- `選択子4表示位地`
- `項目名`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `ASM Pointer:`
- `Columns:`
- `Default Text ID:`
- `Default Value:`
- `Help Text ID:`
- `Icon ID:`
- `ID Text:`
- `Max Value:`
- `Min Value:`
- `Name Text ID:`
- `On/Off Text 1:`
- `On/Off Text 2:`
- `Option Type:`
- `Rows:`
- `Selection Text 1:`
- `Selection Text 2:`
- `Status Screen Options`
- `Write`
- `X Position:`
- `Y Position:`
- `Yes Text ID:`

### ToolLZ77Form
WF labels: **28** · AV labels: **2** · WF-only: **28** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Base64`
- `Base64 Text to File`
- `Base64 Text to Run Emulator`
- `DEST`
- `Emulator`
- `File to Base64 Text`
- `FROM`
- `LENGTH`
- `lz77再圧縮`
- `Plain`
- `SRC`
- `SRC開始アドレス`
- `TO`
- `この領域をゼロクリア`
- `この領域を移動する`
- `再圧縮`
- `再圧縮する`
- `別ファイル選択`
- `圧縮する`
- `戦闘アニメOAM`
- `戦闘アニメOAMの最適化`
- `消去`
- `移動`
- `解凍する`
- `音楽GOTO-FINE`
- `音楽GOTO-FINE最適化`
- `顔画像`
- `顔画像を圧縮する`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `LZ77 Compression Tool`

### OPClassDemoForm
WF labels: **27** · AV labels: **16** · WF-only: **27** · AV-only: **16** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `/60 (秒)`
- `00`
- `??`
- `Commamd`
- `Size:`
- `アドレス`
- `アニメ指定
ポインタ書き込み`
- `アニメ指定のポインタ`
- `パレットID`
- `リストの拡張`
- `使用可能表示武器`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメ`
- `敵味方カラー`
- `日本語名
ポインタ書き込み`
- `日本語名の長さ`
- `日本語名ポインタ`
- `書き込み`
- `英語ポインタ`
- `表示地形右半分`
- `表示地形左半分`
- `説明文ID`
- `読込数`
- `選択アドレス:`
- `魔法エフェクト`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Ally/Enemy Color:`
- `Anime Pointer:`
- `Battle Anime:`
- `Description Text ID:`
- `Display Weapon:`
- `English Name Pointer:`
- `Japanese Name Length:`
- `Japanese Name Pointer:`
- `Magic Effect:`
- `OP Class Demo Editor`
- `Palette ID:`
- `Terrain Left:`
- `Terrain Right:`
- `Unknown (0x12):`
- `Write`

### ImageMagicCSACreatorForm
WF labels: **25** · AV labels: **2** · WF-only: **25** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `dim`
- `FrameData`
- `OBJBGLeftToRight`
- `OBJBGRightToLeft`
- `OBJLeftToRight`
- `OBJRightToLeft`
- `Size:`
- `アドレス`
- `インターネットから新しいリソースを探す`
- `エディタ`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `フレーム`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `拡大`
- `書き込み`
- `表示例`
- `読込数`
- `選択アドレス:`
- `魔法アニメの書出し`
- `魔法アニメの読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `CSA Magic Creator`

### ImageMagicFEditorForm
WF labels: **25** · AV labels: **2** · WF-only: **25** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `dim`
- `FrameData`
- `OBBGLeftToRight`
- `OBJBGRightToLeft`
- `OBJLeftToRight`
- `OBJRightToLeft`
- `Size:`
- `アドレス`
- `インターネットから新しいリソースを探す`
- `エディタ`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `フレーム`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `拡大`
- `書き込み`
- `表示例`
- `読込数`
- `選択アドレス:`
- `魔法アニメの書出し`
- `魔法アニメの読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Magic Effect Editor (FEditor)`

### AIScriptForm
WF labels: **24** · AV labels: **2** · WF-only: **24** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Close`
- `アドレス`
- `コメント`
- `バイナリコード`
- `パラメータ1`
- `パラメータ2`
- `パラメータ3`
- `パラメータ4`
- `パラメータ5`
- `ファイルからインポート`
- `ファイルへエクスポート`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `切替`
- `削除`
- `名前`
- `命令変更`
- `変更`
- `新規挿入`
- `書き込み`
- `説明`
- `読込バイト数`
- `読込数`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Script Editor`

### OPClassDemoFE7UForm
WF labels: **24** · AV labels: **13** · WF-only: **24** · AV-only: **13** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `/60 (秒)`
- `00`
- `??`
- `Commamd`
- `Size:`
- `アドレス`
- `アニメ指定
ポインタ書き込み`
- `アニメ指定のポインタ`
- `クラス`
- `パレットID`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメ`
- `敵味方カラー`
- `書き込み`
- `英語ポインタ`
- `表示地形右半分`
- `表示地形左半分`
- `説明文ID`
- `読込数`
- `選択アドレス:`
- `魔法エフェクト`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Ally/Enemy Color:`
- `Anime Pointer:`
- `Battle Anime:`
- `Class ID:`
- `Description Text ID:`
- `English Name Pointer:`
- `Japanese Name Length:`
- `Magic Effect:`
- `OP Class Demo (FE7U) Editor`
- `Terrain Left:`
- `Terrain Right:`
- `Write`

### SkillAssignmentClassCSkillSysForm
WF labels: **24** · AV labels: **5** · WF-only: **24** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `EnemyOnly(LV+64)`
- `Hard only (LV+128)`
- `Normal&&Hard (LV+96)`
- `PlayerOnly(LV+32)`
- `Size:`
- `このテーブルは、複数のクラスで参照されています。`
- `アドレス`
- `クラススキル`
- `スキル`
- `リストの拡張`
- `レベル`
- `一括インポート`
- `一括エクスポート`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `習得レベル`
- `習得レベルとスキルの詳細は、ここをクリックしてください。`
- `習得レベルの内訳`
- `読込数`
- `選択アドレス:`
- `選択クラスの分離独立`
- `領域が確保されていません。
「リストの拡張ボタン」を押して領域を確保してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class Skill:`
- `Skill Assignment - Class (CSkillSys)`
- `Skill system editors require a compatible skill patch to be installed.
Use the Patch Manager to install a skill system patch first.

Supported skill systems: CSkillSys, FE8N Skill System`
- `Write`

### SkillAssignmentClassSkillSystemForm
WF labels: **24** · AV labels: **5** · WF-only: **24** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `EnemyOnly(LV+64)`
- `Hard only (LV+128)`
- `Normal&&Hard (LV+96)`
- `PlayerOnly(LV+32)`
- `Size:`
- `このテーブルは、複数のクラスで参照されています。`
- `アドレス`
- `クラススキル`
- `スキル`
- `リストの拡張`
- `レベル`
- `一括インポート`
- `一括エクスポート`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `習得レベル`
- `習得レベルとスキルの詳細は、ここをクリックしてください。`
- `習得レベルの内訳`
- `読込数`
- `選択アドレス:`
- `選択クラスの分離独立`
- `領域が確保されていません。
「リストの拡張ボタン」を押して領域を確保してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Assigns a skill to each class via the SkillSystem patch.`
- `Class Skill:`
- `Skill Assignment (Class)`
- `Write`

### WorldMapPointForm
WF labels: **24** · AV labels: **21** · WF-only: **24** · AV-only: **21** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `いつでも入れるかどうか`
- `アドレス`
- `イベント分岐用フラグ`
- `クリア前アイコン`
- `クリア後アイコン`
- `フリーマップの種類`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `次の拠点ID(エイリーク)`
- `次の拠点ID(エイリーク2回目)`
- `次の拠点ID(エフラム)`
- `次の拠点ID(エフラム2回目)`
- `武器屋`
- `秘密の店`
- `章ID`
- `船の指定`
- `読込数`
- `道具屋`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Always Accessible:`
- `Armory Pointer:`
- `Chapter ID 1:`
- `Chapter ID 2:`
- `Coordinate X:`
- `Coordinate Y:`
- `Event Branch Flag:`
- `Free Map Type:`
- `Name Text ID:`
- `Next Node (Eirika 2nd):`
- `Next Node (Eirika):`
- `Next Node (Ephraim 2nd):`
- `Next Node (Ephraim):`
- `Post-Clear Icon:`
- `Pre-Clear Icon:`
- `Secret Shop Pointer:`
- `Ship Setting:`
- `Vendor Pointer:`
- `World Map Point Editor`
- `Write`

### EDFE7Form
WF labels: **23** · AV labels: **2** · WF-only: **23** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `0000`
- `Size:`
- `その後`
- `アドレス`
- `クリア後　その後　`
- `ユニット`
- `リストの拡張`
- `リン編`
- `リン編ユニット`
- `先頭アドレス`
- `内容`
- `再取得`
- `名前`
- `指定`
- `撤退`
- `撤退指定 02`
- `撤退時　その後　`
- `書き込み`
- `条件:`
- `登場ユニット`
- `読込数`
- `通り名`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `ED (FE7)`

### OPClassDemoFE8UForm
WF labels: **23** · AV labels: **13** · WF-only: **23** · AV-only: **13** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `/60 (秒)`
- `00`
- `??`
- `Commamd`
- `Size:`
- `アドレス`
- `アニメ指定
ポインタ書き込み`
- `アニメ指定のポインタ`
- `クラス`
- `パレットID`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメ`
- `敵味方カラー`
- `書き込み`
- `表示地形右半分`
- `表示地形左半分`
- `説明文ID`
- `読込数`
- `選択アドレス:`
- `魔法エフェクト`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Ally/Enemy Color:`
- `Anime Pointer:`
- `Anime Type:`
- `Battle Anime:`
- `Class ID:`
- `Description Text ID:`
- `Display Weapon:`
- `Magic Effect:`
- `OP Class Demo (FE8U) Editor`
- `Terrain Left:`
- `Terrain Right:`
- `Write`

### SupportUnitFE6Form
WF labels: **23** · AV labels: **38** · WF-only: **23** · AV-only: **38** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `初期値`
- `区切り00`
- `名前`
- `支援人数`
- `支援相手1`
- `支援相手10`
- `支援相手2`
- `支援相手3`
- `支援相手4`
- `支援相手5`
- `支援相手6`
- `支援相手7`
- `支援相手8`
- `支援相手9`
- `書き込み`
- `読込数`
- `進行度`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Growth Rate 1:`
- `Growth Rate 10:`
- `Growth Rate 2:`
- `Growth Rate 3:`
- `Growth Rate 4:`
- `Growth Rate 5:`
- `Growth Rate 6:`
- `Growth Rate 7:`
- `Growth Rate 8:`
- `Growth Rate 9:`
- `Growth Rates`
- `Initial Value 1:`
- `Initial Value 10:`
- `Initial Value 2:`
- `Initial Value 3:`
- `Initial Value 4:`
- `Initial Value 5:`
- `Initial Value 6:`
- `Initial Value 7:`
- `Initial Value 8:`
- `Initial Value 9:`
- `Initial Values`
- `Partner 1:`
- `Partner 10:`
- `Partner 2:`
- `Partner 3:`
- `Partner 4:`
- `Partner 5:`
- `Partner 6:`
- `Partner 7:`
- `Partner 8:`
- `Partner 9:`
- `Partner Count:`
- `Partner Count / Separator`
- `Separator:`
- `Support Partners`
- `Support Units (FE6)`
- `Write`

### MonsterProbabilityForm
WF labels: **22** · AV labels: **15** · WF-only: **22** · AV-only: **15** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `Size:`
- `アドレス`
- `コメント`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `出現魔物1`
- `出現魔物2`
- `出現魔物3`
- `出現魔物4`
- `出現魔物5`
- `名前`
- `座標設定の2バイトの一番左が5の時のクラスが確率テーブル`
- `書き込み`
- `読込数`
- `選択アドレス:`
- `魔物1出現確率`
- `魔物2出現確率`
- `魔物3出現確率`
- `魔物4出現確率`
- `魔物5出現確率`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class ID 1:`
- `Class ID 2:`
- `Class ID 3:`
- `Class ID 4:`
- `Class ID 5:`
- `Monster Probability Editor`
- `Probability 1:`
- `Probability 2:`
- `Probability 3:`
- `Probability 4:`
- `Probability 5:`
- `Unknown 1:`
- `Unknown 2:`
- `Write`

### SkillConfigSkillSystemForm
WF labels: **22** · AV labels: **5** · WF-only: **22** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アイコン`
- `アドレス`
- `アニメーション`
- `アニメーション取出`
- `アニメーション読込`
- `エディタ`
- `フレーム`
- `リストの拡張`
- `一括インポート`
- `一括エクスポート`
- `先頭アドレス`
- `再取得`
- `名前`
- `拡大`
- `書き込み`
- `画像取出`
- `画像読込`
- `表示例`
- `詳細`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Configures skill text details for the SkillSystem patch.`
- `Skill Config (SkillSystem)`
- `Text Detail:`
- `Write`

### SupportUnitForm
WF labels: **22** · AV labels: **30** · WF-only: **22** · AV-only: **30** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `初期値`
- `区切り00`
- `名前`
- `支援元`
- `支援相手`
- `支援相手1`
- `支援相手2`
- `支援相手3`
- `支援相手4`
- `支援相手5`
- `支援相手6`
- `支援相手7`
- `書き込み`
- `相手の初期値も自動調整する`
- `読込数`
- `進行度`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Growth Rate 1:`
- `Growth Rate 2:`
- `Growth Rate 3:`
- `Growth Rate 4:`
- `Growth Rate 5:`
- `Growth Rate 6:`
- `Growth Rate 7:`
- `Growth Rates`
- `Initial Value 1:`
- `Initial Value 2:`
- `Initial Value 3:`
- `Initial Value 4:`
- `Initial Value 5:`
- `Initial Value 6:`
- `Initial Value 7:`
- `Initial Values`
- `Partner 1:`
- `Partner 2:`
- `Partner 3:`
- `Partner 4:`
- `Partner 5:`
- `Partner 6:`
- `Partner 7:`
- `Partner Count:`
- `Partner Count / Separator`
- `Separator 1:`
- `Separator 2:`
- `Support Partners`
- `Support Unit Editor`
- `Write`

### ToolTranslateROMForm
WF labels: **22** · AV labels: **2** · WF-only: **22** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `..`
- `from:`
- `OneLiner`
- `to:`
- `すべてのテキストをテキストファイルに書きだします。`
- `フォント取込`
- `全テキストの書出し`
- `全テキストの読込`
- `利用フォント`
- `変更`
- `定型文ROM FROM`
- `定型文ROM TO`
- `定型文の翻訳(FROM ROMと TO ROMから、定型文を取得し、翻訳の参考にする)`
- `改造されたテキストのみ取得する`
- `日本語フォントの上書き`
- `現行ROMに足りないフォントを、以下のROMにあるフォントからコピーする`
- `簡易`
- `翻訳データ`
- `翻訳開始`
- `詳細`
- `足りないフォントの自動生成`
- `追加フォント ROM`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `ROM Translation Tool`

### ImagePortraitFE6Form
WF labels: **23** · AV labels: **14** · WF-only: **21** · AV-only: **12** · Common: **2**

WF-only labels (candidates for missing fields in AV):

- `00`
- `Size:`
- `アドレス`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレット`
- `フレーム`
- `マップ顔`
- `ユニット顔`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `口座標`
- `名前`
- `書き込み`
- `画像取出`
- `画像読込`
- `表示例`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Export PAL`
- `Export PNG`
- `Import PAL`
- `Import PNG`
- `Map Face:`
- `Mouth Coord:`
- `Palette:`
- `Portrait Editor (FE6)`
- `Unit Face:`
- `Unused (B14):`
- `Unused (B15):`

### MapExitPointForm
WF labels: **21** · AV labels: **4** · WF-only: **21** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `Size:`
- `X:`
- `Y:`
- `これは敵のエスケープポイントです。
NPC用は、左上のコンボボックスを切り替えてください。`
- `アドレス`
- `マップ名`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `新規領域の確保`
- `書き込み`
- `条件:`
- `消滅方法`
- `読込数`
- `選択アドレス:`
- `離脱ポインタ`
- `離脱ポインタ書き込み`
- `離脱ポイント再取得`
- `離脱座標`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Exit Pointer:`
- `Map Exit Point Editor`
- `Write`

### MenuCommandForm
WF labels: **21** · AV labels: **13** · WF-only: **21** · AV-only: **13** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `MenuCommandID`
- `Size:`
- `アドレス`
- `カーソルで選択されたときの動作ポインタ`
- `キャンセルされたときの動作ポインタ`
- `ヘルプID`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `可否診断ルーチンポインタ`
- `名前`
- `名前(日本語の場合、未使用)`
- `描画ルーチンポインタ`
- `日本語の名前ポインタ`
- `書き込み`
- `色`
- `読込数`
- `選択アドレス:`
- `選択時に実行する効果ポインタ`
- `選択時に毎ターン呼び出されるポインタ`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Cancel Action:`
- `Color/ID (u32@8):`
- `Cursor Select Action:`
- `Draw Routine:`
- `Effect Routine:`
- `Help Text ID:`
- `Japanese Name Pointer:`
- `Menu Command Editor`
- `Name Text ID:`
- `Per-Turn Callback:`
- `Usability Routine:`
- `Write`

### PointerToolForm
WF labels: **21** · AV labels: **20** · WF-only: **21** · AV-only: **20** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `LoadROMNAME`
- `この場所への最初の参照`
- `アドレス`
- `アドレスがポインタの場合の
指されるデータ位置`
- `アドレスの種類判定`
- `アドレス値の参照先の
データがある場所`
- `スライドして追加検索`
- `バッチ (一括処理)`
- `ポインタ化`
- `リトルエンディアン`
- `上記データの参照場所`
- `内容`
- `別ROM読込`
- `参照値から追跡
マッチアドレス`
- `探索にASMMapを利用する`
- `比較サイズ`
- `比較方法`
- `自動追跡システム`
- `警告:0地帯です`
- `警告:元データと離れすぎ`
- `警告システム`

AV-only labels (usually fine — layout polish or rewording):

- `Address`
- `Auto Tracking`
- `Batch`
- `Close`
- `Compare current ROM against another version to locate
specific data such as images or programs that are
shared between FE7 and FE8.`
- `Comparison Size`
- `Content Type`
- `Data Address
(if pointer)`
- `e.g. 0x08000000`
- `First Reference`
- `Little Endian`
- `Match Method`
- `Other ROM
Data Address`
- `Other ROM Ref`
- `Pointer`
- `Search Options`
- `Slide Search`
- `Use ASM Map for search`
- `Warning Level`
- `What Is`

### EDForm
WF labels: **20** · AV labels: **8** · WF-only: **20** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `0000`
- `Size:`
- `その後`
- `アドレス`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `内容`
- `再取得`
- `名前`
- `指定`
- `撤退`
- `撤退指定 02`
- `書き込み`
- `条件:`
- `登場ユニット`
- `読込数`
- `通り名`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Condition:`
- `Condition: 00=Died, 01=Wounded/Left, 02=Wounded/Stayed`
- `Ending Event Editor`
- `Unit ID:`
- `Unknown (0x02):`
- `Unknown (0x03):`
- `Write`

### EventMapChangeForm
WF labels: **20** · AV labels: **2** · WF-only: **20** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `H:`
- `size:`
- `W:`
- `X:`
- `Y:`
- `アドレス`
- `サイズ`
- `マップ名前`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `変化データ
ポインタ先へのインポート`
- `変化データポインタ`
- `座標`
- `書き込み`
- `番号`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Map Change Event Editor`

### ImageBGForm
WF labels: **20** · AV labels: **6** · WF-only: **20** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `グラフィックツール`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレット`
- `ヘッダ付きTSA`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `参照箇所`
- `名前`
- `書き込み`
- `減色ツール`
- `画像`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Background Image Editor`
- `Export PAL`
- `Export PNG`
- `Import PAL`
- `Import PNG`

### ImageBattleBGForm
WF labels: **20** · AV labels: **9** · WF-only: **20** · AV-only: **9** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `TSA`
- `アドレス`
- `グラフィックツール`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `参照箇所`
- `名前`
- `書き込み`
- `減色ツール`
- `画像`
- `画像取出し`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Battle Background Editor`
- `Export PAL`
- `Image (D0):`
- `Import PAL`
- `Import PNG`
- `Palette (D8):`
- `TSA (D4):`
- `Write`

### ImageMapActionAnimationForm
WF labels: **20** · AV labels: **7** · WF-only: **20** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `ID=00 Emptyはnullデータとして予約されています。
0x0以外の値を設定しないでください。`
- `Size:`
- `アドレス`
- `アニメーション`
- `アニメーション取出`
- `アニメーション読込`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `フレーム`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `拡大`
- `書き込み`
- `表示例`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Animation Ptr (D0):`
- `Map Action Animation`
- `Padding 1 (W4):`
- `Padding 2 (W6):`
- `Preview (Frame 0):`
- `Write`

### MapTileAnimation2Form
WF labels: **20** · AV labels: **8** · WF-only: **20** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `B`
- `G`
- `GBAカラー`
- `R`
- `Size:`
- `アドレス`
- `アニメーション間隔`
- `データ個数`
- `リストの拡張`
- `一括インポート`
- `一括エクスポート`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き換えるパレットデータ`
- `書き換え始めパレット番号`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Animation Interval:`
- `Data Count:`
- `Map Tile Animation Type 2 (Palette)`
- `Palette Data Pointer:`
- `Start Palette Index:`
- `Unknown (0x07):`
- `Write`

### SkillConfigFE8UCSkillSys09xForm
WF labels: **20** · AV labels: **6** · WF-only: **20** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アイコン`
- `アドレス`
- `アニメーション`
- `アニメーション取出`
- `アニメーション読込`
- `エディタ`
- `スキル名`
- `フレーム`
- `先頭アドレス`
- `再取得`
- `名前`
- `拡大`
- `書き込み`
- `画像取出`
- `画像読込`
- `表示例`
- `詳細`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Description:`
- `Skill Configuration (CSkillSys 0.9.x)`
- `Skill Name:`
- `Skill system editors require a compatible skill patch to be installed.
Use the Patch Manager to install a skill system patch first.

Supported skill systems: CSkillSys, FE8N Skill System`
- `Write`

### SongTableForm
WF labels: **20** · AV labels: **8** · WF-only: **20** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Priority(PlayerType)`
- `Size:`
- `♪`
- `アドレス`
- `コメント`
- `サウンドルームに曲を登録すると、曲名をつけられます。
FEには、曲と効果音の違いはありません。
`
- `サウンドルームへ`
- `ソングヘッダー`
- `ソングヘッダーへ`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `参照箇所`
- `名前`
- `曲の楽譜を表示します。
ここからは、曲のインポート、エクスポートを行います。`
- `曲を構成する楽器テーブルを表示します。
通常は気にする必要はありません。開発者向けの機能です。`
- `書き込み`
- `楽器テーブルへ`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Header Priority:`
- `Header Reverb:`
- `Priority (PlayerType):`
- `Song Header Pointer:`
- `Song Table Editor`
- `Track Count:`
- `Write`

### ErrorPaletteTransparentForm
WF labels: **19** · AV labels: **2** · WF-only: **19** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Info`
- `No.1`
- `No.10`
- `No.11`
- `No.12`
- `No.13`
- `No.14`
- `No.15`
- `No.16`
- `No.2`
- `No.3`
- `No.4`
- `No.5`
- `No.6`
- `No.7`
- `No.8`
- `No.9`
- `パレットの透過色はどれですか？`
- `通常、1番最初のパレットを背景の透過色にするべきですが、この画像はそうなっていないように思われます。
背景の透過色はどれですか？`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `Palette Transparency Error`

### ImageCGFE7UForm
WF labels: **20** · AV labels: **11** · WF-only: **19** · AV-only: **10** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `00`
- `10分割画像`
- `??`
- `Size:`
- `アドレス`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `減色ツール`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `10-Split Image:`
- `Address:`
- `CG Editor (FE7U)`
- `Export PAL`
- `Export PNG`
- `Image Type:`
- `Import PAL`
- `Import PNG`
- `Palette:`
- `Reserved (B1-B3):`

### ImageItemIconForm
WF labels: **19** · AV labels: **7** · WF-only: **19** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `インターネットから新しいリソースを探す`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレットの変更`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `原寸大`
- `参照アイテム`
- `名前`
- `拡大`
- `書き込み`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Export PAL`
- `Export PNG`
- `Image Pointer:`
- `Import PNG`
- `Item/Weapon Icon Viewer`
- `Palette Pointer:`

### MapChangeForm
WF labels: **21** · AV labels: **15** · WF-only: **19** · AV-only: **13** · Common: **2**

WF-only labels (candidates for missing fields in AV):

- `??`
- `H:`
- `Size:`
- `W:`
- `アドレス`
- `コメント`
- `サイズ`
- `マップエディタ Jump`
- `マップ名`
- `マップ変化の再取得`
- `マップ変更ポインタ`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `座標`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Change ID:`
- `Change Pointer:`
- `Change Records`
- `Height:`
- `Map Change Editor`
- `Map Pointer`
- `Record Address:`
- `Selected Record`
- `Tile Data Ptr:`
- `Width:`
- `Write Pointer`
- `Write Record`

### MenuDefinitionForm
WF labels: **20** · AV labels: **15** · WF-only: **19** · AV-only: **14** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Height(0でいい)`
- `idk`
- `On B Press`
- `On HelpBox`
- `On R Press`
- `OnEnd`
- `OnInit`
- `Size:`
- `X`
- `Y`
- `アドレス`
- `メニューコマンド`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Height:`
- `Menu Command Ptr:`
- `Menu Definition Editor`
- `On B Press Routine:`
- `On HelpBox Routine:`
- `On R Press Routine:`
- `OnEnd Routine:`
- `OnInit Routine:`
- `Style Data:`
- `Unknown Routine:`
- `Write`
- `X Position:`
- `Y Position:`

### SongTrackImportMidiForm
WF labels: **19** · AV labels: **8** · WF-only: **19** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `BEND,BENDR命令を無視する`
- `FEBuilderGBAでインポート`
- `LFOS,LFODL命令を無視する`
- `mid2agbでインポート`
- `midfix4agbのPATHが設定されていません。`
- `midfix4agbを利用する`
- `midi2agbのmodscを有効にする`
- `MOD,MODT命令を無視する`
- `それでも改善しない場合は、次のオプションも利用できます。`
- `インポートする`
- `オプション`
- `マスターボリューム`
- `古いオプション`
- `楽器セット`
- `楽譜の前方の無音区間を無視する`
- `楽譜の後方の無音区間を無視する`
- `無音区間を除去します。`
- `選択する`
- `音が「みょーん」となる場合は、有効にしてみてください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Browse MIDI File...`
- `Import to ROM (Experimental)`
- `MIDI File Metadata`
- `MIDI Import`
- `MIDI write-back to ROM is not yet fully implemented. You can preview MIDI file metadata above, but importing MIDI data into the ROM may produce unexpected results.`
- `No file selected`
- `Note`

### AITargetForm
WF labels: **18** · AV labels: **23** · WF-only: **18** · AV-only: **23** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `それぞれのAIが何を優先するか指定します。詳細はこちらをクリック。`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `包囲警戒度`
- `反撃ダメージ警戒度`
- `名前`
- `敵との距離優先度`
- `敵のクラス優先度`
- `敵の残りHP優先度`
- `書き込み`
- `現在ターン優先度`
- `自分の残りHP警戒度`
- `致死ダメージ＆最終ダメージ優先度`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Targeting`
- `Counter Damage Warning:`
- `Current Turn Priority:`
- `Enemy Class Priority:`
- `Enemy Distance Priority:`
- `Enemy Remaining HP Priority:`
- `Lethal Damage Priority:`
- `Self Remaining HP Warning:`
- `Surround Warning:`
- `Unknown 10:`
- `Unknown 11:`
- `Unknown 12:`
- `Unknown 13:`
- `Unknown 14:`
- `Unknown 15:`
- `Unknown 16:`
- `Unknown 17:`
- `Unknown 18:`
- `Unknown 19:`
- `Unknown 8:`
- `Unknown 9:`
- `Write`

### DumpStructSelectDialogForm
WF labels: **18** · AV labels: **2** · WF-only: **18** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1234`
- `C言語の構造体の表示`
- `NightmareModule nmmファイルの作成`
- `no$gbaの読込ブレークポイントとしてコピー`
- `インポートする`
- `クリップボード`
- `クリップボードへコピー`
- `ダンプしていたデータのインポート`
- `データダンプ`
- `バイナリエディタ`
- `ポインタとしてクリップボードへコピー`
- `リトルエンディアンポインタとしてクリップボードへコピー`
- `値:`
- `構造体`
- `表示しているリストにあるすべてのデータのCSV形式を取得`
- `表示しているリストにあるすべてのデータのEA形式を取得`
- `表示しているリストにあるすべてのデータのTSV形式を取得`
- `選択しているアドレスをバイナリエディタで表示`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Struct Dump Selector`

### EventBattleTalkFE7Form
WF labels: **18** · AV labels: **2** · WF-only: **18** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `イベント`
- `テキスト`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `反撃側ユニット`
- `名前`
- `攻撃側ユニット`
- `新規イベント`
- `書き込み`
- `章ID`
- `読込数`
- `達成フラグ`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Battle Dialogue (FE7)`

### ImageCGForm
WF labels: **18** · AV labels: **6** · WF-only: **18** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `10分割画像`
- `Size:`
- `TSA`
- `アドレス`
- `コメント`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `減色ツール`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `CG Image Editor`
- `Export PAL`
- `Export PNG`
- `Import PAL`
- `Import PNG`

### ImageChapterTitleForm
WF labels: **18** · AV labels: **9** · WF-only: **18** · AV-only: **9** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `セーブ画像`
- `セーブ画像取出し`
- `セーブ画像読込`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `章タイトル`
- `章タイトル取出し`
- `章タイトル読込`
- `章画像`
- `章画像取出し`
- `章画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Chapter Image Ptr:`
- `Chapter Title Editor`
- `Export PAL`
- `Export PNG`
- `Import PNG`
- `Save Image Ptr:`
- `Title Image Ptr:`
- `Write`

### ItemStatBonusesForm
WF labels: **19** · AV labels: **15** · WF-only: **18** · AV-only: **14** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アイテムを追加したい場合、アイテムエディタ画面にある、「能力補正」の項目から追加してください。`
- `アドレス`
- `体格`
- `先頭アドレス`
- `再取得`
- `名前`
- `守備`
- `幸運`
- ` 技 `
- `攻撃`
- `書き込み`
- `移動`
- `該当アイテム`
- `速さ`
- `選択アドレス:`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Con:`
- `Defense:`
- `Item Stat Bonuses Editor`
- `Luck:`
- `Move:`
- `Resistance:`
- `Skill:`
- `Speed:`
- `Str/Mag:`
- `Unknown (byte 10):`
- `Unknown (byte 11):`
- `Unknown (byte 9):`
- `Write`

### PaletteChangeColorsForm
WF labels: **21** · AV labels: **9** · WF-only: **18** · AV-only: **6** · Common: **3**

WF-only labels (candidates for missing fields in AV):

- `No.1`
- `No.10`
- `No.11`
- `No.12`
- `No.13`
- `No.14`
- `No.15`
- `No.16`
- `No.2`
- `No.3`
- `No.4`
- `No.5`
- `No.6`
- `No.7`
- `No.8`
- `No.9`
- `リセット`
- `変更`

AV-only labels (usually fine — layout polish or rewording):

- `[Palette color grid - 16 color slots]`
- `Apply`
- `Close`
- `Color Grid (16 colors):`
- `Palette Change Colors`
- `Palette Color Editor allows editing individual colors in a 16-color GBA palette.
Select a palette slot to modify its RGB values.`

### PaletteSwapForm
WF labels: **18** · AV labels: **6** · WF-only: **18** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Info`
- `No.1`
- `No.10`
- `No.11`
- `No.12`
- `No.13`
- `No.14`
- `No.15`
- `No.16`
- `No.2`
- `No.3`
- `No.4`
- `No.5`
- `No.6`
- `No.7`
- `No.8`
- `No.9`
- `場所を入れ替える色を選択してください`

AV-only labels (usually fine — layout polish or rewording):

- `Cancel`
- `Destination Palette:`
- `Palette Swap`
- `Palette Swap exchanges palette assignments between entries.
Select source and destination palette slots to exchange their color data.`
- `Source Palette:`
- `Swap`

### SupportAttributeForm
WF labels: **18** · AV labels: **11** · WF-only: **18** · AV-only: **11** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `コメント`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `命中`
- `回避`
- `属性:-`
- `必殺`
- `必殺回避`
- `攻撃`
- `書き込み`
- `読込数`
- `選択アドレス:`
- `防御`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Affinity Type:`
- `Attack Bonus:`
- `Avoid Bonus:`
- `Crit Avoid Bonus:`
- `Crit Bonus:`
- `Defense Bonus:`
- `Hit Bonus:`
- `Support Attribute Editor`
- `Unknown 7:`
- `Write`

### EventBattleTalkForm
WF labels: **17** · AV labels: **2** · WF-only: **17** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `イベント`
- `テキスト`
- `マップ`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `反撃側ユニット`
- `名前`
- `攻撃側ユニット`
- `新規イベント`
- `書き込み`
- `読込数`
- `達成フラグ`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Battle Dialogue Editor`

### EventHaikuForm
WF labels: **17** · AV labels: **2** · WF-only: **17** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `イベント`
- `テキスト`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `新規イベント`
- `書き込み`
- `章ID`
- `編`
- `読込数`
- `達成フラグ`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Haiku Event Editor`

### FE8SpellMenuExtendsForm
WF labels: **17** · AV labels: **2** · WF-only: **17** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `このテーブルは、複数のクラスで参照されています。`
- `アドレス`
- `リストの拡張`
- `レベル`
- `レベルアップで取得する魔法の先頭アドレス`
- `上級職のみ`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `習得レベル`
- `読込数`
- `選択アドレス:`
- `選択クラスの分離独立`
- `領域が確保されていません。
「リストの拡張ボタン」を押して領域を確保してください。`
- `魔法`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Spell Menu Extensions`

### GraphicsToolForm
WF labels: **17** · AV labels: **12** · WF-only: **17** · AV-only: **12** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Data Dump`
- `No`
- `PageDown`
- `PageUp`
- `PatchMaker`
- `TSA`
- `TSA Editor(非推奨)`
- `パレット`
- `パレットエディタ`
- `パレット番号`
- `幅/8`
- `画像が利用している16色パレットの個数`
- `画像アドレス`
- `画像取出`
- `画像読込`
- `第2画像`
- `高さ/8`

AV-only labels (usually fine — layout polish or rewording):

- `0x08000000`
- `4bpp`
- `Close`
- `Draw`
- `Graphics Tool`
- `Image Address:`
- `LZ77 Compressed`
- `Palette Address:`
- `Tiles X:`
- `Tiles Y:`
- `View tile graphics from ROM. Enter addresses and click Draw.`
- `Zoom:`

### ImagePortraitImporterForm
WF labels: **17** · AV labels: **2** · WF-only: **17** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `BlockX`
- `BlockY`
- `H`
- `Preview`
- `STATUS`
- `W`
- `X`
- `Y`
- `この画像は16色を超えているので、減色処理が必要です`
- `インポートする`
- `フレーム:`
- `口の位置`
- `目の位置`
- `簡易`
- `縁を黒どりする`
- `自動的に減色してインポートする`
- `詳細な減色`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Portrait Import Wizard`

### ItemStatBonusesSkillSystemsForm
WF labels: **19** · AV labels: **17** · WF-only: **17** · AV-only: **15** · Common: **2**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アイテムを追加したい場合、アイテムエディタ画面にある、「能力補正」の項目から追加してください。`
- `アドレス`
- `体格`
- `先頭アドレス`
- `再取得`
- `名前`
- `守備`
- `幸運`
- `技`
- `攻撃`
- `書き込み`
- `移動`
- `該当アイテム`
- `速さ`
- `選択アドレス:`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Con`
- `Defense`
- `Growth Rate Bonuses`
- `Luck`
- `Magic/??`
- `Move`
- `Padding`
- `Resistance`
- `Skill`
- `Speed`
- `Stat Bonuses`
- `Stat Bonuses (Skill Systems)`
- `Str/Mag`
- `Write`

### ItemStatBonusesVennoForm
WF labels: **18** · AV labels: **14** · WF-only: **17** · AV-only: **13** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アイテムを追加したい場合、アイテムエディタ画面にある、「能力補正」の項目から追加してください。`
- `アドレス`
- `体格`
- `先頭アドレス`
- `再取得`
- `名前`
- `守備`
- `幸運`
- `技`
- `攻撃`
- `書き込み`
- `移動`
- `該当アイテム`
- `速さ`
- `選択アドレス:`
- `魔防`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Con`
- `Defense`
- `Growth Rate Bonuses`
- `Luck`
- `Move`
- `Resistance`
- `Skill`
- `Speed`
- `Stat Bonuses`
- `Stat Bonuses (Venno)`
- `Str/Mag`
- `Write`

### ItemWeaponEffectForm
WF labels: **17** · AV labels: **14** · WF-only: **17** · AV-only: **14** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `1か2`
- `??`
- `Size:`
- `アイテムID`
- `アドレス`
- `エフェクトID`
- `ダメージエフェクト`
- `マップ使用時エフェクト`
- `モーション`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `被弾色`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Anim Type:`
- `Damage Effect:`
- `Effect ID:`
- `Hit Color:`
- `Item ID:`
- `Item Weapon Effect Editor`
- `Map Effect Pointer:`
- `Motion:`
- `Unknown (byte 1):`
- `Unknown (byte 15):`
- `Unknown (byte 3):`
- `Unknown (bytes 6-7):`
- `Write`

### MapTileAnimation1Form
WF labels: **17** · AV labels: **6** · WF-only: **17** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `アニメーション間隔`
- `データ個数`
- `パレット`
- `リストの拡張`
- `一括インポート`
- `一括エクスポート`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き換えるマップチップ`
- `書き込み`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Animation Interval:`
- `Data Count:`
- `Map Tile Animation Type 1`
- `Map Tile Data Pointer:`
- `Write`

### SkillAssignmentUnitCSkillSysForm
WF labels: **17** · AV labels: **5** · WF-only: **17** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `このテーブルは、複数のクラスで参照されています。`
- `アドレス`
- `スキル`
- `リストの拡張`
- `一括インポート`
- `一括エクスポート`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `習得レベル`
- `習得レベルとスキルの詳細は、ここをクリックしてください。`
- `読込数`
- `選択アドレス:`
- `選択クラスの分離独立`
- `領域が確保されていません。
「リストの拡張ボタン」を押して領域を確保してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Skill Assignment - Unit (CSkillSys)`
- `Skill system editors require a compatible skill patch to be installed.
Use the Patch Manager to install a skill system patch first.

Supported skill systems: CSkillSys, FE8N Skill System`
- `Unit Skill:`
- `Write`

### SkillAssignmentUnitSkillSystemForm
WF labels: **17** · AV labels: **5** · WF-only: **17** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `このテーブルは、複数のクラスで参照されています。`
- `アドレス`
- `スキル`
- `リストの拡張`
- `一括インポート`
- `一括エクスポート`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `習得レベル`
- `習得レベルとスキルの詳細は、ここをクリックしてください。`
- `読込数`
- `選択アドレス:`
- `選択クラスの分離独立`
- `領域が確保されていません。
「リストの拡張ボタン」を押して領域を確保してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Assigns a skill to each unit via the SkillSystem patch.`
- `Skill Assignment (Unit)`
- `Unit Skill:`
- `Write`

### TextCharCodeForm
WF labels: **17** · AV labels: **8** · WF-only: **17** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ASCII`
- `FFFF`
- `Size:`
- `アイテムフォント`
- `アドレス`
- `セリフフォント`
- `使用回数検索`
- `先頭アドレス`
- `再取得`
- `名前`
- `回以下しか出現しない文字取得`
- `文字文字`
- `文字検索`
- `書き込み`
- `番号 文字 回数　登場会話ID`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Char Code (ASCII):`
- `Character Code Table`
- `Character Display:`
- `Close`
- `Item Font`
- `Lists all character codes in the ROM's text encoding table.`
- `Serif Font`
- `Terminator (FFFF):`

### ToolProblemReportForm
WF labels: **17** · AV labels: **2** · WF-only: **17** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `BeginPage`
- `DiscordコミニティURL`
- `EndPage`
- `Step1Page`
- `Step2Page`
- `この機能の説明`
- `どの章？`
- `ファイル選択`
- `作成`
- `始める`
- `完了`
- `完了ボタンでDiscordコミニティURLを開く`
- `戻る`
- `次へ`
- `添付データ`
- `無改造ROM`
- `誰？`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Problem Reporter`

### UnitCustomBattleAnimeForm
WF labels: **17** · AV labels: **6** · WF-only: **17** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `このテーブルは、複数のクラスで参照されています。`
- `アドレス`
- `アニメ内容の先頭アドレス`
- `アニメ番号`
- `アニメ設定`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `専用アニメポインタの書き込み`
- `書き込み`
- `武器種類`
- `特殊`
- `読込数`
- `選択アドレス:`
- `選択クラスの分離独立`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Animation Number:`
- `Custom Battle Animation`
- `Special:`
- `Weapon Type:`
- `Write`

### WorldMapEventPointerForm
WF labels: **17** · AV labels: **4** · WF-only: **17** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `エイリークエンディング`
- `エフラムエンディング`
- `オープニングイベント`
- `リストの拡張`
- `ワールドマップ拠点へJump`
- `ワールドマップ道へJump`
- `先頭アドレス`
- `再取得`
- `前の拠点クリア後に発生するイベント`
- `名前`
- `拠点選択後に発生するイベント`
- `新規イベント`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Pointer (u32@0):`
- `World Map Event Editor`
- `Write`

### EventHaikuFE7Form
WF labels: **16** · AV labels: **2** · WF-only: **16** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `イベント`
- `テキスト`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `新規イベント`
- `書き込み`
- `章ID`
- `読込数`
- `達成フラグ`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Haiku (FE7)`

### ItemUsagePointerForm
WF labels: **16** · AV labels: **5** · WF-only: **16** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ASMポインタ`
- `CCアイテム`
- `IERがインストールされているため、パッチ画面から設定してください。`
- `Size:`
- `このバージョンのFEでは利用しません。`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `条件:`
- `能力補正`
- `読込数`
- `選択アドレス:`
- `関連項目`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Function pointers for item usability checks`
- `Item Usage Pointer Editor`
- `Usability Pointer:`
- `Write`

### MainSimpleMenuImageSubForm
WF labels: **16** · AV labels: **2** · WF-only: **16** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `CG`
- `WMAP画像`
- `アイテムアイコン`
- `キャラパレット`
- `システムアイコン`
- `フォント`
- `待機アイコン`
- `戦闘アニメ`
- `戦闘地形`
- `戦闘画面`
- `戦闘背景`
- `移動アイコン`
- `章タイトル`
- `背景`
- `追加魔法`
- `顔画像`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Image Sub-Menu`

### MapEditorForm
WF labels: **16** · AV labels: **10** · WF-only: **16** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Address`
- `Redo`
- `UNDO`
- `サイズ変更`
- `スタイル編集`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `タイルの拡大`
- `ファイルから読込`
- `ファイルに保存`
- `マップサイズ`
- `マップスタイル`
- `マップ変化追加`
- `拡大`
- `書き込み`
- `編集マップ変更`

AV-only labels (usually fine — layout polish or rewording):

- `+`
- `-`
- `1x`
- `No tile selected`
- `Refresh Map`
- `Tile Editor (click on map to select)`
- `Tile ID (hex):`
- `Visual Map Editor`
- `Write Tile`
- `Zoom:`

### StatusRMenuForm
WF labels: **16** · AV labels: **12** · WF-only: **16** · AV-only: **12** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Getter`
- `Loop`
- `Size:`
- `TID`
- `X`
- `Y`
- `アドレス`
- `上 RMenuPointer`
- `下 RMenuPointer`
- `先頭アドレス`
- `再取得`
- `右 RMenuPointer`
- `名前`
- `左 RMenuPointer`
- `書き込み`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Down RMenuPointer:`
- `Getter Routine:`
- `Left RMenuPointer:`
- `Loop Routine:`
- `Right RMenuPointer:`
- `Status R-Menu Editor`
- `Text ID (TID):`
- `Up RMenuPointer:`
- `Write`
- `X Position:`
- `Y Position:`

### SupportTalkFE7Form
WF labels: **16** · AV labels: **13** · WF-only: **16** · AV-only: **13** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `A会話`
- `B会話`
- `C会話`
- `Size:`
- `♪`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `支援相手1`
- `支援相手2`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `A Support Song:`
- `A Support Text:`
- `Address:`
- `B Support Song:`
- `B Support Text:`
- `C Support Song:`
- `C Support Text:`
- `Jump`
- `Padding:`
- `Support Partner 1:`
- `Support Partner 2:`
- `Support Talk (FE7)`
- `Write`

### SupportTalkForm
WF labels: **16** · AV labels: **12** · WF-only: **16** · AV-only: **12** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `A会話`
- `B会話`
- `C会話`
- `Size:`
- `♪`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `支援相手1`
- `支援相手2`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `A Support Song:`
- `A Support Text:`
- `Address:`
- `B Support Song:`
- `B Support Text:`
- `C Support Song:`
- `C Support Text:`
- `Jump`
- `Support Partner 1:`
- `Support Partner 2:`
- `Support Talk`
- `Write`

### TextDicForm
WF labels: **16** · AV labels: **14** · WF-only: **16** · AV-only: **14** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `タイトル`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `既読フラグ`
- `書き込み`
- `章タイトル`
- `表示フラグ`
- `詳細`
- `読込数`
- `選択アドレス:`
- `項目名`

AV-only labels (usually fine — layout polish or rewording):

- `12-byte records: title index, chapter index, text IDs, flags, unit/class.`
- `Chapter Index:`
- `Class:`
- `Class ID:`
- `Flag 1:`
- `Flag 2:`
- `Preview:`
- `Text Dictionary`
- `Text ID 1:`
- `Text ID 2:`
- `Title Index:`
- `Unit:`
- `Unit ID:`
- `Write`

### EventHaikuFE6Form
WF labels: **15** · AV labels: **2** · WF-only: **15** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `死亡時`
- `章ID`
- `終章`
- `読込数`
- `達成フラグ`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Haiku (FE6)`

### ImageTSAAnimeForm
WF labels: **15** · AV labels: **6** · WF-only: **15** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `グラフィックツール`
- `パレット`
- `ヘッダ付きTSA`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `減色ツール`
- `画像`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Export PAL`
- `Export PNG`
- `Import PAL`
- `Import PNG`
- `TSA Animation Editor`

### ItemEffectivenessForm
WF labels: **15** · AV labels: **5** · WF-only: **15** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `このデータは、複数のアイテムで参照されています。`
- `アドレス`
- `クラス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `特効クラス`
- `特効ポインタアドレス`
- `特効再取得`
- `該当アイテム`
- `選択アイテムの分離独立`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class ID:`
- `Item Effectiveness Editor`
- `Weapon effectiveness 2x/3x class list (FE8 only)`
- `Write`

### ItemEffectivenessSkillSystemsReworkForm
WF labels: **15** · AV labels: **2** · WF-only: **15** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `ClassType`
- `coefficient_times*2`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `特効クラス`
- `特効ポインタアドレス`
- `特効再取得`
- `該当アイテム`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Effectiveness (Skill Systems Rework)`

### MonsterWMapProbabilityForm
WF labels: **15** · AV labels: **4** · WF-only: **15** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `それぞれのルートでクリアする章を指定します。
これは、まだクリアしていない拠点には魔物を出せないためです。`
- `アドレス`
- `フリーマップ終了イベント`
- `フリーマップ開始イベント`
- `先頭アドレス`
- `再取得`
- `名前`
- `拠点ID`
- `拠点ごとの魔物の発生確率を定義します。`
- `書き込み`
- `章ID`
- `読込数`
- `選択アドレス:`
- `魔物が出現する拠点IDを指定します。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Base Point ID:`
- `World Map Monster Editor`
- `Write`

### SupportTalkFE6Form
WF labels: **15** · AV labels: **11** · WF-only: **15** · AV-only: **11** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `A会話`
- `B会話`
- `C会話`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `支援相手1`
- `支援相手2`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `A Support Text:`
- `Address:`
- `B Support Text:`
- `C Support Text:`
- `Jump`
- `Padding 1:`
- `Padding 2:`
- `Support Partner 1:`
- `Support Partner 2:`
- `Support Talk (FE6)`
- `Write`

### UnitPaletteForm
WF labels: **15** · AV labels: **10** · WF-only: **15** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `上級クラス1`
- `上級クラス2`
- `上級クラス3`
- `上級クラス4`
- `先頭アドレス`
- `再取得`
- `名前`
- `基本クラス1`
- `基本クラス2(見習いキャラのみ)`
- `書き込み`
- `見習いクラス(見習いキャラのみ)`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Advanced Class 1:`
- `Advanced Class 2:`
- `Advanced Class 3:`
- `Advanced Class 4:`
- `Base Class 1:`
- `Base Class 2 (trainee only):`
- `Trainee Class (trainee only):`
- `Unit Palette Assignment`
- `Write`

### EventBattleTalkFE6Form
WF labels: **14** · AV labels: **2** · WF-only: **14** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `テキスト`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `章ID`
- `読込数`
- `達成フラグ`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Battle Dialogue (FE6)`

### EventForceSortieFE7Form
WF labels: **14** · AV labels: **8** · WF-only: **14** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `マップ名`
- `ユニット`
- `先頭アドレス`
- `再取得`
- `名前`
- `強制出撃ポインタ`
- `強制出撃ポインタ書き込み`
- `強制出撃ポイント再取得`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Force Sortie (FE7)`
- `Unit ID:`
- `Unit List Pointer:`
- `Unknown 1:`
- `Unknown 2:`
- `Unknown 3:`
- `Write`

### FontForm
WF labels: **14** · AV labels: **2** · WF-only: **14** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `アドレス`
- `サンプル`
- `フォントの種類`
- `フォント幅`
- `一括インポート`
- `一括エクスポート`
- `利用フォント`
- `変更`
- `書き込み`
- `検索`
- `検索文字`
- `画像取出`
- `画像読込`
- `自動生成`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Font Editor`

### OPPrologueForm
WF labels: **14** · AV labels: **10** · WF-only: **14** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `TSA`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `画像`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Export PAL`
- `Export PNG`
- `Image Pointer:`
- `Import PAL`
- `Import PNG`
- `OP Prologue Editor`
- `Palette Address:`
- `TSA Pointer:`
- `Write`

### SongTrackImportWaveForm
WF labels: **14** · AV labels: **2** · WF-only: **14** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `DPCM lookahead`
- `DPCM圧縮`
- `m4a_hq_mixer Patchがインストールされていないので、DPCM圧縮は利用できません。`
- `Preview`
- `Waveファイルは効果音に使うことを想定しています。
それを、音楽に利用すると、大量に容量を消費しますが、インポートしてもよろしいですか？`
- `インポートする`
- `キャンセル`
- `チャンネル`
- `前後の無音除去`
- `格納方法と最適化`
- `楽譜`
- `楽譜のループ`
- `音質を下げる`
- `音量`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Wave Track Import`

### SoundRoomForm
WF labels: **14** · AV labels: **8** · WF-only: **14** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `BGMID`
- `Size:`
- `♪`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `曲の長さ`
- `曲名`
- `書き込み`
- `表示条件ASM`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Display Cond ASM:`
- `Jump`
- `Song ID:`
- `Song Length:`
- `Sound Room Editor`
- `Text ID:`
- `Write`

### StatusParamForm
WF labels: **15** · AV labels: **11** · WF-only: **14** · AV-only: **10** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `00`
- `?? Bitmap`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `字下げ`
- `文字列ポインタ`
- `書き込み`
- `色`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `B10:`
- `B11:`
- `Bitmap:`
- `Color:`
- `Indent:`
- `Status Parameters Editor`
- `String Pointer:`
- `String Text:`
- `Write`

### WorldMapPathForm
WF labels: **14** · AV labels: **10** · WF-only: **14** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `0000`
- `NULLの場合、拠点間を直線で通る`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `道の起点になる拠点ID`
- `道を移動するパスのポインタ`
- `道データへのポインタ`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `(NULL path move pointer = straight line between nodes)`
- `Address:`
- `End Base Point ID:`
- `Padding (B6):`
- `Padding (B7):`
- `Path Data Pointer:`
- `Path Move Pointer:`
- `Start Base Point ID:`
- `World Map Paths`
- `Write`

### WorldMapPathForm
WF labels: **14** · AV labels: **2** · WF-only: **14** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `0000`
- `NULLの場合、拠点間を直線で通る`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `道の起点になる拠点ID`
- `道を移動するパスのポインタ`
- `道データへのポインタ`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Path Editor`

### AIPerformItemForm
WF labels: **13** · AV labels: **7** · WF-only: **13** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `AIがアイテムを使用するかどうか判定する関数を指定します。
なお、章ごとにアイテムを使えるかどうかの設定は、「AIの章ごとの設定」にあります。`
- `ASM`
- `Size:`
- `アイテム`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Item Performance`
- `ASM Pointer:`
- `Item:`
- `Specifies the function that determines whether AI will use an item.`
- `Unused:`
- `Write`

### AIPerformStaffForm
WF labels: **13** · AV labels: **7** · WF-only: **13** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `ASM`
- `Size:`
- `アイテム`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `杖を利用できるAIが杖を使用するかどうか判定する関数を指定します。`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Staff Performance`
- `ASM Pointer:`
- `Item (Staff):`
- `Specifies the function that determines whether AI will use a staff.`
- `Unused:`
- `Write`

### CCBranchForm
WF labels: **13** · AV labels: **6** · WF-only: **13** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `CC3分岐パッチよって追加された値です。
上の2つの同じであれば無視されます。
この値は、クラスデータの「クラスチェンジ」に保存されます。`
- `CC時に表示されるクラスの英語表記へJump`
- `Size:`
- `この値を0にしないでください`
- `アドレス`
- `クラスチェンジ前`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`
- `選択中のクラス`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `CC Branch Editor`
- `Promotion Class 1:`
- `Promotion Class 2:`
- `Upstream Chain (classes that promote to this one):`
- `Write`

### EDSensekiCommentForm
WF labels: **13** · AV labels: **7** · WF-only: **13** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦績悪`
- `戦績普通`
- `戦績良`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Conversation Text 1:`
- `Conversation Text 2:`
- `Conversation Text 3:`
- `ED Senseki Comment`
- `Unit ID:`
- `Write`

### EventBattleDataFE7Form
WF labels: **13** · AV labels: **2** · WF-only: **13** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `もし、リストを縮めたい場合は、「攻撃側」で「戦闘終了」を選択してください。`
- `アドレス`
- `ダメージ`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `攻撃側`
- `攻撃方法`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Battle Data (FE7)`

### FontZHForm
WF labels: **13** · AV labels: **2** · WF-only: **13** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `アドレス`
- `サンプル`
- `フォントの種類`
- `フォント幅`
- `一括エクスポート`
- `利用フォント`
- `変更`
- `書き込み`
- `検索`
- `検索文字`
- `画像取出`
- `画像読込`
- `自動生成`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Font Editor (Chinese)`

### ImageGenericEnemyPortraitForm
WF labels: **13** · AV labels: **3** · WF-only: **13** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `xxx`
- `アドレス`
- `パレット`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `画像`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Generic Enemy Portraits`
- `Image Pointer:`

### ImageTSAAnime2Form
WF labels: **13** · AV labels: **9** · WF-only: **13** · AV-only: **9** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `IMG`
- `Size:`
- `アドレス`
- `パレット`
- `ヘッダー書き込み`
- `ヘッダ付きTSA`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Import PNG`
- `TSA Animation Editor v2`
- `TSA w/ Header (P8):`
- `Unknown 0 (W0):`
- `Unknown 2 (W2):`
- `Unknown 4 (W4):`
- `Unknown 6 (W6):`
- `Write`

### ItemWeaponTriangleForm
WF labels: **13** · AV labels: **8** · WF-only: **13** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `命中補正`
- `攻撃武器`
- `攻撃補正`
- `書き込み`
- `読込数`
- `選択アドレス:`
- `防御武器`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Bonus:`
- `Penalty:`
- `Weapon triangle bonus/penalty data`
- `Weapon Triangle Editor`
- `Weapon Type 1:`
- `Weapon Type 2:`
- `Write`

### SoundBossBGMForm
WF labels: **13** · AV labels: **10** · WF-only: **13** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `♪`
- `アドレス`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `曲番号`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Boss BGM Editor`
- `Jump`
- `Pick...`
- `Song ID:`
- `Unit ID:`
- `Unknown 1:`
- `Unknown 2:`
- `Unknown 3:`
- `Write`

### SoundRoomFE6Form
WF labels: **13** · AV labels: **6** · WF-only: **13** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `BGMID`
- `Size:`
- `♪`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `曲名`
- `書き込み`
- `説明`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `BGM ID:`
- `Description (Text ID):`
- `Song Name (Text ID):`
- `Sound Room (FE6)`
- `Write`

### WorldMapBGMForm
WF labels: **13** · AV labels: **6** · WF-only: **13** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `♪`
- `この拠点が、次の目的地となったときに再生するBGMを選択します。`
- `アドレス`
- `エイリーク編`
- `エフラム編`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Jump`
- `Song ID 1 (u16@0):`
- `Song ID 2 (u16@2):`
- `World Map BGM Editor`
- `Write`

### WorldMapEventPointerFE7Form
WF labels: **13** · AV labels: **2** · WF-only: **13** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `エリウッドエンディング`
- `ヘクトルエンディング`
- `リストの拡張`
- `ワールドマップイベント`
- `先頭アドレス`
- `再取得`
- `名前`
- `新規イベント`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Pointer (FE7)`

### WorldMapImageFE7Form
WF labels: **13** · AV labels: **2** · WF-only: **13** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `12分割TSA`
- `12分割画像`
- `TSA`
- `イベント用`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `パレット`
- `ポインタを書き込む`
- `メインフィールドマップ`
- `減色ツール`
- `画像`
- `画像取出`
- `画像読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `World Map Image (FE7)`

### AIStealItemForm
WF labels: **12** · AV labels: **6** · WF-only: **12** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `Size:`
- `アイテム`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `盗賊AIが、アイテムを盗むときに利用する、アイテムの優先度を設定します。
リストの先頭がもっとも優先度が高いアイテムになります。`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Steal Item Logic`
- `Item:`
- `Sets the priority of items that thief AI will try to steal. Items at the top of the list have the highest priority.`
- `Unused 1:`
- `Write`

### ArenaClassForm
WF labels: **12** · AV labels: **4** · WF-only: **12** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `クラス`
- `リストの拡張`
- `上級職、下級職は自動で調整されます。
闘技場で敵として使用されるユニットは、0xFD 対戦相手です。
ほかのユニットが出ることはないようです。`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `条件:`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Arena Class Editor`
- `Class ID:`
- `Write`

### BigCGForm
WF labels: **12** · AV labels: **10** · WF-only: **12** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `10分割画像`
- `size:`
- `TSA`
- `アドレス`
- `パレット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Big CG Editor`
- `Export PAL`
- `Export PNG`
- `Import PAL`
- `Import PNG`
- `Palette Pointer:`
- `Table Pointer:`
- `TSA Pointer:`
- `Write`

### DecreaseColorTSAToolForm
WF labels: **12** · AV labels: **2** · WF-only: **12** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `+余白`
- `TSAを無視`
- `サイズ補正方法`
- `パレット数`
- `ファイル選択`
- `元ファイル`
- `出力ファイル`
- `幅`
- `種類`
- `透過色`
- `開始`
- `高さ`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Color Reduction Tool`

### EDFE6Form
WF labels: **12** · AV labels: **2** · WF-only: **12** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `ショート版`
- `ロイと支援Ａ時`
- `ロング版`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `肩書`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `ED (FE6)`

### EDStaffRollForm
WF labels: **12** · AV labels: **5** · WF-only: **12** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `TSA`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `画像`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Image Pointer:`
- `Staff Roll Editor`
- `TSA Pointer:`
- `Write`

### EventForceSortieForm
WF labels: **12** · AV labels: **6** · WF-only: **12** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `章ID`
- `編`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Chapter ID:`
- `Force Sortie Editor`
- `Squad:`
- `Unit:`
- `Write`

### ImageChapterTitleFE7Form
WF labels: **12** · AV labels: **7** · WF-only: **12** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `セーブ画像`
- `セーブ画像取出し`
- `セーブ画像読込`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Chapter Title FE7 Editor`
- `Export PAL`
- `Export PNG`
- `Import PNG`
- `Save Image Ptr:`
- `Write`

### ImageRomAnimeForm
WF labels: **12** · AV labels: **2** · WF-only: **12** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `アニメの書出し`
- `アニメの読込`
- `グラフィックツール`
- `ソースファイルを開く`
- `ソースフォルダーを開く`
- `フレーム`
- `名前`
- `書き込み`
- `表示例`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `ROM Animation Viewer`

### ItemPromotionForm
WF labels: **12** · AV labels: **5** · WF-only: **12** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `CCアイテムの名前`
- `IERがインストールされているため、パッチ画面から設定してください。`
- `Size:`
- `アドレス`
- `クラス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Item Promotion Editor`
- `Promotion target class per source class`
- `Target Class ID:`
- `Write`

### ItemShopForm
WF labels: **12** · AV labels: **6** · WF-only: **12** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `Size:`
- `アイテム `
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `店の名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Item ID:`
- `Item Shop Editor`
- `Preparation shop item list`
- `Quantity/Uses:`
- `Write`

### MantAnimationForm
WF labels: **12** · AV labels: **2** · WF-only: **12** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `よくわからなければ、この設定を変更しないでください。
バグの原因になります。`
- `アドレス`
- `マント設定`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメへ移動する`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Mant Animation`

### MapStyleEditorAppendPopupForm
WF labels: **12** · AV labels: **4** · WF-only: **12** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `(FE7のみ)`
- `MapPointer(PLIST)を拡張`
- `OK`
- `PLIST拡張`
- `オブジェクトタイプ`
- `オブジェクトタイプ2`
- `キャンセル`
- `タイルアニメーション1`
- `タイルアニメーション2`
- `チップセットクタイプ`
- `パレット`
- `既に拡張済みです`

AV-only labels (usually fine — layout polish or rewording):

- `Append`
- `Append Map Style`
- `Cancel`
- `Do you want to append a new map style entry? This will add a new tileset configuration at the end of the list.`

### MenuExtendSplitMenuForm
WF labels: **14** · AV labels: **15** · WF-only: **12** · AV-only: **13** · Common: **2**

WF-only labels (candidates for missing fields in AV):

- `X`
- `Y`
- `先頭アドレス`
- `文字列0`
- `文字列1`
- `文字列2`
- `文字列3`
- `文字列4`
- `文字列5`
- `文字列6`
- `文字列7`
- `書き込み`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Split Menu Definition`
- `String 0:`
- `String 1:`
- `String 2:`
- `String 3:`
- `String 4:`
- `String 5:`
- `String 6:`
- `String 7:`
- `Write`
- `X Position:`
- `Y Position:`

### OPClassFontFE8UForm
WF labels: **12** · AV labels: **4** · WF-only: **12** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `OPフォント`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Image Pointer:`
- `OP Class Font (FE8U) Editor`
- `Write`

### OPClassFontForm
WF labels: **12** · AV labels: **5** · WF-only: **12** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `OPフォント`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `画像取出`
- `画像読込`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Export PNG`
- `Image Pointer:`
- `OP Class Font Editor`
- `Write`

### StatusUnitsMenuForm
WF labels: **12** · AV labels: **7** · WF-only: **12** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `RMenu`
- `Size:`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `参照データ`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`
- `項目名`
- `順番`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Item Name Text ID:`
- `Order:`
- `Reference Data:`
- `RMenu Text ID:`
- `Status Units Menu Editor`
- `Write`

### ToolExportEAEventForm
WF labels: **12** · AV labels: **14** · WF-only: **12** · AV-only: **14** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `EA形式でイベントをエクスポート`
- `EA形式でイベントをエクスポートします。`
- `EA形式でワールドマップイベント(選択時)をエクスポート`
- `EA形式でワールドマップイベントをエクスポート`
- `UndoBufferのエクスポート`
- `インポートは、メニューの「実行」->「Event Assemblerで追加」で追加から実行してください。`
- `エクスポート時にaddEndGuardsを付与する`
- `マップ名`
- `ユニットやクラス、アイテムなどのテーブルをダンプします。`
- `主要テーブルのエクスポート`
- `今回このROMに行った変更点を出力します`
- `最新版のEAでは拡張領域のデータがダンプできないことがあるので、その場合は、古いEA ver9を利用してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Add EndGuards on export`
- `Data`
- `Dumps unit, class, item, and other tables.`
- `Export changes made to the ROM in this session.`
- `Export Events in EA format`
- `Export events in EA format.`
- `Export Main Tables`
- `Export Undo Buffer`
- `Export World Map Events (selected) in EA format`
- `Export World Map Events in EA format`
- `For import, use Run > Event Assembler Add.`
- `Map Name`
- `The latest EA may not dump extended area data. In that case, use EA ver9.`
- `Undo Buffer`

### WorldMapPathMoveEditorForm
WF labels: **12** · AV labels: **7** · WF-only: **12** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `X`
- `Y`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `書き込み`
- `経過時間`
- `読込数`
- `道`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Coordinate X:`
- `Coordinate Y:`
- `Elapsed Time:`
- `Elapsed Time controls how long the unit pauses at this node. Lower values = longer pause. Total movement uses 4096 time units. Sum of all elapsed times across nodes must be <= 4095, otherwise instant movement occurs.`
- `Path Movement Editor`
- `Write`

### AIUnitsForm
WF labels: **11** · AV labels: **5** · WF-only: **11** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Units Evaluation`
- `Unit:`
- `Unknown 1:`
- `Write`

### EventFinalSerifFE7Form
WF labels: **11** · AV labels: **2** · WF-only: **11** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `セリフ`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Final Serif (FE7)`

### EventMoveDataFE7Form
WF labels: **11** · AV labels: **5** · WF-only: **11** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `時間または速度`
- `書き込み`
- `移動方向`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Direction values: 00=Left, 01=Right, 02=Down, 03=Up, 04=End, 09=Highlight, 0A=Collision mark, 0C=Speed change`
- `Move Data (FE7)`
- `Move Direction:`
- `Write`

### EventTemplate2Form
WF labels: **11** · AV labels: **2** · WF-only: **11** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `テンプレート1`
- `友軍(NPC/緑)が侵入したら発動するイベントを作成`
- `敵軍(赤)が侵入したら発動するイベントを作成`
- `新規にイベントを割り振りますか？`
- `新規にイベント領域を割り振り、空のイベントを定義します。`
- `既存イベントを呼び出す`
- `特定のユニットが侵入したらゲームオーバーイベント`
- `特定のユニットが侵入したら発動するイベントを作成`
- `砂漠の財宝`
- `章終了イベントを呼び出す(章クリア)`
- `自軍が侵入したら発動するイベントを作成`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Template 2`

### EventTemplate3Form
WF labels: **11** · AV labels: **2** · WF-only: **11** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `カウンターを利用して特定のイベントから数ターン増援`
- `ゲームオーバーイベント`
- `テンプレート`
- `会話イベント`
- `援軍`
- `敵増援`
- `敵増援(難易度:ハードのみ)`
- `新規にイベントを割り振りますか？`
- `新規にイベント領域を割り振り、空のイベントを定義します。`
- `既存イベントを呼び出す`
- `章終了イベントを呼び出す(章クリア)`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Template 3`

### ExtraUnitFE8UForm
WF labels: **11** · AV labels: **5** · WF-only: **11** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `フラグ`
- `ユニット情報`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Extra Unit (FE8U)`
- `Flag ID:`
- `Unit Info Pointer:`
- `Write`

### ItemRandomChestForm
WF labels: **11** · AV labels: **5** · WF-only: **11** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アイテム `
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `確率%`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Item:`
- `Probability %:`
- `Random Chest Items`
- `Write`

### MapLoadFunctionForm
WF labels: **11** · AV labels: **6** · WF-only: **11** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `マップを読み込んだ時の追加処理`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `別のパッチでデータが書きかれられているため、修正することができません。`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Function Pointer:`
- `Function pointers called when entering each map chapter.`
- `Info:`
- `Map Load Functions (FE8)`
- `Write`

### MapPointerForm
WF labels: **11** · AV labels: **4** · WF-only: **11** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `PLIST分割`
- `Size:`
- `Text`
- `アドレス`
- `ポインタ`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Map Data Pointer:`
- `Map Pointer Editor`
- `Write`

### MapTerrainBGLookupTableForm
WF labels: **11** · AV labels: **4** · WF-only: **11** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `アドレス`
- `値`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメの床へJump`
- `拡張された領域にデータが割り当てられていません。
パッチ「戦闘床地形と戦闘背景のリストを拡張する」から、データを割り振ってください。`
- `書き込み`
- `条件:`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Battle BG:`
- `Terrain BG Lookup Table`
- `Write`

### MapTerrainFloorLookupTableForm
WF labels: **11** · AV labels: **4** · WF-only: **11** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `アドレス`
- `値`
- `先頭アドレス`
- `再取得`
- `名前`
- `戦闘アニメの背景へJump`
- `拡張された領域にデータが割り当てられていません。
パッチ「戦闘床地形と戦闘背景のリストを拡張する」から、データを割り振ってください。`
- `書き込み`
- `条件:`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Terrain Battle Floor:`
- `Terrain Floor Lookup Table`
- `Write`

### OPClassAlphaNameForm
WF labels: **11** · AV labels: **4** · WF-only: **11** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `↓文字列内訳`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `分岐CC選択時のクラス名`
- `名前`
- `書き込み`
- `読込数`
- `警告:アルファベット以外の文字が入っています。`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Alpha Name:`
- `OP Class Alpha Name Editor`
- `Write`

### SongInstrumentImportWaveForm
WF labels: **11** · AV labels: **2** · WF-only: **11** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `DPCM lookahead`
- `DPCM圧縮`
- `m4a_hq_mixer Patchがインストールされていないので、DPCM圧縮は利用できません。`
- `Preview`
- `Waveは容量をたくさん消費するため、低音質に変換してインポートすることをお勧めします。`
- `インポートする`
- `キャンセル`
- `チャンネル`
- `前後の無音除去`
- `音質を下げる`
- `音量`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Wave Import`

### SoundFootStepsForm
WF labels: **11** · AV labels: **4** · WF-only: **11** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `別のパッチでデータが書きかれられているため、修正することができません。`
- `名前`
- `書き込み`
- `読込数`
- `足音`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Data Pointer:`
- `Footstep Sounds Editor`
- `Write`

### SummonUnitForm
WF labels: **11** · AV labels: **5** · WF-only: **11** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `召喚士ユニット`
- `名前`
- `呼びされるユニット`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Summon Unit Editor`
- `Summoned Unit:`
- `Summoner:`
- `Write`

### ToolASMInsertForm
WF labels: **11** · AV labels: **2** · WF-only: **11** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Patch Maker`
- `SRC`
- `Undo`
- `アセンブリ(コンパイル)に成功した場合は、ROMに埋め込みますか?`
- `デバッグ用のシンボル `
- `フックに利用するレジスタ`
- `フリーエリアの定義`
- `中間ファイル`
- `別ファイル選択`
- `実行`
- `自動ラベルチェック`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `ASM Insertion Tool`

### ToolWorkSupportForm
WF labels: **12** · AV labels: **14** · WF-only: **11** · AV-only: **13** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `Open`
- `UpdateInfo:`
- `コミニティ`
- `バージョン`
- `他の作品を表示する`
- `名前`
- `最新バージョンに更新する`
- `現在自動フィードバックは有効になっています。ご協力に感謝します。`
- `自動フィードバックを無効にする`
- `著者`
- `開発コミニティにアクセスする`

AV-only labels (usually fine — layout polish or rewording):

- `Actions`
- `Author:`
- `Check Update`
- `Close`
- `Community:`
- `Name:`
- `Open Community`
- `Open Info File`
- `Project Information`
- `Show All Works`
- `Status`
- `Version:`
- `Work Support`

### UnitIncreaseHeightForm
WF labels: **11** · AV labels: **2** · WF-only: **11** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `ステータス画面で背丈を伸ばす補正をするか？`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `別のパッチでデータが書きかれられているため、修正することができません。`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Unit Height Adjustment`

### AITilesForm
WF labels: **10** · AV labels: **4** · WF-only: **10** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `タイル`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Tiles Evaluation`
- `Tile:`
- `Write`

### ClassOPFontForm
WF labels: **10** · AV labels: **2** · WF-only: **10** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `OPフォント`
- `size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class OP Font`

### EventFunctionPointerFE7Form
WF labels: **10** · AV labels: **5** · WF-only: **10** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `??`
- `Size:`
- `アドレス`
- `イベント命令の関数ポインタテーブル`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Command Function Pointer:`
- `Event Command Function Pointer Table (FE7)`
- `Unknown (offset 4):`
- `Write`

### EventFunctionPointerForm
WF labels: **10** · AV labels: **4** · WF-only: **10** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `イベント命令の関数ポインタテーブル`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `条件:`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Command Function Pointer:`
- `Event Command Function Pointer Table`
- `Write`

### EventTalkGroupFE7Form
WF labels: **10** · AV labels: **4** · WF-only: **10** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `セリフ`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Talk Group (FE7)`
- `Text ID:`
- `Write`

### EventTemplate1Form
WF labels: **10** · AV labels: **2** · WF-only: **10** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `テンプレート`
- `何もしないイベント1を設定`
- `新規にイベントを割り振りますか？`
- `新規にイベント領域を割り振り、空のイベントを定義します。`
- `既存イベントを呼び出す`
- `村でのアイテム取得イベントを作成`
- `村でのゴールド取得イベントを作成`
- `村での仲間加入イベントを作成`
- `民家での会話イベントを作成`
- `章終了イベントを呼び出す(章クリア)`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Template 1`

### ExtraUnitForm
WF labels: **10** · AV labels: **4** · WF-only: **10** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `フラグ`
- `ユニット情報`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Extra Unit Editor`
- `Unit Data Pointer:`
- `Write`

### ImageSystemAreaForm
WF labels: **13** · AV labels: **8** · WF-only: **10** · AV-only: **5** · Common: **3**

WF-only labels (candidates for missing fields in AV):

- `GBAカラー`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `GBA Color (W0):`
- `Preview:`
- `System Area Graphics`
- `Write`

### LinkArenaDenyUnitForm
WF labels: **10** · AV labels: **4** · WF-only: **10** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `ユニット`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Link Arena Deny Unit Editor`
- `Unit ID:`
- `Write`

### SMEPromoListForm
WF labels: **11** · AV labels: **9** · WF-only: **10** · AV-only: **8** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `BaseClass`
- `PromoClass`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Base Class`
- `Count:`
- `Expand List`
- `Name`
- `Promo Class`
- `Reload`
- `Write`

### SomeClassListForm
WF labels: **10** · AV labels: **5** · WF-only: **10** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Class`
- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class ID (u8):`
- `Class List Editor`
- `Null-terminated list of class IDs (1 byte per entry).`
- `Write`

### StatusOptionOrderForm
WF labels: **10** · AV labels: **4** · WF-only: **10** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `ゲームオプション`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Option ID (u8@0):`
- `Status Option Order Editor`
- `Write`

### ToolDiffDebugSelectForm
WF labels: **10** · AV labels: **10** · WF-only: **10** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `..`
- `↑最新`
- `このROMが最後に安定していたROMとして、
相違点を取得する`
- `より古い↓`
- `バックアップROMがある場所を追加`
- `バックアップ履歴(上が最新) `
- `探索するprefix`
- `無改造ROM`
- `選択されているROM情報`
- `選択しているROMをエミュレータでテストプレイする`

AV-only labels (usually fine — layout polish or rewording):

- `...`
- `Add backup ROM location`
- `Backup History (newest on top)`
- `Older ↓`
- `Search prefix`
- `Selected ROM Info`
- `Test play selected ROM in emulator`
- `Use this ROM as the last stable baseline and get differences`
- `Vanilla ROM`
- `↑ Newest`

### ToolROMRebuildForm
WF labels: **10** · AV labels: **2** · WF-only: **10** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `nullの連続数`
- `ファイル選択`
- `フリー領域の利用`
- `フリー領域の定義`
- `ポインタの共有`
- `再構築アドレス`
- `変更点をファイルに書きだす`
- `探索開始アドレス`
- `無改造ROM`
- `追加設定ファイル`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `ROM Rebuild Tool`

### UnitActionPointerForm
WF labels: **10** · AV labels: **2** · WF-only: **10** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `このROMには、UnitAction Patchが適応されています。`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`
- `関数ポインタ`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Unit Action Pointers`

### WelcomeForm
WF labels: **10** · AV labels: **8** · WF-only: **10** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `EN`
- `FEBuilderGBAの使い方の説明書を見る`
- `FEBuilderGBAを最新版へ更新する`
- `JP`
- `ROMを開く`
- `ZH`
- `アップデートチェック`
- `オンラインマニュアル`
- `他のFE ROMを開く`
- `最後に開いたROM`

AV-only labels (usually fine — layout polish or rewording):

- `Avalonia Cross-Platform Edition`
- `Check for Updates`
- `FEBuilderGBA`
- `No recent files`
- `Online Manual`
- `Open Last ROM`
- `Open ROM`
- `Recent Files`

### WorldMapEventPointerFE6Form
WF labels: **10** · AV labels: **2** · WF-only: **10** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `ワールドマップイベント`
- `先頭アドレス`
- `再取得`
- `名前`
- `新規イベント`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Pointer (FE6)`

### WorldMapImageFE6Form
WF labels: **10** · AV labels: **2** · WF-only: **10** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `256色画像`
- `ZOOM NE`
- `ZOOM NW`
- `ZOOM SE`
- `ZOOM SW`
- `パレット`
- `ポインタを書き込む`
- `メインフィールドマップ`
- `画像取出`
- `画像読込`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `World Map Image (FE6)`

### AIMapSettingForm
WF labels: **9** · AV labels: **7** · WF-only: **9** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `章ごとにAIが実行できる行動を定義します。
例えば、扉の鍵をドロップするAIがいるのに、敵AIが扉の鍵を利用可能にすると、敵は勝手に扉を開けてしまいます。`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Map Settings`
- `Trait 1 (flags):`
- `Trait 2 (flags):`
- `Trait 3 (flags):`
- `Trait 4 (flags):`
- `Write`

### AOERANGEForm
WF labels: **9** · AV labels: **8** · WF-only: **9** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `AoE攻撃の範囲を指定します。
中心点は、攻撃が炸裂する中心点です。
攻撃するマスを1に、それ以外を0に指定してください。`
- `中心点`
- `中心点X`
- `中心点Y`
- `先頭アドレス`
- `再取得`
- `幅`
- `書き込み`
- `高さ`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Area of Effect Range`
- `Center X:`
- `Center Y:`
- `Height:`
- `Specifies the AoE attack range mask. Center point is where the attack detonates. Set attacked tiles to 1, others to 0.`
- `Width:`
- `Write`

### ArenaEnemyWeaponForm
WF labels: **9** · AV labels: **4** · WF-only: **9** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `ランクアップ武器`
- `先頭アドレス`
- `再取得`
- `基本武器`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Arena Enemy Weapon Editor`
- `Weapon ID:`
- `Write`

### Command85PointerForm
WF labels: **9** · AV labels: **2** · WF-only: **9** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `イベント命令の関数ポインタテーブル`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Command 0x85 Pointer`

### DisASMDumpAllForm
WF labels: **9** · AV labels: **12** · WF-only: **9** · AV-only: **12** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Arg Grep`
- `IDAにimportできる形式でMAPファイルを生成します。`
- `MAKE IDAMapFile`
- `MAKE no$gba sym File`
- `no$gba debuggerで利用できるsym形式のMAPファイルを作成します。
ROMと同じディレクトリに設置してください。`
- `このゲームのプログラムデータをすべてファイルに出力します。
処理には時間がかかるものがあります。`
- `すべてのコードを逆アセンブルしてファイルに保存します。`
- `全部逆アセンブルして保存する`
- `特定の関数の引数だけを抽出します。`

AV-only labels (usually fine — layout polish or rewording):

- `0x08000000`
- `0x100`
- `Address:`
- `Address Range`
- `Close`
- `DisASM`
- `Disassembly Dump All`
- `IDA MAP`
- `Length:`
- `No$GBA SYM`
- `Run Dump`
- `Select an output format and run the disassembly dump.`

### ErrorPaletteShowForm
WF labels: **9** · AV labels: **2** · WF-only: **9** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `できるだけ規定値2になるように再構築をしてインポートします。`
- `できるだけ規定値になるように再構築をしてインポートします。`
- `インポート処理をキャンセルします.`
- `キャンセル`
- `パレットが規定値と違います。`
- `無視して強行`
- `規定値2で再構築してインポート`
- `規定値で再構築してインポート`
- `規定値と違いますが、このまま強引にインポートします。`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `Palette Error Display`

### EventAssemblerForm
WF labels: **9** · AV labels: **2** · WF-only: **9** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Event Assemblerでeventスクリプトを読み込んで現在のROMに適応します。`
- `UNDO`
- `スクリプト`
- `スクリプトのアンインストール`
- `スクリプト読込`
- `デバッグ用のシンボル `
- `フリーエリアの定義`
- `別ファイル選択`
- `更新されたプログラムを再コンパイル`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Assembler`

### EventTemplate4Form
WF labels: **9** · AV labels: **2** · WF-only: **9** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `アイテム取得イベントを作成`
- `テンプレート`
- `ユニットを説得しパーティ加入`
- `会話イベントを作成`
- `何もしないイベント1を設定します。`
- `新規にイベントを割り振りますか？`
- `新規にイベント領域を割り振り、空のイベントを定義します。`
- `既存イベントを呼び出す`
- `章終了イベントを呼び出す(章クリア)`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Template 4`

### ItemEffectPointerForm
WF labels: **9** · AV labels: **5** · WF-only: **9** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `イベント命令の関数ポインタテーブル`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Effect Pointer:`
- `Indirect effect pointer table`
- `Item Effect Pointer Editor`
- `Write`

### RAMRewriteToolMAPForm
WF labels: **9** · AV labels: **10** · WF-only: **9** · AV-only: **10** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `no$gbaの読込ブレークポイントとしてコピー`
- `X`
- `Y`
- `クリップボードへコピー`
- `データの直書き換え`
- `ポインタとしてクリップボードへコピー`
- `リトルエンディアンポインタとしてクリップボードへコピー`
- `値`
- `値の書き換え`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Close`
- `e.g. 0x01`
- `e.g. 0x02000000`
- `e.g. 0xFF`
- `Map ID:`
- `RAM Rewrite Tool (MAP)`
- `Rewrite RAM values for map data in a running emulator.`
- `Value:`
- `Write`

### SoundRoomCGForm
WF labels: **10** · AV labels: **4** · WF-only: **9** · AV-only: **3** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Sound Room CG`
- `Write`

### ToolFELintForm
WF labels: **9** · AV labels: **2** · WF-only: **9** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Error`
- `NoError`
- `Scan`
- `このROMには、自動的に検出できるエラーは存在しません。`
- `以下のマップにエラーが存在します。`
- `再取得(Ctrl+R)`
- `名前`
- `比較デバッグツール`
- `非表示のエラーも表示`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `FELint GUI`

### UnitsShortTextForm
WF labels: **9** · AV labels: **7** · WF-only: **9** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `セリフ`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Maps each unit index to a description text ID (2 bytes per entry).`
- `Preview:`
- `Text ID:`
- `Unit:`
- `Units Short Text Editor`
- `Write`

### VennouWeaponLockForm
WF labels: **9** · AV labels: **7** · WF-only: **9** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `リストの拡張`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Description:`
- `First byte = type (0-3), rest = unit/class IDs. Null-terminated.`
- `Linked Name:`
- `Lock Type / ID:`
- `Weapon Lock (Vennou) Editor`
- `Write`

### DisASMDumpAllArgGrepForm
WF labels: **8** · AV labels: **6** · WF-only: **8** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `blまたはb呼び出しの関数名
または、関数のアドレスを指定ください。`
- `事前に逆アセンブラによって、ソースコードをすべて逆アセンブルしてください。`
- `参照`
- `探すレジスタ`
- `検索結果に関数呼び出しは含めない`
- `検索開始`
- `用途が判明していない関数呼び出しのみ表示`
- `許容する行数`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `Disassembly Argument Grep`
- `Enter search pattern...`
- `Pattern:`
- `Search`
- `Search disassembly output by argument pattern.`

### MapMiniMapTerrainImageForm
WF labels: **8** · AV labels: **2** · WF-only: **8** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ASMポインタ`
- `Size:`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Mini-Map Terrain`

### PatchFilterExForm
WF labels: **8** · AV labels: **5** · WF-only: **8** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `イベント命令(#EVENT)`
- `インストール済みのパッチ(!)`
- `エンジン(#ENGINE)`
- `ソート`
- `フィルタ解除`
- `定番の修正(#ESSENTIALFIXES)`
- `画像(#IMAGE)`
- `音楽(#SOUND)`

AV-only labels (usually fine — layout polish or rewording):

- `Cancel`
- `Enter filter keywords...`
- `Filter text:`
- `OK`
- `Patch Filter`

### ToolDiffForm
WF labels: **8** · AV labels: **2** · WF-only: **8** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `フリースペースはまとめる`
- `別ファイル選択`
- `容認差異`
- `差分をBINパッチとして作成する`
- `比較ファイル`
- `比較ファイルA`
- `比較ファイルB`
- `比較方法`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `ROM Diff Tool`

### ErrorPaletteMissMatchForm
WF labels: **7** · AV labels: **2** · WF-only: **7** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `できるだけ元の画像を再現できるように再構築をしてインポートします。`
- `インポート処理をキャンセルします.`
- `キャンセル`
- `パレットの並び順が違います。`
- `並び順が違うようですが、このまま強引にインポートします。`
- `無視して強行`
- `規定値で再構築してインポート`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `Palette Mismatch`

### ErrorReportForm
WF labels: **7** · AV labels: **2** · WF-only: **7** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `このボタンを押して開発者にエラーを報告してください。 ---->`
- `アップデートを確認してください`
- `エラーを開発者に報告する`
- `エラーメッセージ`
- `スタックトレース`
- `何をしたらエラーが起きたのか教えてください`
- `未知のエラーが発生しました。開発者までご連絡ください。`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `Error Report`

### ErrorTSAErrorForm
WF labels: **7** · AV labels: **2** · WF-only: **7** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `この画像は、必要な形式の基準を満たしていません。`
- `インポート処理をキャンセルします.`
- `キャンセル`
- `減色処理や、パレットの入れ替えを行い、自動的に形式を満たせる形式に変換します`
- `自動変換してインポート`
- `規約を守っていないデータはパレットの最初の色としてインポートします。`
- `違反データは0に指定`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `TSA Error`

### EventTemplate6Form
WF labels: **7** · AV labels: **2** · WF-only: **7** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ゲームオーバーイベント`
- `テンプレート`
- `何もしないイベント1を設定`
- `新規にイベントを割り振りますか？`
- `新規にイベント領域を割り振り、空のイベントを定義します。`
- `既存イベントを呼び出す`
- `章終了イベントを呼び出す(章クリア)`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Template 6`

### MapEditorResizeDialogForm
WF labels: **11** · AV labels: **11** · WF-only: **7** · AV-only: **7** · Common: **4**

WF-only labels (candidates for missing fields in AV):

- `B`
- `L`
- `R`
- `T`
- `サイズ`
- `位置`
- `変更する`

AV-only labels (usually fine — layout polish or rewording):

- `Bot`
- `Left`
- `Position`
- `Resize`
- `Right`
- `Size`
- `Top`

### MapPointerNewPLISTPopupForm
WF labels: **7** · AV labels: **6** · WF-only: **7** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `MapPointer(PLIST)を拡張`
- `PLIST割り当て`
- `PLIST割り当てがありません。`
- `PLIST拡張`
- `キャンセル`
- `割り当て`
- `既に拡張済みです`

AV-only labels (usually fine — layout polish or rewording):

- `Cancel`
- `Enter the PLIST number for the new map pointer entry.`
- `Extend PLIST Range`
- `OK`
- `PLIST (Pointer List) assigns a numeric ID to each map.
Choose an unused PLIST number to add a new map pointer entry.
The PLIST ID is used internally to reference map data.`
- `PLIST ID:`

### MapTerrainNameEngForm
WF labels: **7** · AV labels: **5** · WF-only: **7** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Resolved Name:`
- `Terrain Name (English)`
- `Terrain Name Text ID:`
- `Write`

### MapTerrainNameForm
WF labels: **7** · AV labels: **5** · WF-only: **7** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Name:`
- `Terrain Name Editor`
- `Text ID:`
- `Write`

### MapTerrainNameForm
WF labels: **7** · AV labels: **5** · WF-only: **7** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Name:`
- `Name Pointer:`
- `Terrain Name Editor`
- `Write`

### OPClassAlphaNameFE6Form
WF labels: **7** · AV labels: **5** · WF-only: **7** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `先頭アドレス`
- `再取得`
- `名前`
- `書き込み`
- `読込数`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Alpha Name:`
- `Name Pointer:`
- `OP Class Alpha Name (FE6) Editor`
- `Write`

### RAMRewriteToolForm
WF labels: **7** · AV labels: **9** · WF-only: **7** · AV-only: **9** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `no$gbaの読込ブレークポイントとしてコピー`
- `クリップボードへコピー`
- `データの直書き換え`
- `ポインタとしてクリップボードへコピー`
- `リトルエンディアンポインタとしてクリップボードへコピー`
- `値`
- `値の書き換え`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Close`
- `e.g. 0x02000000`
- `e.g. 0xFF`
- `Platform Notice`
- `Please use the Windows (WinForms) version of FEBuilderGBA for this functionality.`
- `RAM Rewrite Tool`
- `RAM rewriting requires Windows P/Invoke to access emulator process memory and is not available in the cross-platform Avalonia version.`
- `Value:`

### ToolFlagNameForm
WF labels: **7** · AV labels: **2** · WF-only: **7** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ディフォルトに戻す`
- `フラグに名前を設定すると、より理解しやすくなります。
フラグの名前は、ROMごとに別ファイルに保存します。`
- `フラグの名前`
- `リストの拡張`
- `名前`
- `書き込み`
- `章内で利用しているフラグの確認`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Flag Name Editor`

### DevTranslateForm
WF labels: **6** · AV labels: **2** · WF-only: **6** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Convert`
- `form design`
- `Reverse`
- `日本語ではなく、可能な限り英語から翻訳する`
- `翻訳`
- `言語`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Developer Translation Tool`

### EventUnitColorForm
WF labels: **6** · AV labels: **2** · WF-only: **6** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `MESSAGE`
- `友軍`
- `敵軍`
- `第4軍`
- `自軍`
- `適応`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Unit Color Assignment`

### PointerToolCopyToForm
WF labels: **6** · AV labels: **6** · WF-only: **6** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `no$gbaの読込ブレークポイントとしてコピー`
- `クリップボードへコピー`
- `バイナリエディタ`
- `ポインタとしてクリップボードへコピー`
- `リトルエンディアンポインタとしてクリップボードへコピー`
- `値:`

AV-only labels (usually fine — layout polish or rewording):

- `Copy (No $ / GBA / Rad / BreakPoint)`
- `Copy as Hex`
- `Copy as Little Endian`
- `Copy as Pointer`
- `Copy to Clipboard`
- `Value:`

### SongTrackChangeTrackForm
WF labels: **6** · AV labels: **2** · WF-only: **6** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `このトラックのPANに、指定された値を足します。
マイナスは左側、プラスは右側です。`
- `このトラックの音量に、指定された値を足しこみます。
大きくするほど、大きな音になります。`
- `アドレス`
- `ベロシティも補正する`
- `変更する`
- `変更先`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Track Change`

### TextToSpeechForm
WF labels: **6** · AV labels: **6** · WF-only: **6** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `このサイズ以上の文字列を読み上げる`
- `合成音声エンジンで自動的にテキストを読み上げます。
タイプミスを見つけるには音読するのが一番です。`
- `読み上げエンジン`
- `読み上げ停止`
- `読み上げ速度`
- `読み上げ開始`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `Enter or paste text to have it read aloud using the system TTS engine.`
- `Enter text here...`
- `Ready`
- `Speak`
- `Text to Speech`

### ToolThreeMargeForm
WF labels: **6** · AV labels: **14** · WF-only: **6** · AV-only: **14** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Mark&List`
- `Set&Mark`
- `アドレス`
- `アドレス,長さ,対処法,ヒント`
- `変更をすべてキャンセルする`
- `相違点を現在のROMへマージします。書き込んだらF5でエミュレータを動作して確認してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Both (same): `
- `Browse...`
- `Close`
- `Conflict bytes: `
- `Conflicts (resolve each: Mine or Theirs)`
- `Merge`
- `Merge Statistics`
- `My changes: `
- `My ROM:`
- `Original ROM:`
- `Save Merged ROM`
- `Their changes: `
- `Their ROM:`
- `Three-Way ROM Merge`

### ToolUpdateDialogForm
WF labels: **6** · AV labels: **2** · WF-only: **6** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Gitでパッチデータを更新します`
- `アップデートしません`
- `ブラウザでURLを開きます`
- `プログラム本体を更新します`
- `全自動でアップデートします`
- `最新版({0})があるようです。
アップデートしますか？

{1}`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Update Checker`

### AIASMCALLTALKForm
WF labels: **5** · AV labels: **8** · WF-only: **5** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `00`
- `From`
- `To`
- `先頭アドレス`
- `書き込み`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI ASM Call Talk`
- `Configures enemy AI to trigger a talk event (like FE7 Farina).`
- `From Unit:`
- `To Unit:`
- `Unused 2:`
- `Unused 3:`
- `Write`

### EventTemplate5Form
WF labels: **5** · AV labels: **2** · WF-only: **5** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `何もしないイベント1を設定`
- `新規にイベントを割り振りますか？`
- `新規にイベント領域を割り振り、空のイベントを定義します。`
- `既存イベントを呼び出す`
- `章終了イベントを呼び出す(章クリア)`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Template 5`

### HexEditorForm
WF labels: **6** · AV labels: **6** · WF-only: **5** · AV-only: **5** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `DisASM`
- `&Jump`
- `Mark&List`
- `Set&Mark`
- `Write`

AV-only labels (usually fine — layout polish or rewording):

- `0x00000000`
- `Address:`
- `Go`
- `Page Down`
- `Page Up`

### ImageBGSelectPopupForm
WF labels: **5** · AV labels: **4** · WF-only: **5** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `TSAを利用しないBG224色(会話用)`
- `TSAを利用しないBG256色(カットシーン)`
- `インポートするBGの形式を指定してください。`
- `バニラの仕様であるTSAを利用した最大8パレットを指定します。
減色ツールの「01=背景(BG,CG)」で減色した画像を指定してください。`
- `バニラ形式。TSA方式を利用する方法でインポートする`

AV-only labels (usually fine — layout polish or rewording):

- `Background Image Selector.
Choose a background image from the available BG entries in the ROM.`
- `BG Image Select`
- `Cancel`
- `Select`

### MapEditorAddMapChangeDialogForm
WF labels: **5** · AV labels: **6** · WF-only: **5** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `キャンセル`
- `マップ変化の設定画面を出します。`
- `マップ変化を減らす場合は、設定画面から消去してください。
変更したマップ変化は、マップを切り替えたときに読み込まれます。`
- `新規にマップ変化を割り当てます。`
- `訪問村や、宝箱、壊れる壁、古木などのために、
マップ変化を新規に割り当てる場合は、ここから新規にマップ変化を割り当ててください。`

AV-only labels (usually fine — layout polish or rewording):

- `Assign a new map change ID. A new PLIST entry will be allocated automatically.`
- `Assign new map change`
- `Cancel`
- `Do you want to create additional map changes?`
- `Open map change settings`
- `Open the map change configuration screen to edit existing map change entries.`

### MapSettingDifficultyForm
WF labels: **5** · AV labels: **2** · WF-only: **5** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ノーマルモードペナルティ`
- `ハードブースト`
- `変更する`
- `簡易モードペナルティ`
- `難易度補正`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Difficulty Settings`

### MapStyleEditorImportImageOptionForm
WF labels: **5** · AV labels: **5** · WF-only: **5** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `マップチップのインポート`
- `一枚絵としてインポートする。(この画像からmap_configを自動生成する)`
- `一枚絵のインポート`
- `画像だけをインポートする。(パレットはインポートしない)`
- `画像と同時にパレットもインポートする(推奨)`

AV-only labels (usually fine — layout polish or rewording):

- `Import as one picture (auto-generate map_config from this image)`
- `Import image only (do not import palette)`
- `Import image with palette (recommended)`
- `Map Chip Import`
- `One Picture Import`

### SongInstrumentDirectSoundForm
WF labels: **7** · AV labels: **7** · WF-only: **5** · AV-only: **5** · Common: **2**

WF-only labels (candidates for missing fields in AV):

- `LengthByte`
- `LoopStartByte`
- `これより下のアドレスには、waveデータが、LengthByteだけ格納されています。`
- `先頭アドレス`
- `書き込み`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Direct Sound Instruments`
- `Length Byte:`
- `Loop Start Byte:`
- `Write`

### SongTrackAllChangeTrackForm
WF labels: **5** · AV labels: **2** · WF-only: **5** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `全トラックのPANに、指定された値を足します。
マイナスは左側、プラスは右側です。`
- `全トラックのテンポに、指定された値を足します。
大きくするほど早くなります。`
- `全トラックの音量に、指定された値を足しこみます。
大きくするほど、大きな音になります。`
- `変更する`
- `変更先`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Bulk Track Change`

### ToolCustomBuildForm
WF labels: **5** · AV labels: **2** · WF-only: **5** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `スキル割り当ての引き継ぎ`
- `ターゲット:ビルドするスキル拡張`
- `ビルド開始`
- `別ファイル選択`
- `無改造ROM`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Custom Build Tool`

### ToolUPSOpenSimpleForm
WF labels: **5** · AV labels: **2** · WF-only: **5** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `UPSパッチを適応したROMを、UPSファイル名.gbaとして保存する`
- `UPSパッチを開く`
- `UPSパッチを開くために無改造のROMを選択してください`
- `ファイル選択`
- `無改造ROM`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `UPS Patch Applier`

### ToolUndoForm
WF labels: **5** · AV labels: **2** · WF-only: **5** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `↑最新`
- `このバージョンに戻す`
- `このバージョンをエミュレータでテストプレイ`
- `より古い↓`
- `履歴(上が最新)`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Undo History Viewer`

### EventUnitItemDropForm
WF labels: **4** · AV labels: **2** · WF-only: **4** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `この敵を倒すと、アイテムドロップするようにしますか？
アイテムドロップする場合、一番最後に持っているアイテムが対象になります。`
- `アイテムドロップしない`
- `アイテムドロップする`
- `キャンセル`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Unit Item Drop Editor`

### EventUnitNewAllocForm
WF labels: **4** · AV labels: **2** · WF-only: **4** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `増援等で、追加で登場させたいユニットのために、
新規に領域を確保します。`
- `確保`
- `確保した領域を使わないで、
ユニット配置ウィンドウを閉じてしまうと、
利用されない無駄データとなってしまうので
注意してください。`
- `確保する人数`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Unit Allocation Editor`

### HowDoYouLikePatch2Form
WF labels: **4** · AV labels: **3** · WF-only: **4** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Enable`
- `Reason`
- `このパッチを推奨しない`
- `無視して続行する`

AV-only labels (usually fine — layout polish or rewording):

- `Apply`
- `Patch Review`
- `Skip`

### HowDoYouLikePatchForm
WF labels: **4** · AV labels: **3** · WF-only: **4** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Enable`
- `Reason`
- `このパッチを推奨しない`
- `無視して続行する`

AV-only labels (usually fine — layout polish or rewording):

- `Apply`
- `Patch Review`
- `Skip`

### MainSimpleMenuEventErrorIgnoreErrorForm
WF labels: **4** · AV labels: **4** · WF-only: **4** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `このエラーを非表示にしますか？`
- `エラーを非表示にする`
- `キャンセル`
- `非表示にする理由`

AV-only labels (usually fine — layout polish or rewording):

- `Cancel`
- `Do you want to hide this error?`
- `Hide this error`
- `Reason for hiding:`

### OAMSPForm
WF labels: **4** · AV labels: **2** · WF-only: **4** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `名前`
- `選択アドレス:`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `OAM Sprite Editor`

### OtherTextForm
WF labels: **4** · AV labels: **2** · WF-only: **4** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Size:`
- `アドレス`
- `名前`
- `書き込み`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Other Text Strings`

### SkillAssignmentUnitFE8NForm
WF labels: **4** · AV labels: **7** · WF-only: **4** · AV-only: **7** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `スキル1`
- `スキル2`
- `個別スキル`
- `適応`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Personal Skill:`
- `Skill Assignment - Unit (FE8N)`
- `Skill Set 1:`
- `Skill Set 2:`
- `Skill system editors require a compatible skill patch to be installed.
Use the Patch Manager to install a skill system patch first.

Supported skill systems: CSkillSys, FE8N Skill System`
- `Write`

### TextBadCharPopupForm
WF labels: **4** · AV labels: **5** · WF-only: **4** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `Error_MessageLabel`
- `方法1  あきらめる`
- `方法2 Anti-Huffman`
- `方法3 符号テーブル`

AV-only labels (usually fine — layout polish or rewording):

- `Anti-Huffman`
- `Bad Character Detected`
- `Characters that cannot be encoded in the ROM's text encoding table will cause display errors in-game.

Common issues:
  - Using characters outside the ROM's supported character set
  - Pasting text from external sources with special Unicode characters
  - Using control characters that are not valid escape sequences

Please choose one of the options below to resolve the issue.`
- `Encoding Table`
- `Give Up`

### TextRefAddDialogForm
WF labels: **4** · AV labels: **6** · WF-only: **4** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `この文字列の参照を追加しますか？`
- `キャンセル`
- `参照される場所について説明してください`
- `参照の更新`

AV-only labels (usually fine — layout polish or rewording):

- `Add Text Reference`
- `Cancel`
- `Enter reference text...`
- `OK`
- `Reference Text:`
- `Text ID:`

### ToolAutomaticRecoveryROMHeaderForm
WF labels: **4** · AV labels: **3** · WF-only: **4** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ROMヘッダーを自動復旧する`
- `ファイル選択`
- `損傷したROMヘッダー 0x0 - 0x100を自動復帰します。`
- `無改造ROM`

AV-only labels (usually fine — layout polish or rewording):

- `Recover ROM Header`
- `Select File`
- `Unmodified ROM`

### ToolBGMMuteDialogForm
WF labels: **4** · AV labels: **2** · WF-only: **4** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `label1`
- `toggle`
- `このトラックだけを再生する`
- `すべてのトラックを再生する`

AV-only labels (usually fine — layout polish or rewording):

- `Play all tracks`
- `Play only this track`

### ToolUPSPatchSimpleForm
WF labels: **4** · AV labels: **2** · WF-only: **4** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ファイル選択`
- `差分をUPSパッチとして作成する`
- `無改造ROM`
- `現在のデータをUPSパッチとして保存します。
特別な理由がない限り、通常の保存をしたあとで利用してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `UPS Patch Creator`

### ToolWorkSupport_SelectUPSForm
WF labels: **4** · AV labels: **3** · WF-only: **4** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `UPSパッチを開く`
- `UPSパッチを開くために無改造のROMを選択してください`
- `ファイル選択`
- `無改造ROM`

AV-only labels (usually fine — layout polish or rewording):

- `Open UPS Patch`
- `Select File`
- `Vanilla ROM`

### AIASMCoordinateForm
WF labels: **5** · AV labels: **7** · WF-only: **3** · AV-only: **5** · Common: **2**

WF-only labels (candidates for missing fields in AV):

- `00`
- `先頭アドレス`
- `書き込み`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Coordinate Editor`
- `Unused 2:`
- `Unused 3:`
- `Write`

### AIScriptCategorySelectForm
WF labels: **3** · AV labels: **11** · WF-only: **3** · AV-only: **11** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `カテゴリ`
- `命令を選択する`
- `検索`

AV-only labels (usually fine — layout polish or rewording):

- `(select a command)`
- `0x08XXXXXX or hex offset`
- `Address:`
- `AI Script Editor`
- `Close`
- `Commands`
- `Disassemble`
- `Insert Command...`
- `Parameters`
- `Refresh`
- `Write to ROM`

### GraphicsToolPatchMakerForm
WF labels: **3** · AV labels: **9** · WF-only: **3** · AV-only: **9** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ファイルとして保存する`
- `選択されている内容を変更するパッチ`
- `閉じる`

AV-only labels (usually fine — layout polish or rewording):

- `Browse...`
- `Changed bytes: `
- `Close`
- `Compare two ROMs and generate a patch of changed regions`
- `Generate Patch`
- `Modified ROM:`
- `Original ROM:`
- `Regions: `
- `Save Patch File`

### LogForm
WF labels: **3** · AV labels: **2** · WF-only: **3** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `クリップボードへ`
- `ファイルに保存`
- `ログディレクトリを開く`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Log Viewer`

### MainSimpleMenuEventErrorForm
WF labels: **3** · AV labels: **2** · WF-only: **3** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `エラー`
- `再取得(Ctrl+R)`
- `非表示のエラーも表示`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Event Error Display`

### MapEditorMarSizeDialogForm
WF labels: **3** · AV labels: **3** · WF-only: **3** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `データサイズが不一致。
(データ数/2) % 幅 == 0 ではありません`
- `幅`
- `適応`

AV-only labels (usually fine — layout polish or rewording):

- `Apply`
- `Data size mismatch.
(DataCount/2) % Width != 0`
- `Width`

### MoveCostFE6Form
WF labels: **3** · AV labels: **6** · WF-only: **3** · AV-only: **6** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `再取得`
- `書き込み`
- `選択クラスの分離独立`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class Name:`
- `Cost Type:`
- `Move Cost (FE6) Editor`
- `Terrain Move Costs (51 entries):`
- `Write`

### MoveCostForm
WF labels: **3** · AV labels: **4** · WF-only: **3** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `再取得`
- `書き込み`
- `選択クラスの分離独立`

AV-only labels (usually fine — layout polish or rewording):

- `Cost Type:`
- `Move Cost Editor`
- `Terrain Move Costs (65 terrains: 0x00 - 0x40):`
- `Write`

### OpenLastSelectedFileForm
WF labels: **3** · AV labels: **2** · WF-only: **3** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ディレクトリの場所を開く`
- `最後に利用したファイル`
- `関連付けされたアプリケーションで開く`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Open Last Selected File`

### PaletteClipboardForm
WF labels: **3** · AV labels: **8** · WF-only: **3** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `キャンセル`
- `パレット値`
- `変更`

AV-only labels (usually fine — layout polish or rewording):

- `[No palette data in clipboard]`
- `Clear`
- `Clipboard Contents:`
- `Close`
- `Copy Current`
- `Palette Clipboard`
- `Palette Clipboard Manager stores and retrieves palette data.
Copy palettes between different graphics entries or save them for later use.`
- `Paste`

### PatchFormUninstallDialogForm
WF labels: **3** · AV labels: **4** · WF-only: **3** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `アンインストール`
- `パッチを含んでいないROM`
- `ファイル選択`

AV-only labels (usually fine — layout polish or rewording):

- `Please select the ROM from before this patch was installed for recovery.

This feature does not guarantee a reliable uninstallation.
It may fail, so please make a backup beforehand.
Also, while the patch code can be removed, associated data may not always be removable.
In that case, there may be a loss of a few hundred bytes. Please understand.`
- `ROM without patch`
- `Select file`
- `Uninstall`

### ProcsScriptCategorySelectForm
WF labels: **3** · AV labels: **11** · WF-only: **3** · AV-only: **11** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `カテゴリ`
- `命令を選択する`
- `検索`

AV-only labels (usually fine — layout polish or rewording):

- `(select a command)`
- `0x08XXXXXX or hex offset`
- `Address:`
- `Close`
- `Commands`
- `Disassemble`
- `Insert Command...`
- `Parameters`
- `Procs Script Editor`
- `Refresh`
- `Write to ROM`

### SongExchangeForm
WF labels: **3** · AV labels: **2** · WF-only: **3** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `<---------
選択した曲を
移植する
<---------`
- `サウンドテーブル`
- `別ROMを開く`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Song Exchange Tool`

### SongTrackImportSelectInstrumentForm
WF labels: **3** · AV labels: **8** · WF-only: **3** · AV-only: **8** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ディフォルト値から変更しない`
- `楽器セット`
- `選択する`

AV-only labels (usually fine — layout polish or rewording):

- `About Instrument Selection`
- `Address:`
- `Current Instrument Set`
- `Instrument Selection`
- `Instrument set selection is not yet available in the Avalonia UI. To select a different instrument set for MIDI import, please use the WinForms version. This feature requires porting PatchUtil.SearchInstrumentSet to the cross-platform Core library.`
- `No song selected`
- `Not Yet Implemented`
- `This view allows selecting the instrument set (voicegroup) used when importing MIDI or .s files into the ROM. The instrument set determines which GBA sound samples are mapped to MIDI program changes.`

### ToolChangeProjectnameForm
WF labels: **3** · AV labels: **4** · WF-only: **3** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `プロジェクト名の変更する`
- `新しい名前`
- `現在の名前`

AV-only labels (usually fine — layout polish or rewording):

- `Change Project Name`
- `Current Name:`
- `Enter new project name`
- `New Name:`

### ToolClickWriteFloatControlPanelButtonForm
WF labels: **3** · AV labels: **3** · WF-only: **3** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `どちらのボタンをクリックしますか？`
- `変更`
- `新規挿入`

AV-only labels (usually fine — layout polish or rewording):

- `Insert New`
- `Update`
- `❓`

### ToolEmulatorSetupMessageForm
WF labels: **3** · AV labels: **2** · WF-only: **3** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `InitWizardで自動設定する`
- `Option画面から手動で設定する`
- `エミュレータが設定されていません。
動作テストに利用するエミュレータを設定してください。`

AV-only labels (usually fine — layout polish or rewording):

- `Automatically configure with Init Wizard`
- `Manually configure from Options screen`

### ToolRunHintMessageForm
WF labels: **3** · AV labels: **2** · WF-only: **3** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `このメッセージを再度表示しない`
- `これよりテスト実行を開始します。`
- `開始`

AV-only labels (usually fine — layout polish or rewording):

- `Do not show this message again`
- `Start`

### ToolThreeMargeCloseAlertForm
WF labels: **3** · AV labels: **3** · WF-only: **3** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `マージをやめます。マージでの変更点をすべてキャンセルします。`
- `現在の結果で強制終了します。`
- `終了せずに、まだマージ作業を続けます。`

AV-only labels (usually fine — layout polish or rewording):

- `Abort merge. Cancel all merge changes.`
- `Continue merging without closing.`
- `Force close with current results.`

### ToolUndoPopupDialogForm
WF labels: **3** · AV labels: **4** · WF-only: **3** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `このバージョンに戻す`
- `このバージョンをエミュレータでテストプレイ`
- `キャンセル`

AV-only labels (usually fine — layout polish or rewording):

- `Cancel`
- `Revert to This Version`
- `Test Play in Emulator`
- `↩`

### AIASMRangeForm
WF labels: **6** · AV labels: **8** · WF-only: **2** · AV-only: **4** · Common: **4**

WF-only labels (candidates for missing fields in AV):

- `先頭アドレス`
- `書き込み`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `AI Range Editor`
- `Checks if a unit is within the range from (X1,Y1) to (X2,Y2).`
- `Write`

### CStringForm
WF labels: **2** · AV labels: **2** · WF-only: **2** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `書き込み`
- `直接ROMに書き込まれた文字列`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `C-String Editor`

### DumpStructSelectToTextDialogForm
WF labels: **2** · AV labels: **4** · WF-only: **2** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `ファイルに保存する`
- `閉じる`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `Dump Struct to Text`
- `File name: `
- `Save to File...`

### PackedMemorySlotForm
WF labels: **2** · AV labels: **4** · WF-only: **2** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `MESSAGE`
- `適応`

AV-only labels (usually fine — layout polish or rewording):

- ` + `
- `=`
- `Apply`
- `Packed Memory Slot`

### PointerToolBatchInputForm
WF labels: **2** · AV labels: **3** · WF-only: **2** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `一括アドレス変換`
- `値:`

AV-only labels (usually fine — layout polish or rewording):

- `0x08000000
0x08000004
...`
- `Batch Address Convert`
- `Value:`

### ResourceForm
WF labels: **2** · AV labels: **5** · WF-only: **2** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `クリップボードへ`
- `ファイルに保存`

AV-only labels (usually fine — layout polish or rewording):

- `Configuration`
- `Resources`
- `ROM and Configuration Information`
- `ROM Data Sections`
- `ROM Information`

### ToolProblemReportSearchSavForm
WF labels: **3** · AV labels: **2** · WF-only: **2** · AV-only: **1** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `savファイルが含まれていないようです。
問題を確実に再現するためには、savファイルが必要です。
対応するsavがある場合は、パスを指定してください。`
- `参照`

AV-only labels (usually fine — layout polish or rewording):

- `Browse...`

### ToolUseFlagForm
WF labels: **2** · AV labels: **2** · WF-only: **2** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `マップ名`
- `全マップ共通のフラグも表示する`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Flag Usage Viewer`

### ToolWorkSupport_UpdateQuestionDialogForm
WF labels: **2** · AV labels: **3** · WF-only: **2** · AV-only: **3** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `強制アップデート`
- `閉じる`

AV-only labels (usually fine — layout polish or rewording):

- `Close`
- `Force Update`
- `❓`

### UbyteBitFlagForm
WF labels: **2** · AV labels: **11** · WF-only: **2** · AV-only: **11** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `MESSAGE`
- `適応`

AV-only labels (usually fine — layout polish or rewording):

- `Apply`
- `Bit 0 (0x01)`
- `Bit 1 (0x02)`
- `Bit 2 (0x04)`
- `Bit 3 (0x08)`
- `Bit 4 (0x10)`
- `Bit 5 (0x20)`
- `Bit 6 (0x40)`
- `Bit 7 (0x80)`
- `Byte Bit Flags (8-bit)`
- `Hex:`

### UshortBitFlagForm
WF labels: **2** · AV labels: **20** · WF-only: **2** · AV-only: **20** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `MESSAGE`
- `適応`

AV-only labels (usually fine — layout polish or rewording):

- `Apply`
- `Bit 0  (0x01)`
- `Bit 1  (0x02)`
- `Bit 10 (0x04)`
- `Bit 11 (0x08)`
- `Bit 12 (0x10)`
- `Bit 13 (0x20)`
- `Bit 14 (0x40)`
- `Bit 15 (0x80)`
- `Bit 2  (0x04)`
- `Bit 3  (0x08)`
- `Bit 4  (0x10)`
- `Bit 5  (0x20)`
- `Bit 6  (0x40)`
- `Bit 7  (0x80)`
- `Bit 8  (0x01)`
- `Bit 9  (0x02)`
- `High byte:`
- `Low byte:`
- `Short Bit Flags (16-bit)`

### UwordBitFlagForm
WF labels: **2** · AV labels: **38** · WF-only: **2** · AV-only: **38** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `MESSAGE`
- `適応`

AV-only labels (usually fine — layout polish or rewording):

- `Apply`
- `Bit 0  (0x01)`
- `Bit 1  (0x02)`
- `Bit 10 (0x04)`
- `Bit 11 (0x08)`
- `Bit 12 (0x10)`
- `Bit 13 (0x20)`
- `Bit 14 (0x40)`
- `Bit 15 (0x80)`
- `Bit 16 (0x01)`
- `Bit 17 (0x02)`
- `Bit 18 (0x04)`
- `Bit 19 (0x08)`
- `Bit 2  (0x04)`
- `Bit 20 (0x10)`
- `Bit 21 (0x20)`
- `Bit 22 (0x40)`
- `Bit 23 (0x80)`
- `Bit 24 (0x01)`
- `Bit 25 (0x02)`
- `Bit 26 (0x04)`
- `Bit 27 (0x08)`
- `Bit 28 (0x10)`
- `Bit 29 (0x20)`
- `Bit 3  (0x08)`
- `Bit 30 (0x40)`
- `Bit 31 (0x80)`
- `Bit 4  (0x10)`
- `Bit 5  (0x20)`
- `Bit 6  (0x40)`
- `Bit 7  (0x80)`
- `Bit 8  (0x01)`
- `Bit 9  (0x02)`
- `Byte 0:`
- `Byte 1:`
- `Byte 2:`
- `Byte 3:`
- `Word Bit Flags (32-bit)`

### ErrorLongMessageDialogForm
WF labels: **2** · AV labels: **2** · WF-only: **1** · AV-only: **1** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `以下のエラーが発生しました。`

AV-only labels (usually fine — layout polish or rewording):

- `The following error has occurred.`

### EventScriptTemplateForm
WF labels: **1** · AV labels: **2** · WF-only: **1** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `このテンプレートを選択する`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Script Template Browser`

### ProcsScriptForm
WF labels: **1** · AV labels: **2** · WF-only: **1** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `名前`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Procs Script Editor`

### SkillSystemsEffectivenessReworkClassTypeForm
WF labels: **1** · AV labels: **5** · WF-only: **1** · AV-only: **5** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `適応`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Class Type:`
- `Effectiveness Rework - Class Type`
- `Skill system editors require a compatible skill patch to be installed.
Use the Patch Manager to install a skill system patch first.

Supported skill systems: CSkillSys, FE8N Skill System`
- `Write`

### ToolASMEditForm
WF labels: **1** · AV labels: **4** · WF-only: **1** · AV-only: **4** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `書き込む`

AV-only labels (usually fine — layout polish or rewording):

- `ASM Code Editor`
- `Close`
- `Compile`
- `Enter ARM/Thumb ASM code here...`

### ToolAllWorkSupportForm
WF labels: **1** · AV labels: **2** · WF-only: **1** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `更新チェック`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Work Support`

### ToolProblemReportSearchBackupForm
WF labels: **2** · AV labels: **2** · WF-only: **1** · AV-only: **1** · Common: **1**

WF-only labels (candidates for missing fields in AV):

- `参照`

AV-only labels (usually fine — layout polish or rewording):

- `Browse...`

### ToolUnitTalkGroupForm
WF labels: **1** · AV labels: **2** · WF-only: **1** · AV-only: **2** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `会話グループを選択してください`

AV-only labels (usually fine — layout polish or rewording):

- `Address:`
- `Talk Group Editor`

### VersionForm
WF labels: **1** · AV labels: **0** · WF-only: **1** · AV-only: **0** · Common: **0**

WF-only labels (candidates for missing fields in AV):

- `開発者機能: 翻訳`

