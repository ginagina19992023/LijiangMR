# 漓江回声 · 模块地图(工具/配置怎么归类)

一张"从上往下看"的图:全部脚本、菜单、配置归成 **8 大模块**。每块写清**管什么、入口在哪、能不能整合**。
细到"某个工具具体怎么点",看 `TOOLS-GUIDE.md`;战斗制作流程看 `WORKFLOW-BATTLE.md`;真机跑测看 `VR-TESTING.md`。

---

## 1. 启动与流程(场景怎么串起来)
把玩家从开机带到各阶段。
- `Bootstrap.unity`(0号场景,开机进这里)→ `Stage_Start` → `Stage_Select` → `LijiangEchoMR_Main`(旧主场景:过场→描绘→战斗→结算)。
- `LijiangEchoGameFlow`:场景桥接(加载/卸载阶段场景、`EnterLegacyFlow`)。
- `StartStageController` / `SelectStageController`:开始、选关(滚轮)两个独立阶段。
- `LijiangEchoGameController`:旧主场景里**运行时创建**的总控制器,跑过场/描绘/战斗/结算。
  - ⚠️ 它由 `sceneLoaded` 事件在旧主场景加载后创建(修过一次:之前只在进程启动那一次判断,导致进不去关)。
- **整合建议**:过场/描绘/战斗/结算仍挤在 `LijiangEchoGameController` 一个大文件里,是后续最该继续拆场景化的地方(和 Start/Select 一样各自独立)。

## 2. 谱面(音符时间点 + 类型)
决定"什么时候、出哪种音符"。
- 菜单 `漓江回声/谱面/…`:检测拍子生成、贴类型、备份。
- `LijiangEchoChartWindow`(预览窗口)、`LijiangEchoChartGenerator`(从音乐能量包络出拍)。
- 离线扒谱:`tools/lijiang_beatmap.py`(最干净的鼓点)。
- 产物:`Resources/LijiangEchoCharts/chart_level{N}`。
- 四种类型:单击=鱼(Strike)、长按=蛇(Hold)、滑动=蛙(Swipe)、双击=鸟(Double)。

## 3. 纹样 & 圆环 Prefab(音符/圆环长什么样)
"每种音符/中间圆环用哪张图、多大、居中、光晕、反馈脚本"。
- **枢纽工具**:菜单 `漓江回声/纹样/纹样绑定总表` —— 看清+替换每个类型的纹样、绑已有 Prefab、④中间圆环。
- 生成类菜单:生成4个可编辑纹样 Prefab、空占位、左右手、默认圆环、白剪影/原彩色切换。
- 脚本:`LijiangEchoNoteBinderWindow`(总表)、`LijiangEchoNotePrefabTool`(生成)、`LijiangEchoRingFeedback`(圆环反馈基类)+ `LijiangEchoRingHitFlash`(示例子类)。
- 产物:`Resources/LijiangEchoNotes/Note_鱼/鸟/蛇/蛙`、`Ring_Center` 等。
- **整合建议**:生成类菜单(6 个)和"绑定总表"是同一件事的两半,可把生成入口并进总表窗口,统一成一个"纹样/圆环工作台"。

## 4. 战斗选项(不改代码就能切的运行时开关)★镜像功能在这里
一份 `ScriptableObject` 资源集中管所有"战斗表现开关",运行时由总控制器读取。
- 资源:`Resources/LijiangEchoBattleSettings.asset`(脚本 `LijiangEchoBattleSettings`)。
- 入口:菜单 `漓江回声/战斗选项/…`(带 ✓,点一下切)或"选中设置资源"在 Inspector 里改。
- 现有开关:
  - **双击=镜像汇合(左右对飞)** `doubleNoteMirrorConverge`。
  - **★音符按飞入方向自动镜像** `autoMirrorNotesByDirection`(总开关)+ 按类型 `mirrorStrike/Hold/Swipe/Double`(默认只鱼纹)。
    - 规则:纹样默认朝左;从左侧飞入 → 水平镜像朝右(朝飞行方向),从右侧进入保持朝左。
    - 用法:菜单点总开关;想让蛇/蛙/鸟也参与,去"选中设置资源"在 Inspector 勾对应类型。重进战斗生效。
- **这是"战斗表现开关"的统一归口** —— 以后再有类似"某某要不要翻/要不要变"的表现开关,都加到这份资源 + 这个菜单,别散在代码里。

## 5. 场景化 / 布局 / 怪物(把运行时画面烘焙成可编辑场景)
- 菜单 `漓江回声/场景化/…`:捕获画面→烘焙场景、补动效、修手臂关节、怪物共用 Prefab、多关卡布局同步;开始界面烘焙。
- 脚本群:`LijiangEchoSceneBakeTool` / `LijiangEchoStageBakeTool` / `LijiangEchoSceneSplitTool` / `LijiangEchoLayoutSyncTool` / `LijiangEchoMonsterPrefabTool` / `LijiangEchoBattleMotionTool`。
- **整合建议**:这 6 个脚本是同一条"烘焙+同步"流水线,菜单已归在"场景化"下;可加一句流程说明(A→B→动效→同步的顺序)减少误用。

## 6. 音频
- 菜单 `漓江回声/音频/战斗音乐(设置 + 诊断)`(`LijiangEchoBattleMusicWindow`)。
- 导入设置:`LijiangEchoAssetImportSettings`(`battle_music` 走 streaming 等)、`LijiangEchoTexturePostprocessor`。
- 资源:`Resources/LijiangEchoAudio/battle_music`(战斗音乐,当前用它)。

## 7. 调试
- 菜单 `漓江回声/调试/…`:直接进 开始/选关/过场/描绘/战斗(关卡N)/结算,清除跳转标记。
- 脚本:`LijiangEchoDebugMenu`(写 PlayerPrefs 标记 → 进 Play 直接跳该阶段)。
- **编辑器里 Play + 鼠标**即可测流程,不必每次 build(选关滚轮、描绘都做了鼠标兜底;描绘双手时按住 Shift 用鼠标画左手)。

## 8. 材质 / 导入 / XR 装配(底层,一般不碰)
- `LijiangEchoShaderInclude`(打击纹样 shader 加入 Always Included)、`LijiangEchoMrValidation`(MR 校验)、`SetupTrackingSpaceMovement`、`漓江回声/拆分场景/搬迁 XR Rig 到 Bootstrap`。

---

## 一句话整合结论
- **已经归好口的**:战斗表现开关(模块4,镜像功能进了这里)、调试(模块7)。
- **最值得再整合的**:模块3 的"生成纹样 Prefab 菜单"并进"绑定总表"窗口;模块1 的过场/描绘/战斗继续从 `LijiangEchoGameController` 拆成独立阶段场景。
- **加新"表现开关"的规矩**:一律加到 `LijiangEchoBattleSettings` + `战斗选项` 菜单,不要再散写进控制器。
