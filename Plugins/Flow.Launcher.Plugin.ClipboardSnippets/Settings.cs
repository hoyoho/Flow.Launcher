using System.Collections.Generic;

namespace Flow.Launcher.Plugin.ClipboardSnippets
{
    public class Settings
    {
        public List<Snippet> Snippets { get; set; } = new();

        public bool NotifyCopySuccess { get; set; } = true;
    }

    public class Snippet
    {
        public string Title { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;
    }
}
