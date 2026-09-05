using System.Collections.Generic;
using System.Linq;
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

        private const int DefaultResultCount = 6;

        public List<Result> Query(Query query)
        {
            var search = query.Search?.Trim();

            if (string.IsNullOrWhiteSpace(search))
            {
                if (query.IsHomeQuery)
                    return [];

                return Settings.Snippets
                    .Where(snippet => !string.IsNullOrWhiteSpace(snippet.Title))
                    .Take(DefaultResultCount)
                    .Select(snippet => CreateResult(snippet, 5))
                    .ToList();
            }

            var results = new List<Result>();
            foreach (var snippet in Settings.Snippets)
            {
                if (string.IsNullOrWhiteSpace(snippet.Title))
                    continue;

                var match = Context.API.FuzzySearch(search, snippet.Title);
                if (!match.IsSearchPrecisionScoreMet())
                    continue;

                results.Add(CreateResult(snippet, match.Score, match.MatchData));
            }

            return results;
        }

        private static Result CreateResult(Snippet snippet, int score, IList<int> highlightData = null)
        {
            var capturedSnippet = snippet;
            return new Result
            {
                Title = snippet.Title,
                SubTitle = Preview(snippet.Content),
                IcoPath = IconPath,
                Score = score,
                TitleHighlightData = highlightData,
                Action = _ =>
                {
                    CopyToClipboard(capturedSnippet);
                    return true;
                }
            };
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
