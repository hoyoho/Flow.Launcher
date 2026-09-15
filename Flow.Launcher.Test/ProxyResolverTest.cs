using NUnit.Framework;
using NUnit.Framework.Legacy;
using Flow.Launcher.Infrastructure.Http;

namespace Flow.Launcher.Test
{
    [TestFixture]
    class ProxyResolverTest
    {
        [TestCase("127.0.0.1:7890", "http", "127.0.0.1", 7890)]
        [TestCase("  127.0.0.1:7890  ", "http", "127.0.0.1", 7890)]
        [TestCase("http://127.0.0.1:7890", "http", "127.0.0.1", 7890)]
        [TestCase("HTTP://127.0.0.1:7890", "http", "127.0.0.1", 7890)]
        [TestCase("https://proxy.corp.com:8443", "https", "proxy.corp.com", 8443)]
        [TestCase("socks5://127.0.0.1:1080", "socks5", "127.0.0.1", 1080)]
        [TestCase("SOCKS5://127.0.0.1:1080", "socks5", "127.0.0.1", 1080)]
        [TestCase("socks://127.0.0.1:1080", "socks5", "127.0.0.1", 1080)]
        [TestCase("user:pass@127.0.0.1:7890", "http", "127.0.0.1", 7890)]
        [TestCase("http://user:pass@127.0.0.1:7890/path", "http", "127.0.0.1", 7890)]
        [TestCase("127.0.0.1:7890/path", "http", "127.0.0.1", 7890)]
        [TestCase("[::1]:8080", "http", "[::1]", 8080)]
        [TestCase("socks5://[::1]:1080", "socks5", "[::1]", 1080)]
        public void GivenValidAddress_WhenResolved_ThenEndpointShouldBeReturned(string address, string expectedScheme, string expectedHost, int expectedPort)
        {
            var endpoint = ProxyResolver.Resolve(address);

            ClassicAssert.IsNotNull(endpoint);
            ClassicAssert.AreEqual(expectedScheme, endpoint.Scheme);
            ClassicAssert.AreEqual(expectedHost, endpoint.Host);
            ClassicAssert.AreEqual(expectedPort, endpoint.Port);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("127.0.0.1")]
        [TestCase("http://127.0.0.1")]
        [TestCase("host:port")]
        [TestCase("127.0.0.1:0")]
        [TestCase("127.0.0.1:99999")]
        [TestCase("http://")]
        [TestCase("://8080")]
        public void GivenInvalidAddress_WhenResolved_ThenNullShouldBeReturned(string address)
        {
            ClassicAssert.IsNull(ProxyResolver.Resolve(address));
        }
    }
}
