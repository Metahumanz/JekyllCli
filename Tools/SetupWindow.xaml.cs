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
            string defaultPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Blog"));
            if (Directory.Exists(defaultPath) && File.Exists(Path.Combine(defaultPath, "_config.yml")))
            {
                BlogPathBox.Text = defaultPath;
            }
            else
            {
                // Standalone release build test
                defaultPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Blog"));
                if (Directory.Exists(defaultPath) && File.Exists(Path.Combine(defaultPath, "_config.yml")))
                {
                    BlogPathBox.Text = defaultPath;
                }
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
