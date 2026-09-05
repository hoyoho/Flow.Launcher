using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Flow.Launcher.Plugin.ClipboardSnippets
{
    public partial class SettingsPanel : UserControl
    {
        private readonly Settings _settings;

        public SettingsPanel()
        {
            InitializeComponent();

            _settings = Main.Settings;
            NotifyCheckBox.IsChecked = _settings.NotifyCopySuccess;

            RefreshList();
        }

        private void AddButton_OnClick(object sender, RoutedEventArgs e)
        {
            var window = new SnippetWindow { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true)
                return;

            var snippet = new Snippet
            {
                Title = window.SnippetTitle,
                Content = window.SnippetContent
            };

            _settings.Snippets.Add(snippet);
            Save();
            RefreshList();
            Select(snippet);
        }

        private void DeleteButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (SnippetsList.SelectedItem is not Snippet current)
            {
                ShowHint(Localize.flowlauncher_plugin_clipboardsnippets_select_delete_error());
                return;
            }

            _settings.Snippets.Remove(current);
            Save();
            RefreshList();
        }

        private void SnippetsList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SnippetsList.SelectedItem is not Snippet current)
                return;

            var window = new SnippetWindow(current.Title, current.Content) { Owner = Window.GetWindow(this) };
            if (window.ShowDialog() != true)
                return;

            current.Title = window.SnippetTitle;
            current.Content = window.SnippetContent;

            Save();
            RefreshList();
            Select(current);
        }

        private void NotifyCheckBox_OnChanged(object sender, RoutedEventArgs e)
        {
            if (_settings == null)
                return;

            _settings.NotifyCopySuccess = NotifyCheckBox.IsChecked == true;
            Save();
        }

        private void RefreshList()
        {
            SnippetsList.ItemsSource = null;
            SnippetsList.ItemsSource = _settings.Snippets;
            HideHint();
        }

        private void Select(Snippet snippet)
        {
            SnippetsList.SelectedItem = snippet;
            SnippetsList.ScrollIntoView(snippet);
        }

        private void Save()
        {
            Main.Context.API.SaveSettingJsonStorage<Settings>();
        }

        private void ShowHint(string message)
        {
            HintText.Text = message;
            HintText.Visibility = Visibility.Visible;
        }

        private void HideHint()
        {
            HintText.Visibility = Visibility.Collapsed;
        }
    }
}
