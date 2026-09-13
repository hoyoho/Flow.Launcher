using Flow.Launcher.Core.Grep;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Flow.Launcher.Test
{
    [TestFixture]
    public class GrepTest
    {
        [Test]
        public void Kwset_FixedStringMatch()
        {
            var kwset = new Kwset();
            kwset.Add("hello");
            kwset.Prepare();

            var m = kwset.Search("say hello world");
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Value.Offset, Is.EqualTo(4));
            Assert.That(m.Value.Length, Is.EqualTo(5));
        }

        [Test]
        public void Kwset_CaseInsensitive()
        {
            var kwset = new Kwset(char.ToLowerInvariant);
            kwset.Add("HELLO");
            kwset.Prepare();

            var m = kwset.Search("say hello world");
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Value.Offset, Is.EqualTo(4));
        }

        [Test]
        public void Kwset_MultiplePatterns()
        {
            var kwset = new Kwset();
            kwset.Add("foo");
            kwset.Add("bar");
            kwset.Prepare();

            Assert.That(kwset.Search("this has foo"), Is.Not.Null);
            Assert.That(kwset.Search("this has bar"), Is.Not.Null);
            Assert.That(kwset.Search("nothing here"), Is.Null);
        }

        [Test]
        public void GrepMatcher_FixedString()
        {
            var opts = new GrepOptions { MatcherType = GrepMatcherType.Fixed };
            var matcher = new GrepMatcher(new[] { "hello" }, opts);

            Assert.That(matcher.IsMatch("say hello world"), Is.True);
            Assert.That(matcher.IsMatch("nothing here"), Is.False);
        }

        [Test]
        public void GrepMatcher_IgnoreCase()
        {
            var opts = new GrepOptions { MatcherType = GrepMatcherType.Fixed, IgnoreCase = true };
            var matcher = new GrepMatcher(new[] { "HELLO" }, opts);

            Assert.That(matcher.IsMatch("say hello world"), Is.True);
        }

        [Test]
        public void GrepMatcher_Invert()
        {
            var opts = new GrepOptions { MatcherType = GrepMatcherType.Fixed, Invert = true };
            var matcher = new GrepMatcher(new[] { "hello" }, opts);

            Assert.That(matcher.IsMatch("say hello world"), Is.False);
            Assert.That(matcher.IsMatch("nothing here"), Is.True);
        }

        [Test]
        public void GrepMatcher_WordMatch()
        {
            var opts = new GrepOptions { MatcherType = GrepMatcherType.Fixed, WordMatch = true };
            var matcher = new GrepMatcher(new[] { "cat" }, opts);

            Assert.That(matcher.IsMatch("the cat sat"), Is.True);
            Assert.That(matcher.IsMatch("the category sat"), Is.False);
        }

        [Test]
        public void GrepMatcher_LineMatch()
        {
            var opts = new GrepOptions { MatcherType = GrepMatcherType.Fixed, LineMatch = true };
            var matcher = new GrepMatcher(new[] { "hello" }, opts);

            Assert.That(matcher.IsMatch("hello"), Is.True);
            Assert.That(matcher.IsMatch("hello world"), Is.False);
        }

        [Test]
        public void GrepMatcher_RegexExtended()
        {
            var opts = new GrepOptions { MatcherType = GrepMatcherType.ExtendedRegex };
            var matcher = new GrepMatcher(new[] { "a.*b" }, opts);

            Assert.That(matcher.IsMatch("axxxb"), Is.True);
            Assert.That(matcher.IsMatch("bxxxa"), Is.False);
        }

        [Test]
        public void GrepMatcher_BasicRegex_DotIsMetachar()
        {
            var opts = new GrepOptions { MatcherType = GrepMatcherType.BasicRegex };
            var matcher = new GrepMatcher(new[] { "a.b" }, opts);

            // In BRE, "." is a metacharacter (matches any char)
            Assert.That(matcher.IsMatch("axb"), Is.True);
            Assert.That(matcher.IsMatch("a.b"), Is.True);
        }

        [Test]
        public void GrepCommand_ParseSimple()
        {
            Assert.That(GrepCommand.TryParse("hello", out var cmd), Is.True);
            Assert.That(cmd, Is.Not.Null);
            Assert.That(cmd.Matcher.IsMatch("say hello world"), Is.True);
        }

        [Test]
        public void GrepCommand_ParseOptions()
        {
            Assert.That(GrepCommand.TryParse("-i hello", out var cmd), Is.True);
            Assert.That(cmd, Is.Not.Null);
            Assert.That(cmd.Matcher.Options.IgnoreCase, Is.True);
        }

        [Test]
        public void GrepCommand_ExtractPipeChain()
        {
            bool found = GrepCommand.TryExtractPipeGrepChain("search term | grep -i foo | grep bar",
                out var searchQuery, out var grepCommands);

            Assert.That(found, Is.True);
            Assert.That(searchQuery, Is.EqualTo("search term"));
            Assert.That(grepCommands.Count, Is.EqualTo(2));
            Assert.That(grepCommands[0].Matcher.Options.IgnoreCase, Is.True);
        }

        [Test]
        public void GrepCommand_NoPipe()
        {
            bool found = GrepCommand.TryExtractPipeGrepChain("just a search",
                out var searchQuery, out var grepCommands);

            Assert.That(found, Is.False);
            Assert.That(grepCommands, Is.Null);
        }

        [Test]
        public void GrepCommand_EscapedPipe()
        {
            bool found = GrepCommand.TryExtractPipeGrepChain("a \\| b | grep foo",
                out var searchQuery, out var grepCommands);

            Assert.That(found, Is.True);
            Assert.That(searchQuery, Is.EqualTo("a | b"));
            Assert.That(grepCommands.Count, Is.EqualTo(1));
        }
    }
}
