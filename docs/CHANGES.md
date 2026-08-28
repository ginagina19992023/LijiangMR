# 漓江回声 · 改动总览（Claude 本轮）

分支:`stage-start-authoring`(A,主) / `spec-chart-b`(B,对比) / 标签 `rollback-t1-t4`(总回退)
所有代码**仅静态检查、未在 Unity 运行**;每条独立提交可 `git revert`。

---

## 一、改了什么(修改现有文件)

| 文件 | 改动 | 对应 |
|---|---|---|
| `Scripts/Stages/StartStageController.cs` | 布局提为 `public static BuildStartScreenLayout`;改成**双模式**:场景有烘焙根节点用场景内容、否则运行时构建,不重复叠加 | T1、T5 |
| `Scripts/Bootstrap/LijiangEchoStageKit.cs` | `AddLayer/AddIcon` 创建时自动挂 `LijiangEchoSpriteLayer`;拆出 `AnchorStageRoot`(定位已有根节点) | T2、T3 |
| `Scripts/LijiangEchoGameController.cs` | 描绘阶段加山背景;描绘线加全程指引+头→尾发光渐变;音符进圆环变发光长条;长按龙头→龙尾消失;新增 Double 音符种类+双击 index(按需求映射) | ②/P5、P1、P2、P3、P4/P6 |

## 二、增加了什么(新文件)

| 文件 | 作用 |
|---|---|
| `Scripts/Stages/LijiangEchoSpriteLayer.cs` | 图层数据组件(哪张图/拟合/层级/透明度),Inspector 可拖图替换 |
| `Scripts/Stages/LijiangEchoMotion.cs` | 动效数据组件(种类/振幅/速度/相位) |
| `Scripts/Editor/LijiangEchoStageBakeTool.cs` | 编辑器工具:菜单 `漓江回声/场景化/` 下 1.捕获基线 2.烘焙场景 3.校验 |
| `docs/GOALS.md` | 目标清单 |
| `docs/VERIFY-CHECKLIST.md` | 手把手验收手册 + Prefab 说明 |
| `docs/CHANGES.md` | 本文件 |
| （B 分支）`LijiangEchoGameController.cs` 内 | 需求 33 音符谱面 + `ExternalUseSpecChart` 开关 + `SelectNoteChart()` |

## 三、还没做什么

- **Prefab 化**(打击对象/点击模式/关卡选择)—— 前置:先把战斗打击对象从"代码生成"提取成实体。**建议我写个提取工具(你说一声)**,再存 Prefab。
- **P4 双击的"真输入判定"**(圆环内快速点两下)—— 当前只做了**视觉区分**,输入仍按单击。要不要真做,待定。
- **双击贴图** 现用鸟纹,需求原意蛙纹 —— 待你定,一行可改。
- **B 谱面是否采用** —— 架构已就绪,是否切换到 B、录制用 A/B,待你定。
- **纹样生成器**(上传图→自动路径+发光)—— Backlog,需你先给真实纹样 PNG(仓库是 LFS 指针拿不到像素)。
- **Quest 3 真机测试 / 最终录制** —— 硬件+人工,只能你做。
- **场景化推广**到其余阶段(卡片→结算→描绘→过场→战斗)—— 试点(Stage_Start)验收通过后再做;过场/战斗需先解决裁切图问题。

## 四、还有哪些没验收(全部——我无 Unity,你还没跑)

**① Stage_Start**
- ☐ Part 0 编译无报错
- ☐ T1 Play 画面零变化 + 捕获基线得 20 图层 + JSON 抽查
- ☐ T2 再捕获 diff 无输出
- ☐ T3 开始/选关回归正常
- ☐ T4 校验先红灯 → 烘焙 20 子物体 → 校验绿灯"完全一致"
- ☐ T5 烘焙后 Play 不重复叠加
- ☐ T6 换图即时生效 / 拖绣球 Play 一致

**②③ 战斗/描绘**
- ☐ ②/P5 描绘阶段山背景补上
- ☐ P1 全程指引线 + 头→尾发光渐变
- ☐ P2 音符进圆环变发光长条
- ☐ P3 长按龙头→龙尾消失(方向对不对)
- ☐ P4/P6 指定秒数出现双击音符(鸟纹)

**B 分支**
- ☐ 切 `UseSpecChartDefault=true` 后 B 谱面手感

> 详细每步操作见 `docs/VERIFY-CHECKLIST.md`。任一步不对贴报错给 Claude。
