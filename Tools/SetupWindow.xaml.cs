using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BlogTools.Services;
using Microsoft.Win32;

namespace BlogTools
{
    public partial class SetupWindow : Wpf.Ui.Controls.FluentWindow
    {
        public string SelectedBlogPath { get; private set; } = string.Empty;
        public bool IsSetupSuccessful { get; private set; } = false;

        public SetupWindow()
        {
            InitializeComponent();
            
            // Search for adjacent "Blog" directory by default
            string defaultPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Blog"));
            if (!Directory.Exists(defaultPath))
            {
                // Development mode check
                defaultPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Blog"));
            }

            if (Directory.Exists(defaultPath))
            {
                if (File.Exists(Path.Combine(defaultPath, "_config.yml")))
                {
                    BlogPathBox.Text = defaultPath;
                    CreateBlogExpander.IsExpanded = false;
                }
                UseBundleBtn.IsEnabled = true;
                UseBundleBtn.ToolTip = $"检测到内置模板：{defaultPath}";
            }
            else
            {
                UseBundleBtn.IsEnabled = false;
                UseBundleBtn.ToolTip = "未找到内置模板文件夹 (Blog/)，请从 GitHub 拉取或手动选择。";
                CreateBlogExpander.IsExpanded = true;
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = "请选择 Chirpy 博客本地根目录"
            };

            if (dialog.ShowDialog() == true)
            {
                BlogPathBox.Text = dialog.FolderName;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsSetupSuccessful = false;
            Close();
        }

        private void UseBundle_Click(object sender, RoutedEventArgs e)
        {
            string bundlePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Blog"));
            if (!Directory.Exists(bundlePath))
            {
                bundlePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Blog"));
            }

            if (Directory.Exists(bundlePath))
            {
                BlogPathBox.Text = bundlePath;
                CreateBlogExpander.IsExpanded = false;
                ErrorBar.IsOpen = false;
            }
            else
            {
                ErrorBar.Message = "内置模板已丢失！请尝试从 GitHub 拉取。";
                ErrorBar.IsOpen = true;
            }
        }

        private async void CloneRemote_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "选择克隆博客的存放目录",
                FileName = "MyNewBlog", // Just as a target folder name hint
                Filter = "Folder|*.directory" // Hacky way to let user pick folder or use SaveFileDialog for path
            };

            // Use OpenFolderDialog since we already use Microsoft.Win32 (though WPF-UI has its own)
            var folderDialog = new OpenFolderDialog
            {
                Title = "选择存放克隆博客的父目录"
            };

            if (folderDialog.ShowDialog() == true)
            {
                string parentDir = folderDialog.FolderName;
                string targetPath = Path.Combine(parentDir, "JekyllBlog");
                
                if (Directory.Exists(targetPath))
                {
                    ErrorBar.Message = $"目录 {targetPath} 已存在，请选择其他位置。";
                    ErrorBar.IsOpen = true;
                    return;
                }

                SetLoadingState(true, "正在从 GitHub 拉取 Chirpy 模板 (可能需要较长时间)...建议保持网络畅通");
                
                try
                {
                    // Using a standard starter template URL for Chirpy
                    string repoUrl = "https://github.com/cotes2020/jekyll-theme-chirpy"; 
                    var result = await GitService.CloneAsync(repoUrl, targetPath);
                    
                    if (Directory.Exists(targetPath) && File.Exists(Path.Combine(targetPath, "_config.yml")))
                    {
                        BlogPathBox.Text = targetPath;
                        SetLoadingState(false, "拉取成功！");
                        CreateBlogExpander.IsExpanded = false;
                    }
                    else
                    {
                        SetLoadingState(false, "");
                        ErrorBar.Message = "拉取完成但未检测到有效的 Jekyll 配置，请检查网络或 Git 是否安装。";
                        ErrorBar.IsOpen = true;
                    }
                }
                catch (Exception ex)
                {
                    SetLoadingState(false, "");
                    ErrorBar.Message = $"克隆失败: {ex.Message}";
                    ErrorBar.IsOpen = true;
                }
            }
        }

        private void SetLoadingState(bool isLoading, string status)
        {
            InitProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            InitStatusText.Visibility = !string.IsNullOrEmpty(status) ? Visibility.Visible : Visibility.Collapsed;
            InitStatusText.Text = status;
            
            UseBundleBtn.IsEnabled = !isLoading;
            CloneRemoteBtn.IsEnabled = !isLoading;
            FinishBtn.IsEnabled = !isLoading; // Need to name Finish button in XAML
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            string path = BlogPathBox.Text;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || !File.Exists(Path.Combine(path, "_config.yml")))
            {
                ErrorBar.Message = "所选目录无效，必须包含 _config.yml 文件！";
                ErrorBar.IsOpen = true;
                return;
            }

            SelectedBlogPath = path;

            try
            {
                var jekyllContext = new JekyllService(path);
                var config = jekyllContext.LoadConfig();
                
                string lang = (LanguageBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "zh-CN";
                config["lang"] = lang;
                
                if (lang == "zh-CN") {
                    config["timezone"] = "Asia/Shanghai";
                }
                
                jekyllContext.SaveConfig(config);
            }
            catch (Exception ex)
            {
                ErrorBar.Message = $"初始修改配置失败: {ex.Message}";
                ErrorBar.IsOpen = true;
                return;
            }

            IsSetupSuccessful = true;
            Close();
        }
    }
}
