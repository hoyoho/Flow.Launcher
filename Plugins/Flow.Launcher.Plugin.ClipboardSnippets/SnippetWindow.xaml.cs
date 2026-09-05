using System.Windows;

namespace Flow.Launcher.Plugin.ClipboardSnippets
{
    public partial class SnippetWindow : Window
    {
        public string SnippetTitle => TitleBox.Text.Trim();

        public string SnippetContent => ContentBox.Text;

        public SnippetWindow()
        {
            InitializeComponent();
            Title = Localize.flowlauncher_plugin_clipboardsnippets_add_snippet_title();
        }

        public SnippetWindow(string title, string content) : this()
        {
            Title = Localize.flowlauncher_plugin_clipboardsnippets_edit_snippet_title();
            TitleBox.Text = title;
            ContentBox.Text = content;
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                ShowStatus(Localize.flowlauncher_plugin_clipboardsnippets_title_empty_error());
                TitleBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(ContentBox.Text))
            {
                ShowStatus(Localize.flowlauncher_plugin_clipboardsnippets_content_empty_error());
                ContentBox.Focus();
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_OnClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ShowStatus(string message)
        {
            StatusText.Text = message;
            StatusText.Visibility = Visibility.Visible;
        }
    }
}
