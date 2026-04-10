# JekyllCli 🚀

English | [简体中文](../README.md) | [Chinese User Guide](USER_GUIDE_ZH.md)

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
   ├── Tools/          // WPF local client project root
   │   ├── src/        // Pages, windows, services, models, and language resources
   │   ├── Assets/     // Application icons & KaTeX embedded resources
   │   └── App.xaml    // Entry point configuration
   ├── .github/
   └── README.md
   ```
3. **Run Locally**:
   - Enter the `Tools` directory and execute:
   ```bash
   dotnet run
   ```

---

## IV. Custom Packaging & Automated Release

If you add private features in `Tools` and want to publish it yourself:

### Method 1: Single File Publishing
Navigate to `Tools/` in your command line and run the following command to generate a single `.exe` file:
```powershell
dotnet publish
```

### Method 2: GitHub Actions Automation (Recommended)
This project has built-in **GitHub Actions release workflows** (`.github/workflows/release.yml`):
- Simply push a Tag starting with **`v`** (e.g., v1.0.0) locally:
  ```bash
  git tag v1.0.0
  git push origin v1.0.0
  ```
- GitHub will automatically compile, compress, and publish to the Release page in the cloud.

---

## V. Version Changelog

- **v1.6.8**: Continued polishing the writing page: improved the readability of the find-and-replace popup; added a richer editor context menu plus working arrow-key navigation and `Shift + Arrow` selection inside the editor; preview links now stay inside the app with icon-only back, forward, and return-to-preview controls; also hardened WebView2 recovery to reduce crashes after the writing page sits idle.
- **v1.6.7**: Added a find-and-replace popup to the writing page that matches the existing visual language, with `Ctrl+F` to open, previous/next navigation, replace, replace all, match-case support, and `Esc` to close; when a post title changes, the app now moves the related `assets/img/inposts` image folder and updates image references in both the body and front matter; also added `Esc` blur behavior for metadata inputs so editor focus handling feels smoother.
- **v1.6.6**: Fixed the Manage Posts page so mouse-wheel scrolling works reliably over the grid and feels consistently smooth across different hover regions; also added a `PerMonitorV2` DPI-aware application manifest to improve blurry rendering under Windows display scaling.
- **v1.6.5**: Split the previous all-in-one settings screen into two dedicated pages: Blog Settings and App Settings. Blog Settings now focuses on site metadata, navigation tabs, favicon management, and save/publish actions, while App Settings centralizes workspace path selection, writing preferences, appearance controls, and update management. The App Settings entry was also moved to the lower-left area of the navigation pane, while Blog Settings stays in the original settings position.
- **v1.6.4**: Fixed the global font option in Settings so the selected font now applies consistently to the main window, navigation pane, title bar, and post management list; also removed manual text entry from the font dropdown and kept it as a searchable picker.
- **v1.6.3**: Added three writing layouts for the editor: Write Only, Split, and Preview Only, with draggable split sizing that can snap from split view into single-pane modes; redesigned the top mode switch, save draft, publish, and clear actions so they share the same visual language as the editor toolbar; refined tag entry so `Enter` adds the tag and exits input, while `Esc` discards the current text and immediately removes focus.
- **v1.6.2**: Streamlined the startup update prompt so a newly detected version can be downloaded directly from the dialog instead of sending users to Settings; added a dedicated download progress window and kept the post-download confirmation flow for applying the update immediately.
- **v1.6.1**: Fixed the Markdown toolbox buttons so they keep a readable background and clearer contrast in dark theme; changed the heading tools to render stable `H1/H2/H3/H4` labels and replaced the divider tool with a clean horizontal-line glyph to avoid missing or garbled Fluent icon characters.
- **v1.6.0**: Upgraded the writing area into a draggable, customizable Markdown toolbox with quick actions for headings, emphasis, lists, code blocks, tables, and more, and allowed favorite tools to be pinned to the center rail; added the link insertion dialog, richer hover tips, and a setting that controls whether pinning keeps a copy in the toolbox; improved mouse/touch scrolling and modified-date sorting on the post management page, fixed WPF build failures caused by intermediate-output permission issues, and reorganized the `Tools` source tree into a dedicated `src/` layout.
- **v1.5.0**: Added an "Insert Link" shortcut to the writing area with a dialog for display text, URL, and same-tab/new-tab behavior; introduced a unified hover lift effect for common UI components so the interface feels lighter and more responsive.
- **v1.4.1**: Fully implemented UI internationalization with adaptive English/Chinese switching; extended physics-based smooth scrolling to all pages for an ultra-smooth experience; fixed several hardcoded Chinese strings.
- **v1.4.0**: Added a physics-based spring algorithm (exponential decay) for the settings page smooth scrolling engine; added an optional auto-completion feature for the last modified time; fixed scrolling bugs in the font dropdown list and added search support; supported single-file publishing for the minimal version.
- **v1.3.3**: Fixed the issue where GitHub API update detection silently failed on some networks without a system proxy; added a quick jump-to-GitHub-Star entry in the settings interface.
- **v1.3.2**: Optimized the executable anti-collision destruction mechanism using the rename method; fixed the issue where independent icons did not take effect.
- **v1.3.1**: Introduced new calm and professional icons, and implemented automatic adaptive switching logic for light and dark theme icons.
- **v1.3.0**: Comprehensively refactored the build chain to achieve true single-file publishing; introduced an in-app self-hot-update mechanism based on "executable file renaming".
- **v1.2.0**: Optimized the logic for generating a single EXE, stripped out redundant XML documents, and reduced the release package size.
- **v1.1.0**: Supported quick upload of article illustrations, WebView2 synchronous scrolling, and visual configuration of Bilibili social links.
- **v1.0.0**: JekyllCli official release, supporting blog directory binding and basic management features.

---

## VI. Open Source License

1. The overall architecture and the WPF `Tools` auxiliary program are developed by the author and are open-sourced under the **GPLv3 License**.
2. The [Chirpy code](https://github.com/cotes2020/jekyll-theme-chirpy) under the `Blog` folder complies with the original **MIT License**.
