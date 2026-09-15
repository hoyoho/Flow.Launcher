using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Flow.Launcher.Core.ExternalPlugins;
using Flow.Launcher.Infrastructure.Http;
using Flow.Launcher.Infrastructure.UserSettings;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.SettingPages.ViewModels;

public partial class SettingsPaneProxyViewModel : BaseModel
{
    public Settings Settings { get; }

    private static readonly TimeSpan ProxyTestTimeout = TimeSpan.FromSeconds(10);

    public SettingsPaneProxyViewModel(Settings settings)
    {
        Settings = settings;
    }

    public class ProxyModeData : DropdownDataGeneric<ProxyMode> { }

    public List<ProxyModeData> ProxyModes { get; } = DropdownDataGeneric<ProxyMode>.GetValues<ProxyModeData>("ProxyMode");

    public ProxyMode ProxyMode
    {
        get => Settings.Proxy.Mode;
        set
        {
            Settings.Proxy.Mode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManual));
        }
    }

    public bool IsManual => Settings.Proxy.Mode == ProxyMode.Manual;

    [RelayCommand]
    private async Task OnTestProxyClickedAsync()
    {
        var message = await TestProxyAsync();
        App.API.ShowMsgBox(App.API.GetTranslation(message));
    }

    private async Task<string> TestProxyAsync()
    {
        WebProxy manualProxy = null;

        if (Settings.Proxy.Mode == ProxyMode.Manual)
        {
            var endpoint = ProxyResolver.Resolve(Settings.Proxy.Address);
            if (endpoint == null)
                return "proxyAddressInvalid";

            manualProxy = new WebProxy(new Uri($"{endpoint.Scheme}://{endpoint.Host}:{endpoint.Port}"), true);
            var hasCredentials = !string.IsNullOrEmpty(Settings.Proxy.UserName)
                                 && !string.IsNullOrEmpty(Settings.Proxy.Password);
            if (hasCredentials)
                manualProxy.Credentials = new NetworkCredential(Settings.Proxy.UserName, Settings.Proxy.Password);
        }

        try
        {
            var status = await SendProbeAsync(manualProxy);

            if (status == HttpStatusCode.OK)
                return "proxyIsCorrect";

            if (status == HttpStatusCode.ProxyAuthenticationRequired)
                return string.IsNullOrEmpty(Settings.Proxy.UserName) ? "proxyAuthRequired" : "proxyAuthFailed";

            return "proxyConnectFailed";
        }
        catch
        {
            return "proxyConnectFailed";
        }
    }

    /// <summary>
    /// Probes the plugin manifest the way a real request would:
    /// System mode goes through the current default proxy (the OS proxy settings),
    /// Direct mode connects directly, Manual mode goes through the parsed proxy.
    /// </summary>
    private async Task<HttpStatusCode> SendProbeAsync(WebProxy proxy)
    {
        var handler = new HttpClientHandler
        {
            // System mode (Proxy = null, UseProxy = true) goes through the
            // current default proxy, i.e. the OS proxy settings.
            UseProxy = Settings.Proxy.Mode != ProxyMode.Direct,
            Proxy = proxy,
            UseDefaultCredentials = false
        };

        using var client = new HttpClient(handler) { Timeout = ProxyTestTimeout };
        try
        {
            using var response = await client.GetAsync(PluginsManifest.PrimaryManifestUrl);
            return response.StatusCode;
        }
        catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
        {
            return HttpStatusCode.ProxyAuthenticationRequired;
        }
    }
}
