/*
 * GrepMatcher.cs - grep matching engine for Flow Launcher's global grep filter.
 *
 * Combines the independently-implemented Kwset (fixed-string, -F) with .NET
 * Regex (-G/-E/-P) and implements grep matching options
 * (-i, -v, -w, -x, -m, -c, -o, -A, -B, -C).
 */

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Flow.Launcher.Core.Grep
{
    /// <summary>The pattern matcher type selected by grep options.</summary>
    public enum GrepMatcherType
    {
        /// <summary>Basic regular expression (default, -G).</summary>
        BasicRegex,

        /// <summary>Extended regular expression (-E).</summary>
        ExtendedRegex,

        /// <summary>Fixed string, no regex (-F).</summary>
        Fixed,

        /// <summary>Perl-compatible regular expression (-P).</summary>
        PerlRegex
    }

    /// <summary>Options that control how a <see cref="GrepMatcher"/> matches text.</summary>
    public sealed class GrepOptions
    {
        public GrepMatcherType MatcherType { get; set; } = GrepMatcherType.BasicRegex;

        /// <summary>-i: ignore case.</summary>
        public bool IgnoreCase { get; set; }

        /// <summary>-v: invert match (keep non-matching lines).</summary>
        public bool Invert { get; set; }

        /// <summary>-w: match whole words only.</summary>
        public bool WordMatch { get; set; }

        /// <summary>-x: match whole line only.</summary>
        public bool LineMatch { get; set; }

        /// <summary>-m N: stop after N matching lines (0 = unlimited).</summary>
        public int MaxCount { get; set; }

        /// <summary>-c: only output a count of matching lines.</summary>
        public bool CountOnly { get; set; }

        /// <summary>-o: print only the matched parts.</summary>
        public bool OnlyMatching { get; set; }

        /// <summary>-A N: print N lines of trailing context.</summary>
        public int AfterContext { get; set; }

        /// <summary>-B N: print N lines of leading context.</summary>
        public int BeforeContext { get; set; }

        /// <summary>-C N: print N lines of context (sets both -A and -B).</summary>
        public int Context
        {
            get => Math.Max(AfterContext, BeforeContext);
            set { AfterContext = value; BeforeContext = value; }
        }
    }

    /// <summary>A single match within a searched text.</summary>
    public readonly struct GrepMatch
    {
        public int Offset { get; }
        public int Length { get; }

        public GrepMatch(int offset, int length)
        {
            Offset = offset;
            Length = length;
        }
    }

    /// <summary>
    /// A compiled grep matcher. Holds the compiled pattern (Kwset or Regex)
    /// and applies grep options to determine matches.
    /// </summary>
    public sealed class GrepMatcher
    {
        private readonly GrepOptions _options;
        private readonly Kwset _kwset;
        private readonly Regex _regex;
        private readonly string _pattern;

        public GrepOptions Options => _options;

        /// <summary>
        /// Creates a matcher from a list of patterns and options.
        /// </summary>
        public GrepMatcher(IList<string> patterns, GrepOptions options)
        {
            if (patterns == null || patterns.Count == 0)
                throw new ArgumentException("At least one pattern is required.", nameof(patterns));

            _options = options ?? throw new ArgumentNullException(nameof(options));
            _pattern = patterns[0];

            if (options.MatcherType == GrepMatcherType.Fixed)
            {
                Func<char, char> trans = options.IgnoreCase ? char.ToLowerInvariant : (Func<char, char>)null;
                _kwset = new Kwset(trans);
                foreach (var p in patterns)
                    _kwset.Add(p);
                _kwset.Prepare();
            }
            else
            {
                _regex = CompileRegex(patterns, options);
            }
        }

        private static Regex CompileRegex(IList<string> patterns, GrepOptions options)
        {
            var opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;
            if (options.IgnoreCase)
                opts |= RegexOptions.IgnoreCase;

            string pattern;
            if (patterns.Count == 1)
            {
                pattern = ConvertPattern(patterns[0], options.MatcherType, options.LineMatch, options.WordMatch);
            }
            else
            {
                // Multiple patterns: join with alternation.
                var parts = new string[patterns.Count];
                for (int i = 0; i < patterns.Count; i++)
                    parts[i] = ConvertPattern(patterns[i], options.MatcherType, false, false);
                pattern = string.Join("|", parts);
                if (options.LineMatch)
                    pattern = "^(?:" + pattern + ")$";
                else if (options.WordMatch)
                    pattern = @"(?:(?<=^|\W)" + pattern + @"(?=\W|$))";
            }

            return new Regex(pattern, opts);
        }

        /// <summary>
        /// Converts a grep pattern to a .NET regex pattern based on the matcher type.
        /// </summary>
        private static string ConvertPattern(string pattern, GrepMatcherType type, bool lineMatch, bool wordMatch)
        {
            string converted;

            switch (type)
            {
                case GrepMatcherType.BasicRegex:
                    // BRE: (, ), {, }, |, +, ? are literal unless escaped.
                    // We convert them to their ERE equivalents.
                    converted = ConvertBreToEre(pattern);
                    break;

                case GrepMatcherType.ExtendedRegex:
                case GrepMatcherType.PerlRegex:
                    // ERE and PCRE: metacharacters are already special.
                    // .NET regex is close enough for common usage.
                    converted = pattern;
                    break;

                case GrepMatcherType.Fixed:
                default:
                    converted = Regex.Escape(pattern);
                    break;
            }

            if (lineMatch)
                converted = "^" + converted + "$";
            else if (wordMatch)
                converted = @"(?<!\w)" + converted + @"(?!\w)";

            return converted;
        }

        /// <summary>
        /// Converts a Basic Regular Expression to Extended syntax for .NET Regex.
        /// In BRE, (, ), {, }, |, +, ? are literal unless preceded by backslash.
        /// </summary>
        private static string ConvertBreToEre(string bre)
        {
            var result = new System.Text.StringBuilder(bre.Length);
            int i = 0;
            while (i < bre.Length)
            {
                char c = bre[i];
                if (c == '\\' && i + 1 < bre.Length)
                {
                    char next = bre[i + 1];
                    switch (next)
                    {
                        case '(':
                        case ')':
                        case '{':
                        case '}':
                        case '|':
                        case '+':
                        case '?':
                            // Escaped metacharacter in BRE => special in ERE.
                            result.Append(next);
                            i += 2;
                            continue;
                        default:
                            // Keep the backslash (e.g. \., \w, etc.).
                            result.Append('\\').Append(next);
                            i += 2;
                            continue;
                    }
                }

                switch (c)
                {
                    case '(':
                    case ')':
                    case '{':
                    case '}':
                    case '|':
                    case '+':
                    case '?':
                        // Literal in BRE => escape for ERE.
                        result.Append('\\').Append(c);
                        break;
                    default:
                        result.Append(c);
                        break;
                }
                i++;
            }
            return result.ToString();
        }

        /// <summary>
        /// Returns all non-overlapping matches of the pattern in <paramref name="text"/>.
        /// </summary>
        public List<GrepMatch> Matches(string text)
        {
            var matches = new List<GrepMatch>();
            if (string.IsNullOrEmpty(text))
                return matches;

            if (_kwset != null)
            {
                int offset = 0;
                while (offset <= text.Length)
                {
                    var m = _kwset.Search(text.Substring(offset), longest: _options.WordMatch);
                    if (m == null)
                        break;

                    int absOffset = offset + m.Value.Offset;

                    if (_options.WordMatch && !IsWordMatch(text, absOffset, m.Value.Length))
                    {
                        // Skip this match, continue searching after it.
                        offset = absOffset + 1;
                        continue;
                    }

                    matches.Add(new GrepMatch(absOffset, m.Value.Length));
                    offset = absOffset + Math.Max(1, m.Value.Length);
                }
            }
            else
            {
                foreach (Match m in _regex.Matches(text))
                {
                    if (m.Length == 0)
                        continue;
                    matches.Add(new GrepMatch(m.Index, m.Length));
                }
            }

            return matches;
        }

        /// <summary>
        /// Determines whether the match at <paramref name="offset"/> with
        /// <paramref name="length"/> satisfies the word-boundary constraint (-w).
        /// </summary>
        private static bool IsWordMatch(string text, int offset, int length)
        {
            bool leftOk = offset == 0 || !char.IsLetterOrDigit(text[offset - 1]);
            bool rightOk = offset + length >= text.Length || !char.IsLetterOrDigit(text[offset + length]);
            return leftOk && rightOk;
        }

        /// <summary>
        /// Returns true if the text matches the pattern (considering -v invert).
        /// </summary>
        public bool IsMatch(string text)
        {
            if (_options.LineMatch)
            {
                bool matched;
                if (_kwset != null)
                {
                    // -x: the entire line must equal a pattern.
                    matched = false;
                    foreach (var p in new[] { _pattern })
                    {
                        if (string.Equals(text, p, _options.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                        {
                            matched = true;
                            break;
                        }
                    }
                }
                else
                {
                    matched = _regex.IsMatch(text);
                }
                return _options.Invert ? !matched : matched;
            }

            bool hasMatch = Matches(text).Count > 0;
            return _options.Invert ? !hasMatch : hasMatch;
        }
    }
}
