# JekyllCli

JekyllCli 是一款专为 Jekyll 静态博客（特别是基于 [Chirpy](https://github.com/cotes2020/jekyll-theme-chirpy) 主题）打造的现代化、高颜值的 Windows 桌面端管理工具集。

本项目包含两个主要部分：
- `Blog/`: 一份干净的 Chirpy 主题博客模板，作为全新博客的基地。
- `Tools/`: 基于 .NET 10 WPF 与 [WPF UI](https://github.com/lepoco/wpfui) 构建的一站式文章管理与发布套件。

[English Version](#english-documentation) | [中文说明](#中文说明)

---

## 中文说明

JekyllCli 深度融合了 Windows 11 Fluent Design（流畅设计体系），为您省去手动修改文件、写繁琐 Git 命令以及配置环境的麻烦。首次启动即可可视化初始化博客，支持一键推送托管并使用 GitHub Actions 实现自动化部署。

### ✨ 核心特性

- **现代 Fluent UI 界面**：支持沉浸式的深色/浅色模式，系统级微动画，提供原生的 Windows 程序体验。
- **文章创作与管理**：
  - 基于 WebView2 极速实时预览 Markdown，内嵌 **KaTeX 数学公式** 离线支持。
  - 轻松编辑文章的元数据（Title，分类，标签等）。
- **新手友好的配置项**：
  - **无忧的 Bilibili (B站) 集成**：只需要在 `_config.yml` 的 `social.links` 添加 B站链接，系统即可自动显示相应的图标！
  - **跨平台一键部署**：保存配置或编辑完文章后一键 Push，即可利用 **GitHub Actions** 自动编译并部署您的站点！

### 🚀 快速开始

1. **环境准备**：
   - 您的电脑需要安装 [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download)
   - 确保安装并配置了支持 SSH 的 [Git](https://git-scm.com/) 环境。
2. **下载与编译**：
   ```bash
   git clone https://github.com/yourname/JekyllCli.git
   cd JekyllCli/Tools
   dotnet run
   ```
3. **初始化博客配置**：
   在工具初次启动时，会自动进入**新手引导面板**。请按照说明选择根目录的 `Blog` 文件夹，并配置您需要的语言选项。

### 📄 开源许可证

本项目整体代码遵循 **GPLv3 协议** 开源。
注意：`Blog` 目录内的原生 Chirpy 主题模板遵循其原有作者规定的 **MIT 协议**。

---

## English Documentation

JekyllCli is a modern, beautiful Windows desktop management tool designed specifically for Jekyll static blogs (especially those based on the [Chirpy](https://github.com/cotes2020/jekyll-theme-chirpy) theme).

This repository contains two main parts:
- `Blog/`: A clean Chirpy theme blog template serving as a fresh base for new blogs.
- `Tools/`: An all-in-one article management and publishing suite built with .NET 10 WPF and [WPF UI](https://github.com/lepoco/wpfui).

### ✨ Features

- **Modern Fluent UI Interface**: Supports immersive dark/light modes and system-level animations for a native Windows 11 experience.
- **Article Creation & Management**:
  - Lightning-fast real-time Markdown preview via WebView2 with offline **KaTeX math formulas** support.
  - Effortlessly modify article metadata (Categories, Tags, Math toggles, etc.).
- **Beginner-Friendly Configuration**:
  - Easily configure your site config (`_config.yml`) without knowing YAML rules.
  - **One-click deployment via GitHub Actions**: Save your posts, click publish, and let your automated CI/CD flow build your blog for you.

### 🚀 Getting Started

1. **Requirements**:
   - [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download)
   - [Git](https://git-scm.com/) (configured with SSH auth to your remote repository).
2. **Run The Tool**:
   ```bash
   git clone https://github.com/yourname/JekyllCli.git
   cd JekyllCli/Tools
   dotnet run
   ```
3. **Setup**:
   The first time you start the app, a setup wizard will guide you to connect it to the `Blog` directory and set up your preferred site language.

### 📄 License

This repository as a whole is licensed under the **GPLv3 License**. 
Note: The source code within the `Blog/` directory strictly adheres to its original author's **MIT License**.
