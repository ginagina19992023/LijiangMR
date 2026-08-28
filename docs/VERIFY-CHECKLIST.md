# 漓江回声 · 完整审核手册(孟苏阳 · 手把手最新版)

> 分支 `stage-start-authoring`。所有代码仅静态检查、未运行。每条独立提交可 `git revert`;
> 总回退 `git checkout rollback-t1-t4`。
> 面板:Hierarchy=左层级 · Inspector=右属性 · Project=下资源 · Scene视图=编辑视图 ·
> Game视图=运行画面 · Console=菜单 Window→General→Console。

---

## Part 0 · 拉代码 + 编译
1. `git pull`(需 git 代理已配好)
2. 打开 Unity,等右下角编译转圈转完
3. 打开 Console,Clear
- [ ] **无红色报错**（=我全部代码能编译)。有报错复制整行发我。

## Part 1 · 两个入口 + 调试菜单
- Play `Scenes/Bootstrap.unity` = 新体系(开始/选关场景化)
- `Scenes/LijiangEchoMR_Main.unity` = 旧体系(过场/描绘/战斗/结算)
- **战斗只在 Main 里跑。单独测某段用菜单 `漓江回声→调试`**:
  进 开始/选关/过场/描绘/战斗(关卡1/2/3)/结算 → 一键自动开主场景+Play+跳到那段。

---

## Part 2 · ① Stage_Start 场景化(T1→T6)

**T1 运行时零变化**
1. 双击 `Scenes/Bootstrap.unity` → ▶ Play
- [ ] 开始界面与改造前一模一样、悬停高亮、扣扳机进选关 → 退 Play

**T4 烘焙+校验(严格 2→1→3 顺序)**
2. 双击 `Scenes/Stages/Stage_Start.unity`
3. 菜单 `漓江回声→场景化→2. 烘焙 Stage_Start 场景` → 确认
   - 会自动把贴图转 Sprite(Console 可能刷重导入,正常)
   - [ ] 弹窗 **"已烘焙 20/20 个图层"**(若 0/20 → 贴 Console)
   - [ ] Hierarchy 出现 `开始舞台` 展开 20 子物体;Scene 视图见画面
4. 菜单 `1. 捕获 Stage_Start 基线` → "已记录 20 个图层"
5. 菜单 `3. 校验` → [ ] **"20 个图层与基线完全一致"**(绿灯=T4过)
6. 提交:`git add Assets/Scenes/Stages/Stage_Start.unity Assets/Resources/LijiangEchoArt/start Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs.meta && git commit -m "chore: 烘焙Stage_Start+贴图转Sprite"`

**T6 编辑器可视化**
7. `Stage_Start.unity` → 选 `开始舞台/开始远山一` → Inspector `Lijiang Echo Sprite Layer` → `Sprite` 换图
- [ ] Scene 立即变、自动拟合 → Ctrl+Z
8. 选 `绣球` → Scene 拖到别处 → Bootstrap Play 位置一致 → Ctrl+Z → Ctrl+S

---

## Part 3 · 过场(菜单 调试→进 过场)
- [ ] **远方地平线有层叠远山**,持续出现、不随漂浮素材横移(位置高低不对→调 ShowIntro 里 y/alpha)

## Part 4 · 描绘(菜单 调试→进 描绘)
- [ ] **P1** 全程淡指引线 + 已画线头→尾发光渐变
- (描绘背景已恢复淡紫边框;远山已移到过场)

## Part 5 · 战斗音符(菜单 调试→进 战斗(关卡1))
- [ ] 单击=**鱼纹** · 长按=蛇纹 · 双击=鸟纹
- [ ] 音符有**明显金色发光光晕**(围绕轮廓一圈,脉动)——不够明显→调光晕 1.35倍/脉动速度
- [ ] 音符**变小 + 飞向中心 + 进圆环淡出**
- [ ] **长按**按住时纹样**龙头→龙尾消失**(方向反→`HoldWipeTowardEntrySide`)
- [ ] 约 19/41/42…96 秒出现双击音符

## Part 6 · 谱面:从音乐生成(核心)
**前置**:选 `Resources/LijiangEchoAudio/battle_music` → Inspector **Load Type=Decompress On Load** + 勾 Preload Audio Data → **Apply**。
1. 菜单 `漓江回声→谱面→1. 从音乐检测拍子生成谱面`
   - [ ] 弹"检测到 N 个拍子点",生成 `Resources/LijiangEchoCharts/chart_generated.txt`
   - 点太多/少 → 改 `LijiangEchoChartGenerator.cs` 顶部 `Sensitivity`/`MinGapSeconds` 再跑
2. 菜单 `2. 把需求类型贴到最近拍子` → 写上单/双/长按
3. `调试→进 战斗` → [ ] Console 打印 **"已从谱面表格加载 N 个音符"** = 战斗已用你音乐生成的谱面
   - 说明:没生成 chart_generated 时,战斗默认用需求表 `chart_liusanjie.txt`(33音符);
     两个都删则回代码默认 108 音符。

## Part 7 · 打击点 Prefab(特殊/例外打击用)
1. 菜单 `漓江回声→打击点→生成「打击点」Prefab 模板` → 生成 `Assets/Prefabs/打击点.prefab`
2. 拖进场景 → Inspector `Lijiang Hit Point` 指定 Sprite/类型/位置
- [ ] 能拖入、能指定纹样和位置

## Part 8 · B 谱面对比(可选)
`git checkout spec-chart-b` → 改 `UseSpecChartDefault=true` → 战斗用需求33音符,对比手感

---

## 需你决策 / 待你回一句
- [ ] 双击贴图 **鸟纹 / 蛙纹**(单击已定鱼纹)
- [ ] 右下角"待描绘纹样"(静止蛇纹)**保留 / 删 / 挪**
- [ ] 双手镜像描绘开关 —— 要做说"做镜像开关"
- [ ] 中心圆环也做 Prefab?
- [ ] 每个 .unity 单开能 Play(自动带测试相机)?

## 只能你 / 硬件
- [ ] Prefab 化 · Quest 3 真机测试 · 最终录制

---
*任一步不对:贴 Console 报错 / 现象 / 截图给 Claude,当条 `git revert` 或直接修。*
