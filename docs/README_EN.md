# JekyllCli 🚀

English | [简体中文](../README.md)

JekyllCli is a **modern, high-aesthetic Windows desktop management toolset** tailored for Jekyll static blogs (deeply adapted for the [Chirpy theme](https://github.com/cotes2020/jekyll-theme-chirpy)). It combines a Fluent Design interface with a painless configuration experience, making the operation and collaboration of tech blogs as simple as writing local notes.

This project offers both a **Full version with built-in templates** and a **Lightweight clean version**, meeting the needs of bloggers at different stages.

---

## ✨ Features

- 🎨 **Adaptive Icon Switching**: Introduces newly designed, calm, and professional icons, with an adaptive logic for light and dark themes. The window and taskbar icons will switch in real-time according to the application's theme color.
- 📦 **True Single-File Publishing**: The compiled tool is now a single `JekyllCli.exe`. All managed libraries, native DLLs, and the KaTeX rendering engine are perfectly integrated inside the EXE, enabling true out-of-the-box usage.
- 🔄 **In-App Hot Update**: Added a self-updating feature based on a "rename collision prevention mechanism". The tool silently checks for the latest GitHub Release upon startup, supports one-click downloading, and completes automatic replacement and restart within the app.
- 🌊 **Physics-Based Fluid Scrolling**: Enjoy an incredibly smooth UI experience with custom exponential decay (spring-like) physics algorithms powering all document and dropdown scrolling.

---

## 📖 Table of Contents

- [I. Quick Start (For Beginners)](#i-quick-start-for-beginners)
- [II. Initial Configuration & Usage](#ii-initial-configuration--usage)
- [III. Advanced: Modifying the Source Code](#iii-advanced-modifying-the-source-code)
- [IV. Custom Packaging & Automated Release](#iv-custom-packaging--automated-release)
- [V. Version Changelog](#v-version-changelog)
- [VI. Open Source License](#vi-open-source-license)

---

## I. Quick Start (For Beginners)

If you just want to use this tool to manage your static blog without touching the underlying code, please follow these steps:

1. **Install Dependencies**:
   - Make sure your Windows PC has the [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download) installed.
   - [Git](https://git-scm.com/) must be installed and configured in the command line (the tool uses Git under the hood to synchronize with the cloud).
2. **Download Guide (MUST READ)**:
   Go to the repository's **[Releases page](https://github.com/Metahumanz/JekyllCli/releases)** and choose the file based on your needs:

   | Recommended For | File Name | Contents | Features |
   | :--- | :--- | :--- | :--- |
   | **Beginners/Quick Trial** | `JekyllCli-win-x64-bundle.zip` | **Tool + Blog Template** | **Highly Recommended**. Ready to use right after unzipping, comes with a complete blog foundation. |
   | **Existing Blog/Veterans** | `JekyllCli-win-x64-minimal.zip` | **Tool Only (Single EXE)** | Extremely minimal. Only one EXE, suitable for users who already have a local blog directory. |

   > 💡 **Tip**: This tool supports **automatic update checking**. Once a new version is released, you will receive a pop-up notification upon startup, and you can click to complete a seamless upgrade.

3. **Run the Tool**:
   - Unzip the package and double-click `JekyllCli.exe` to start.

## II. Initial Configuration & Usage

If this is your **first time starting the tool**, how do you complete the blog configuration?

### 1. Link Blog Data Source
Opening JekyllCli for the first time will bring up an **auto-height-adapting initial welcome wizard**:
- **Select Blog Directory**:
    - **Existing Blog**: Directly specify the local folder containing `_config.yml`.
    - **Start from Scratch**: If you downloaded the `bundle` version, click "Use built-in template" for a one-click initialization.
- **Select Language and Time Zone**: Choose your language from the dropdown menu in the second step of the wizard. The system will automatically write your time zone and language settings.

### 2. Core Features
- **Dashboard Overview**: Read local Markdown statistics in real-time.
- **Fluent Writing Editor**:
  - **Formula Support**: Built-in KaTeX math formula engine, supporting offline rendering.
  - **Synchronized Scrolling**: Two-way positioning between preview and editing.
- **Modern Settings Panel**:
  - **Update Center**: One-click detection and silent in-app upgrade execution.
  - **Static Resource Upload**: Supports batch uploading of website Favicons and blog illustrations.

---

## III. Advanced: Modifying the Source Code

The desktop client of this project is written in **C# 10 + WPF + WPF UI (.NET 10)**.

1. **Environment Preparation**:
   - **Visual Studio 2022** / **Rider** or other IDEs, equipped with the `.NET Desktop Development` workload.
2. **Understanding Directory Structure**:
   ```text
   JekyllCli/
   ├── Blog/           // Clean Chirpy Starter blog files
   ├── Tools/          // All source code for the WPF local client
   │   ├── Assets/     // Application icons & KaTeX embedded resources
   │   ├── Services/   // Core logic: Git invocation, Jekyll parsing, update services
   │   └── App.xaml    // Entry point configuration
   ├── .github/
   └── README.md
   ```
3. **Run Locally**:
   - Enter the `Tools` directory and execute:
   ```bash
   dotnet build
   dotnet run
   ```

---

## IV. Custom Packaging & Automated Release

If you add private features in `Tools` and want to publish it yourself:

### Method 1: Single File Publishing
Navigate to `Tools/` in your command line and run the following command to generate a single `.exe` file:
```powershell
dotnet publish BlogTools.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o ../publish_output
```

### Method 2: GitHub Actions Automation (Recommended)
This project has built-in **GitHub Actions release workflows** (`.github/workflows/release.yml`):
- Simply push a Tag starting with **`v`** (e.g., v1.4.0) locally:
  ```bash
  git tag v1.4.0
  git push origin v1.4.0
  ```
- GitHub will automatically compile, compress, and publish to the Release page in the cloud.

---

## V. Version Changelog

- **v1.4.0 (2026-04-09)**: Added a physics-based spring algorithm (exponential decay) for a global ultra-smooth scrolling engine; added an optional auto-completion feature for the last modified time; fixed scrolling bugs in the font dropdown list and added search support; supported single-file publishing for the minimal version.
- **v1.3.3 (2026-04-09)**: Fixed the issue where GitHub API update detection silently failed on some networks without a system proxy; added a quick jump-to-GitHub-Star entry in the settings interface.
- **v1.3.2**: Optimized the executable anti-collision destruction mechanism using the rename method; fixed the issue where independent icons did not take effect.
- **v1.3.1**: Introduced new calm and professional icons, and implemented automatic adaptive switching logic for light and dark theme icons.
- **v1.3.0**: Comprehensively refactored the build chain to achieve true single-file publishing; introduced an in-app self-hot-update mechanism based on "executable file renaming".
- **v1.2.0**: Optimized the logic for generating a single EXE, stripped out redundant XML documents, and reduced the release package size.
- **v1.1.0**: Supported quick upload of article illustrations, WebView2 synchronous scrolling, and visual configuration of Bilibili social links.
- **v1.0.0**: JekyllCli official release, supporting blog directory binding and basic management features.

---

## VI. Open Source License

1. The overall architecture and the WPF `Tools` auxiliary program are developed by the original author and are open-sourced under the **GPLv3 License**.
2. The Chirpy code under the `Blog` folder complies with the **MIT License**.
