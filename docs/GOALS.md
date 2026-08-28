# 漓江回声 MR · Goal 清单与 Stage_Start 验收 Runbook

> 负责人:**孟苏阳**(凡涉及 Unity 均由你完成)· 目标设备 Quest 3 · 更新于 2026-08-28
> 状态图例:✅ 已完成 · 🔵 代码已推待你 Unity 验收 · 🟡 进行中 · 🟣 已决策待制作 · ⚪ 待办

分工:**Claude 出代码 → 推分支;你在 Unity 逐任务验收**。Unity 编辑器操作按计划约束只能人在 GUI 手动执行(本机 headless 授权不可用)。

---

## ① Stage_Start 场景化改造 〔代码 · 进行中〕

把开始界面 20 个美术图层从「运行时代码硬生成」→「预先摆进 `Stage_Start.unity`、编辑器可拖拽替换」。**硬性要求:视觉零变化。** 全套 7 关场景化的试点。

分支:`stage-start-authoring`(T1–T4 代码已推,4 个提交)。

- 🔵 **T1** 提取布局为静态方法 + 基线捕获工具(改 `StartStageController`、新增 `LijiangEchoStageBakeTool`)
- 🔵 **T2** 两个数据组件 `LijiangEchoSpriteLayer` / `LijiangEchoMotion` + StageKit 挂载
- 🔵 **T3** StageKit 拆出 `AnchorStageRoot`,支持锚定场景已有舞台根节点
- 🔵 **T4** 烘焙工具(菜单 2. 烘焙 / 3. 校验)——工具代码已推,**烘焙写入场景需你在 Unity 里执行**
- ⚪ **T5** `StartStageController` 瘦身、移除一次性命令 —— **须等你跑完 T4 烘焙**(它会删掉捕获/烘焙命令,不能提前),你烘焙好回我,我立刻写并推
- ⚪ **T6** 人工验收:不进 Play 能看到画面 / 换图即时生效 / 拖动绣球 Play 一致
- ▸ 试点通过后推广(成本从低到高):**卡片 → 结算 → 描绘 → 过场 → 战斗**(过场/战斗需先解决裁切图问题)

### Stage_Start 验收 Runbook(你在 Unity 里按顺序跑)

**拉代码**
```bash
cd /d/GitHub/LijiangMR
git fetch origin
git checkout stage-start-authoring
```
打开 Unity,等编译完成,**Console 不能有红色报错**。

**① T1 基线**
1. 打开 `Scenes/Bootstrap.unity` 进 Play → 开始界面与以前一模一样,手柄悬停按钮高亮,扣扳机进选关 → 退 Play
2. 菜单 `漓江回声 → 场景化 → 1. 捕获 Stage_Start 基线` → 应为 **20 个图层**,生成 `ValidationCaptures/Baseline_Stage_Start.json`
3. 抽查 JSON:`开始界面底框` sortingOrder=-20 / alpha=0.04;`绣球` → `.../start/embroidered_ball.png`;`开始后云一` motionKind=FloatX / motionSpeed=0.55

**② T2 组件(确认外观没变)**
```bash
cp ValidationCaptures/Baseline_Stage_Start.json ValidationCaptures/Baseline_Stage_Start.before.json
```
再点 `1. 捕获基线`,然后 `diff` 两文件 → **应无任何输出**。确认后 `rm` 掉 `.before.json`。

**③ T3(纯拆分,回归)**
Bootstrap 进 Play,开始界面 + 选关都正常 → 退 Play。

**④ T4 烘焙 + 校验**
1. 打开 `Scenes/Stages/Stage_Start.unity`,菜单 `3. 校验` → 应报「找不到开始舞台节点」(预期红灯)
2. 菜单 `2. 烘焙 Stage_Start 场景` → 确认 → 生成 20 个子物体,Scene 视图能看到画面
3. 菜单 `3. 校验` → 应弹 **「20 个图层与基线完全一致」**
4. 提交场景:
   ```bash
   git add Assets/Scenes/Stages/Stage_Start.unity Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs.meta
   git commit -m "chore: 烘焙 Stage_Start 场景"
   ```
5. **回 Claude 一声「烘焙好了」** → Claude 写并推 T5。

哪一步红灯/对不上,把 Console 报错或 diff 结果贴给 Claude 判断修还是回退。

---

## ② 关卡二背景补全 〔美术/场景 · 你〕

- ⚪ **B1** 把「山」背景图加到关卡二场景**最底层**(来源:美术包 UI 界面第二排最后一个;具体山体素材位置你已指明)。修复画面空洞。

---

## ③ 核心纹样玩法呈现 〔玩法 · Unity 实现你,美术方向颜仪晖〕

**已敲定的决策**

- 🟣 **P1** 打击方案 = **方案一:双手对称打击画完整图案**,加**侧边虚线**引导双手绘制(方案二「点阵依次击打」不作主玩法)。检测逻辑已支持按顺序击打点阵。
- 🟣 **P2** 纹样从两侧**移入中心圆环内发光显示**,进入圆环后显示为**发光长条**。
- 🟣 **P3** 长按动画:**龙头 → 龙尾逐步消失**(按住后从起点渐消至尾部)。

**当前视频缺、待制作**

- ⚪ **P4** 补**纹样**(单击 / 双击视觉)
- ⚪ **P5** 补**背景**(与 ② 联动)
- ⚪ **P6** 做出**双击 / 长按**的视觉区分

---

## ◆ 里程碑与交付

- ⚪ **Prefab 化** — 打击对象 / 点击模式 / 关卡选择封装成 Prefab(你)
- ⚪ **真机测试** — Prefab 完成后连 Quest 3 测试(你)
- ⚠️ **最终录制** — 原定 8/20 **已过,需与团队重定**

---

*每完成一项,把对应状态标记从 ⚪/🔵 改成 ✅。此文件由 Claude 与孟苏阳共同维护。*
