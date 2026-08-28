# 漓江回声 · 完整验收手册(孟苏阳 · 详细操作版)

> 分支 `stage-start-authoring`。所有代码仅静态检查、未运行。每条独立提交可 `git revert`;
> 总回退点 `git checkout rollback-t1-t4`。
> 面板名词:Hierarchy=左边层级 · Inspector=右边属性 · Project=下方资源 · Scene视图=编辑视图 ·
> Game视图=运行画面 · Console=菜单 Window→General→Console。

---

## Part 0 · 拉代码 + 编译(最关键)
1. 在 `D:\GitHub\LijiangMR` 里:`git pull`(网络需 git 代理已配好)。
2. 打开 Unity,等右下角编译转圈转完。
3. 菜单 Window→General→Console → Clear。
- [ ] **Console 无红色报错**(= 我全部盲写代码能编译)。有报错 → 复制整行发我。

---

## Part 1 · 两个测试入口 + 调试菜单
项目是两套体系:
| 入口 | 跑什么 |
|---|---|
| Play `Scenes/Bootstrap.unity` | 新体系:开始 / 选关(Stage_Start / Stage_Select 场景) |
| `Scenes/LijiangEchoMR_Main.unity` | 旧体系:过场 / 描绘 / 战斗 / 结算(全程序生成) |

**战斗只在 LijiangEchoMR_Main 里跑。要单独测某一段用调试菜单:**
- 顶部菜单 **`漓江回声 → 调试`** → 点 `进 开始界面 / 进 选关 / 进 过场 / 进 描绘 / 进 战斗(关卡1/2/3) / 进 结算`
- 点一下 = **自动打开主场景 + 进 Play + 直接跳到那一段**,免走完整流程。
- [ ] 随便点个「进 战斗(关卡1)」→ 能直接进战斗

---

## Part 2 · ① Stage_Start 场景化(T1→T6)

### T1 运行时零变化
1. Project 进 `Scenes/`,双击 `Bootstrap.unity`。
2. 点顶部 ▶ 播放。
- [ ] 开始界面与改造前**一模一样**,悬停按钮高亮,扣扳机进选关。退 Play。

### T4 烘焙 + 校验(**严格按 2→1→3 顺序**)
3. Project 进 `Scenes/Stages/`,双击 `Stage_Start.unity`。
4. 菜单 **`漓江回声→场景化→2. 烘焙 Stage_Start 场景`** → 确认。
   - 会自动把 20 张贴图导入成 Sprite(Console 可能刷重导入,正常)。
   - [ ] 弹窗 **"已烘焙 20/20 个图层"**(若 "0/20 被跳过" → 贴 Console 给我)
   - [ ] Hierarchy 出现 `开始舞台`,展开有 **20 个子物体**;Scene 视图能看到画面
5. 菜单 **`1. 捕获 Stage_Start 基线`** → 弹 "已记录 20 个图层"。
6. 菜单 **`3. 校验`** → [ ] 弹 **"20 个图层与基线完全一致"**(绿灯 = T4 过)
   - 若报"缩放不符/缺少图层" → 多半重跑一次 2→1→3;还不行贴 Console。
7. 提交:`git add Assets/Scenes/Stages/Stage_Start.unity Assets/Resources/LijiangEchoArt/start Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs.meta && git commit -m "chore: 烘焙Stage_Start+贴图转Sprite"`

### T6 编辑器可视化
8. Stage_Start.unity,Hierarchy 选 `开始舞台/开始远山一` → Inspector 的 `Lijiang Echo Sprite Layer` → `Sprite` 换张图。
- [ ] Scene 立即变、自动拟合 → Ctrl+Z
9. 选 `绣球` → Scene 里拖到别处 → 开 Bootstrap ▶ Play → 绣球在新位置 → 退 → Ctrl+Z → Ctrl+S。

---

## Part 3 · 描绘阶段(菜单 调试→进 描绘)
- [ ] **②背景**:层叠远山填满地平线,有前后景深、不再空洞
- [ ] **P1**:全程淡指引线 + 已画线头→尾发光渐变
- 浓淡/层次要调 → 告诉我改 ShowTrace 里的 alpha/z

## Part 4 · 战斗音符视觉(菜单 调试→进 战斗(关卡1))
- [ ] 音符**变小 + 黄色发光**
- [ ] 音符**飞向中心原点**,进圆环时**淡出到最低**(P2)
- [ ] **长按**音符按住时纹样**龙头→龙尾逐步消失**(P3);方向反 → 改常量 `HoldWipeTowardEntrySide`
- [ ] 纹样区分:单击=铜钱纹 · 长按=蛇纹 · 双击=鸟纹
- 大小/黄色不合适 → 改常量 `NoteSizeScale` / 黄色值 `(1,0.86,0.2)`

---

## Part 5 · 谱面:从音乐生成(★ 核心,新增)
谱面 = 一张"时间点→打击方式"的表,时间点根据音乐拍子来。工具在 **`漓江回声 → 谱面`** 菜单。

**前置**:选中 `Assets/Resources/LijiangEchoAudio/battle_music`,Inspector 里
**Load Type 设 Decompress On Load**(否则读不到采样)→ Apply。

1. 菜单 **`漓江回声→谱面→1. 从音乐检测拍子生成谱面`**
   - [ ] 弹窗显示"检测到 N 个拍子点",生成 `Assets/Resources/LijiangEchoCharts/chart_generated.txt`
   - [ ] 打开该 txt,看时间点是不是大致贴着音乐节奏
   - 点**太多/太少** → 打开 `Assets/Scripts/Editor/LijiangEchoChartGenerator.cs`,改顶部
     `Sensitivity`(大=点少)/ `MinGapSeconds`(大=点稀)→ 存 → 再跑菜单 1
2. 菜单 **`2. 把需求类型贴到最近拍子`**
   - 把需求表 `chart_liusanjie.txt`(依据文档的单/双/长按)吸附到检测点上
   - [ ] 弹窗显示贴了几个类型;chart_generated.txt 里出现 single/double/hold
3. **说明**:目前战斗仍用代码里的 108 音符 `noteTimes`。要让战斗**改用这张生成的谱面**,
   需要我再写一步"读表驱动战斗"(把 chart_generated.txt 装载进 noteTimes)。**要接上告诉我。**

## Part 6 · 打击点 Prefab(特殊/例外打击用)
主玩法走上面的表格;个别特殊打击可手摆打击点。
1. 菜单 **`漓江回声→打击点→生成「打击点」Prefab 模板`** → 生成 `Assets/Prefabs/打击点.prefab`
2. 把它拖进场景 → Inspector 的 `Lijiang Hit Point`:指定 Sprite、类型(单/双/长)、位置
- [ ] 能拖进场景、能指定纹样和位置

## Part 7 · B 谱面对比(可选,另一分支)
`git checkout spec-chart-b` → 改 `UseSpecChartDefault=true` → 进战斗用需求 33 音符谱面,对比手感。

---

## 需要你决策 / 我待做(回一句即可)
- [ ] **战斗接上生成谱面** —— 要不要我写"读 chart_generated.txt 驱动战斗"这一步(把音乐生成的谱面真正用起来)
- [ ] 双击贴图 **鸟纹 / 蛙纹**
- [ ] 右下角"待描绘纹样"(静止蛇纹)**保留 / 删 / 挪**
- [ ] 双手镜像描绘开关 —— 要做说"做镜像开关"
- [ ] 中心圆环也做成 Prefab?(用途不大)
- [ ] 每个 .unity 单开就能 Play(自动带测试相机)—— 要就说

## 只能你 / 硬件做
- [ ] Prefab 化 · Quest 3 真机测试 · 最终录制

---
*任一步不对:贴 Console 报错 / 现象 / 截图给 Claude,当条 `git revert` 或直接修。*
