# 漓江回声 · 完整审核清单(孟苏阳在 Unity 里逐项验)

Claude 代码侧全部完成并推送。Claude 本机无 Unity,**所有候选仅静态检查、未运行**。
每条候选独立提交,可单独 `git revert`。

## 分支
| 分支 | 内容 | 作用 |
|---|---|---|
| `stage-start-authoring` @6be9231 | 全部候选 + 双击(A 近似映射) | **主验收分支** |
| `spec-chart-b` @fe3d169 | 需求 33 音符谱面 + A/B 运行时切换 | 对比用(可选) |
| 标签 `rollback-t1-t4` | T1–T4 干净点 | 总回退点 |

---

## Part 0 · 拉代码 + 编译(最关键的一次)
```bash
cd /d/GitHub/LijiangMR
git fetch origin
git checkout stage-start-authoring
git pull
```
- [ ] Unity 编译完成,**Console 无红色报错**（这一步同时验证我全部盲写代码能编译）

有报错 → 贴原文给我,立刻改。

---

## Part 1 · ① Stage_Start 场景化(T1–T6)
- [ ] **T1** Bootstrap 进 Play,开始界面与改造前**一模一样**,悬停按钮高亮,扣扳机进选关 → 退 Play
- [ ] **T1** 菜单 `漓江回声→场景化→1. 捕获基线` → 弹 **20 个图层**,生成 `ValidationCaptures/Baseline_Stage_Start.json`
- [ ] **T1** 抽查 JSON:`开始界面底框` sortingOrder=-20/alpha=0.04;`绣球`→`start/embroidered_ball.png`;`开始后云一` FloatX/0.55
- [ ] **T2** 备份基线→再捕获一次→`diff` 两份**无输出**(组件未改数值)
- [ ] **T3** Bootstrap 进 Play,开始/选关位置正常(纯拆分,零变化)
- [ ] **T4** 打开 `Scenes/Stages/Stage_Start.unity`→菜单 `3. 校验`→应报"找不到开始舞台节点"(预期红灯)
- [ ] **T4** 菜单 `2. 烘焙 Stage_Start 场景`→生成 20 子物体,Scene 视图见画面
- [ ] **T4** 菜单 `3. 校验`→弹 **"20 个图层与基线完全一致"**
- [ ] **T5** 烘焙后进 Play → 画面正常且**不重复叠加**(双模式生效)
- [ ] **T6** 换图:选 `开始舞台/开始远山一`,SpriteLayer 换 Sprite→Scene 即时变、自动拟合
- [ ] **T6** 拖动 `开始舞台/绣球`→进 Play 位置一致
- [ ] 提交烘焙场景:`git add Assets/Scenes/Stages/Stage_Start.unity Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs.meta && git commit -m "chore: 烘焙 Stage_Start"`

## Part 2 · ②/P5 背景 + P1 描绘增强(进游戏到"描绘/绘制纹样"段)
- [ ] **②/P5** 山背景(`ui/mountain_background`)铺在最底层,画面不再空洞
      （若有需求里"原本那张"指定背景图,告诉我文件名,一行替换)
- [ ] **P1** 全程**淡指引线**沿纹样形状显示
- [ ] **P1** 手柄描绘时,已画线**头→尾发光渐变**(纹样逐渐点亮)
- [ ] 指引线要**虚线**观感? → 需换虚线纹理材质,告诉我

## Part 3 · P2 + P3 战斗阶段
- [ ] **P2** 音符进中心圆环时**拉宽成发光长条**高亮(幅度看着对不对,常量 `RingBarWiden` 可调)
- [ ] **P3** 长按音符按住时,纹样**龙头→龙尾逐步消失**(方向对不对,常量 `HoldWipeTowardEntrySide` 可翻转)

## Part 4 · P4/P6 双击音符(A 近似映射,commit 6be9231)
- [ ] 战斗中约 **19/41/42/45/49/50/53/64/82/85/89/90/96 秒**附近出现**双击音符**(当前用**鸟纹**与单击区分)
- [ ] 双击贴图要用**蛙纹**(需求原意)还是鸟纹? → 告诉我,SpawnDueNotes 里 Double 分支一行改
- [ ] 首尾几个双击时间偏差 1~2.3s(A 谱面 108 音符与需求 33 音符不一一对应所致)——能接受吗?
- [ ] 需要"圆环内快速点两下"的**真双击输入判定**吗?(当前仅视觉,按单击命中)

## Part 5 · B 对比分支(可选,spec-chart-b)
```bash
git checkout spec-chart-b
```
- [ ] 把 `LijiangEchoGameController.UseSpecChartDefault` 改 `true`(或运行时设 `ExternalUseSpecChart=true`)→ 进战斗用 **B 谱面**(需求 33 音符,单击/双击/长按精确按秒)
- [ ] 与 A 对比手感/密度,决定录制用 A 还是 B
- [ ] 要我把"调试键切 A/B"或"对比入口场景"写出来? → 告诉我用哪个键/怎么进

---

## 需要你决策(我等你一句话)
- [ ] 录制用 **A** 还是 **B** 谱面
- [ ] 双击贴图:**鸟纹 / 蛙纹**
- [ ] 纹样玩法现在改还是决赛后(颜仪晖倾向先不大改)

## 只能你 / 硬件做
- [ ] Prefab 化(打击对象 / 点击模式 / 关卡选择)
- [ ] 连 Quest 3 真机测试
- [ ] 最终录制

## Backlog(你说"有空再做")
- [ ] 纹样生成器(上传纹样图→自动轮廓/路径→绘制发光)。**需你先给我几张真实纹样 PNG**(仓库这边是 LFS 指针拿不到像素)。

---
*任何一条不对:贴 Console 报错 / 现象 / 截图给 Claude,当条 `git revert` 或直接修。*
