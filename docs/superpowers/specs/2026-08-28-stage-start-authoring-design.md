# 阶段场景内容可视化改造 —— Stage_Start 试点设计

日期：2026-08-28
状态：待评审
范围：**仅 Stage_Start 一个阶段**（试点）

---

## 1. 目标

让阶段场景里的美术内容**可以在 Unity 编辑器里直接摆放和替换**，而不是写死在代码里、运行时才生成。

改造完成后，期望的工作流是：

- 打开 `Stage_Start.unity`，Scene 视图里**直接看到**开始界面的完整画面
- 换一张远山：把新图拖到对应物体的 Sprite 字段上，立刻看到效果
- 挪位置 / 改大小：直接拖动 Transform，所见即所得，不需要进 Play 模式
- 加一个新元素：直接往场景里拖一张图

### 非目标

- 不改变游戏的**视觉呈现**（改造前后画面应当一致）
- 不改变阶段之间的跳转逻辑
- 不处理裁切图（`AddCroppedSprite`）——开始界面不使用，留到过场/战斗阶段再解决
- 不在本次改造其余 6 个阶段

---

## 2. 现状与问题

`StartStageController.BuildStartScreen()` 用约 20 行代码在运行时搭出整个开始界面：

```csharp
AddLayer("start/frame_16_9", "开始界面底框", Vector3.zero, MainCanvasWidth, -20, 0.04f);
AddLayer("start/back_mountain_1", "开始远山一", new Vector3(0f, -0.02f, 0.34f), WideStripWidth, -16, 0.9f);
```

每一张图的**资源路径、坐标、宽度、排序层级、透明度**全部是代码里的字面量。`Stage_Start.unity` 场景文件里只有一个空的 GameObject，Scene 视图中看不到任何画面。

因此美术调整的每一次迭代都需要：改代码 → 等编译 → 进 Play → 观察 → 退出 Play。

### 已确认的有利条件

美术资源的导入设置已经是 Sprite 且像素密度与代码一致，可以直接拖进场景使用：

```
textureType: 8            → Sprite (2D and UI)
spriteMode: 1             → Single
spritePixelsToUnits: 520  → 与 LijiangEchoStageKit.PixelsPerUnit 一致
```

---

## 3. 设计

### 3.1 场景结构

`Stage_Start.unity` 的层级改为：

```
漓江回声_开始阶段          (StartStageController)
└── 舞台根节点              (运行时被摆到相机前方)
    ├── 开始界面底框        (SpriteRenderer + LijiangEchoSpriteLayer)
    ├── 开始远山一          (SpriteRenderer + LijiangEchoSpriteLayer)
    ├── 开始后云一          (… + LijiangEchoMotion)
    ├── 进入游戏主按钮      (… )
    └── …
```

舞台根节点**预先存在于场景中**，不再由 `PrepareStageRoot()` 运行时创建。运行时只做一件事：把它移动到相机前方并设置朝向与缩放（沿用现有 `StageDistance` / `StageWorldScale` 逻辑）。

这样美术内容作为它的子物体，在 Scene 视图中就是可见、可拖动的。

### 3.2 新增组件：`LijiangEchoSpriteLayer`

标记 `[ExecuteAlways]`，使其在编辑器中即时生效，Scene 视图所见即运行时所得。

| 字段 | 说明 |
|---|---|
| `Sprite sprite` | **直接拖美术资源到这里**（主要交互入口） |
| `FitMode fitMode` | `Width` / `Height` / `None` —— 对应现有 `AddLayer` / `AddIcon` / 不缩放 |
| `float fitSize` | 目标宽度或高度（世界单位） |
| `int sortingOrder` | 排序层级 |
| `float alpha` | 透明度 |

职责：把这些参数应用到同物体上的 `SpriteRenderer`。取代现有的 `FitRendererWidth` / `FitRendererHeight` / `PlaceVisibleCenter` 在运行时的调用。

### 3.3 新增组件：`LijiangEchoMotion`

承载现有 `RegisterMotion(...)` 的四个参数，可在 Inspector 里调：

| 字段 | 说明 |
|---|---|
| `MotionKind kind` | 复用 `LijiangEchoStageKit.MotionKind` |
| `float amplitude` / `float speed` / `float phase` | 同现有含义 |

阶段 Controller 在 `Start()` 时用 `GetComponentsInChildren<LijiangEchoMotion>()` 收集，交给现有的 `LijiangEchoStageKit.UpdateMotions(...)` 驱动。动效算法本身不改。

### 3.4 改造后的 `StartStageController`

只保留**逻辑**：

- 把舞台根节点摆到相机前方
- 收集子物体上的 `LijiangEchoMotion`，每帧驱动动效
- 开始按钮的悬停/点击判定（现有 `TryGetControllerHover` 逻辑不变）
- 确认后 `GoToStage("Stage_Select")`

按钮的两个 `SpriteRenderer`（`startButtonPanelRenderer` / `startButtonRenderer`）改为 `[SerializeField]` 字段，在 Inspector 里拖引用，不再靠代码创建时接住返回值。

`BuildStartScreen()` 及其全部 `AddLayer` / `AddIcon` / `RegisterMotion` 调用**删除**。

### 3.5 迁移工具：`漓江回声/场景化/烘焙 Stage_Start 内容`

一次性编辑器工具，避免手工重摆 20 个物体：

1. 在编辑模式下按现有 `BuildStartScreen()` 的参数表生成物体
2. 为每个物体挂上 `LijiangEchoSpriteLayer`，字段填入对应参数，`sprite` 指向**已导入的 Sprite 资产**（而非运行时 `Sprite.Create` 的产物，后者无法序列化进场景）
3. 需要动效的物体挂上 `LijiangEchoMotion`
4. 全部置于舞台根节点下，保存场景

工具执行一次即完成使命，之后内容由人在编辑器里维护。

---

## 4. 已识别的风险

### 4.1 Sprite 网格类型差异（主要风险）

现有代码在运行时用 `Sprite.Create(...)` 造精灵，`AddLayer` 传 `SpriteMeshType.FullRect`，`AddIcon` 传 `SpriteMeshType.Tight`。而已导入的 Sprite 资产统一是 `spriteMeshType: 1`（Tight）。

两者的 `sprite.bounds` 可能不同，会影响 `FitRendererWidth`（按 bounds 宽度算缩放）与 `PlaceVisibleCenter`（按 bounds 中心定位）的结果，导致**烘焙后位置或大小与原来有偏差**。

**应对**：烘焙工具对每个物体同时用两种方式计算一遍，比对差异；若有偏差，在烘焙时把最终 Transform 数值直接写死（以现有运行时表现为准），保证视觉一致。这是试点要验证的头号问题。

### 4.2 舞台根节点的锚定时机

现有 `PrepareStageRoot()` 每次创建新根节点并立即定位。改为复用场景中已有的根节点后，需确认：Bootstrap 的 XR Rig 就绪之后才做定位（现有 `LijiangEchoGameFlow` 已有等待头显位姿的逻辑，沿用即可）。

### 4.3 `[ExecuteAlways]` 的副作用

编辑模式下运行的组件若写坏了可能污染场景数据。应对：`LijiangEchoSpriteLayer` 只写同物体的 `SpriteRenderer`，不触碰其他物体，不做资源加载以外的 IO。

---

## 5. 验证方式

1. **视觉比对**：改造前先在 Play 模式下截图开始界面；改造后同位置截图，逐层比对（云、鸟、绣球、按钮的位置与大小）
2. **交互验证**：从 `Bootstrap.unity` 进 Play，手柄射线悬停开始按钮应有高亮变化，扣扳机应进入选关
3. **编辑器工作流验证**（本次改造的真正验收标准，由使用者本人执行）：
   - 打开 `Stage_Start.unity`，Scene 视图中能看到完整开始界面
   - 把某张远山换成另一张图，Scene 视图立即反映
   - 拖动绣球位置，进 Play 确认位置与编辑器中一致
4. **回退**：全部改动在 git 中，`git checkout` 即可还原

---

## 6. 试点之后

若试点验证通过，按成本从低到高推广：**卡片 → 结算 → 描绘 → 过场 → 战斗**。

其中过场与战斗需要先解决裁切图问题。已查明的现状与结论如下（不属于本设计范围，留待彼时执行）：

**成因**：美术在同一个 PSD 里完成全部内容，再按图层逐个导出，每个文件都保留了完整画布尺寸（`transition/` 全部为 3207×630），元素待在原位，其余透明。代码中的 `RectInt` 即是照着各图层内容边界量出的坐标。

**三个已确认的问题**：

1. **画质损失** —— 导入设置 `maxTextureSize: 2048`，5000×5000 的 `pattern/*` 一进 Unity 就被压到 2048，代码再从这张已降采样的图上裁切。`snake_done` 实际只有约 388×1028 像素在显示，而非预期的 948×2510。
2. **内存与包体** —— 单个过场素材导入后约占 2048×402（≈3.1 MB 未压缩），30 个合计约 90 MB，且全部位于 `Resources/` 下无条件打进 APK，仅为使用其中很小一块内容。
3. **坐标已发生漂移** —— 抽查发现 `transition/animal_1` 的代码坐标 `RectInt(600, 344, 198, 110)` 与图中实际内容边界 `(623, 345, 174, 108)` 相差 23 像素，导致该元素定位偏移。同一份"位置与尺寸"信息同时存在于美术资产与 C# 常量中，已经失同步。

**方案**：写工具从原图全分辨率导出预裁小图，**按 alpha 自动计算内容边界**而非读取代码中的 `RectInt`（后者可能已过时，自动计算可顺带修正 `animal_1` 一类的偏移）。裁切后删除 `GetCroppedSprite` 及全部 `RectInt` 常量与 `sourceWidth` 硬编码补偿逻辑，原始大图移出 `Resources/`，另建 Sprite Atlas 重新打包。

**关于合批**：当前 30 个过场元素分属 30 个不同贴图，本就无法合批；改为小图 + Sprite Atlas 后合批情况优于现状，不存在退步。

**后续导出约定**：美术导出时按内容裁剪（Trim），一个元素一个文件，位置信息交由场景承载。

若试点验证不通过，改动仅限 `Stage_Start` 与两个新组件，回退代价可控。
