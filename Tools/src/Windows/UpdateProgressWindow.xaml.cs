using System.ComponentModel;
using System.Windows;

namespace BlogTools
{
    public partial class UpdateProgressWindow : Window
    {
        public bool AllowClose { get; set; }

        public UpdateProgressWindow(Window? owner = null)
        {
            InitializeComponent();
            Owner = owner;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            App.ApplyThemeIcon(this);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!AllowClose)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }

        public void UpdateProgress(string message, int percent)
        {
            StatusText.Text = message;
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = percent;
        }

        public void UpdateStatus(string message, bool isIndeterminate = false, double value = 0)
        {
            StatusText.Text = message;
            DownloadProgressBar.IsIndeterminate = isIndeterminate;

            if (!isIndeterminate)
            {
                DownloadProgressBar.Value = value;
            }
        }
    }
}
