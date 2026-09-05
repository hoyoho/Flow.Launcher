using System.Collections.Generic;
using System.Text;
using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.ClipboardSnippets
{
    public class Main : IPlugin, IPluginI18n, ISettingProvider
    {
        internal static PluginInitContext Context { get; private set; }
        internal static Settings Settings { get; private set; }

        internal const string IconPath = "Images/clipboard.png";

        private const int PreviewMaxLength = 80;

        public List<Result> Query(Query query)
        {
            var search = query.Search?.Trim();
            if (string.IsNullOrWhiteSpace(search))
                return [];

            var results = new List<Result>();
            foreach (var snippet in Settings.Snippets)
            {
                if (string.IsNullOrWhiteSpace(snippet.Title))
                    continue;

                var match = Context.API.FuzzySearch(search, snippet.Title);
                if (!match.IsSearchPrecisionScoreMet())
                    continue;

                var capturedSnippet = snippet;
                results.Add(new Result
                {
                    Title = snippet.Title,
                    SubTitle = Preview(snippet.Content),
                    IcoPath = IconPath,
                    Score = match.Score,
                    TitleHighlightData = match.MatchData,
                    Action = _ =>
                    {
                        CopyToClipboard(capturedSnippet);
                        return true;
                    }
                });
            }

            return results;
        }

        private static void CopyToClipboard(Snippet snippet)
        {
            if (ClipboardHelper.TryCopy(snippet.Content, out var error))
            {
                if (Settings.NotifyCopySuccess)
                    Context.API.ShowMsg(Localize.flowlauncher_plugin_clipboardsnippets_plugin_name(),
                        Localize.flowlauncher_plugin_clipboardsnippets_copied_notification(snippet.Title));
            }
            else
            {
                Context.API.ShowMsgError(Localize.flowlauncher_plugin_clipboardsnippets_plugin_name(),
                    Localize.flowlauncher_plugin_clipboardsnippets_copy_failed());
            }
        }

        private static string Preview(string content)
        {
            if (string.IsNullOrEmpty(content))
                return Localize.flowlauncher_plugin_clipboardsnippets_copy_empty_subtitle();

            var singleLine = new StringBuilder(content.Length);
            foreach (var c in content)
            {
                singleLine.Append(char.IsWhiteSpace(c) && c != ' ' ? ' ' : c);
            }

            var preview = singleLine.ToString().Trim();
            if (preview.Length <= PreviewMaxLength)
                return Localize.flowlauncher_plugin_clipboardsnippets_copy_subtitle(preview);

            return Localize.flowlauncher_plugin_clipboardsnippets_copy_subtitle(preview[..PreviewMaxLength].TrimEnd() + "...");
        }

        public void Init(PluginInitContext context)
        {
            Context = context;
            Settings = context.API.LoadSettingJsonStorage<Settings>();
        }

        public string GetTranslatedPluginTitle()
        {
            return Localize.flowlauncher_plugin_clipboardsnippets_plugin_name();
        }

        public string GetTranslatedPluginDescription()
        {
            return Localize.flowlauncher_plugin_clipboardsnippets_plugin_description();
        }

        public Control CreateSettingPanel()
        {
            return new SettingsPanel();
        }
    }
}
