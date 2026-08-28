# 漓江回声 · 验收确认清单(孟苏阳在 Unity 里跑)

Claude 代码侧已全部完成并推到分支 `stage-start-authoring`(12 个提交)。Claude 本机无 Unity,
**所有候选均只做过静态检查、未运行**。下面是**需要你在 Unity 里确认的项 + 步骤**。
每条候选独立提交,可单独 `git revert`;总回退点:`git checkout rollback-t1-t4`。

---

## 0. 拉代码 + 编译(最关键的一次确认)
```bash
cd /d/GitHub/LijiangMR
git fetch origin
git checkout stage-start-authoring
git pull
```
打开 Unity,等编译完成。
- [ ] **Console 没有红色报错**（这一步同时确认我全部盲写代码能编译——我这边确认不了,只有你能）

若有报错,把报错原文贴给我,我立刻改。

---

## 1. ① Stage_Start 场景化(T1–T6)
- [ ] 打开 `Scenes/Bootstrap.unity` 进 Play → 开始界面与以前**一模一样**,悬停按钮高亮,扣扳机进选关 → 退 Play（**未烘焙走运行时模式,应零变化**）
- [ ] 菜单 `漓江回声 → 场景化 → 1. 捕获 Stage_Start 基线` → 弹 **20 个图层**,生成 `ValidationCaptures/Baseline_Stage_Start.json`
- [ ] 备份基线后**再捕获一次**,`diff` 两份应**无输出**（确认 T2 组件没改变数值）
- [ ] 打开 `Scenes/Stages/Stage_Start.unity`,菜单 `3. 校验` → 应报"找不到开始舞台节点"（预期红灯）
- [ ] 菜单 `2. 烘焙 Stage_Start 场景` → 生成 20 子物体,Scene 视图能看到画面
- [ ] 菜单 `3. 校验` → 应弹 **"20 个图层与基线完全一致"**
- [ ] 烘焙后再进 Play → 画面正常且**不重复叠加**（双模式生效）
- [ ] 换图验收:选中 `开始舞台/开始远山一`,LijiangEchoSpriteLayer 换张 Sprite → Scene 立即变、自动拟合
- [ ] 提交烘焙结果:`git add Assets/Scenes/Stages/Stage_Start.unity Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs.meta && git commit -m "chore: 烘焙 Stage_Start"`

## 2. ②/P5 + P1 描绘阶段(进游戏走到"描绘/绘制纹样"那段)
- [ ] **山背景**已铺在最底层,画面不再空洞（如需换成指定那张背景图,告诉我文件名）
- [ ] **全程淡指引线**沿纹样形状显示(P1)
- [ ] 手柄描绘时,**已画的线沿方向头→尾发光渐变**(纹样逐渐点亮)
- [ ] 若指引线要做成**虚线**观感,告诉我(需换一张虚线纹理材质)

## 3. P2 + P3 战斗阶段
- [ ] 音符飞入中心圆环时,**进环后拉宽成发光长条**高亮(P2)——幅度看着对不对(常量 `RingBarWiden` 可调)
- [ ] **长按音符**按住时,纹样**从龙头→龙尾逐步消失**(P3)——方向对不对(常量 `HoldWipeTowardEntrySide` 可翻转)

---

## 需要你提供的数据 / 决策(我这边接好了管道,等你一句话)
- [ ] **P4/P6 双击音符**:往 `LijiangEchoGameController.doubleNoteIndices` 填哪些音符 index → 双击即以鸟纹显示、与单击区分。默认空=现状不变。
  - 要换双击纹样贴图 / 要真做"圆环内快速点两下"输入判定 → 告诉我
- [ ] **纹样玩法是否现在改**:颜仪晖倾向临近 8/20 先不大改,你定现在上还是决赛后

## 只能你 / 硬件做的里程碑
- [ ] Prefab 化(打击对象 / 点击模式 / 关卡选择)
- [ ] 连 Quest 3 真机测试
- [ ] 最终录制(原定 8/20 已过,与团队重定)

## Backlog(你说"有空再做")
- [ ] 纹样生成器(上传纹样图 → 自动轮廓/路径 → 绘制发光)。**需要你先给我几张真实纹样 PNG**(仓库这边是 LFS 指针,拿不到像素),算法我用代码做,不需 AI 生图。

---
*任何一条不对,把 Console 报错 / 现象 / 截图贴给 Claude,当条 `git revert` 或直接修。*
