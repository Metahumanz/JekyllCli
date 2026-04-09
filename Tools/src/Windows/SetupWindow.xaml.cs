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
            Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this);
            
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
                UseBundleBtn.ToolTip = string.Format(Application.Current.FindResource("SetupTooltipTemplateFound").ToString()!, defaultPath);
            }
            else
            {
                UseBundleBtn.IsEnabled = false;
                UseBundleBtn.ToolTip = Application.Current.FindResource("SetupTooltipTemplateNotFound").ToString();
                CreateBlogExpander.IsExpanded = true;
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog
            {
                Title = Application.Current.FindResource("SetupMsgInviteSelect").ToString()!
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
                ErrorBar.Message = Application.Current.FindResource("SetupMsgTemplateMissing").ToString()!;
                ErrorBar.IsOpen = true;
            }
        }

        private async void CloneRemote_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = Application.Current.FindResource("SetupMsgCloneSelect").ToString()!,
                FileName = "MyNewBlog", // Just as a target folder name hint
                Filter = "Folder|*.directory" // Hacky way to let user pick folder or use SaveFileDialog for path
            };

            // Use OpenFolderDialog since we already use Microsoft.Win32 (though WPF-UI has its own)
            var folderDialog = new OpenFolderDialog
            {
                Title = Application.Current.FindResource("SetupMsgCloneSelectParent").ToString()!
            };

            if (folderDialog.ShowDialog() == true)
            {
                string parentDir = folderDialog.FolderName;
                string targetPath = Path.Combine(parentDir, "JekyllBlog");
                
                if (Directory.Exists(targetPath))
                {
                    ErrorBar.Message = string.Format(Application.Current.FindResource("SetupMsgCloneDirExists").ToString()!, targetPath);
                    ErrorBar.IsOpen = true;
                    return;
                }

                SetLoadingState(true, Application.Current.FindResource("SetupMsgCloning").ToString()!);
                
                try
                {
                    // Using a standard starter template URL for Chirpy
                    string repoUrl = "https://github.com/cotes2020/jekyll-theme-chirpy"; 
                    var result = await GitService.CloneAsync(repoUrl, targetPath);
                    
                    if (Directory.Exists(targetPath) && File.Exists(Path.Combine(targetPath, "_config.yml")))
                    {
                        BlogPathBox.Text = targetPath;
                        SetLoadingState(false, Application.Current.FindResource("SetupMsgCloneSuccess").ToString()!);
                        CreateBlogExpander.IsExpanded = false;
                    }
                    else
                    {
                        SetLoadingState(false, "");
                        ErrorBar.Message = Application.Current.FindResource("SetupMsgCloneNoConfig").ToString()!;
                        ErrorBar.IsOpen = true;
                    }
                }
                catch (Exception ex)
                {
                    SetLoadingState(false, "");
                    ErrorBar.Message = string.Format(Application.Current.FindResource("SetupMsgCloneError").ToString()!, ex.Message);
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
                ErrorBar.Message = Application.Current.FindResource("SetupMsgInvalidDir").ToString()!;
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
                ErrorBar.Message = string.Format(Application.Current.FindResource("SetupMsgConfigError").ToString()!, ex.Message);
                ErrorBar.IsOpen = true;
                return;
            }

            IsSetupSuccessful = true;
            Close();
        }
    }
}
