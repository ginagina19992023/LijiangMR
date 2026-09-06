# 第二步 · 战斗与结算的场景拆分（待做）

> 这份文档是「以后回来接着做」的施工书。第一步（删死代码）已完成，见文末进度。
> 配套阅读：`REFACTOR-SCENE-SPLIT.md`（总方案）、`REFACTOR-COOP-GUIDE.md`（协作循环）。

---

## 0. 现在到哪了

拆分从前往后推进，目前：

```
Bootstrap → Stage_Start → Stage_Select → Stage_Intro → LijiangEchoMR_Main
   常驻        已拆         已拆          已拆        ← 只剩战斗/卡面/结算
                                       (含描绘模块)
```

`LijiangEchoGameController` 现在 **4350 行**，只剩三个阶段：

| 阶段 | 约占行数 | 说明 |
|---|---|---|
| `ShowBattle` | ~1800 | 谱面、音符、圆环、判定、镜像、烘焙背景 |
| `ShowCard` | ~110 | 15 张纹样解析卡面 + 翻页 |
| `ShowResult` | ~245 | 成功/失败标识 |

**第一步已经把开始/选关/过场/描绘四份重复实现删掉了（1460 行）**，所以现在这个文件里没有"新旧两套"的歧义，读起来干净很多。

### 已经解决的、不必再管的事

- 旧控制器不再 `DontDestroyOnLoad`，**随旧主场景生死**。所以它不会在新场景里抢按键、不会带过期状态重建旧阶段。
- 暂停菜单已经是挂在 Bootstrap 上的全局组件（`LijiangEchoPauseMenu`），旧控制器那套只在旧主场景在跑时接管。
- `GoToStageRoutine` 已串行化，不会再出现两个场景叠加。

---

## 1. 到底要不要做第二步

**不做也能交付 9.1 全部需求。** 第二步的收益只有两条：

1. **战斗画面可视化编辑** —— 背景、圆环、手的位置能拖着摆，不用改代码试参数
2. `ShowBattle` 那 1800 行独立成文件，以后改判定不用在 4000 行里翻

**什么时候值得做**：
- 美术要反复调战斗画面的构图 / 位置 / 层次
- 要做第二、第三关，且每关战斗画面差异较大
- 战斗逻辑要大改（比如加新音符类型、改判定规则）

**什么时候别做**：
- 只是修 bug 或调参数 —— 现在的结构够用
- 临近交付 —— 这是全项目最大的一块，别在验收前动

---

## 2. 拆分顺序（从小到大，每步独立可验）

### 2.1 先拆 `Stage_Result`（最小，练手）

结算只有一个成功/失败标识 + 背景，约 245 行，依赖最少。

**我做（代码）**
1. 新建 `Assets/Scripts/Stages/ResultStageController.cs`，用 `LijiangEchoStageKit` 重写 `ShowResult` 的内容
2. 胜负判定的输入（`score` / `noteTimes.Length`）通过 `LijiangEchoGameFlow` 传递 —— 需要在 GameFlow 上加两个字段：`LastScore`、`LastNoteCount`
3. 战斗结束时改成 `GoToStage("Stage_Result")`，并带上守卫：场景没建就退回 `ShowResult()`

**你做（Unity）**
1. 复制 `Stage_Select.unity` → 改名 `Stage_Result.unity`
2. 根物件换挂 `ResultStageController`
3. Build Settings 放在 `LijiangEchoMR_Main` 之后
4. Play 验：打完 → 成功/失败标识 → 按一下 → 卡面

**验过之后**我才摘掉旧的 `ShowResult`。

### 2.2 再拆 `Stage_Card`（卡面解析）

约 110 行，15 张卡面 + 翻页按钮。**这一步顺便把卡面做成 Prefab**，让翻页按钮的位置能拖着调（现在 `CardArrowX` 是硬编码的，撞过一次"点箭头直接退出"的坑）。

步骤同 2.1。守卫：`Stage_Card` 没建就退回 `ShowCard()`。

### 2.3 最后拆 `Stage_Battle`（大头，1800 行）

**这一步必须再切细，不要一次搬完。** 建议按下面的顺序，每一小步都能单独 Play 验：

| 子步 | 搬什么 | 验什么 |
|---|---|---|
| a | 场景骨架 + 背景（含 `Battle_level{N}` 烘焙背景的采用逻辑） | 进战斗能看到背景，没有音符 |
| b | 谱面读取 + 音符生成/飞行/销毁 | 音符按谱面飞出来，不判定 |
| c | 中间圆环 + 手部 Prefab | 圆环和手显示正常 |
| d | 判定（含左右手、双手同时、蛙纹放宽） | 打得中、判得对 |
| e | 计分 / 连击 / 反馈文字 / 涟漪 | 分数连击正常 |
| f | 战斗音乐 + 结束衔接 | 音乐播完 → 进结算 |

**每一小步都是"新增，不删旧"**：新控制器写好但 `Stage_Battle` 没进 Build Settings 时不生效，旧路径照跑。全部验完再摘旧代码。

**需要注意的既有机制**（别在搬运中弄丢）：
- `TryAdoptBakedBattleBackground()` —— 进战斗若已加载 `Battle_level{N}` 就采用它当背景，否则运行时构建
- 纹样 Prefab 优先：`Note_level{关}_{类型}` → `Note_{Fish/Bird/Snake/Frog}` → 代码生成
- 圆环 Prefab：`Ring_level{关}` → `Ring_Center` → 贴图兜底（**这两个目前都不存在，一直走贴图兜底**）
- 战斗选项资源 `LijiangEchoBattleSettings` 里的全部开关都要继续读

---

## 3. 铁律（沿用总方案，别破）

1. **一次只推进一小步**，推完在 Unity 里 Play 验一次，通过了再动下一步
2. **旧路径先留着**：新场景验证通过前，旧控制器里对应代码不删，只是走不到
3. 每步小提交，可随时 `git revert`
4. 我编译不了、也看不到美术 —— **每一步都要你在 Unity 里 Play 验一次才算过**

## 4. 分工

- **我做**：新建 `XxxStageController`、把逻辑搬过去改成用 `LijiangEchoStageKit`、补 StageKit 缺的公共方法
- **你做**：复制场景 → 换挂控制器 → 进 Build Settings → Play 验证 → 回报

回报模板：
```
步骤：2.3 子步 b / 编译：通过 or 报错
现象：音符有没有飞出来 / 时机对不对 / 有没有漏
报错：Console 第一条红字整条贴这
```

---

## 5. 进度

- [x] **第一步**：删掉旧控制器里已被独立场景取代的 1460 行死代码（开始/选关/过场/描绘）
- [x] 旧控制器不再 `DontDestroyOnLoad`，随旧主场景生死
- [x] 场景切换串行化，杜绝场景叠加
- [x] 菜单重组为 6 个大入口
- [ ] 2.1 `Stage_Result`
- [ ] 2.2 `Stage_Card`（含卡面 Prefab 化）
- [ ] 2.3 `Stage_Battle`（a → f 六小步）
- [ ] 全部验过后，删掉 `LijiangEchoGameController`
