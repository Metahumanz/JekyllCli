# JekyllCli 🚀

English | [简体中文](../README.md) | [Chinese User Guide](USER_GUIDE_ZH.md) | [English User Guide](USER_GUIDE_EN.md)
Also available on [my blog](https://pacil.dpdns.org/posts/JekyllCli-intro)

JekyllCli is a **modern, polished Windows desktop management toolkit** built for Jekyll static blogs, with deep adaptation for the [Chirpy theme](https://github.com/cotes2020/jekyll-theme-chirpy). It combines a Fluent Design interface with a low-friction setup experience, making blog maintenance and writing feel as easy as working with local notes.

This project provides both a **full package with built-in templates** and a **lightweight clean package**, covering bloggers at different stages.

---

## ✨ Features

- 🎨 **Adaptive Icon Switching**: Introduces a newly designed calm and professional icon set with automatic light/dark theme switching. The window icon and taskbar icon update in real time with the app theme.
- 📦 **True Single-File Publishing**: The compiled app is now a single `JekyllCli.exe`. Managed libraries, native DLLs, and the KaTeX rendering engine are bundled into the EXE for true unzip-and-run usage.
- 🔄 **In-App Hot Update**: Adds a self-update flow based on a rename-based conflict-prevention mechanism. On startup, the app silently checks the latest GitHub Release and supports one-click download, replacement, and restart inside the app.

---

## 📖 Table of Contents

- [I. Quick Start (Beginner Path)](#i-quick-start-beginner-path)
- [II. Initial Setup and Usage](#ii-initial-setup-and-usage)
- [III. Advanced: Modifying the Source Code](#iii-advanced-modifying-the-source-code)
- [IV. Custom Packaging and Automated Release](#iv-custom-packaging-and-automated-release)
- [V. Version Changelog](#v-version-changelog)
- [VI. Open Source License](#vi-open-source-license)

---

## I. Quick Start (Beginner Path)

If you just want to use this tool to manage your static blog without touching the underlying code, follow these steps:

1. **Install dependencies**:
   - Make sure your Windows PC has the [.NET 10.0 Desktop Runtime](https://dotnet.microsoft.com/download) installed.
   - Install and configure [Git](https://git-scm.com/) in the command line, since the app uses Git for cloud synchronization under the hood.
2. **Download guide**:
   Go to the repository's **[Releases page](https://github.com/Metahumanz/JekyllCli/releases)** and choose the package that fits your needs:

   | Recommended For | File Name | Contents | Notes |
   | :--- | :--- | :--- | :--- |
   | **Beginners / Quick Trial** | `JekyllCli-win-x64-bundle.zip` | **Tool + blog template** | **Recommended**. Unzip and start right away, with a complete blog foundation included. |
   | **Existing blog / advanced users** | `JekyllCli-win-x64-minimal.zip` | **Tool only (single EXE)** | Extremely lightweight. Best for users who already have a local blog directory. |

   > 💡 **Tip**: The app supports **automatic update checking**. When a new release is available, you will get a startup prompt and can complete the upgrade directly from there.

3. **Run the tool**:
   - Unzip the package and double-click `JekyllCli.exe`.

## II. Initial Setup and Usage

If this is your **first time launching the app**, here is how to finish the initial blog setup.

### 1. Link the blog data source

The first launch of JekyllCli opens an **auto-height welcome wizard**:

- **Choose a blog directory**:
  - **Existing blog**: directly select the local folder that contains `_config.yml`.
  - **Start from scratch**: if you downloaded the `bundle` package, click `Use built-in template` to initialize in one step.
- **Choose language and time zone**:
  Select your language in the second step of the wizard. The app will help write the language and time zone settings automatically.

### 2. Core interface features

- **Dashboard overview**: read local Markdown statistics in real time.
- **Fluent writing editor**:
  - **Formula support**: built-in KaTeX math rendering with offline support.
  - **Synchronized scrolling**: two-way positioning between editing and preview.
- **Modern settings panel**:
  - **Update center**: one-click detection and silent in-app upgrades.
  - **Static resource upload**: supports batch importing website favicons and blog illustrations.

---

## III. Advanced: Modifying the Source Code

The desktop app is written in **C# 10 + WPF + WPF UI (.NET 10)**.

1. **Environment preparation**:
   - Use **Visual Studio 2022**, **Rider**, or another IDE with the `.NET Desktop Development` workload installed.
2. **Understand the directory structure**:
   ```text
   JekyllCli/
   ├── Blog/           // Official clean Chirpy Starter blog files
   ├── Tools/          // WPF local client project root
   │   ├── src/        // Pages, windows, services, models, and language resources
   │   ├── Assets/     // App icons and KaTeX embedded resources
   │   └── App.xaml    // Entry configuration
   ├── .github/
   └── README.md
   ```
3. **Build and run locally**:
   - Enter the `Tools` directory and run:
   ```powershell
   dotnet run
   ```

---

## IV. Custom Packaging and Automated Release

If you add private features under `Tools` and want to publish your own build:

### Method 1: Single-file publishing

Open a terminal in `Tools/` and run:

```powershell
dotnet publish
```

### Method 2: GitHub Actions automation (recommended)

This project already includes a **GitHub Actions release workflow** in `.github/workflows/release.yml`:

- Push a tag that starts with **`v`** locally, for example:
  ```bash
  git tag v1.0.0
  git push origin v1.0.0
  ```
- GitHub will automatically build, compress, and publish the release package in the cloud.

---

## V. Version Changelog

- **v1.6.9**: Replaced the old dark-mode toggle in App Settings with a full `Follow System / Dark / Light` theme mode selector, and made that choice apply consistently to the main window, setup wizard, link dialog, and update progress window with matching theme-aware icons; also unified the corner radii of cards and buttons across the dashboard, editor, post management, and settings views for a more cohesive overall look.
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
- **v1.0.0**: Official JekyllCli release with blog directory binding and basic management features.

---

## VI. Open Source License

1. The overall architecture and the WPF `Tools` companion application are developed by the author and released under the **GPLv3 License**.
2. The [Chirpy code](https://github.com/cotes2020/jekyll-theme-chirpy) in the `Blog` folder follows the original **MIT License**.
