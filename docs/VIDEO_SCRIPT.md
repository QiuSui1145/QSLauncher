# QS Launcher 介绍视频脚本

> 成片为无声动效字幕版；下表同时给出可直接用于配音的旁白文案。

## A. 完整介绍片 `QSLauncherIntro`（54 秒）

| 时间 | 画面 | 屏幕文案 | 旁白建议 |
| --- | --- | --- | --- |
| 0–5s | 首页实机从右侧滑入，标题浮出 | 把 Minecraft 交还给你；完全便携、去中心化、官方源 | 这是一个把控制权交还给玩家的 Minecraft 启动器。 |
| 5–11s | 本地节点连接 Mojang / Modrinth / 加载器官方节点 | 去中心化，不经过任何中间服务器 | 没有启动器账号，没有中转下载，也没有遥测。 |
|  |  | 所有下载相关功能都直接请求官方源 | Mojang 版本清单与资源、Modrinth API、Fabric / Forge / NeoForge / Quilt 官方元数据。 |
| 11–19s | 首页工作台实机，标注三个区域 | 打开就是你的工作台 | 当前实例、智能内存、最近实例，一屏就能启动。控件可以拖动、缩放、隐藏。 |
| 19–27s | 版本管理 + 下载页叠放 | 版本、加载器、实例，一处管理 | 920 个官方版本，每个实例独立 mods、config、saves 与 Java 设置。 |
| 27–35s | 整合包页 + 模组工作台 | 整合包，拖进来就能装 | 支持 Modrinth、CurseForge、Prism 与普通 ZIP；拖到窗口，确认后覆盖到当前挂载目录。 |
| 35–43s | 设置页实机 | 你的启动器，应该像你的 | 深浅色、强调色、背景图与视频、磨砂强度，以及微软 / 离线 / LittleSkin 多账户。 |
| 43–49s | Java、资源、日志三张实机叠放 | Java、空间、日志，全都管好 | 官方 Java 运行时一键安装，数据全部留在 data/，日志可以打包成 ZIP。 |
| 49–54s | 品牌收尾 | 去中心化 · 官方源 · 完全便携 | QS Launcher 1.2.0，单文件，免安装。 |

## B. 1.2.0 更新片 `QSLauncherUpdate120`（30 秒）

| 时间 | 画面 | 屏幕文案 | 旁白建议 |
| --- | --- | --- | --- |
| 0–4s | 标题 | 更聪明，更干净，更顺手 | 1.2.0 带来三项直接能感知的升级。 |
| 4–11s | 首页 + 内存数字动画 | 内存不再保守 | 读取本机当前可用内存，可用充足时自动分配更大的 Java 堆。 |
| 11–18s | 设置页 + 账户菜单特写 | 菜单不再是白块 | 账户菜单、下拉框和次级按钮改为半透明磨砂，面板颗粒比菜单高 10%。 |
| 18–25s | 整合包页 + ZIP 拖拽动画 | 整合包，直接拖进窗口 | 自动识别 ZIP / MRPACK / CurseForge / Prism，松开并确认即可安装到当前实例。 |
| 25–30s | 收尾 | 现在就可以更新 | QSLauncher-1.2.0-win-x64.exe，单文件便携。 |

## C. 必须出现的声明

1. **去中心化**：没有启动器账号、没有中转服务器、没有遥测。
2. **所有下载相关功能使用官方源**：
   - Mojang `version_manifest_v2`、版本 JSON、资源与官方 CDN；
   - Modrinth 官方 API；
   - Fabric / Quilt 官方 meta，Forge / NeoForge 官方 Maven 与安装器。
3. **实机演示纯净性**：演示使用独立 `artifacts/demo/data`，不含任何用户数据。

## D. 素材与复现

- 演示数据：`tools/prepare-demo.ps1`
- 实机截图：`tools/capture-demo.ps1`
- 视频工程：`video/`（Remotion）
- 成片：`artifacts/video/QSLauncher-Intro-1.2.0.mp4`、`artifacts/video/QSLauncher-Update-1.2.0.mp4`
- 封面：`artifacts/video/QSLauncher-Cover-1.2.0.png`
