# 漓江回声 · 场景拆分重构计划(分支 refactor-scene-split)

目标:把 **过场(悬浮过场 + 入关视频)、描绘** 从 4000 行的 `LijiangEchoGameController` 里拆出来,
做成**各自独立的场景 + 独立控制器**,和现有的 `Stage_Start` / `Stage_Select` 一样。以后各改各的、互不污染。

## 铁律(避免重蹈覆辙)
1. **一次只拆一个阶段**,拆完在 Unity 里验通过,再拆下一个。**绝不一次性大改。**
2. **旧路径先留着**:新场景验证通过前,旧的 `LijiangEchoGameController` 里对应阶段代码**不删**,只是不再被走到;验证通过后再摘掉。
3. 每步小提交,可随时回退。
4. 我(Claude)**编译/测试/看美术都做不了**——所以**每一步都要你在 Unity 里 Play 验一次**才算过。

## 分工约定
- **我做(代码)**:新建阶段控制器类(`XxxStageController`),把该阶段逻辑从总控制器搬过去、改成用 `LijiangEchoStageKit`;补齐 StageKit 缺的公共方法(如带像素裁剪的 `AddCroppedSprite`、手柄射线落点缓存等)。
- **你做(Unity 编辑器,我做不了)**:
  1. **建场景**:复制 `Stage_Select.unity` 当模板 → 改名 `Stage_Trace.unity` → 把上面的 `SelectStageController` 换成新的 `TraceStageController`(其余 XR Rig / 相机原样保留)。
  2. **Build Settings**:把新场景加进去,放在正确顺序(见 WORKFLOW-BATTLE 第6章)。
  3. **接流程**:把上一阶段的"进入下一步"改成 `GoToStage("Stage_Trace")`。
  4. **Play 验证**,通过了告诉我,再摘旧代码。

## 现有可复用的地基
- `LijiangEchoGameFlow`:场景桥接(`GoToStage` / `EnterLegacyFlow` / `SelectedLevel`)。已够用。
- `LijiangEchoStageKit`:精灵拼装(`AddLayer/AddIcon/AddLineRenderer`)、手柄输入(`UpdateControllerInput/TryGetControllerHover/TryGetActivePointer/ReadHorizontalStep`)、射线投影(`TryProjectRay`)、动效、音频。**描绘需要但 StageKit 目前没有的**:带像素裁剪的 `AddCroppedSprite`、`GetCroppedSprite`、`GetSpriteVisibleCenter`、单手/某手射线落点——这些要我先补进 StageKit(纯新增,不动旧逻辑)。

---

## 拆分顺序(从前往后拆 —— 已定)

**为什么从前往后**:每拆一个,旧主场景入口就往后挪一格,旧场景一趟只进一次,避免"进两次 + 持久化控制器重入"的麻烦。
运行时顺序:`选关 →【过场:悬浮→视频】→ 描绘 → 战斗 → 结算`。

### 第 1 步:过场(悬浮过场 + 入关视频,合成一个)→ `Stage_Intro`
**合成一个场景**:悬浮和视频是连续的一段过场,视频部分很小(一个 VideoPlayer + 黑底),不单独拆。

- 我做:新建 `IntroStageController`(用 StageKit):搬 `BuildIntroWalkStage`(漂浮山/房子)、悬浮动画、视频段(`VideoPlayer` + 已修好的"播完才进/坏了短黑屏跳过")→ 完了 `EnterLegacyFlow` 让旧主场景**从描绘开始**跑。
- 你做:建 `Stage_Intro.unity`(注意 StreamingAssets 的 `pre_level.mp4` 打包)、Build Settings(放 `Stage_Select` 之后)、接流程(选关确认 → `GoToStage("Stage_Intro")`)。
- 验:选关 →【悬浮播完 + 视频完整播放,不砍断、不长黑】→ 进描绘 → 战斗。
- 收尾:验过后我把旧主场景的过场代码摘掉,旧场景入口改成从"描绘"开始。

### 第 2 步:描绘(Trace)→ `Stage_Trace`
- 我做:StageKit 若缺 `TryGetHandPointer(左/右手落点)` 先补;新建 `TraceStageController`:搬 `BuildTracePath` / `ShowTrace` 视觉 / `UpdateTrace`(单手+双手独立) / 完成 → 让旧主场景**从战斗开始**跑。
- 你做:建 `Stage_Trace.unity`、进 Build Settings(放 `Stage_Intro` 之后)、接流程(过场完 → `GoToStage("Stage_Trace")`)。
- 验:选关 → 过场 → 描绘(单手/双手、完成)→ 战斗。

### 第 3 步(可选,最后):战斗+结算 → `Stage_Battle`
- 最大、最晚。牵扯谱面/音符/圆环/打击/结算,单独一轮干净地做。可暂不拆,先享受前两步的收益。

---

## Tools 整理(与拆场景并行、低风险)
- **保持现状即可的**:战斗选项(`LijiangEchoBattleSettings` + `战斗选项` 菜单)、调试菜单——已归口。
- **建议整合(单独一轮)**:模块3 的"生成纹样 Prefab 的 6 个菜单"并进「纹样绑定总表」窗口,统一成一个"纹样/圆环工作台"。这是纯编辑器工具改动,不影响运行时,风险低,但也要你在 Unity 里点一遍验证。
- 详见 `docs/MODULES.md` 的 8 大模块与整合结论。

---

## 现在的状态
- 分支 `refactor-scene-split` 已从干净的 `battle-visual-hands` 切出(含黑屏修复)。
- 顺序已定:**过场(悬浮+视频合一)先拆 → 描绘 → 战斗**。
- 正在做「第 1 步:过场」的代码(新建 `IntroStageController`,纯新增、不动旧代码,旧过场照常能跑),做完给你 Unity 那几步。
