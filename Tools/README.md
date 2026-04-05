# BlogTools

BlogTools 是一款专为 Jekyll 静态博客（如 Chirpy 主题）打造的现代化、高颜值的 Windows 桌面端管理工具。采用 .NET 10 WPF 与 [WPF UI](https://github.com/lepoco/wpfui) 构建，深度融合了 Windows 11 Fluent Design（流畅设计体系），并提供了一站式的“写作-管理-发布”体验。

## ✨ 核心特性

- 🎨 **现代 Fluent UI 界面**：支持沉浸式的深色/浅色模式，以及系统级过渡动画，给您原生的 Windows 11 视觉体验。
- 📝 **实时 Markdown 预览编辑器**：
  - 基于 WebView2 的极速实时预览。
  - 完美支持 **KaTeX 数学公式**（内嵌本地资产，免网络加载）。
  - 支持快捷配置文章分类（双分类支持）与动态标签组。
  - 草稿与“一键上线”功能：自动执行 Git Commit & Push 并触发云端部署（如 Cloudflare Pages/GitHub Actions）。
- 📊 **可视化仪表盘**：直观展示网站状态（总文章数、分类数、标签数），一键跨源同步 (Git Pull) 以及防止文件冲突的安全拦截机制。
- 🗂️ **本地文章管理**：全局极速检索、双击编辑、彻底删除。
- ⚙️ **环境与 SEO 统一设置**：无需手动修改 `_config.yml`，可通过 UI 直观配置网站标题、一句话副标题、SEO 描述、社交账号链接等全局参数。

## 🚀 快速开始

### 系统依赖
1. **操作系统**：Windows 10 / Windows 11。
2. **运行时环境**：[.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download)
3. **环境工具**：本地需安装 [Git](https://git-scm.com/)，并确保其已配置好向远端仓库推送的免密认证（SSH 或 Credential Helper）。
4. **WebView2**：系统需安装有 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)。

### 编译与运行
```bash
# 克隆仓库
git clone <your-repo-url>
cd BlogTools

# 使用 .NET CLI 编译
dotnet build

# 运行程序
dotnet run
```

## 🛠️ 技术栈

- **框架**: .NET 10 (WPF / C#)
- **UI 库**: [WPF UI (Lepo.co)](https://wpfui.lepo.co/)
- **Markdown 引擎**: [Markdig](https://github.com/xoofx/markdig)
- **预览内核**: Microsoft.Web.WebView2
- **排版引擎**: KaTeX (Local Assets)
- **数据结构**: YamlDotNet (解析 Jekyll `_config.yml` 与 Front Matter)

## 📁 目录结构

```text
BlogTools/
├── Assets/                 # 静态资源 (KaTeX 等离线渲染库)
├── Models/                 # 数据模型 (BlogPost 等)
├── Services/               # 核心服务
│   ├── GitService.cs       # Git 自动化拉取/合并不防冲突封装
│   └── JekyllService.cs    # 静态文件解析与保存服务
├── Helpers/                # UI 与逻辑辅助类
├── Views/
│   ├── DashboardPage.xaml  # 仪表盘控制台
│   ├── EditorPage.xaml     # Markdown 沉浸式编辑器
│   ├── ManagePostsPage.xaml# 文章库管理检索
│   └── SettingsPage.xaml   # 网站环境配置 (Config)
└── App.xaml / MainWindow   # 应用程序入口与主导航界框
```

## 🤝 贡献与反馈

如果您在使用过程中遇到任何 Bug 或是希望增加新特性，欢迎提交 Issue。
本软件作为私人 Jekyll 博客维护者的效率工具包，会根据 Jekyll/Chirpy 主题的基础目录结构约定 (`_posts`, `_config.yml`) 进行优化适配。

## 📄 许可证

MIT License
