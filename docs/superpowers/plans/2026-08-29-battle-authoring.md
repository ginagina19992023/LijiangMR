# 战斗场景化(拆战斗)实施计划

目标:把"战斗"阶段拆成可在 **Scene 视图不 Play 就能看/改位置** 的场景,和 Stage_Start 一样。
分支:`battle-visual-hands`。作者写代码静态检查,孟苏阳在 Unity 里烘焙/验收。

## 现状与难点
- 战斗在**巨石** `LijiangEchoGameController`(旧体系,只在 `LijiangEchoMR_Main` 里跑),
  不像 Stage_Start 那样在新体系(`LijiangEchoGameFlow` + `LijiangEchoStageKit` + 每阶段一个 MonoBehaviour)。
- 战斗 = **静态舞台背景**(远山/人群/怪物多臂/火焰/祭坛/装饰手/边框)+ **运行时动态**(音符、判定圆环、
  左右挥手、描绘纹样、倒计时、进度/分数)。只有**静态背景**值得拆成可摆位场景;动态部分留运行时。
- Stage_Start 的烘焙工具捕获的是挂了 `LijiangEchoSpriteLayer` 的对象;战斗背景由巨石自己的 `AddLayer`
  生成(普通 SpriteRenderer,**没打标记**),所以现有烘焙工具**不能直接**吃战斗对象。
- 战斗动效用巨石的 `MotionKind.{Monster,Wing,Hand,Flame}`;StageKit 的 MotionKind 目前只有
  `FloatX/FloatY/Pulse` —— 复用 StageKit 需**补齐这几种动效**。

## 分步(每步独立提交、可回退、你在 Unity 验收后再下一步)

- [x] **T1 安全抽取(已做)**:把 ShowBattle 里的静态背景块抽成 `BuildBattleBackground()`,
      视觉零变化。→ 你验收:进战斗,画面与之前**完全一样**(远山/怪物/火焰/边框都在)。

- [x] **T2/T3 通用烘焙工具(已做,`LijiangEchoSceneBakeTool.cs`)**:按孟苏阳要求做成**一个通用工具、
      所有阶段复用**,不为每个场景单独写。两步:
      - `通用A. 捕获当前画面(Play中)`:抓 `漓江回声_关卡画面`(stageRoot)整棵子树,按**路径**记录
        每个图层(位置/缩放/层级/透明度/贴图)→ `ValidationCaptures/SceneBake_Last.json`。按路径记录
        天然区分重名(装饰左手 vs 挥手左手)。
      - `通用B. 烘焙成可编辑场景(退出Play)`:读 JSON → 新建场景 → 按层级重建(挂 SpriteRenderer +
        `LijiangEchoSpriteLayer`、贴图解析回资源)→ 放预览相机 → 弹保存框自己命名(每阶段存一个 .unity)。
      - 限制:静态背景准确;个别裁剪图图层(如"待描绘纹样")会显示整图;运行时白块解析不到贴图→留空节点。

- [ ] **T4 双模式接入**:ShowBattle 里先找「战斗舞台」根;有就用场景里摆好的背景(跳过
      `BuildBattleBackground` 运行时构建),没有就照旧运行时构建(和 StartStageController 一样)。
      → 你在场景里改的位置,进战斗就生效。

- [ ] **T5 动效补齐(可选)**:若烘焙后的背景要保留怪物/火焰动效,给 StageKit MotionKind 补
      `Monster/Wing/Hand/Flame`,并在烘焙场景里给对应对象挂 `LijiangEchoMotion`。

## 决策记录
- 只拆**静态背景**为可摆位场景;音符/挥手/圆环/描绘等动态逻辑**留在巨石运行时**(它们不是"摆位"内容)。
- 先做战斗;描绘/过场/结算后续同法推广。
