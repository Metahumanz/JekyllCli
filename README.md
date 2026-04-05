# JekyllCli 🚀

JekyllCli 是一款专为 Jekyll 静态博客（深度适配 [Chirpy 主题](https://github.com/cotes2020/jekyll-theme-chirpy)）打造的**现代化、高颜值 Windows 桌面管理工具集**。它结合了 Fluent Design 界面与无痛配置体验，让技术博客的运营和协作跟写本地笔记一样简单。

本项目同时提供**自带模板的完整版**与**轻量纯净版**，满足不同阶段博主的需求。

---

## ✨ v1.1.1 全新特性

- 📦 **双版本发布**：提供自带 `Blog` 模板的 `Bundle` 版与不带模板的 `Minimal` 纯净版。
- 🛠️ **轻量级更迭**：抛弃了原本臃肿的 Velopack 自动更新机制，回归纯净绿色的解压即用模式。
- 🖼️ **文章图片与网站图标**：编辑器新增 `上传本地图片(Alt+I)` 快捷导入，自动归档并插入 Markdown；设置页支持一键导入 Favicons 网站图标集。
- 🧭 **站点导航管理**：新增 `_tabs` 导航栏选项卡的可视化配置（包括 About, Archives, Categories, Tags）。
- 📺 **B站深度适配**：直接深度读取并写入 `_data/contact.yml`，完美将推特替换为 Bilibili，原生兼容 Chirpy 最新规范。

---

## 📖 目录

- [一、快速开始（小白直通车）](#一快速开始小白直通车)
- [二、初始配置与如何使用](#二初始配置与如何使用)
- [三、进阶：如何修改工具源码](#三进阶如何修改工具源码)
- [四、自行打包与自动化发布](#四自行打包与自动化发布)
- [五、开源协议](#五开源协议)

---

## 一、快速开始（小白直通车）

如果您只是想用这个工具来管理您的静态博客，而不想碰底层代码，请参考以下步骤：

1. **安装环境依赖**：
   - 请确保您的 Windows 电脑上安装了 [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download)。
   - 需要安装并在命令行配置过 [Git](https://git-scm.com/)（工具底层会调用 Git 帮您同步云端）。
2. **下载指南 (README 必看)**：
   前往仓库的 **[Releases 页面](https://github.com/Metahumanz/JekyllCli/releases)**，根据你的需求选择文件：

   | 推荐人群 | 下载文件名 | 包含内容 | 特点 |
   | :--- | :--- | :--- | :--- |
   | **新手/快速试用** | `JekyllCli-win-x64-bundle.zip` | **工具 + 博客模板** | **最推荐**。解压即开即用，自带完整的博客基座。 |
   | **长期使用/追求稳定** | `JekyllCli-win-Setup.exe` | **安装程序** | 将工具安装到系统，支持桌面快捷方式和全自动静默更新。 |
   | **已有博客/老鸟** | `JekyllCli-win-x64-minimal.zip` | **仅工具** | 纯净极简。适合已有博客目录，只需一个管理工具的用户。 |

   > 💡 **提示**：从此版本开始，本工具为绿色免安装无后台拉取的纯粹应用，您可以随时下载新版本直接覆盖。

3. **运行工具**：
   - **压缩包版**：解开压缩包，双击运行 `JekyllCli.exe`。
   - **安装版 (Setup.exe)**：双击安装后，从桌面快捷方式启动。

## 二、初始配置与如何使用

如果您是**第一次启动该工具**，或者刚刚将本仓库克隆下来，该如何完成博客配置呢？

### 1. 链接博客数据源
首次打开 JekyllCli 会弹出**初始欢迎向导**：
- **选择博客目录**：
    - **已有博客**：直接指定包含 `_config.yml` 的本地文件夹。
    - **零基础开始**：如果你下载的是 `bundle` 版，点击“使用内置模板”即可一键完成初始化。
    - **从 GitHub 拉取**：点击“从 GitHub 拉取”，工具将自动克隆 Chirpy 官方模板库到你指定的路径。
- **选择语言和时区**：在向导第二步下拉菜单选择你的第一语言。如果是中国用户，系统将自动帮助写入 `Asia/Shanghai` 时区和中文设定。

### 2. 界面核心功能
- **仪表盘大盘**：实时读取本地 Markdown 共计写了多少文章。支持直接检测与远端仓库（比如 GitHub）的差异版本。
- **WebView2 沉浸式写作编辑**：
  - **分类联想**：新增一级/二级分类双下拉框，自动扫描历史库中的所有分类供快速选择，也支持直接键入新分类。
  - **公式支持**：内置 KaTeX 数学公式引擎，支持离线渲染。
  - **同步滚动**：支持编辑区与预览区的双向同步定位。
- **可视化设置面板**：
  - **外观定制**：读取并应用用户操作系统中的字体，让界面符合你的视觉审美。
  - **社交与导航配置**：支持 Bilibili 主页链接配置，以及文章导航栏 _tabs 的名称排序修改。
  - **静态资源上传**：支持批量上传网站 Favicons 与博客文章插图，自动存入 `assets/img/` 专属目录。
  - **更新中心**：一键检测并前往 GitHub Releases 下载最新版本覆盖。

---

## 三、进阶：如何修改工具源码

本项目的桌面端是用 **C# 10 + WPF + WPF UI (.NET 10)** 编写的。如果你懂 C#，你可以随心所欲定制这把瑞士军刀：

1. **环境准备**：
   - **Visual Studio 2022** / **Rider** 等集成开发环境，并装配 `.NET 桌面开发` 工作负载。
2. **理解目录结构**：
   ```text
   JekyllCli/
   ├── Blog/           // 官方干净 Chirpy Starter 博客文件存放处
   ├── Tools/          // WPF 本地客户端的所有源码存放处
   │   ├── Assets/     // 挂载如离线公式等需要随包被分发的外部资产
   │   ├── Services/   // 核心运行层：含调用 Git 命令解析，以及 `_config.yml`、Markdown FrontMatter 解析
   │   ├── Views/      // XAML 界面库
   │   └── App.xaml    // 入口配置
   ├── .github/
   └── README.md
   ```
3. **本地 Run 调试**：
   - 通过终端进入源码目录 `cd Tools`，执行：
   ```bash
   dotnet build
   dotnet run
   ```
4. 开发时如遇到数据源挂在旧地址引发卡顿或 bug，可手工清理掉隐藏在此目录生成的 `Settings.json` 即可重置缓存。

---

## 四、自行打包与自动化发布

如果您在 `Tools` 里加了私有功能，并且想分享给别人，可以通过这两种方式发布 release 版本：

### 方法一：使用本机 .NET CLI 本地发布
命令行进入 `Tools/` 后运行原生命令生成单一 `.exe` 文件（包含免安装依赖）：
```powershell
dotnet publish BlogTools.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -o ../publish_output
```
随后只需将生成的 `publish_output` 里面的所有文件打包进 zip 压缩包即可散发。

### 方法二：完全利用 GitHub Actions 云打包 (已配置！)
无需借助肉身操作，本项目已内置 **GitHub Actions 释放流** (`.github/workflows/release.yml`) 🚀：
- 从你的本地进行 Git 标记： 
  ```bash
  git tag v1.0.0
  git push origin v1.0.0
  ```
- 只要推送的标头为 **`v`**（如 v1.1, v2.0），都会自动触发云端 Windows 服务器编译环境！
- Action 会编译发布单文件版后，**自动在您的仓库页面创建 Release 并附加 Zip**，并附生成变更日志供别人下载。

> 📌 **最后注意关于您的博客本身发布（而非工具的发布）**： 强烈建议打开 `Blog` 上的 Actions 把这当做一个正常的云端构建使用（Push 即刷新博文），这部分请查阅 [Chirpy 的官方部署向导](https://chirpy.cotes.page/posts/getting-started/)。

---

## 五、开源协议

1. 本套件的整体架构及 WPF `Tools` 辅助程序由原始作者开发，**采用 GPLv3 协议开源**。
2. 在 `Blog` 文件夹下寄托的老 Chirpy 代码由 [@cotes2020](https://github.com/cotes2020) 等维护，原文件遵从的 **MIT 协议**。
