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

## 拆分顺序(最低风险优先)

### 第 1 步:描绘(Trace)→ `Stage_Trace`
**为什么先拆它**:输入输出清晰(选关→描绘→战斗),状态相对自成一体;不涉及视频。

- 我做:
  1. StageKit 补齐:`AddCroppedSprite / GetCroppedSprite / GetSpriteVisibleCenter`、`TryGetHandPointer(左/右手落点)`(从总控制器搬,纯新增)。
  2. 新建 `TraceStageController`:搬 `BuildTracePath` / `ShowTrace` 视觉 / `UpdateTrace`(单手+双手独立) / 完成判定 → `GoToStage` 进战斗(或先桥回旧流程的战斗)。
- 你做:建 `Stage_Trace.unity`、进 Build Settings、把"选关确认后"改成先进 `Stage_Trace`(而不是直接进旧主场景的过场)。
- 验:选关 → 描绘(单手/双手、居中、完成)→ 进战斗。

### 第 2 步:过场(悬浮过场 + 入关视频)→ `Stage_Intro`
- 我做:新建 `IntroStageController`:搬 `BuildIntroWalkStage`(漂浮山/房子)、视频段(`VideoPlayer` + 刚修好的"播完才进关/坏了短黑屏跳过")→ 进描绘。
- 你做:建 `Stage_Intro.unity`(注意 StreamingAssets 的 `pre_level.mp4` 打包)、Build Settings、接流程(选关→过场→描绘)。
- 验:选关 → 过场(悬浮播完 + 视频完整播放,不砍断、不长黑)→ 描绘。

### 第 3 步(可选,最后):战斗(Battle)→ `Stage_Battle`
- 最大、最晚。战斗牵扯谱面/音符/圆环/打击/结算,单独一轮干净地做。可暂不拆,先享受前两步的收益。

---

## Tools 整理(与拆场景并行、低风险)
- **保持现状即可的**:战斗选项(`LijiangEchoBattleSettings` + `战斗选项` 菜单)、调试菜单——已归口。
- **建议整合(单独一轮)**:模块3 的"生成纹样 Prefab 的 6 个菜单"并进「纹样绑定总表」窗口,统一成一个"纹样/圆环工作台"。这是纯编辑器工具改动,不影响运行时,风险低,但也要你在 Unity 里点一遍验证。
- 详见 `docs/MODULES.md` 的 8 大模块与整合结论。

---

## 现在的状态
- 分支 `refactor-scene-split` 已从干净的 `battle-visual-hands` 切出(含黑屏修复)。
- 本文件 = 计划。**代码还没开始搬**——等你确认从「第 1 步:描绘」开始,我就先做「StageKit 补齐 + 新建 TraceStageController」这部分(纯新增,不动旧代码,旧描绘照常能跑),做完给你 Unity 那几步。
