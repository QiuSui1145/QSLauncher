# QS Launcher 介绍视频

基于 [Remotion](https://www.remotion.dev/) 的 1080p / 30fps 动效视频工程。

## 两条片子

| Composition | 时长 | 用途 |
| --- | ---: | --- |
| `QSLauncherIntro` | 54 秒 | 启动器完整介绍：UI 特色、功能支持、去中心化与官方源声明 |
| `QSLauncherUpdate120` | 30 秒 | 1.2.0 更新片：智能内存、磨砂界面、ZIP 拖拽安装 |

两条片子都**没有音轨**，信息全部由中文动效字幕与界面标注表达，便于后期自行配音或配 BGM。

## 素材来源

所有界面图都是启动器的真实运行截图，来自 `artifacts/demo/` 这个**独立演示目录**：

- 由 `tools/prepare-demo.ps1` 从零生成，删除后重建不会碰用户数据。
- 不含真实账户、真实 `.minecraft` 挂载、真实日志或任何用户文件。
- 实例、模组 JAR、资源包和壁纸都是脚本生成的占位内容。
- 截图通过 `tools/capture-demo.ps1` 用 `PrintWindow` 抓取真实渲染结果；账户菜单因是独立 `Popup` 窗口，改为抓取窗口矩形。

截图位于 `video/public/shots/`，壁纸位于 `video/public/wallpaper.png`。

## 开发预览

```powershell
cd video
npm install
npx remotion studio
```

浏览器打开 `http://localhost:3000`，左侧选择 Composition。

## 渲染

```powershell
cd video
npx remotion render QSLauncherIntro out/qs-launcher-intro.mp4 --codec=h264 --crf=18 --pixel-format=yuv420p --color-space=bt709
npx remotion render QSLauncherUpdate120 out/qs-launcher-1.2.0-update.mp4 --codec=h264 --crf=18 --pixel-format=yuv420p --color-space=bt709
npx remotion still QSLauncherCover out/qs-launcher-cover.png
```

如果 Studio 已占用 `3000` 端口，给渲染命令加 `--port=3100`。

## 工程结构

```text
src/
  Root.tsx                 注册两条 Composition
  Intro.tsx                54 秒介绍片时间轴
  Update120.tsx            30 秒更新片时间轴
  components/
    Backdrop.tsx           持久极光 / 网格 / 噪点背景
    Scene.tsx              场景淡入淡出，不切断背景
    Shot.tsx               实机截图卡片与标注
    Type.tsx               标题、正文、标签、玻璃面板
    Progress.tsx           底部章节进度
  scenes/
    HeroScene.tsx          标题与首页实机
    PrincipleScene.tsx     去中心化 / 官方源拓扑
    HomeScene.tsx          工作台首页
    VersionsScene.tsx      版本、加载器、实例
    ModpacksScene.tsx      整合包与模组
    AppearanceScene.tsx    主题、背景、账户
    ToolingScene.tsx       Java / 数据 / 日志
    OutroScene.tsx         收尾
```

## 重新出片

```powershell
# 1. 干净的演示数据 + 1.2.0 发布件
powershell -ExecutionPolicy Bypass -File tools\prepare-demo.ps1 -Version 1.2.0

# 2. 启动演示启动器，逐页截图
Start-Process artifacts\demo\QSLauncher.exe -WorkingDirectory artifacts\demo
powershell -ExecutionPolicy Bypass -File tools\capture-demo.ps1 `
  -Pages "首页,版本管理,下载,整合包,模组,Java,资源,日志,设置" `
  -OutputDir artifacts\demo\shots -Prefix dark -AccountMenu

# 3. 把截图复制到 public/shots 后渲染
```

## 声明口径

视频中明确说明：

- 启动器**去中心化**：没有启动器账号、没有中转下载、没有遥测。
- **所有下载相关功能直连官方源**：Mojang 版本清单 / 资源 / CDN、Modrinth 官方 API、Fabric / Forge / NeoForge / Quilt 官方元数据。
- 实机演示使用独立 `data` 目录，不含任何用户数据。
