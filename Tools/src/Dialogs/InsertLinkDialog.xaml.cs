using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Appearance;

namespace BlogTools
{
    public partial class InsertLinkDialog : Wpf.Ui.Controls.FluentWindow
    {
        public string LinkText => LinkTextBox.Text.Trim();
        public string LinkUrl => LinkUrlBox.Text.Trim();
        public bool OpenInNewTab => OpenInNewTabSwitch.IsChecked == true;

        public InsertLinkDialog()
        {
            InitializeComponent();
            App.ConfigureThemeWindow(this);
            Loaded += InsertLinkDialog_Loaded;
        }

        private void InsertLinkDialog_Loaded(object sender, RoutedEventArgs e)
        {
            LinkTextBox.Focus();
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            InsertButton.IsEnabled =
                !string.IsNullOrWhiteSpace(LinkTextBox.Text) &&
                !string.IsNullOrWhiteSpace(LinkUrlBox.Text);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Insert_Click(object sender, RoutedEventArgs e)
        {
            if (!InsertButton.IsEnabled)
            {
                return;
            }

            DialogResult = true;
            Close();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Enter && InsertButton.IsEnabled)
            {
                DialogResult = true;
                Close();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
        }
    }
}
