/*
 * GrepCommand.cs - Parses "grep [options] pattern" command strings and
 * extracts multi-stage grep pipelines ("... | grep ... | grep ...") from
 * a Flow Launcher query.
 */

using System;
using System.Collections.Generic;
using System.Text;

namespace Flow.Launcher.Core.Grep
{
    /// <summary>
    /// A parsed grep command: a compiled matcher plus its display text.
    /// </summary>
    public sealed class GrepCommand
    {
        /// <summary>The raw command text after "grep ".</summary>
        public string RawCommand { get; }

        /// <summary>The compiled matcher for this command.</summary>
        public GrepMatcher Matcher { get; }

        public GrepCommand(string rawCommand, GrepMatcher matcher)
        {
            RawCommand = rawCommand;
            Matcher = matcher;
        }

        /// <summary>
        /// Tries to extract a grep pipeline from a query string.
        /// </summary>
        /// <param name="query">The full user query, e.g. "search term | grep -i foo | grep bar".</param>
        /// <param name="searchQuery">
        /// The part of the query before the first "| grep", to be sent to plugins.
        /// </param>
        /// <param name="grepCommands">
        /// The list of parsed grep commands, in pipeline order.
        /// </param>
        /// <returns>True if at least one grep stage was found.</returns>
        public static bool TryExtractPipeGrepChain(string query, out string searchQuery, out List<GrepCommand> grepCommands)
        {
            searchQuery = query;
            grepCommands = null;

            if (string.IsNullOrEmpty(query))
                return false;

            // Split on " | grep " (pipe with grep keyword).
            // We support "|grep", "| grep", "|  grep" etc.
            var stages = SplitOnGrepPipe(query);
            if (stages.Count <= 1)
                return false;

            searchQuery = stages[0].Trim();
            grepCommands = new List<GrepCommand>(stages.Count - 1);

            for (int i = 1; i < stages.Count; i++)
            {
                var raw = stages[i].Trim();
                if (string.IsNullOrEmpty(raw))
                    continue;

                if (TryParse(raw, out var command))
                    grepCommands.Add(command);
            }

            return grepCommands.Count > 0;
        }

        /// <summary>
        /// Splits the query on "| grep" segments, respecting escaped pipes.
        /// </summary>
        private static List<string> SplitOnGrepPipe(string query)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            int i = 0;

            while (i < query.Length)
            {
                // Check for escaped pipe "\|".
                if (query[i] == '\\' && i + 1 < query.Length && query[i + 1] == '|')
                {
                    current.Append('|');
                    i += 2;
                    continue;
                }

                // Check for "| grep" (case-insensitive, allowing spaces).
                if (query[i] == '|')
                {
                    int j = i + 1;
                    while (j < query.Length && char.IsWhiteSpace(query[j]))
                        j++;

                    if (j + 4 <= query.Length &&
                        (query[j] == 'g' || query[j] == 'G') &&
                        (query[j + 1] == 'r' || query[j + 1] == 'R') &&
                        (query[j + 2] == 'e' || query[j + 2] == 'E') &&
                        (query[j + 3] == 'p' || query[j + 3] == 'P'))
                    {
                        // Ensure "grep" is a whole word (followed by space/end or another pipe).
                        bool wholeWord = j + 4 >= query.Length ||
                                         char.IsWhiteSpace(query[j + 4]) ||
                                         query[j + 4] == '|';

                        if (wholeWord)
                        {
                            result.Add(current.ToString());
                            current.Clear();
                            i = j + 4; // skip "grep"
                            continue;
                        }
                    }
                }

                current.Append(query[i]);
                i++;
            }

            result.Add(current.ToString());
            return result;
        }

        /// <summary>
        /// Parses a grep command string (the part after "grep ") into a
        /// <see cref="GrepCommand"/>.
        /// </summary>
        public static bool TryParse(string commandText, out GrepCommand command)
        {
            command = null;

            if (string.IsNullOrWhiteSpace(commandText))
                return false;

            var options = new GrepOptions();
            var patterns = new List<string>();

            // Tokenize: split on whitespace, respecting quotes.
            var tokens = Tokenize(commandText);
            if (tokens.Count == 0)
                return false;

            bool expectArg = false;
            char pendingOption = '\0';

            for (int i = 0; i < tokens.Count; i++)
            {
                string token = tokens[i];

                if (expectArg)
                {
                    ApplyOptionArg(pendingOption, token, options, patterns);
                    expectArg = false;
                    continue;
                }

                if (token.StartsWith("-") && token.Length > 1)
                {
                    // Option cluster, e.g. "-ivw" or "--extended-regexp".
                    if (token.StartsWith("--"))
                    {
                        if (!TryParseLongOption(token, options, ref patterns, ref expectArg, ref pendingOption))
                            return false;
                    }
                    else
                    {
                        for (int k = 1; k < token.Length; k++)
                        {
                            char opt = token[k];
                            if (TryParseShortOption(opt, options, ref patterns, ref expectArg, ref pendingOption))
                            {
                                if (expectArg)
                                {
                                    // The rest of the token might be the argument (e.g. -m2).
                                    if (k + 1 < token.Length)
                                    {
                                        ApplyOptionArg(opt, token.Substring(k + 1), options, patterns);
                                        expectArg = false;
                                        break;
                                    }
                                    // Otherwise the next token is the argument.
                                    break;
                                }
                            }
                            else
                            {
                                return false;
                            }
                        }
                    }
                }
                else
                {
                    // Non-option token: this is the pattern.
                    patterns.Add(token);
                }
            }

            if (expectArg)
            {
                // Missing argument for the last option.
                return false;
            }

            if (patterns.Count == 0)
                return false;

            try
            {
                var matcher = new GrepMatcher(patterns, options);
                command = new GrepCommand(commandText, matcher);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyOptionArg(char opt, string arg, GrepOptions options, List<string> patterns)
        {
            switch (opt)
            {
                case 'm':
                    if (int.TryParse(arg, out int max))
                        options.MaxCount = max;
                    break;
                case 'A':
                    if (int.TryParse(arg, out int after))
                        options.AfterContext = after;
                    break;
                case 'B':
                    if (int.TryParse(arg, out int before))
                        options.BeforeContext = before;
                    break;
                case 'C':
                    if (int.TryParse(arg, out int ctx))
                        options.Context = ctx;
                    break;
                case 'e':
                    patterns.Add(arg);
                    break;
            }
        }

        private static bool TryParseShortOption(char opt, GrepOptions options, ref List<string> patterns,
            ref bool expectArg, ref char pendingOption)
        {
            switch (opt)
            {
                case 'F': options.MatcherType = GrepMatcherType.Fixed; return true;
                case 'E': options.MatcherType = GrepMatcherType.ExtendedRegex; return true;
                case 'G': options.MatcherType = GrepMatcherType.BasicRegex; return true;
                case 'P': options.MatcherType = GrepMatcherType.PerlRegex; return true;
                case 'i': options.IgnoreCase = true; return true;
                case 'v': options.Invert = true; return true;
                case 'w': options.WordMatch = true; return true;
                case 'x': options.LineMatch = true; return true;
                case 'c': options.CountOnly = true; return true;
                case 'o': options.OnlyMatching = true; return true;
                case 'm':
                case 'A':
                case 'B':
                case 'C':
                case 'e':
                    expectArg = true;
                    pendingOption = opt;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseLongOption(string token, GrepOptions options, ref List<string> patterns,
            ref bool expectArg, ref char pendingOption)
        {
            switch (token)
            {
                case "--fixed-strings": options.MatcherType = GrepMatcherType.Fixed; return true;
                case "--extended-regexp": options.MatcherType = GrepMatcherType.ExtendedRegex; return true;
                case "--basic-regexp": options.MatcherType = GrepMatcherType.BasicRegex; return true;
                case "--perl-regexp": options.MatcherType = GrepMatcherType.PerlRegex; return true;
                case "--ignore-case": options.IgnoreCase = true; return true;
                case "--invert-match": options.Invert = true; return true;
                case "--word-regexp": options.WordMatch = true; return true;
                case "--line-regexp": options.LineMatch = true; return true;
                case "--count": options.CountOnly = true; return true;
                case "--only-matching": options.OnlyMatching = true; return true;
                default:
                    // --max-count=N, --after-context=N, etc.
                    if (token.StartsWith("--max-count="))
                    {
                        if (int.TryParse(token.Substring("--max-count=".Length), out int m))
                            options.MaxCount = m;
                        return true;
                    }
                    if (token.StartsWith("--after-context="))
                    {
                        if (int.TryParse(token.Substring("--after-context=".Length), out int a))
                            options.AfterContext = a;
                        return true;
                    }
                    if (token.StartsWith("--before-context="))
                    {
                        if (int.TryParse(token.Substring("--before-context=".Length), out int b))
                            options.BeforeContext = b;
                        return true;
                    }
                    if (token.StartsWith("--context="))
                    {
                        if (int.TryParse(token.Substring("--context=".Length), out int c))
                            options.Context = c;
                        return true;
                    }
                    if (token.StartsWith("--regexp="))
                    {
                        patterns.Add(token.Substring("--regexp=".Length));
                        return true;
                    }
                    return false;
            }
        }

        /// <summary>
        /// Splits a command string into tokens, respecting single and double quotes.
        /// </summary>
        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inSingle = false;
            bool inDouble = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (inSingle)
                {
                    if (c == '\'') inSingle = false;
                    else current.Append(c);
                }
                else if (inDouble)
                {
                    if (c == '"') inDouble = false;
                    else if (c == '\\' && i + 1 < input.Length)
                    {
                        current.Append(input[++i]);
                    }
                    else current.Append(c);
                }
                else
                {
                    if (c == '\'') inSingle = true;
                    else if (c == '"') inDouble = true;
                    else if (char.IsWhiteSpace(c))
                    {
                        if (current.Length > 0)
                        {
                            tokens.Add(current.ToString());
                            current.Clear();
                        }
                    }
                    else current.Append(c);
                }
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());

            return tokens;
        }
    }
}
