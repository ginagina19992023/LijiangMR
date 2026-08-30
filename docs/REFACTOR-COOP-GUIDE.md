# 场景拆分 · 详细合作操作手册

配合 `docs/REFACTOR-SCENE-SPLIT.md`(总方案)。本文只讲**具体怎么协作、你在 Unity 里点什么、怎么验、出错怎么回报**。
分工一句话:**我(Claude)只动代码;建场景 / Build Settings / Play 验证 由你在 Unity 做。**

---

## 0. 每一步都走这个「协作循环」(重要)

```
① 我:写代码(纯新增,不动旧逻辑)→ push → 告诉你【改了哪些文件】+【你要在 Unity 做的几步】
② 你:git pull → 等编译完
      ├─ 有红色报错 → 把【第一条红字整条】发我 → 我修 → 回到②
      └─ 编译通过 → 按我给的步骤在 Unity 操作 → Play 验证
③ 你:回报结果(用下面的「回报模板」)
      ├─ 通过 → 我摘掉旧代码 → 你再 pull 验一次 → 进下一步
      └─ 不对 → 发我现象/截图(传 debug/ 文件夹)→ 我改 → 回到②
```

**铁律**:一次只推进一个阶段;新场景没验过之前,旧代码我不删;每步小提交,可随时回退。

---

## 1. 你会反复用到的 Unity 操作(通用)

### 1.1 从模板复制一个阶段场景
1. Project 窗口:`Assets/Scenes/Stages/Stage_Select.unity` 上右键 → **Duplicate**;
2. 改名成目标名(如 `Stage_Trace`);
3. 双击打开它。

### 1.2 换掉场景里的控制器组件
1. 打开该场景,在 Hierarchy 里找到挂着 `SelectStageController` 的那个物体;
2. Inspector 里点 `SelectStageController` 右上角 ⋮ → **Remove Component**;
3. **Add Component** → 搜我新写的控制器名(如 `TraceStageController`)→ 加上;
4. **Ctrl+S 保存场景**。

### 1.3 加进 Build Settings(顺序不能乱)
1. `File → Build Settings`;
2. 把新场景从 Project 拖进「Scenes In Build」;
3. **拖到正确顺序**(见 `WORKFLOW-BATTLE.md` 第 6 章)。当前顺序:
   `Bootstrap → Stage_Start → Stage_Select → (新场景插这里) → LijiangEchoMR_Main`。

### 1.4 Play 验证(不用头显、不用 build)
1. 打开 `Assets/Scenes/Bootstrap.unity`;
2. 按 **Play**;
3. **鼠标**走流程(选关点/拖、描绘用鼠标描;双手描绘按住 **Shift** 用鼠标画左手)。

---

## 2. 第 1 步 playbook:描绘 → `Stage_Trace`

### 2.1 我先做(等我 push 后通知你)
- 新建 `Assets/Scripts/Stages/TraceStageController.cs`(自包含,搬 `BuildTracePath`/描绘视觉/单手+双手描绘/完成→进战斗)。
- **此时不生效**:没有场景挂它,旧描绘照常在 `LijiangEchoMR_Main` 里跑 → 现在的游戏不受影响。
- 我会告诉你:改了哪些文件、以及下面 2.2 的具体参数。

### 2.2 你做(Unity)
1. **建场景**:按 1.1 复制 `Stage_Select` → 改名 `Stage_Trace`;
2. **换控制器**:按 1.2 把 `SelectStageController` 换成 `TraceStageController`;
3. **Build Settings**:按 1.3 把 `Stage_Trace` 放在 `Stage_Select` **之后**、`LijiangEchoMR_Main` **之前**;
4. **接流程**:这一步我会在代码里改好(选关确认 → 先进 `Stage_Trace`,描绘完再进旧主场景的战斗),或明确告诉你改哪一行。你只需确认。

### 2.3 验证清单(逐项打勾回我)
- [ ] 选关确认后,**进入描绘画面**(不是直接进战斗/不卡);
- [ ] **单手**模式:一只手能描完整只纹样 → 显示「绘制成功」;
- [ ] **双手**模式:左右各描一半、两半都描完才成功(编辑器按 Shift 用鼠标画左手);
- [ ] 参考纹样位置**可接受**(居中问题我们后面单独收拾,先不卡在这);
- [ ] 描绘完成 → **正常进入战斗**;
- [ ] 战斗/后续**没被影响**(音符、圆环照旧)。

### 2.4 通过之后
- 你回我「描绘第1步通过」→ 我把 `LijiangEchoMR_Main` 里旧的描绘阶段代码**摘掉**(改成:过场结束不再进旧描绘,而是 `GoToStage("Stage_Trace")`)→ 你再 pull 验一次 → 进第 2 步(过场)。

---

## 3. 回报模板(复制着填,我最省猜)

```
步骤:第1步 描绘 / 编译:通过或报错
现象:(能不能进描绘 / 单手 / 双手 / 完成 / 进战斗 各自结果)
报错:(有的话,Console 第一条红字整条贴这;或截图传 debug/)
```

---

## 4. 出错怎么回退(随时可用)
- **编译不过 / 新场景有问题**:告诉我,我改;实在要立刻恢复 → `git checkout battle-visual-hands`(回到没开始拆的稳定分支)。
- **只想扔掉某个新场景**:Unity 里删 `Stage_Xxx.unity` + 从 Build Settings 移除即可(旧代码还在,游戏照常)。
- 每一步都是小提交,`git log` 里能看到,必要时单条 `git revert`。

---

## 5. 进度勾选(我们一起维护)
- [x] 第 0 步:黑屏修复 + 建分支 + 方案/本手册
- [ ] 第 1 步:描绘 → `Stage_Trace`
- [ ] 第 2 步:过场(悬浮 + 视频)→ `Stage_Intro`
- [ ] 第 3 步(可选):战斗 → `Stage_Battle`
- [ ] 并行:tools 整理(生成纹样菜单并进「纹样绑定总表」)
