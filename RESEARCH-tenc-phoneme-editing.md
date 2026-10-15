# tenc 音素化曲线编辑 — 调研交接文档（2026-10-14）

> 目标：给 tenc 这类表情曲线编辑器加上音素粒度（phoneme-level）操作，类似 TEN 这种传统参数的编辑方式。
> 本文档是纯调研成果，路线 A/B 尚未拍板，新会话从这里接手。

## 结论速览

**"音素化编辑"通路在这个项目里已经存在一半**：vel 等 Numerical 表达式已经能在曲线编辑区里逐音素拖动编辑（`UpdatePhonemeExp`），而 ExpressionGraph 已经打通"音素网格 ⇄ 曲线网格"互转。缺口只在：tenc 是纯 Curve 型 descriptor，音素存储和渲染链没接它。

## 关键事实（文件:行号）

### 数据模型
- `tenc` 注册于 `OpenUtau.Core/Format/USTx.cs:66`：`Curve` 类型，"tension (curve)"，范围 -100..100，默认 0。传统 UTAU 的 TEN 是每音素数值，但这里它只是曲线。
- `UCurve`（`OpenUtau.Core/Ustx/UCurve.cs`）：稀疏点列 `xs/ys`（:13-14），固定 **5 tick 网格** `interval=5`（:10）；`Sample()` 二分+线性插值（:57-67）；`Set()` 拖笔吸附并补锚点（:80-100）；`Simplify()` RDP 压缩（:120）；`ReplaceRange()` 整段替换（:190-242）。.ustx 只序列化 `xs/ys/abbr`，挂在 `UVoicePart.curves`（`Ustx/UPart.cs:48`）。
- 音素级表达式是另一套：`UExpression`（`Ustx/UExpression.cs:121-158`）= 每音素一个值，带 `index`+`value`，存 `UNote.phonemeExpressions`（`UNote.cs:27`）。Numerical 例子：`vel 0-200`（USTx.cs:50）、`vol/atk/dec`（:51-53；ATK 即旧 acc，迁移见 :153-168）。
- `UMaskedCurve`（`UMaskedCurve.cs:23-141`）：同 5-tick 网格、run-length、无默认值；目前仅 RPIT/PITO 用，只被表达式图读取。
- **缺口**：tenc 没有对应 Numerical descriptor，`phonemeExpressions` 存不了它。

### 求值/渲染链
- 音素级：`UPhoneme.GetExpression/SetExpression`（`UPhoneme.cs:223-259`）三级回退 note.phonemeExpressions → phonemizerExpressions → track 默认；`BuildResamplerFlags`（:278-298）汇总成引擎 flags。
- 曲线级：`Render/RenderPhrase.cs:468-514` 按 5-tick 网格把每个 Curve descriptor 采样成 float[]，`:497` `TENC → tension`；若被图驱动则用图输出替代手绘曲线并记入 `drivenCurves`（:478-488, :535）。
- DiffSinger：`DiffSingerRenderer.cs:444-462` 把 `phrase.tension` 与 variance 预测合成后送 ONNX tensor；预测回写见 `Render/RealCurveUpdater.cs:175`。
- 快照层：`Pipeline/PhraseSource.cs` — `PhonemeSource` 每音素解析 VOL/VEL/FLAGS（:273-301）；`PhonemeAnchors` 以音素 position 为 tick 轴构建（:475-481）。

### ExpressionGraph（音素⇄曲线桥已存在）
- 存储 `ExpressionGraph/UExpressionGraph.cs:14-37`（`project.expressionGraphs`，flow-mapping YAML :84-153）；编辑命令 `ExpressionGraphCommands.cs`。
- 节点类型 `GraphNodeTypes.cs:95-360`：输入含 `curve_input / phoneme_input / pitch_input / lfo / note_envelope` 等；输出 `curve_output / phoneme_output / pitch_output`（:371-377）。
- `phoneme_input`：曲线网格上用 `PhonemeAnchors.Sample`（step/linear/cubic，`GraphNodeTypes.cs:159-176`；插值实现 `PhonemeAnchors.cs:31-62` 含 Fritsch–Carlson 单调三次）；音素网格上 `At()` 精确取值（`ExpressionGraphProgram.cs:200-208`）。
- `phoneme_output`：经 `PhraseSource.DrivenPhonemes()`（:543-577）+ `PhonemeSource.WithDriven`（:324-352）覆写每音素值并重算包络/flags。
- 限制：`TimingExpressions {alt, shft, vel}` 图只能读不能驱动（`PhraseSource.cs:420-422`）。

### UI 现状
- 曲线区在 `OpenUtau/Controls/PianoRoll.axaml` Row 5（:23-25），左侧 10 个 `ExpSelector`（:73-92，Alt+数字切换），主绘制控件 `ExpressionCanvas`（背景 :649-655 / 前景 :656-667），曲线工具栏 :668-685 绑 `CurveViewModel.CurveToolIndex`。
- 状态：`NotesViewModel.PrimaryKey/SecondaryKey`（`ViewModels/NotesViewModel.cs:76-77`，默认 VEL :330；`SelectExpressionNotification` :1194-1197）；`PianoRollViewModel.CurveViewModel`（:49,123）。
- 鼠标路径：`PianoRoll.axaml.cs:1172-1240` `ExpCanvasPointerPressed/Moved/Released` → `Views/NoteEditStates.cs`：
  - `ExpSetValueState.UpdateCurveExp`（:835-869，pen/line/eraser 画曲线）
  - **`ExpSetValueState.UpdatePhonemeExp`（:798-834）— 现成的音素化编辑通路**：表达式为 Numerical/Options 时，`NotesViewModelHitTest.HitTestExpRange`（`NotesViewModelHitTest.cs:120-145`）按 tick 命中音素，逐 phoneme 发 `SetPhonemeExpressionCommand`；右键重置 `ResetPhonemeExp`（:919-941）；`ExpressionCanvas.Render`（:300-373）按 `phoneme.position/End` 画横杠/圆点。
  - `CurveSelectionState`（:954-1004）、`CurveTransformState` 及 shift/stretch 子类（:1005-1300，用 `MergedSetCurveCommand`）。
- 撤销命令：曲线型走 `Core/Commands/ExpCommands.cs` 的 `SetCurveCommand:312` / `MergedSetCurveCommand:381` / `PasteCurveCommand:435`；`MaskedCurveCommands.cs` 只针对 UMaskedCurve（SetMaskedCurve:45 / Values:58 / Clear:70 / Replace:82）。音素数值：`SetPhonemeExpressionCommand:89`、`SetNotesSameExpressionCommand:52`。
- 数字面板：`NotePropertiesControl.axaml:197` `ExpressionsPanel` → `NotePropertiesViewModel.cs:230-241`（只收 Numerical/Options）、:316 读 `note.phonemeExpressions`、提交 :683。
- 坐标：`NotesViewModel.PointToTick:452-454`、`TickToneToPoint:486-490`；选择/画线吸附 `Project.resolution*4/SnapDiv`（NoteEditStates :971-975, :988-992）。
- `ExpressionsDialog`：`ExpressionsViewModel.cs` `ExpressionBuilder:15-126`（Name/Abbr/类型/Min/Max/默认值/flag/skipOutputIfDefault），`Build():106-123`；类型下拉 `ExpressionsDialog.axaml:67-71`（numerical/options/curve/maskedcurve）。全仓库无 curvemask；全库无 phonemizeOFF。
- `ExpressionGraphEditor.axaml.cs:17-292`：节点图对话框，可参考其命令提交/订阅刷新模式，但曲线绘制参考应直接用 `ExpressionCanvas`。

## 两条路线

### 路线 A（改动小，先行推荐）：tenc 保持 Curve，编辑器加"音素模式"
画曲线时把笔迹按音素边界 snap / 每音素一段常值，用现成 `UCurve.ReplaceRange/Set` + `MergedSetCurveCommand` 提交。
- 触碰：`NoteEditStates.cs`、`ExpressionCanvas.cs`、`CurveViewModel.cs`、`PianoRoll.axaml(.cs)`。
- .ustx 零变更、完全向后兼容，渲染链（RenderPhrase/采样）不动。
- 依赖：音素边界只有运行时 `UPart.phonemes` 里有（phonemizer 产物，不落盘）。

### 路线 B（数据模型变更）：让 tenc 可按音素存值
注册 Numerical 音素表达式（如 `tenp`），或允许 Curve 型 descriptor 进 `phonemeExpressions`。
- 编辑直接复用 `UpdatePhonemeExp` + 数字面板，通路现成。
- 但必须改渲染侧：`RenderPhrase.cs:468-514`（:497 TENC 采样处）改成"有音素值→`PhonemeAnchors` 插值成 tension，否则手绘曲线"；并定义与图驱动 `drivenCurves`（:478）的优先级。
- .ustx 新字段兼容性、`GetExpression` 回退链都要考虑。

## 备注
- 本仓库只有 4 个分支，开发直接在 master；feature 分支建议 `feat/phonemized-curve-editing`。
- 仓库在 `D:\Download\Code\openutau`（WSL 路径 `/mnt/d/Download/Code/openutau`，9p 慢，优先 Grep 定位再读）。
- 测试参考：`OpenUtau.Test/Core/USTx/UCurveTest.cs`、`ExpressionGraphTest.cs`、`OpenUtau.UiTest/ExpressionGraphEditorTest.cs`。
