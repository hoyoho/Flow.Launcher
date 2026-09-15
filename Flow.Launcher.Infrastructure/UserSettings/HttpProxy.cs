using System;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Infrastructure.UserSettings
{
    public enum ProxyMode
    {
        System = 0,
        Direct,
        Manual
    }

    public class HttpProxy : IJsonOnDeserialized
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ProxyMode Mode
        {
            get => _mode;
            set
            {
                _mode = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Proxy endpoint in the form "host:port", optionally with a scheme prefix:
        /// "http://host:port" (default when omitted), "https://host:port" or "socks5://host:port".
        /// Pasted "user:pass@" userinfo is ignored; use UserName/Password instead.
        /// </summary>
        public string Address
        {
            get => _address;
            set
            {
                _address = value;
                OnPropertyChanged();
            }
        }

        public string UserName
        {
            get => _userName;
            set
            {
                _userName = value;
                OnPropertyChanged();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged();
            }
        }

        // Legacy fields kept only so old settings files deserialize cleanly;
        // OnDeserialized migrates them into Mode/Address.
        public bool Enabled { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }

        private ProxyMode _mode = ProxyMode.System;
        private string _address = string.Empty;
        private string _userName;
        private string _password;

        public event Action Changed;

        private void OnPropertyChanged() => Changed?.Invoke();

        public void OnDeserialized()
        {
            if (string.IsNullOrEmpty(Address) && !string.IsNullOrEmpty(Server) && Port > 0)
            {
                Address = $"http://{Server}:{Port}";
                Mode = Enabled ? ProxyMode.Manual : ProxyMode.System;

                // Clear migrated legacy values so they are not written back
                Enabled = false;
                Server = null;
                Port = 0;
            }
        }
    }
}
