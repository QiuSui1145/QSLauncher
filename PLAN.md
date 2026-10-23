# QS Launcher 设计与实施计划

- 文档状态：**v0.2，已确认，进入实施**
- 日期：2026-10-20
- 目标系统：Windows 10 22H2 / Windows 11，x64（后续可补 arm64）
- 工作名：QS Launcher（可改）
- 核心约束：**不使用 JavaScript、Python、Node.js、Electron、Tauri、WebView2**

## 变更记录

| 版本 | 变更 |
|---|---|
| v1.0 | 首版实施完成：原版/加载器/整合包/模组依赖/Java/账号/主题/背景/实例内容管理；发布自包含便携包。实施偏差见 1.1 |
| v0.2 | 新增深/浅/跟随系统主题 + 自定义调色板；图片/视频背景；可调半透明磨砂；整合包安装提前；正版/离线/LittleSkin 等第三方登录；PCL2 仅作功能参考，UI/交互/动画必须原创 |
| v0.1 | 初版计划 |

### v1.0 实施记录与偏差

- **依赖注入**：未引入 DI 容器，由 `LauncherContext` 手动组装服务；效果等价，减少第三方依赖。
- **WebP 图标**：未引入 ImageSharp；界面不依赖远程 WebP 图标，避免额外原生库。
- **视频背景**：未使用 `MediaElement`，改为自绘 `VideoSurface`（`MediaPlayer` + `DrawingContext.DrawVideo`），可精确裁剪与叠加磨砂层。
- **资源包启用排序**：未改写 `options.txt`，避免与运行中的游戏冲突；只做文件管理。
- **CurseForge**：未默认集成，保留 BYO API Key 的后续扩展。
- **共享资源未使用项清理**：未实装引用集合计算；已实装实例级校验/修复与内容隔离区。

详细功能对照与后续优先级见 `COMPARISON.md`，使用说明见 `README.md`。

---

## 1. 目标

做一个双击即用、可整目录拷贝的 Minecraft Java 版启动器：

1. 界面是真正的 Windows 桌面应用，不是网页套壳。
2. 从 Mojang 官方源下载游戏版本、资源、库文件。
3. 支持 Fabric / Quilt / Forge / NeoForge 模组加载器。
4. **优先支持整合包安装**：Modrinth `.mrpack` 在线安装与本地导入，自动装原版、加载器、文件、覆盖层。
5. 支持 Modrinth 模组搜索、安装、依赖处理、更新、启停和本地导入。
6. 管理 Java 运行环境，并按版本自动选择 Java。
7. 管理版本、实例、模组、资源包、光影包、存档，以及共享的 assets/libraries。
8. 支持正版 Microsoft 登录、离线账户、LittleSkin 等 Yggdrasil 第三方登录。
9. 深色/浅色/跟随系统三态主题，自定义调色板；自定义图片/视频背景；半透明磨砂组件与可调透明度。
10. 所有数据留在程序目录，不写注册表、不写 AppData、不需要管理员权限。

已核实的外部事实（2026-10-20）：

- 官方版本清单最新正式版为 **26.3**，发布于 2026-09-15。
- 26.3 的版本元数据要求 **Java 25**，组件名为 `java-runtime-epsilon`。
- Mojang 版本清单、版本 JSON、Java 运行时索引均可访问。
- Modrinth 官方 API 可访问，26.3 已有模组版本数据。
- Quilt 官方元数据可访问，已有 26.3 加载器版本。
- Fabric 元数据在当前网络下曾超时；实现时会同时准备官方安装器路径，不依赖单一接口。

---

## 2. 不可妥协的约束

| 约束 | 落实方式 |
|---|---|
| 禁用 JavaScript | 不用 Electron、Tauri、WebView2、Node、任何 `.js` 运行时 |
| 禁用 Python | 不用 Python 脚本、pyinstaller、任何 `.py` 依赖 |
| Windows 原生 | C# + WPF，编译为 Windows x64 可执行程序 |
| 便携 | 配置、实例、Java、游戏资源全部在程序目录；整目录可移动 |
| 官方源 | 游戏与 Java 走 Mojang；加载器走各自官方站；模组/整合包走 Modrinth 官方 API |
| 不要求管理员 | 清单标记 `asInvoker`，不写 Program Files、不写 HKLM |
| 不破坏系统 | 不写注册表、不写 AppData、不装服务、不装驱动 |
| UI/动画原创 | PCL2 只作为功能清单参考；不读取、不复用其代码、素材、布局、图标、配色或动效曲线 |
| 登录合规 | 正版走 Microsoft OAuth；第三方走 Yggdrasil + authlib-injector；不伪造正版权益 |

说明：Minecraft 本身需要 Java 才能运行，加载器安装器也是 Java 程序；这是被启动的游戏生态，不是启动器使用 JavaScript/Python。启动器自身只使用 C#。

### 2.1 PCL2 参考边界

只参考公开的功能类别和用户流程，例如“下载版本 / 安装整合包 / 管理 Java / 多账户”。不参考其具体界面排版、配色、图标、文案、动画时长与缓动、控件形状、弹窗结构。QS Launcher 的首页工作台、导航、版本卡片、安装向导、过渡动画均为独立设计，并在仓库中保留设计说明作为原创性记录。

---

## 3. 技术选型

| 项 | 选择 | 理由 |
|---|---|---|
| 语言 | C# 14 | 类型安全、异步/网络/压缩支持完善 |
| 运行时 | .NET 10 LTS | 当前 LTS，长期维护 |
| UI | WPF + 全自绘 XAML 设计系统 | Windows 原生控件、无网页层、易做精细视觉 |
| 架构 | MVVM + 依赖注入 | UI 与下载/安装/启动逻辑解耦，便于测试 |
| JSON | `System.Text.Json` | 内置、源生成序列化、无额外运行时 |
| HTTP | `HttpClient` + `SocketsHttpHandler` | 连接复用、并发控制、Range 断点续传 |
| 压缩 | `System.IO.Compression` | 解压资源包、存档、安装器产物 |
| 图片 | 内置 WPF 解码 + ImageSharp（仅 WebP 图标） | Modrinth 图标多为 WebP，WPF 原生不支持 |
| 背景视频 | WPF `MediaElement`（Windows Media Foundation） | 无 JS、无浏览器；支持系统已安装解码器的 MP4/H.264、WMV 等 |
| 磨砂/透明 | 分层半透明画刷 + 背景模糊 + 细微噪点 | 不依赖 WebView；避免对控件内容本身做模糊 |
| 第三方登录 | Yggdrasil API + authlib-injector | LittleSkin 及兼容 Yggdrasil 的服务器 |
| 测试 | xUnit | 纯 C#，覆盖元数据解析、规则判断、参数拼装 |
| 数据存储 | JSON + 文件系统，不引入数据库 | 便携、可读、可手工修复 |
| 打包 | `dotnet publish` 自包含 win-x64 文件夹 + ZIP | 目标机无需安装 .NET，解压即用 |
| 构建脚本 | PowerShell（仅构建用） | Windows 自带，不引入 JS/Python |

发布两种产物：

- `QSLauncher-Portable-win-x64.zip`：自包含，约 100–160 MB，双击 `QSLauncher.exe` 即用。**默认交付。**
- `QSLauncher-Lite-win-x64.zip`：依赖 .NET 10 Desktop Runtime，体积小，作为可选产物。

不采用单文件自解压：WPF 自包含单文件运行时仍可能往临时目录释放原生库，反而破坏“完全便携”的可验证性。以文件夹形式交付更干净。

---

## 4. 便携性设计

### 4.1 目录布局

```text
QSLauncher/
  QSLauncher.exe
  *.dll
  data/
    config.json                 # 启动器设置
    accounts.dat                # 账户；微软/第三方令牌用 DPAPI 加密
    backgrounds/                # 用户导入的背景图片/视频副本
    themes/                     # 自定义调色板预设
    instances/
      <instance-id>/
        instance.json           # 实例配置
        game/                   # 该实例的游戏目录
          mods/
          resourcepacks/
          shaderpacks/
          saves/
          config/
          options.txt
          logs/
    meta/
      versions/                 # 版本 JSON、client.jar、加载器版本
      libraries/                # 共享库
      assets/                   # 共享资源与资源索引
      java/                     # 启动器下载的 Java 运行时
      authlib-injector/         # 第三方登录注入器（官方发布物，校验哈希）
    cache/
      metadata/                 # 版本清单、Modrinth 元数据缓存
      icons/                    # 已转码的模组图标
    downloads/                  # 临时下载与 .part 文件
    logs/                       # 启动器日志、诊断包
    quarantine/                 # 清理资源的隔离区，可恢复
```

### 4.2 关键规则

- 默认游戏根目录就是 `data/`，不碰 `%APPDATA%\.minecraft`。
- 所有路径以 `AppContext.BaseDirectory` 为基准解析，绝不依赖当前工作目录。
- 实例的 `gameDir` 存相对路径，移动整个目录后仍然有效。
- 共享 `assets`、`libraries`、`versions`，实例只隔离 `game/`，避免每个版本重复下载数 GB 资源。
- 支持把已有 `.minecraft` 目录作为“外部实例”接入，但不主动改写其文件，除非用户执行安装/修复。
- 启动前检查目录可写、磁盘空间、路径长度；只读目录/U 盘写保护时给出明确提示，不静默失败。
- 配置写入使用“临时文件 + 原子替换 + 备份”，避免断电/崩溃导致 JSON 损坏。
- 同一数据目录只允许一个启动器实例运行，防止并发写坏文件。
- 删除程序目录即完成卸载；不保留系统残留。
- 微软/第三方账户令牌使用 Windows DPAPI 加密，绑定当前 Windows 用户。**换电脑/换用户后需要重新登录**，这是安全换来的限制，计划中明确告知，不存明文密码。
- 用户选择背景图片/视频时复制到 `data/backgrounds/`，记录相对路径；不引用原文件，避免移动后失效。
- 视频背景仅在窗口可见且未最小化时播放；切换页面不重建播放器；低性能模式自动暂停视频并使用首帧。
- 所有主题、背景、透明度设置写入 `config.json`，换机拷贝后保留；令牌除外。

### 4.3 路径兼容

- 支持空格、中文、日文、长路径。
- 为进程设置正确的工作目录与参数数组，避免手工拼接引号。
- 对超长路径使用 `\\?\` 前缀的 Win32 文件操作；应用清单声明 `longPathAware`。
- 高 DPI：PerMonitorV2，100%–200% 缩放不糊、不裁切。

---

## 5. 界面与交互设计

### 5.1 视觉方向

工作主题名：**深岩翡翠（Deepslate Emerald）**。

- 三种主题模式：深色、浅色、跟随系统；切换时使用短促的颜色过渡，不闪白。
- 主色默认翡翠绿 `#4ADE80`；辅助色琥珀 `#F5B942`；危险色珊瑚红 `#FF6B6B`。
- **自定义调色板**：内置 10 组原创预设（翡翠、海盐、暮橙、鸢尾、莓红、青瓷、石墨、沙金等），并提供 HSV 色盘、色相条、十六进制输入；实时预览按钮、选中态、进度条、焦点环。切换主题时保留自定义色，不强行覆盖。
- **背景**：无背景/图片/视频三种模式。图片支持 PNG/JPG/BMP/WebP；视频支持系统 Media Foundation 可解码的 MP4/H.264、WMV 等。可设置填充方式、模糊、静音、循环；视频在最小化时暂停。壁纸不叠加全屏暗化、渐变或内容遮罩，保持原始亮度。
- **磨砂玻璃**：侧栏、卡片、弹窗、任务中心使用半透明层；透明度 46%–100% 可调，背景模糊 0–40px 可调，另有细微噪点和 1px 高光描边模拟磨砂质感。面板自身保留可读下限，但不遮挡整张壁纸。
- 背景使用 Windows 11 Mica 材质；有自定义背景时关闭 Mica，避免材质冲突；系统不支持 Mica 时退化为渐变。
- 深色底色 `#0C1117`，卡片 `#121A23`；浅色底色 `#F4F6F8`，卡片 `#FFFFFF`；描边为低对比度半透明线。
- 圆角：卡片 14px，控件 9px；8px 间距网格。
- 字体：Segoe UI Variable + 微软雅黑 UI 回退；日志用 Cascadia Mono / Consolas。
- 图标全部使用矢量 Path 或系统图标字体，不引入版权不明的图标包。
- 首页主视觉为原创等距方块插画（XAML 绘制），不使用 Minecraft 官方素材。
- **原创动效语言**：页面进入为 12px 上移 + 淡入；卡片悬停为 1.5px 抬升与描边提亮；弹窗从 96% 缩放进入；进度条使用柔和的流光。统一 180ms/240ms 两档，遵守系统“减少动画”设置。全部曲线与节奏为本项目自定义，不复制 PCL2。
- 完整重写 Button、TextBox、ComboBox、ScrollBar、ToggleSwitch、Slider、ProgressBar、Tab、ToolTip、ContextMenu，避免默认 WPF 灰色控件破坏整体感。

### 5.2 窗口结构

```text
┌────────────────────────────────────────────────────────────┐
│ 自定义标题栏：Logo / 任务中心 / 账户 / 窗口按钮                    │
├──────────────┬─────────────────────────────────────────────┤
│ 侧边导航      │ 页面内容                                     │
│ 首页          │                                             │
│ 版本与实例    │  卡片、列表、详情抽屉、空状态、骨架屏          │
│ 整合包        │                                             │
│ 模组          │                                             │
│ Java          │                                             │
│ 资源          │                                             │
│ 设置          │                                             │
│              │                                             │
│ 账户切换      │                                             │
└──────────────┴─────────────────────────────────────────────┘
```

- 默认 1280×800，最小 980×620；窗口位置与尺寸限制在当前屏幕工作区内。
- 宽度不足时详情面板改为覆盖式抽屉。
- 任务中心是独立页面，切页不丢失进度；没有任务时也保留入口并显示空状态。
- 所有破坏性操作二次确认；删除存档默认进回收站/隔离区。

### 5.3 页面

1. **首页**
   - 当前实例大卡片：版本、加载器、Java、模组数、上次游玩。
   - 主按钮“启动游戏”；旁边“打开目录”“编辑实例”。
   - 最近实例横向卡片。
   - 磁盘占用摘要、可用空间、快捷入口。
2. **版本与实例**
   - “已安装 / 全部版本”两个视图。
   - 远程版本：正式版/快照/旧版筛选、搜索、发布日期、安装状态。
   - 安装弹窗：原版 / 加载器两个页签，选择版本、加载器版本、实例名、Java、内存，展示预计下载量。
   - 实例详情：概览、内容、设置、日志四个页签。
3. **整合包（优先）**
   - “发现 / 本地导入”切换。
   - Modrinth 整合包搜索、筛选、详情、版本列表。
   - 安装向导：解析 manifest → 展示游戏版本/加载器/文件数/下载量 → 选择实例名与 Java → 安装 → 完成页。
   - 本地 `.mrpack` 拖入或选择文件；解析失败给出具体字段错误。
   - 安装过程中显示“原版 → 加载器 → 文件 → 覆盖层”的阶段进度，可取消并回滚。
4. **模组**
   - “发现 / 已安装”切换。
   - 搜索、加载器筛选、游戏版本筛选、分类、排序。
   - 模组卡片：图标、名称、作者、简述、下载量、兼容标记。
   - 已安装列表：启用开关、更新徽标、依赖状态、删除、打开目录。
   - 支持把 `.jar` 拖进窗口导入。
5. **Java**
   - 已检测运行时：厂商、版本、架构、来源、被哪些实例使用。
   - “下载 Java”弹窗：Mojang 官方组件 / Adoptium LTS。
   - 扫描本地、手动指定路径、测试、设为默认、删除。
6. **资源**
   - 磁盘占用条：versions / libraries / assets / java / instances / cache / logs。
   - 校验与修复、清理未使用资源（先进隔离区）。
   - 实例资源快捷入口：模组、资源包、光影、存档。
7. **设置**
   - **外观**：深色/浅色/跟随系统、自定义调色板、图片/视频背景、背景暗化/模糊、磨砂透明度。
   - **账户**：正版 Microsoft、离线、LittleSkin/自定义 Yggdrasil；登录状态与刷新。
   - 下载并发、代理、默认内存、默认 Java、游戏根目录、日志级别、关于/许可证。
   - “导出诊断包”（自动脱敏令牌与账户信息）。
8. **任务中心**
   - 进行中/已完成/失败，进度、速度、剩余时间。
   - 暂停、继续、取消、重试、全部暂停。
   - 失败项显示可读原因和“复制错误”。

### 5.4 状态与可访问性

- 每个异步页面都有骨架屏、空状态、错误状态，不出现假死白屏。
- 键盘可完成主要操作；焦点环清晰；Tab 顺序合理。
- 控件有 AutomationName；颜色对比度满足 WCAG AA。
- 错误信息使用“发生了什么 + 可能原因 + 下一步”，不直接甩异常堆栈。
- 日志窗口可搜索、暂停滚动、复制、打开完整日志。

---

## 6. 实装功能清单

标记说明：

- **P0**：本计划承诺实装，逐项验收。
- **P1**：需要额外外部条件，确认后才计入。
- **X**：明确不做。

### 6.1 启动器外壳与视觉（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| U1 | 自定义窗口、标题栏、导航、主题 | 深/浅色切换正常；Win11 有 Mica，Win10 有降级背景 |
| U2 | 首页仪表盘 | 显示当前实例、启动按钮、最近实例、磁盘摘要 |
| U3 | 全局搜索 | 能搜索已安装实例、远程版本、已安装模组 |
| U4 | 任务中心 | 下载中切换页面不丢进度；可暂停/继续/取消/重试 |
| U5 | 完整控件样式 | 无默认灰色 WPF 控件；悬停/按下/禁用/焦点态完整 |
| U6 | 高 DPI 与窗口状态记忆 | 100%/125%/150%/200% 不裁切；记住窗口大小与位置 |
| U7 | 原创动画与减少动画 | 自有动效语言；系统开启减少动画时自动关闭；不复用 PCL2 曲线/时长 |
| U8 | 主题三态 | 深色/浅色/跟随系统即时切换；重启保留 |
| U9 | 自定义调色板 | 预设 + HSV 色盘 + HEX 输入；按钮/选中/进度/焦点同步；对比度检查 |
| U10 | 图片/视频背景 | 导入后复制到 data；填充/暗化/模糊可调；视频循环/静音/最小化暂停；格式不支持有提示 |
| U11 | 磨砂玻璃 | 透明度 0–100%、背景模糊 0–40px 实时可调；侧栏/卡片/弹窗生效；低性能模式可关闭模糊 |

### 6.2 账户（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| A1 | 离线账户创建/编辑/删除 | 名称校验；生成稳定离线 UUID；可切换 |
| A2 | 离线启动参数 | 单机/局域网可进游戏；明确提示离线账户不能进正版服务器 |
| A3 | 账户切换与上次使用 | 重启后保持选择；不存明文密码 |
| A4 | 微软正版登录 | 设备码流程；Xbox/XSTS/Minecraft 服务链路；令牌 DPAPI 加密；过期自动刷新；未配置客户端 ID 时给出设置指引 |
| A5 | 正版权益检查 | 无游戏所有权时给出明确提示，不尝试绕过 |
| A6 | LittleSkin 登录 | 预置 `https://littleskin.cn/api/yggdrasil`；账号密码换取 accessToken；选择角色；令牌加密保存 |
| A7 | 自定义 Yggdrasil | 可填写兼容 Yggdrasil 的 API Root；校验 metadata、签名公钥与 skinDomains |
| A8 | 第三方启动 | 自动准备 authlib-injector，启动时注入 API Root；角色 UUID/名称/令牌正确传入 |
| A9 | 账户资料 | 显示角色名、UUID、皮肤来源；第三方角色可刷新；不显示密码 |

> A4 需要一个在 Microsoft Azure 注册的公共客户端 ID，并且该应用要能被 Minecraft 服务接受。启动器不内置任何他人的客户端 ID，也不内置密钥。设置页提供 `Microsoft Client ID` 输入与配置说明；未配置时，登录按钮进入引导而不是报未知错误。
>
> A6/A7 使用标准 Yggdrasil 认证，不保存明文密码。LittleSkin 若启用二次验证，按服务端错误提示用户完成验证；不绕过服务端安全策略。

### 6.3 原版下载与安装（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| V1 | 官方版本清单 | 拉取 `version_manifest_v2.json`；缓存；离线时可用缓存展示 |
| V2 | 远程版本浏览 | 正式版/快照/旧版筛选、搜索、排序；显示安装状态 |
| V3 | 原版安装 | 下载版本 JSON、client.jar、libraries、assets、logging；全部校验 SHA-1 |
| V4 | 下载可靠性 | 并发、断点续传、失败重试、取消、限速、磁盘空间预检 |
| V5 | 资源完整性 | 资产索引与 objects 全量下载；支持旧版 virtual assets |
| V6 | 安装最新版快捷入口 | 一键安装当前最新正式版 |
| V7 | 安装结果校验 | 安装完成后可解析出完整启动命令；缺文件能指出具体文件 |
| V8 | 版本修复 | 哈希校验所有已安装文件；缺失/损坏项重新下载 |
| V9 | 版本删除 | 被实例引用时阻止或明确警告；可选保留存档 |
| V10 | 加载器版本安装 | 加载器安装后生成独立版本条目，能与原版共存 |

### 6.4 Java 环境管理（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| J1 | 本地 Java 扫描 | 扫描启动器目录、常见 JDK/JRE 目录、`JAVA_HOME`、`PATH`；解析版本/架构/厂商 |
| J2 | 官方 Java 下载 | 根据版本 JSON 的 `javaVersion.component` 从 Mojang 官方运行时索引下载；SHA-1 校验 |
| J3 | Java LTS 备用源 | Mojang 不可用时可选 Adoptium Temurin 8/17/21/25；SHA-256 校验 |
| J4 | 安装与验证 | 下载后执行 `java -version` 验证；记录 `runtime.json` |
| J5 | 自动匹配 | 按游戏版本要求选择 Java；旧 Forge 自动提示 Java 8；可手动覆盖 |
| J6 | 实例级 Java | 每个实例可独立指定 Java；显示“由谁使用” |
| J7 | Java 测试与删除 | 启动测试进程；未被使用时允许删除；删除进隔离区 |
| J8 | Java 兼容性提示 | Java 过高/过低时在启动前警告，并给出建议版本 |
| J9 | 不捆绑 JRE | 默认不打包 Java；安装时按需下载，保持安装包体积可控 |

### 6.5 版本与实例管理（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| I1 | 实例列表 | 卡片/列表展示名称、版本、加载器、最后游玩、模组数 |
| I2 | 新建实例 | 从已安装版本或“安装新版本”创建；可设名称、图标色、内存、Java、分辨率 |
| I3 | 实例隔离 | 每个实例独立 `game/`；模组、存档、配置互不影响 |
| I4 | 编辑实例 | 改名、内存、JVM 参数、游戏参数、分辨率、全屏、Java |
| I5 | 复制实例 | 复制配置与可选复制存档/模组；生成新 UUID |
| I6 | 删除实例 | 二次确认；可选保留存档；运行中禁止删除 |
| I7 | 打开目录 | 打开实例根目录、mods、saves、logs |
| I8 | 游玩统计 | 记录最后游玩时间、累计时长、启动次数 |
| I9 | 导入已有游戏目录（P1） | 选择 `.minecraft` 或实例目录，识别版本与模组，不主动破坏原文件 |
| I10 | 实例导出/导入（P1） | 导出配置与可选内容为 ZIP；导入时处理路径与版本缺失 |

### 6.6 模组加载器（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| L1 | 加载器可用性查询 | 按游戏版本查询 Fabric / Quilt / Forge / NeoForge 可用版本 |
| L2 | 推荐版本 | 显示推荐/最新/稳定标记；测试版给出提示 |
| L3 | Fabric 安装 | 通过 Fabric 官方安装器或官方元数据生成版本；校验产物 |
| L4 | Quilt 安装 | 通过 Quilt 官方安装器；校验产物 |
| L5 | Forge 安装 | 通过 Forge 官方安装器；支持 1.12.2 及以上常用版本；自动选 Java 8/17/21/25 |
| L6 | NeoForge 安装 | 通过 NeoForge 官方安装器；支持 1.20.1 及以上 |
| L7 | 隔离安装 | 在临时 staging 目录运行安装器，成功后再导入共享元数据，失败不留半成品 |
| L8 | 安装日志 | 实时显示安装器输出；失败时保留日志并可复制 |
| L9 | 安装后验证 | 解析加载器版本 JSON、主类、库列表；生成可启动实例 |
| L10 | 加载器更新提示 | 检测新版本并提示；不静默自动升级，避免坏档 |
| L11 | 无可用加载器 | 明确显示“该游戏版本上游暂无此加载器”，不伪造选项 |

支持边界：加载器是否可用取决于上游；Forge 很老的版本、NeoForge 1.20.1 之前的版本不承诺。所有安装器均为官方发布物。

### 6.7 整合包安装（P0，优先于单体模组）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| P1 | Modrinth 整合包发现 | 搜索、分类、加载器/游戏版本筛选、分页；详情页显示简介、文件数、下载量 |
| P2 | 版本与文件选择 | 选择整合包版本；显示依赖、文件数、预计下载量、发布类型 |
| P3 | `.mrpack` 解析 | 校验 zip 结构、`modrinth.index.json`、`formatVersion`、`game`、`files`、`dependencies` |
| P4 | 依赖装游戏 | 根据 `dependencies.minecraft` 自动安装对应原版（已安装则复用） |
| P5 | 依赖装加载器 | 根据 `fabric-loader` / `quilt-loader` / `forge` / `neoforge` 自动安装加载器 |
| P6 | 文件下载 | 按 `files[].downloads` 下载；SHA-1/SHA-512 校验；支持多 URL 回退 |
| P7 | 环境过滤 | `env.client` 为 `unsupported` 的文件跳过；`optional` 由用户勾选 |
| P8 | 路径安全 | 拒绝绝对路径、`..`、盘符、保留设备名；所有文件必须落在实例目录内 |
| P9 | 覆盖层 | 解压 `overrides/` 到实例；如需支持 `client-overrides/` 一并处理；不覆盖用户已有存档除非确认 |
| P10 | 安装向导 | 展示“原版 → 加载器 → 文件 → 覆盖层”阶段进度；可取消；失败清理半成品 |
| P11 | 本地导入 | 拖入/选择 `.mrpack`；离线时只要文件 URL 可达即可安装 |
| P12 | 安装记录 | 记录整合包来源、版本、文件清单、安装时间；支持“打开实例”“启动游戏” |
| P13 | 冲突与覆盖 | 同名文件明确覆盖策略；记录被覆盖文件；重要文件先备份 |
| P14 | 整合包更新（P1） | 检查同项目新版本；显示变更；不静默覆盖用户配置 |
| P15 | CurseForge 整合包（P1） | 仅用户自备 API Key 时支持；不内置密钥、不抓取网页 |

> CurseForge 的 `.zip` 整合包本身通常不含模组文件，必须通过 CurseForge API 下载。没有 API Key 时，本期只完整支持 Modrinth `.mrpack` 与本地已带文件的通用 ZIP；不伪装成“支持全部整合包”。

### 6.8 模组安装与管理（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| M1 | Modrinth 搜索 | 关键词、加载器、游戏版本、分类、排序；分页/加载更多 |
| M2 | 项目详情 | 图标、标题、作者、简介、下载量、许可证、兼容版本 |
| M3 | 版本选择 | 按实例的加载器+游戏版本过滤；显示版本号、类型、日期 |
| M4 | 安装 | 下载到实例 `mods/`；SHA-1/SHA-512 校验；原子落盘 |
| M5 | 必需依赖 | 递归解析并安装必需依赖；检测循环；展示将安装的清单 |
| M6 | 可选依赖 | 列出可选依赖，由用户勾选；不偷偷安装 |
| M7 | 冲突与重复 | 按 mod id 与文件哈希检测重复/多版本；给出处理建议 |
| M8 | 已安装管理 | 启用/禁用（`.jar` ↔ `.jar.disabled`）、删除、打开目录 |
| M9 | 更新检查 | 对比已安装版本与最新兼容版本；单条/批量更新；保留启用状态 |
| M10 | 本地导入 | 文件选择或拖拽 `.jar`；解析 fabric/quilt/forge/neoforge 元数据 |
| M11 | 兼容性提示 | 加载器或游戏版本不匹配时警告；不阻止用户自担风险安装 |
| M12 | 网络与缓存 | 正确 User-Agent；429 退避；元数据缓存；失败可重试 |
| M13 | CurseForge 模组（P1） | 仅当用户自备 API Key 时启用；不内置密钥、不抓取网页 |
| M14 | 整合包内模组 | 由整合包安装的文件不强行纳入单模组更新，避免破坏整合包版本 |

### 6.9 版本资源管理（P0）

这里的“版本资源管理”包含两层：

1. 每个实例的内容：模组、资源包、光影包、存档、日志。
2. 全局共享资源：versions、libraries、assets、java、cache、downloads。

| 编号 | 功能 | 验收标准 |
|---|---|---|
| R1 | 实例内容总览 | 显示 mods/资源包/光影/存档数量与占用 |
| R2 | 资源包管理 | 列出 ZIP/文件夹；导入、删除、打开目录；读取 `pack.mcmeta` 名称/格式 |
| R3 | 资源包启用 | 安全编辑 `options.txt` 的 `resourcePacks`；修改前备份；游戏运行中禁止 |
| R4 | 光影包管理 | 列出/导入/删除/打开目录；说明启用由 Iris/OptiFine 在游戏内完成 |
| R5 | 存档管理 | 读取世界名、图标、大小、修改时间；备份为 ZIP；导入；删除到回收站 |
| R6 | 共享资源占用 | 按 versions/libraries/assets/java/instances/cache 分类统计 |
| R7 | 共享资源校验 | 对版本 JSON、client.jar、libraries、assets 做哈希校验，列出损坏项 |
| R8 | 共享资源修复 | 只重下缺失/损坏文件；保留正常文件 |
| R9 | 清理未使用资源 | 计算所有已安装版本引用集合；未引用项移入隔离区，可恢复 |
| R10 | 缓存/日志清理 | 清理临时下载、旧日志、图标缓存；不影响已安装实例 |
| R11 | 版本文件查看 | 查看版本 JSON 摘要、主类、Java 要求、资源索引、库数量 |
| R12 | 打开目录 | 每个资源分类都能在资源管理器中打开 |
| R13 | 运行中保护 | 游戏运行时禁止删除/修改该实例内容 |

### 6.10 启动引擎（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| G1 | 版本继承 | 正确合并 `inheritsFrom`：libraries、arguments、mainClass、assets、logging |
| G2 | 规则判断 | 正确处理 os.name/os.arch/os.version 与 features |
| G3 | 库与 natives | 解析 classifiers、解压 natives、排除 META-INF、处理覆盖规则 |
| G4 | 资源参数 | 正确传 `--assetsDir`、`--assetIndex`、虚拟资源目录 |
| G5 | 参数模板 | 按版本 JSON 注入 username/uuid/accessToken/userType/gameDir 等占位符 |
| G6 | JVM 参数 | 内存、GC 预设、自定义 JVM 参数、`java.library.path`、日志配置 |
| G7 | 游戏参数 | 分辨率、全屏、自定义游戏参数；参数使用数组传递，避免引号问题 |
| G8 | 工作目录隔离 | 进程工作目录为实例 `game/`；不污染启动器目录 |
| G9 | 启动前检查 | Java 可用、版本文件完整、账户有效、目录可写、内存不超物理内存 |
| G10 | 进程与日志 | 隐藏控制台、异步捕获 stdout/stderr、写 latest.log、检测退出码 |
| G11 | 崩溃提示 | 检测 crash-reports；展示摘要、日志位置、复制诊断 |
| G12 | 结束游戏 | 可请求结束进程树；异常退出有明确状态 |
| G13 | 离线 UUID | 基于 `OfflinePlayer:<name>` 的稳定 UUID；不冒充正版 |
| G14 | 启动失败可诊断 | 常见错误映射：Java 版本、内存、缺失库、加载器不兼容、路径权限 |

### 6.11 任务、设置与诊断（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| S1 | 统一下载队列 | 所有下载进入同一队列；优先级、并发、重试、暂停/继续 |
| S2 | 进度与速度 | 文件级+总进度；速度/剩余时间；UI 不卡顿 |
| S3 | 设置持久化 | 主题/调色板/背景/磨砂、语言、并发、代理、内存、Java、游戏根目录 |
| S4 | 代理 | 系统代理/直连/自定义 HTTP 代理；不在日志泄露代理密码 |
| S5 | 日志 | 启动器日志按天滚动；实例日志分离；可搜索 |
| S6 | 诊断包 | 导出脱敏 ZIP：版本、配置、日志、错误摘要 |
| S7 | 单实例运行 | 重复启动聚焦已有窗口，不并发写数据 |
| S8 | 关于与许可证 | 版本号、第三方许可证、数据源说明 |
| S9 | 离线可用 | 无网络时可启动已安装实例、查看本地实例与日志 |

### 6.12 工程与交付（P0）

| 编号 | 功能 | 验收标准 |
|---|---|---|
| E1 | 解决方案分层 | App / Core / Tests；Core 不引用 WPF |
| E2 | 单元测试 | 规则判断、继承合并、参数拼装、离线 UUID、路径安全、依赖解析 |
| E3 | 构建脚本 | 一条命令产出 Portable 与 Lite 两个 ZIP |
| E4 | 便携验收 | 移到含空格/中文路径、换盘符、U 盘后仍可启动 |
| E5 | 净机验收 | 无 .NET、无 Java 的干净 Windows 上，自包含版可启动并完成安装 |
| E6 | 无 JS/Python 审计 | 仓库无 `.js`/`.py` 运行时代码与相关依赖 |
| E7 | 文档 | 中文 README：使用、构建、数据目录、常见问题、许可证 |

---

## 7. 明确不做（本期）

| 项 | 原因 |
|---|---|
| Minecraft Bedrock/基岩版 | 与 Java 版启动链路完全不同 |
| 正版验证绕过、盗版联机、假 token | 不提供规避正版机制的能力 |
| 皮肤/披风编辑器 | 非本期核心，且需要额外账户 API |
| 服务器列表浏览/服务器状态 | 非本期核心 |
| 复用 PCL2 的 UI/动画/素材 | 只参考功能类别；界面与动效必须原创 |
| 无 API Key 的 CurseForge 整合包下载 | API 需要密钥，不能内置；仅在用户自备 Key 时作为 P1 |
| 伪造正版/第三方登录成功 | 不绕过服务端验证、不伪造权益 |
| 在线视频流/网页背景 | 只支持本地图片/视频文件，不加载网页或远程媒体 |
| 自动更新器 | 便携应用自动替换自身风险高；本期提供手动下载新 ZIP |
| 光影自动配置 | 依赖 Iris/OptiFine 各自配置，本期只做文件管理 |
| 世界编辑/地图预览 | 非启动器职责 |
| macOS/Linux | 仅 Windows |
| 第三方下载镜像 | 默认只用官方源；镜像会引入完整性与信任问题 |

---

## 8. 官方数据源

| 用途 | 地址/来源 | 认证 | 说明 |
|---|---|---|---|
| 版本清单 | `https://piston-meta.mojang.com/mc/game/version_manifest_v2.json` | 无 | 启动器启动时拉取并缓存 |
| 版本元数据 | 清单中的 `url` | 无 | 含 libraries、assets、client、logging、javaVersion |
| 客户端/库/资源 | 版本 JSON 内的 URL | 无 | 全部按 SHA-1 校验 |
| Java 运行时索引 | `https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json` | 无 | 按 `windows-x64` + `javaVersion.component` 取 manifest |
| Java 运行时文件 | 组件 manifest 内的 `raw` URL | 无 | 按文件 SHA-1 校验 |
| Java 备用源 | `https://api.adoptium.net/v3/...` | 无 | Temurin LTS；SHA-256 |
| Fabric | `https://meta.fabricmc.net/v2/...`、Fabric 官方安装器 | 无 | 元数据不可达时走安装器 |
| Quilt | `https://meta.quiltmc.org/v3/...`、Quilt 官方安装器 | 无 | 已验证可访问 |
| Forge | `https://files.minecraftforge.net/...`、`https://maven.minecraftforge.net/...` | 无 | 官方安装器 |
| NeoForge | `https://maven.neoforged.net/...` | 无 | 官方安装器 |
| 模组/整合包 | `https://api.modrinth.com/v2/...` | 无 | 必须带 User-Agent；遵守速率限制 |
| 整合包文件 | Modrinth 版本文件中的 CDN URL | 无 | `.mrpack`；校验 SHA-1/SHA-512 |
| 微软登录 | Microsoft identity platform device code | 公共客户端 ID | 需要自有/获准的 Azure 应用 |
| Xbox/Minecraft 认证 | Xbox Live、XSTS、Minecraft services | 无 | 仅正版账户；不绕过权益 |
| LittleSkin | `https://littleskin.cn/api/yggdrasil` | 用户名/密码换 token | 标准 Yggdrasil；API Root 可自定义 |
| 第三方注入器 | `https://github.com/yushijinhun/authlib-injector` 官方 Release | 无 | 下载后校验 SHA-256；启动时 `-javaagent` |
| 第三方角色资料 | `{apiRoot}/sessionserver/session/minecraft/profile/{uuid}` | accessToken | 名称、UUID、皮肤属性 |

实现时只允许向白名单主机发请求；所有下载 URL 必须来自上述官方元数据，禁止让远端 JSON 把文件写到任意路径。

---

## 9. 启动流程

```text
选择实例
  → 读取 instance.json
  → 解析版本链 inheritsFrom
  → 合并 libraries / arguments / mainClass / assets / logging
  → 检查 Java（实例指定 > 版本要求 > 默认）
  → 检查账户与令牌
  → 生成 classpath、natives、资源参数、JVM/游戏参数
  → 启动前完整性检查（缺失则提示修复）
  → 启动 java.exe，工作目录 = 实例 game/
  → 捕获日志、检测崩溃报告、更新游玩统计
```

关键点：

- 加载器版本和原版版本通过继承链合并，而不是复制整份 JSON。
- 参数使用 `ProcessStartInfo.ArgumentList`，避免空格、中文、引号注入问题。
- Natives 解压到实例专属临时目录，启动后按需清理，避免多实例互相覆盖。
- 旧版资源需要 `virtual/legacy` 映射，不能只下载 objects。
- 第三方账户启动时追加 `-javaagent:authlib-injector.jar=<apiRoot>`；角色 UUID/名称/accessToken 来自该 Yggdrasil 服务，不冒充微软账户。
- 微软账户使用 `userType=msa`；离线账户使用 `legacy`；第三方使用对应注入器支持的类型。
- 整合包安装完成后复用同一套启动引擎，不为整合包维护第二套启动代码。
- 启动器自身不提升权限，不修改系统 Java 环境变量。

---

## 10. 架构

```text
src/
  QSLauncher.App/          WPF：窗口、页面、控件、主题、ViewModel、导航
    Themes/                颜色、控件样式、动效、调色板
    Views/
    ViewModels/
    Controls/              磨砂卡片、色盘、背景宿主、标题栏
    Interop/               Mica、暗色标题栏、内存、回收站、窗口特效
  QSLauncher.Core/         无 UI 依赖
    Models/                设置、账户、实例、整合包、版本
    Metadata/              版本清单、版本 JSON、加载器元数据
    Downloads/             队列、断点续传、哈希、重试
    Install/               原版、加载器、Java、模组、整合包安装
    Launch/                继承合并、规则、参数、进程、authlib 注入
    Auth/                  微软设备码、离线、Yggdrasil/LittleSkin
    Mods/                  Modrinth、依赖解析、本地元数据
    Modpacks/              .mrpack 解析、文件计划、覆盖层
    Java/                  扫描、下载、验证
    Storage/               便携路径、原子写、隔离区、占用统计
  QSLauncher.Tests/        xUnit
build/
  publish.ps1
docs/
```

服务边界：

- `IVersionCatalogService`：远程清单与缓存。
- `IVersionInstaller`：原版安装、修复。
- `ILoaderInstaller`：四种加载器。
- `IJavaService`：扫描、下载、验证、选择。
- `IInstanceService`：实例 CRUD 与隔离。
- `IModService`：Modrinth 查询、依赖、安装、更新。
- `ILaunchService`：解析与启动。
- `IDownloadQueue`：统一任务队列。
- `IStorageService`：路径、原子写、隔离区、统计。
- `IAccountService`：离线账户、微软令牌、Yggdrasil/LittleSkin。
- `IModpackService`：`.mrpack` 解析、安装计划、覆盖层、回滚。
- `IAppearanceService`：主题、调色板、背景、磨砂参数与持久化。
- `IDialogService` / `IToastService` / `INavigationService`：UI 抽象。

原则：所有 I/O 异步；UI 线程不做网络/解压/哈希；下载队列是唯一写下载缓存的入口；失败可回滚；关键状态可重建。

---

## 11. 数据模型示例

`data/instances/<id>/instance.json`：

```json
{
  "schemaVersion": 1,
  "id": "3f2c9a1b",
  "name": "1.21.8 Fabric",
  "versionId": "fabric-loader-0.17.2-1.21.8",
  "loader": { "type": "fabric", "version": "0.17.2" },
  "gameDir": "instances/3f2c9a1b/game",
  "java": { "mode": "auto", "path": null, "major": 21 },
  "memoryMb": 4096,
  "jvmArgs": [],
  "gameArgs": [],
  "resolution": { "width": 1280, "height": 720, "fullscreen": false },
  "createdAt": "2026-10-20T12:00:00+08:00",
  "lastPlayedAt": null,
  "playtimeSeconds": 0
}
```

`data/config.json` 的外观部分：

```json
{
  "schemaVersion": 1,
  "appearance": {
    "themeMode": "system",
    "accentColor": "#4ADE80",
    "background": {
      "mode": "video",
      "path": "backgrounds/aurora.mp4",
      "fit": "fill",
      "dim": 0.45,
      "blur": 12,
      "muted": true,
      "loop": true
    },
    "glass": {
      "opacity": 0.72,
      "blur": 18,
      "noise": 0.06
    }
  },
  "accounts": { "lastAccountId": "offline-1" },
  "downloads": { "concurrency": 8 },
  "java": { "defaultRuntimeId": null },
  "microsoft": { "clientId": "" }
}
```

- `gameDir` 为相对 `data/` 的路径；外部实例才使用绝对路径。
- 账户令牌不写入该文件。
- 配置带 `schemaVersion`，后续升级可迁移。
- 背景文件复制后使用相对路径；源文件删除不影响启动器。

---

## 12. 可靠性与安全

- 所有下载先写 `.part`，校验通过后原子移动；校验失败删除重下。
- 解压防止 Zip Slip：规范化目标路径，禁止逃出目标目录。
- Java 运行时 manifest 中的路径同样做安全检查。
- 只允许 HTTPS；证书校验使用系统默认，不跳过。
- 日志脱敏：access token、refresh token、Authorization、代理密码不落盘。
- 执行外部程序仅限：用户选择的 `java.exe`、官方加载器安装器 JAR、authlib-injector JAR。执行前展示来源与 SHA-256/SHA-1。
- 不自动执行模组 JAR；模组只被游戏进程加载。
- 删除内容默认进隔离区或回收站，降低误删风险。
- 下载并发可调，默认 8；对 Modrinth 等 API 单独限流。
- 第三方登录只向用户确认的 API Root 发送凭据；HTTP 必须为 HTTPS；登录失败不记录密码。
- 背景图片/视频只做本地解码，不解析远程网页；视频解码器缺失时回退静态首帧并提示。
- 整合包所有条目按 Zip Slip 规则校验；拒绝设备名、绝对路径、ADS 流、符号链接。
- 自定义调色板检查文字对比度；低于阈值时自动加深/提亮文字，而不是让用户看不清。

---

## 13. 里程碑与验收门

| 阶段 | 交付 | 验收门 |
|---|---|---|
| M0 方案确认 | v0.2 计划、技术选型、界面结构、原创性边界 | 已确认 |
| M1 外壳与外观底座 | 解决方案、便携数据层、三态主题、调色板、图片/视频背景、磨砂、导航、设置、日志 | 双击启动；外观实时调整并持久化；移动目录后设置仍在；不写 AppData |
| M2 原版 + Java + 启动 | 官方清单、原版安装、Java 下载、启动引擎、离线账户 | 在净机安装 26.3，自动下载 Java 25，启动到主菜单 |
| M3 加载器 + 整合包（优先） | Fabric/Quilt/Forge/NeoForge；`.mrpack` 在线/本地安装；覆盖层；回滚 | 选一个 Fabric/NeoForge 整合包，自动装原版+加载器+模组并启动 |
| M4 账户 + 模组 | 微软设备码、LittleSkin/自定义 Yggdrasil、authlib-injector；Modrinth 单模组 | LittleSkin 登录并进第三方服务器；正版登录（配置 ID 后）通过；模组依赖补齐 |
| M5 实例/资源 | 实例隔离、修复、删除、资源包/光影/存档、占用统计、清理 | 两个实例互不影响；损坏库能被修复；清理可恢复 |
| M6 打磨与交付 | 动效、空/错状态、无障碍、诊断包、便携 ZIP、测试 | 干净 Windows 双击即用；无 JS/Python；便携与路径验收通过 |

每个阶段结束都产出可运行版本，不做“最后一次性集成”。

---

## 14. 风险与对策

| 风险 | 影响 | 对策 |
|---|---|---|
| 微软登录需要获准的 Azure 客户端 ID | 无 ID 时无法完成正版登录 | 设置页配置自有 ID；UI/流程完整；未配置时只禁用该入口，不伪造 ID |
| LittleSkin/Yggdrasil 服务端差异 | 第三方登录失败 | 严格按 metadata/认证响应处理；显示服务端错误；LittleSkin 预置 + 自定义 Root；不绕过二次验证 |
| authlib-injector 与新版本兼容性 | 第三方启动崩溃 | 使用官方最新 Release；校验哈希；可在设置中替换版本；日志明确 |
| 整合包格式差异/大文件 | 安装失败、磁盘占用高 | 只承诺 `.mrpack` + 本地完整 ZIP；预检空间；分阶段进度；失败回滚；校验哈希 |
| Forge/NeoForge 安装器行为差异大 | 某些版本安装失败 | 全部在 staging 运行官方安装器；保留原始日志；建立版本矩阵；失败可回滚 |
| Fabric 元数据在当前网络超时 | 加载器列表加载失败 | 官方安装器路径 + 重试 + 代理 + 清晰错误；不使用非官方镜像 |
| Mojang/Modrinth 网络波动 | 下载中断 | 断点续传、重试、并发控制、缓存、离线启动 |
| 自包含 WPF 体积较大 | 下载/拷贝慢 | 同时提供 Lite 版；默认 Portable 保证净机可跑 |
| 未签名 exe 触发 SmartScreen | 首次运行被拦 | 发布 SHA-256 校验值；文档说明；后续可选代码签名 |
| 便携目录权限不足 | 无法写数据 | 启动时预检；提示移动到用户可写目录；不自动提权 |
| DPAPI 令牌不可跨机 | 换机需重登 | 明确提示；不存明文；离线账户不受影响 |
| 老版本 Forge 需要 Java 8 | 启动失败 | 按版本/加载器自动选择 Java；给出明确提示 |
| 26.x 新版本、加载器跟进滞后 | 加载器暂不可用 | 不硬编码版本；按上游实时可用性展示；不伪造 |
| 资源清理误删 | 实例损坏 | 引用集合计算 + 隔离区 + 可恢复 + 二次确认 |
| 视频背景解码依赖系统编解码器 | 部分 WebM/MKV 无法播放 | 明确支持矩阵；失败回退首帧；低性能模式暂停视频 |
| 磨砂/透明导致文字对比度不足 | 可读性差 | 自动对比度保护、暗化滑块、浅色主题专用描边、可一键恢复默认 |
| 参考 PCL2 越界 | 版权/原创性问题 | 只使用公开功能类别；不接触其代码与素材；设计稿和动效参数独立记录 |

---

## 15. 已确认与仍需配置的事项

已确认：

1. 主题：深色/浅色/跟随系统 + 自定义调色板。
2. 背景：本地图片/视频 + 暗化/模糊/填充方式。
3. 组件：半透明磨砂，透明度可调。
4. 整合包：优先于单体模组，先做 Modrinth `.mrpack`。
5. 登录：正版 Microsoft、离线、LittleSkin 及自定义 Yggdrasil。
6. 模组/整合包来源：Modrinth 官方 API；CurseForge 仅 BYO Key，不作为默认。
7. 资源管理：实例内容 + 全局 assets/libraries 校验/修复/清理/统计。
8. UI：原创“深岩翡翠”视觉与动效；PCL2 仅功能参考。

仍需外部配置（不阻塞开发，阻塞对应功能的真机验收）：

- **Microsoft Client ID**：需要你在 Azure 注册一个公共客户端应用，或提供已获准的 ID。启动器会把它保存到设置中，不内置任何密钥。没有它时，正版登录入口显示配置引导。
- **LittleSkin 账户**：用于验收第三方登录和皮肤/角色显示；代码可先用模拟 Yggdrasil 服务做单元测试。
- **视频素材**：用于验收视频背景的 MP4/H.264 文件；启动器不内置受版权保护的视频。
- **整合包样本**：建议选一个 Fabric 或 NeoForge 的 `.mrpack` 作为真实安装验收样本。

视频背景首版支持 Windows Media Foundation 能解码的格式（MP4/H.264、WMV 等）。WebM/MKV 若系统无解码器，会回退首帧并提示；不为此引入浏览器内核。

---

## 16. 本计划完成后你会得到

- 一个可双击运行的 Windows 便携启动器目录和 ZIP。
- 能从官方源安装原版、四种加载器、Modrinth 整合包与模组。
- 能自动下载并管理 Java 25/21/17/8。
- 能使用离线、微软正版、LittleSkin/自定义 Yggdrasil 账户。
- 能隔离管理多个实例及其模组、资源包、光影、存档。
- 能校验/修复/清理共享资源，并看到磁盘占用。
- 深/浅/跟随系统主题、自定义调色板、图片/视频背景、可调磨砂。
- 完整中文 README、构建脚本、测试与许可证说明。
- 仓库内无 JavaScript、无 Python。
- 原创 UI/动效设计说明；不包含 PCL2 的代码、素材或界面复刻。
