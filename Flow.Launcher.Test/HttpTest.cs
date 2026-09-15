using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Flow.Launcher.Infrastructure.Http;
using Flow.Launcher.Infrastructure.UserSettings;

namespace Flow.Launcher.Test
{
    [TestFixture]
    class HttpTest
    {
        private IWebProxy originalDefaultProxy;

        [SetUp]
        public void SetUp()
        {
            originalDefaultProxy = HttpClient.DefaultProxy;
        }

        [TearDown]
        public void TearDown()
        {
            HttpClient.DefaultProxy = originalDefaultProxy;
        }

        [Test]
        public void GivenSystemMode_WhenProxyApplied_ThenDefaultProxyShouldFollowTheSystem()
        {
            var proxy = new HttpProxy();
            Http.Proxy = proxy;
            proxy.Mode = ProxyMode.System;

            ClassicAssert.AreSame(originalDefaultProxy, Http.CurrentProxy);
            ClassicAssert.AreSame(originalDefaultProxy, HttpClient.DefaultProxy);
        }

        [Test]
        public void GivenDirectMode_WhenProxyApplied_ThenDefaultProxyShouldBypassAllProxies()
        {
            var proxy = new HttpProxy();
            Http.Proxy = proxy;
            proxy.Mode = ProxyMode.Direct;

            var current = (WebProxy)Http.CurrentProxy;
            ClassicAssert.IsNotNull(current);
            ClassicAssert.IsNull(current.Address);
        }

        [Test]
        public void GivenManualMode_WhenAddressUpdated_ThenDefaultProxyShouldUseTheParsedEndpoint()
        {
            var proxy = new HttpProxy();
            Http.Proxy = proxy;
            proxy.Mode = ProxyMode.Manual;
            proxy.Address = "127.0.0.1:7890";

            var current = (WebProxy)Http.CurrentProxy;
            ClassicAssert.AreEqual(new Uri("http://127.0.0.1:7890"), current.Address);

            proxy.Address = "socks5://127.0.0.1:1080";
            ClassicAssert.AreEqual(new Uri("socks5://127.0.0.1:1080"), ((WebProxy)Http.CurrentProxy).Address);
        }

        [Test]
        public void GivenManualMode_WhenCredentialsUpdated_ThenDefaultProxyShouldUseThem()
        {
            var proxy = new HttpProxy();
            Http.Proxy = proxy;
            proxy.Mode = ProxyMode.Manual;
            proxy.Address = "127.0.0.1:7890";
            proxy.UserName = "test";
            proxy.Password = "test password";

            var current = (WebProxy)Http.CurrentProxy;
            ClassicAssert.IsNotNull(current.Credentials);
            ClassicAssert.AreEqual(proxy.UserName, current.Credentials.GetCredential(current.Address, "Basic").UserName);
            ClassicAssert.AreEqual(proxy.Password, current.Credentials.GetCredential(current.Address, "Basic").Password);
        }

        [Test]
        public void GivenManualMode_WhenAddressInvalid_ThenShouldFallBackToTheSystemProxy()
        {
            var proxy = new HttpProxy();
            Http.Proxy = proxy;
            proxy.Mode = ProxyMode.Direct;
            proxy.Mode = ProxyMode.Manual;
            proxy.Address = "no-port-host";

            ClassicAssert.AreSame(originalDefaultProxy, Http.CurrentProxy);
        }

        [Test]
        public void GivenLegacyEnabledProxyJson_WhenDeserialized_ThenShouldMigrateToManualMode()
        {
            const string json = /*lang=json*/ """
                { "Enabled": true, "Server": "127.0.0.1", "Port": 7890, "UserName": "test", "Password": "test password" }
                """;

            var proxy = JsonSerializer.Deserialize<HttpProxy>(json);

            ClassicAssert.AreEqual(ProxyMode.Manual, proxy.Mode);
            ClassicAssert.AreEqual("http://127.0.0.1:7890", proxy.Address);
            ClassicAssert.AreEqual("test", proxy.UserName);
            ClassicAssert.IsFalse(proxy.Enabled);
            ClassicAssert.IsNull(proxy.Server);
        }

        [Test]
        public void GivenLegacyDisabledProxyJson_WhenDeserialized_ThenShouldMigrateToSystemMode()
        {
            const string json = /*lang=json*/ """
                { "Enabled": false, "Server": "127.0.0.1", "Port": 7890 }
                """;

            var proxy = JsonSerializer.Deserialize<HttpProxy>(json);

            ClassicAssert.AreEqual(ProxyMode.System, proxy.Mode);
            ClassicAssert.AreEqual("http://127.0.0.1:7890", proxy.Address);
        }

        [Test]
        public void GivenEmptyLegacyProxyJson_WhenDeserialized_ThenShouldStaySystemModeWithoutAddress()
        {
            const string json = /*lang=json*/ """{ "Enabled": false, "Server": "", "Port": 0 }""";

            var proxy = JsonSerializer.Deserialize<HttpProxy>(json);

            ClassicAssert.AreEqual(ProxyMode.System, proxy.Mode);
            ClassicAssert.AreEqual(string.Empty, proxy.Address);
        }
    }
}
