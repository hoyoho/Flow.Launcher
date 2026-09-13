/*
 * Kwset.cs - Fixed-string multi-pattern matching engine.
 *
 * Independent implementation based on well-known algorithms from the
 * public literature:
 *   - Single pattern: Boyer-Moore (Boyer & Moore, 1977)
 *     using the bad-character rule and the strong good-suffix rule.
 *   - Multiple patterns: Aho-Corasick (Aho & Corasick, 1975)
 *     using a trie with failure links, searched in linear time.
 *
 * No code is derived from any existing implementation. Variable names,
 * data structures, and control flow are designed independently for the
 * UTF-16 (System.Char) text-stream filtering use case in Flow Launcher.
 */

using System;
using System.Collections.Generic;

namespace Flow.Launcher.Core.Grep
{
    /// <summary>
    /// Represents a single match found by <see cref="Kwset"/>.
    /// </summary>
    public readonly struct KwsetMatch
    {
        /// <summary>Zero-based index of the keyword that matched.</summary>
        public int Index { get; }

        /// <summary>Offset into the searched text where the match starts.</summary>
        public int Offset { get; }

        /// <summary>Length of the matched substring.</summary>
        public int Length { get; }

        public KwsetMatch(int index, int offset, int length)
        {
            Index = index;
            Offset = offset;
            Length = length;
        }
    }

    /// <summary>
    /// A keyword set that locates any of its fixed-string patterns inside a
    /// text in linear time.
    /// </summary>
    /// <remarks>
    /// When only one pattern is registered the search uses the Boyer-Moore
    /// algorithm; otherwise the Aho-Corasick automaton is used.
    /// </remarks>
    public sealed class Kwset : IDisposable
    {
        private readonly Func<char, char> _trans;
        private readonly List<string> _patterns = new();

        // ---- Boyer-Moore state (single pattern) ----
        private string _bmPattern;
        private int[] _bmBadChar;      // bad-character shift table (size = alphabet)
        private int[] _bmGoodSuffix;   // good-suffix shift table (size = pattern length)
        private bool _useBm;

        // ---- Aho-Corasick state (multiple patterns) ----
        private AcNode _acRoot;
        private int _minLen = int.MaxValue;

        private const int AlphabetSize = 0x10000; // UTF-16 code unit range

        /// <summary>
        /// Creates a new keyword set.
        /// </summary>
        /// <param name="trans">
        /// Optional character translation applied to both patterns and search
        /// text. Pass <c>char.ToLowerInvariant</c> for case-insensitive search.
        /// </param>
        public Kwset(Func<char, char> trans = null)
        {
            _trans = trans;
        }

        /// <summary>Number of patterns registered.</summary>
        public int WordCount => _patterns.Count;

        private char T(char c) => _trans != null ? _trans(c) : c;

        /// <summary>Adds a fixed-string pattern to the set.</summary>
        public void Add(string pattern)
        {
            if (pattern == null)
                throw new ArgumentNullException(nameof(pattern));
            _patterns.Add(pattern);
            if (pattern.Length < _minLen)
                _minLen = pattern.Length;
        }

        /// <summary>
        /// Builds the search structures. Must be called after all patterns
        /// have been added and before the first <see cref="Search"/>.
        /// </summary>
        public void Prepare()
        {
            _useBm = _patterns.Count == 1;
            if (_useBm)
                BuildBoyerMoore();
            else
                BuildAhoCorasick();
        }

        // ---------------------------------------------------------------
        // Boyer-Moore (single pattern)
        // ---------------------------------------------------------------

        private void BuildBoyerMoore()
        {
            _bmPattern = _patterns[0];
            int n = _bmPattern.Length;

            // Translate the pattern once.
            var p = new char[n];
            for (int i = 0; i < n; i++)
                p[i] = T(_bmPattern[i]);

            // Bad-character table: for each character, the rightmost index in
            // the pattern, or -1 if absent. We store the shift (n - 1 - index)
            // directly so the table holds the bad-character shift value.
            _bmBadChar = new int[AlphabetSize];
            for (int i = 0; i < AlphabetSize; i++)
                _bmBadChar[i] = n;          // default: shift by full length
            for (int i = 0; i < n - 1; i++) // last char doesn't contribute
                _bmBadChar[p[i]] = n - 1 - i;

            // Good-suffix table (allocated inside BuildGoodSuffix).
            BuildGoodSuffix(p, n);
        }

        /// <summary>
        /// Computes the strong good-suffix shift table for Boyer-Moore.
        /// Based on the standard preprocessing described in Gusfield's
        /// "Algorithms on Strings, Trees, and Sequences" (1997), chapter 2.
        /// </summary>
        private void BuildGoodSuffix(char[] p, int n)
        {
            // Compute the good-suffix shift table using the border-array
            // method (Gusfield 1997, ch. 2). Both arrays are size n+1.
            // shift[k] holds the shift amount when the matched suffix begins
            // at pattern position k (i.e. mismatch occurred at position k-1).
            var border = new int[n + 1];
            var shift = new int[n + 1];
            int i = n, j = n + 1;
            border[i] = j;
            while (i > 0)
            {
                while (j <= n && p[i - 1] != p[j - 1])
                {
                    if (shift[j] == 0)
                        shift[j] = j - i;
                    j = border[j];
                }
                i--;
                j--;
                border[i] = j;
            }

            j = border[0];
            for (i = 0; i <= n; i++)
            {
                if (shift[i] == 0)
                    shift[i] = j;
                if (i == j)
                    j = border[j];
            }

            _bmGoodSuffix = shift;
        }

        private KwsetMatch? BoyerMooreSearch(string text)
        {
            int n = _bmPattern.Length;
            if (n == 0)
                return new KwsetMatch(0, 0, 0);

            int m = text.Length;
            if (n > m)
                return null;

            if (n == 1)
            {
                char target = T(_bmPattern[0]);
                for (int i = 0; i < m; i++)
                    if (T(text[i]) == target)
                        return new KwsetMatch(0, i, 1);
                return null;
            }

            // Translate pattern for comparisons.
            var p = new char[n];
            for (int k = 0; k < n; k++)
                p[k] = T(_bmPattern[k]);

            int pos = n - 1; // right end of the pattern window in text
            while (pos < m)
            {
                int pi = n - 1; // index in pattern
                int ti = pos;   // index in text

                while (pi >= 0 && T(text[ti]) == p[pi])
                {
                    pi--;
                    ti--;
                }

                if (pi < 0)
                    return new KwsetMatch(0, ti + 1, n); // match

                char bad = T(text[ti]);
                // pi is the mismatch position (0-indexed from left).
                // The matched suffix is p[pi+1..n-1], so the good-suffix
                // shift is stored at index pi+1.
                int shift = Math.Max(_bmBadChar[bad], _bmGoodSuffix[pi + 1]);
                pos += shift;
            }

            return null;
        }

        // ---------------------------------------------------------------
        // Aho-Corasick (multiple patterns)
        // ---------------------------------------------------------------

        private sealed class AcNode
        {
            public Dictionary<char, AcNode> Children = new();
            public AcNode Fail;
            public int Output = -1;  // pattern index if this node is terminal, else -1
            public int Depth;
        }

        private void BuildAhoCorasick()
        {
            _acRoot = new AcNode();

            // Insert every pattern into the trie.
            for (int idx = 0; idx < _patterns.Count; idx++)
            {
                var pat = _patterns[idx];
                var node = _acRoot;
                for (int i = 0; i < pat.Length; i++)
                {
                    char c = T(pat[i]);
                    if (!node.Children.TryGetValue(c, out var child))
                    {
                        child = new AcNode { Depth = node.Depth + 1 };
                        node.Children[c] = child;
                    }
                    node = child;
                }
                // If several patterns end at the same node, keep the first.
                if (node.Output < 0)
                    node.Output = idx;
            }

            // Build failure links using BFS (standard Aho-Corasick).
            var queue = new Queue<AcNode>();
            foreach (var child in _acRoot.Children.Values)
            {
                child.Fail = _acRoot;
                queue.Enqueue(child);
            }

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var kv in node.Children)
                {
                    char c = kv.Key;
                    var child = kv.Value;
                    queue.Enqueue(child);

                    var fail = node.Fail;
                    while (fail != null && !fail.Children.ContainsKey(c))
                        fail = fail.Fail;
                    child.Fail = fail != null && fail.Children.TryGetValue(c, out var fc)
                        ? fc
                        : _acRoot;
                }
            }
        }

        private KwsetMatch? AhoCorasickSearch(string text, bool longest)
        {
            int n = text.Length;
            if (n < _minLen)
                return null;

            var node = _acRoot;
            for (int i = 0; i < n; i++)
            {
                char c = T(text[i]);

                while (node != _acRoot && !node.Children.ContainsKey(c))
                    node = node.Fail;

                if (node.Children.TryGetValue(c, out var next))
                    node = next;

                // Collect all outputs ending at position i (this node and its
                // failure chain). If longest is requested, pick the longest
                // match; otherwise pick the first (shortest).
                int bestIdx = -1;
                int bestLen = 0;
                int bestStart = 0;

                var cur = node;
                while (cur != null && cur != _acRoot)
                {
                    if (cur.Output >= 0)
                    {
                        int plen = _patterns[cur.Output].Length;
                        int pstart = i - plen + 1;
                        if (bestIdx < 0)
                        {
                            bestIdx = cur.Output;
                            bestLen = plen;
                            bestStart = pstart;
                            if (!longest)
                                return new KwsetMatch(bestIdx, bestStart, bestLen);
                        }
                        else if (longest && plen > bestLen)
                        {
                            bestIdx = cur.Output;
                            bestLen = plen;
                            bestStart = pstart;
                        }
                    }
                    cur = cur.Fail;
                }

                if (bestIdx >= 0)
                    return new KwsetMatch(bestIdx, bestStart, bestLen);
            }

            return null;
        }

        // ---------------------------------------------------------------
        // Public search entry point
        // ---------------------------------------------------------------

        /// <summary>
        /// Finds the first occurrence of any registered pattern in
        /// <paramref name="text"/>.
        /// </summary>
        /// <param name="text">Text to search.</param>
        /// <param name="longest">
        /// If true, among all matches ending at the same position return the
        /// longest one. If false, return the first (shortest) match found.
        /// </param>
        /// <returns>The first match, or <c>null</c> if none.</returns>
        public KwsetMatch? Search(string text, bool longest = false)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            return _useBm ? BoyerMooreSearch(text) : AhoCorasickSearch(text, longest);
        }

        public void Dispose()
        {
            // No unmanaged resources.
        }
    }
}
