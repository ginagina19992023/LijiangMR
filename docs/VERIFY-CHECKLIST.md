# 漓江回声 · 完整验收手册(孟苏阳 · 手把手版)

> Claude 代码侧全部完成并推到分支 `stage-start-authoring`。所有代码仅静态检查、未运行。
> 每条候选独立提交,可单独 `git revert`;总回退点 `git checkout rollback-t1-t4`。
> 名词:Hierarchy=左边层级;Inspector=右边属性;Project=下方资源;Scene视图=编辑视图;
> Game视图=运行画面;Console=菜单 Window→General→Console。

---

## Part 0 · 拉代码 + 编译(最关键)
```
git pull
```
等 Unity 右下角编译转圈转完。
- [ ] **Console 无红色报错**（这一步同时验证我全部盲写代码能编译)。有报错贴我原文。

---

## Part 1 · 两个测试入口(先搞清)
项目是两套体系:
| 入口 | 跑什么 | 怎么进 |
|---|---|---|
| **`Bootstrap.unity`** | 新体系:开始/选关(Stage_Start/Stage_Select 场景化) | 打开它 → Play |
| **`LijiangEchoMR_Main.unity`** | 旧体系:过场/描绘/战斗/结算(全程序生成) | **用调试菜单**(见下)一键跳任意段 |

**🎮 调试菜单**(本轮新增):顶部 **`漓江回声 → 调试`** →
`进 开始界面 / 进 选关 / 进 过场 / 进 描绘 / 进 战斗(关卡1/2/3) / 进 结算`
点一下 = 自动开 Main + Play + 直接到那一段。**免走完整流程,单独跑测每一段。**

---

## Part 2 · ① Stage_Start 场景化(T1→T6)

### 编译 + 运行时画面(T1/T5)
- [ ] 打开 `Scenes/Bootstrap.unity` → **▶ Play** → 开始界面与改造前**一模一样**、悬停按钮高亮、扣扳机进选关 → 退 Play(**未烘焙走运行时模式,零变化**)

### 烘焙 + 校验(T2/T3/T4)——**按 2→1→3 顺序**
- [ ] 打开 `Scenes/Stages/Stage_Start.unity`
- [ ] 菜单 **`漓江回声→场景化→2. 烘焙 Stage_Start 场景`** → 预处理会**自动把 20 张贴图导入成 Sprite**(可能卡一下、Console 刷重导入,正常)→ 弹窗应是 **"已烘焙 20/20 个图层"**
      (若弹 "0/20 被跳过" → 把 Console 贴我)
- [ ] Hierarchy 出现 **`开始舞台`** → 展开有 **20 个子物体**;Scene 视图能直接看到画面
- [ ] 菜单 **`1. 捕获 Stage_Start 基线`** → 弹 "已记录 20 个图层"
      (可开 `ValidationCaptures/Baseline_Stage_Start.json` 抽查:`绣球` 的 localScale 不再是 1,1,1、spriteAssetPath 有值)
- [ ] 菜单 **`3. 校验`** → 弹 **"20 个图层与基线完全一致"**(绿灯 = T4 过)
      (若报 "缩放不符/缺少图层" → 基本是顺序/时机问题,把 Console 贴我)
- [ ] 提交烘焙结果 + 变动的贴图 .meta:
      `git add Assets/Scenes/Stages/Stage_Start.unity Assets/Resources/LijiangEchoArt/start Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs.meta && git commit -m "chore: 烘焙 Stage_Start + 贴图转Sprite"`

### 编辑器可视化验收(T6)
- [ ] `Stage_Start.unity` → 选 `开始舞台/开始远山一` → Inspector 的 `Lijiang Echo Sprite Layer` → `Sprite` 换张图 → **Scene 立即变、自动拟合** → Ctrl+Z
- [ ] 选 `绣球` → Scene 里拖到别处 → 开 Bootstrap Play → 绣球在新位置 → 退 → Ctrl+Z → Ctrl+S 保存

---

## Part 3 · ②/P5 描绘阶段背景(层叠远山)
菜单 `漓江回声→调试→进 描绘(关卡1)`:
- [ ] 背景是**层叠远山填满地平线**(远天幕+远山三层+建筑+前山两层+飘云),有前后景深、不再空洞
- [ ] 浓淡/层次不对 → 告诉我调各层 alpha/z(在 ShowTrace 里)

## Part 4 · P1 描绘增强(同一段)
- [ ] 有**全程淡指引线**沿纹样形状
- [ ] 手柄描绘时,已画线**头→尾逐渐发光渐变**
- [ ] 想要**虚线**观感 → 告诉我(需换虚线纹理材质)

## Part 5 · 战斗音符视觉(P2/P3 + 视觉重做)
菜单 `漓江回声→调试→进 战斗(关卡1)`:
- [ ] 音符**变小**了、**黄色发光**
- [ ] 音符**飞向中心原点**,进圆环时**淡出透明度到最低**(P2 重做)
- [ ] **长按**音符按住时纹样**龙头→龙尾逐步消失**(P3);方向反 → 改 `HoldWipeTowardEntrySide`
- [ ] 音符大小不合适 → 改 `NoteSizeScale`;黄色不对 → 改 `(1,0.86,0.2)`

## Part 6 · 音符纹样区分(P4/P6)
战斗中观察:
- [ ] **单击=铜钱纹**、**长按=蛇纹**、**双击=鸟纹**(现状;需求原意单击鱼纹,无鱼纹音符图,你给图我再换)
- [ ] 约 19/41/42/45/49/50/53/64/82/85/89/90/96 秒附近出现**双击(鸟纹)**音符
- [ ] 双击想换**蛙纹** / 要真做"圆环内快速点两下"输入判定 → 告诉我

## Part 7 · B 谱面对比(可选,另一分支)
```
git checkout spec-chart-b
```
- [ ] 改 `LijiangEchoGameController.UseSpecChartDefault` = `true` → 进战斗用 **B 谱面**(需求 33 音符,单/双/长按精确按秒)
- [ ] 对比 A/B 手感,定录制用哪个

---

## 需要你决策(回一句即可)
- [ ] 录制用 **A 谱面** 还是 **B 谱面**
- [ ] 双击贴图 **鸟纹** 还是 **蛙纹**
- [ ] 右下角"待描绘纹样"(静止蛇纹,原有的角落进度指示)要**保留 / 删 / 挪**?
- [ ] 双手镜像描绘开关 —— **要做就说"做镜像开关"**
- [ ] 战斗打击对象 Prefab 工具 —— **要提取哪个**(打击音符 / 圆环 / 带 Collider 的通用打击点),说了我写
- [ ] 每个 .unity 单开就能 Play(自动带测试相机)—— **要就说**,我给场景加自动相机脚本

## 只能你 / 硬件做
- [ ] Prefab 化(等我写提取工具后由你存 Prefab) · Quest 3 真机测试 · 最终录制

## Backlog(有空再做)
- [ ] 纹样生成器(上传纹样图→自动路径+发光)。**需你先发我几张真实纹样 PNG**(仓库里是 LFS 指针拿不到像素)

---
*任一步不对:贴 Console 报错 / 现象 / 截图给 Claude,当条 `git revert` 或直接修。*
