using System;
using System.Text.RegularExpressions;

namespace Flow.Launcher.Infrastructure.Http
{
    public sealed record ProxyEndpoint(string Scheme, string Host, int Port);

    /// <summary>
    /// Parses a user supplied proxy address into a concrete endpoint.
    /// The scheme is inferred from the address prefix ("socks5://" → SOCKS5,
    /// "https://" → HTTPS proxy, anything else — including bare addresses
    /// and "http://" — → HTTP). The port is part of the address
    /// (e.g. "http://127.0.0.1:8080") and is required; an address without
    /// one is invalid. Pasted userinfo ("user:pass@") is discarded in favor
    /// of the dedicated credential fields.
    /// </summary>
    public static class ProxyResolver
    {
        private static readonly Regex SchemeRegex = new(@"^([a-zA-Z][a-zA-Z0-9+.-]*):\/\/", RegexOptions.Compiled);
        private static readonly Regex PortRegex = new(@"^(.+):(\d+)$", RegexOptions.Compiled);

        public static ProxyEndpoint Resolve(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return null;

            var value = address.Trim();
            var scheme = "http";

            var schemeMatch = SchemeRegex.Match(value);
            if (schemeMatch.Success)
            {
                var parsed = schemeMatch.Groups[1].Value.ToLowerInvariant();
                if (parsed is "socks" or "socks5")
                    scheme = "socks5";
                else if (parsed == "https")
                    scheme = "https";
                value = value[schemeMatch.Length..];
            }

            var atIndex = value.LastIndexOf('@');
            if (atIndex != -1)
                value = value[(atIndex + 1)..];

            var slashIndex = value.IndexOf('/');
            if (slashIndex != -1)
                value = value[..slashIndex];

            var port = 0;
            var portMatch = PortRegex.Match(value);
            if (portMatch.Success
                && (!portMatch.Groups[1].Value.Contains(':') || portMatch.Groups[1].Value.StartsWith("[")))
            {
                value = portMatch.Groups[1].Value;
                _ = int.TryParse(portMatch.Groups[2].Value, out port);
            }

            if (string.IsNullOrEmpty(value) || port is <= 0 or > 65535)
                return null;

            return new ProxyEndpoint(scheme, value, port);
        }
    }
}
