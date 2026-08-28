# 漓江回声 · 本批验收指南(手部真资产 / 远山 / 鱼纹 / 谱面预览窗口)

> 分支 **`battle-visual-hands`**(本批新改都在这)。先 `git checkout battle-visual-hands` → `git pull` → 等编译 → Console 无红报错。
> LFS:手部真图是 `7左手/7右手`,你本机装了 git-lfs 会自动还原真图(检查文件不是 131 字节)。
> 战斗/过场用 **`漓江回声→调试`** 菜单一键进。

---

## A. 打击的手(菜单 调试→进 战斗(关卡1))
- [ ] 用的是 **7左手 / 7右手** 真资产(不再是旧 hand_left/right)
- [ ] 平时**看不到手**(手臂朝下、藏在画面下方)
- [ ] 打击瞬间手**从下方向上旋转**冲向中心圆环、显现,然后落回藏起
- [ ] 左手打左半、右手打右半;双击时两手一起
- 位置/幅度不对 → 调 `LijiangEchoGameController.cs` 顶部:`HandPivotY`(轴心高低)、`HandPivotSide`(左右分开)、`HandArmLength`(臂长)、`HandStrikeAngle`(上抬角度)、`HandRestAngle=180`(藏起角度)

## B. 过场地平线远山(菜单 调试→进 过场(关卡1))
- [ ] 远山**又缩小了一半**(mtnHeight 0.10)、**排满整条地平线**(不再只有 7 座)
- [ ] 底面贴在**偏上**的地平线(horizonY 0.18)、静止不横移
- 疏密/高低不对 → 调 ShowIntro 里 `horizonStep`(越小越密)、`horizonHalfSpan`(排布宽度)、`horizonY`、`mtnHeight`

## C. 鱼纹落点(菜单 调试→进 战斗(关卡1))
- [ ] 单击鱼纹落点**往左挪了**(补偿之前偏右);正好落圆心最理想
- 还偏 → 调 `FishNoteXOffset`(现 -0.14,负=更左;偏左了就调大到 -0.08 之类)

## D. 谱面预览窗口(核心新功能,菜单 漓江回声→谱面→0. 打开预览窗口)
前置:选 `LijiangEchoAudio/battle_music` → Inspector **Load Type=Decompress On Load** + 勾 Preload → **Apply**
- [ ] 打开窗口,拖 **灵敏度 / 最小间隔** 两根滑条
- [ ] 点 **① 检测预览** → 下方时间轴出现一排**金色竖线**=每个拍子点;数量/时长/平均间隔显示在底部
- [ ] 调滑条再点①,直观看点变多变少(此时**还没写文件**)
- [ ] 满意 → **② 写入谱面**(写 chart_generated.txt)→ 需要类型再 **③ 贴需求类型**
- [ ] 进战斗 → Console 打印 **"已从谱面表格加载 N 个音符"** = 用上了你调的谱面
- 注:老的菜单 1/2 命令还在、行为不变;窗口是它俩的可视化版

---

## 待你回一句(还没做,等你定)
- [ ] **打击纹样纯白**:鱼纹是彩色图,`SpriteRenderer.color` 白色是"相乘"只能压不能漂白 → 要纯白有两条路:
      (a) 你给一张白色剪影的纹样图;(b) 我写个 unlit 白色材质/shader,用图的 alpha 当形状、输出纯白。**选哪个?**
- [ ] 这批(battle-visual-hands 分支)验收 OK 后,要不要我合回 `stage-start-authoring` / 主分支?
- [ ] 双击贴图鸟纹/蛙纹 · 结算那张纹样卡保留还是去掉 · 各阶段是否单开 scene + 测试相机

## 只能你/硬件
- [ ] Quest 3 真机测试 · 最终录制
---
*任一步不对:贴 Console 报错/现象/截图,当条 `git revert` 或直接修。*
