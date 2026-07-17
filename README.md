# 漓江回声 MR

面向 Meta Quest 3 的 MR 虚拟交互音游 Unity 工程。

## 开发环境

- Unity `6000.3.10f1`
- Meta XR SDK `201.0.0`
- 目标设备：Meta Quest 3
- 主场景：`Assets/Scenes/LijiangEchoMR_Main.unity`

## 获取并打开工程

1. 使用 GitHub Desktop 克隆仓库。
2. 在 Unity Hub 中选择“添加”，打开克隆后的仓库根目录。
3. 等待 Unity 首次导入资源和恢复软件包。
4. 打开主场景 `Assets/Scenes/LijiangEchoMR_Main.unity`。

首次打开时 Unity 会重新生成 `Library`，耗时较长属于正常现象。`Library`、`Temp`、`Logs`、`Builds` 等本地生成内容不会进入仓库。

## 协作约定

- `main`：程序逻辑、可运行版本与最终整合。
- `art`：美术资源和视觉调整。
- 修改前先拉取远程更新，完成后填写清楚提交说明并推送。
- Unity 资源及其同名 `.meta` 文件必须一起提交，禁止单独删除或遗漏 `.meta` 文件。
- 修改、拉取或合并大量 Unity 文件前建议先关闭 Unity 编辑器。
- 不要多人同时修改同一个场景或 Prefab；需要整合时由一人合并并在 Quest 3 上验证。

## 当前说明

工程保留现有 Meta XR 配置。请勿删除或替换 Meta XR 软件包；涉及透视、控制器、手部追踪或 OpenXR 的设置变更，需要在 Quest 3 真机上验证后再合并到 `main`。
