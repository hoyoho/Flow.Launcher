using System;
using System.IO;

namespace Flow.Launcher.Infrastructure.UserSettings
{
    public static class DataLocation
    {
        public const string PortableFolderName = "UserData";
        public const string DeletionIndicatorFile = ".dead";
        public static string PortableDataPath = Path.Combine(Constant.ProgramDirectory, PortableFolderName);

        public static string RoamingDataPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlowLauncher");

        public static bool IsDevVersion => Constant.Version == "1.0.0";

        static DataLocation()
        {
            if (!IsUnderPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Constant.ProgramDirectory)
                && !IsDevVersion)
            {
                try
                {
                    Directory.CreateDirectory(PortableDataPath);
                }
                catch (System.Exception)
                {
                }
            }

            CacheDirectory = Path.Combine(DataDirectory(), Constant.Cache);
            SettingsDirectory = Path.Combine(DataDirectory(), Constant.Settings);
            PluginsDirectory = Path.Combine(DataDirectory(), Constant.Plugins);
            ThemesDirectory = Path.Combine(DataDirectory(), Constant.Themes);
            PluginSettingsDirectory = Path.Combine(SettingsDirectory, Constant.Plugins);
            PluginCacheDirectory = Path.Combine(DataDirectory(), Constant.Cache, Constant.Plugins);
            PluginEnvironmentsPath = Path.Combine(DataDirectory(), PluginEnvironments);
        }

        public static string DataDirectory()
        {
            if (PortableDataLocationInUse())
                return PortableDataPath;

            return RoamingDataPath;
        }

        public static bool PortableDataLocationInUse()
        {
            if (Directory.Exists(PortableDataPath) &&
                !File.Exists(Path.Combine(PortableDataPath, DeletionIndicatorFile)))
                return true;

            return false;
        }

        public static string VersionLogDirectory => Path.Combine(LogDirectory, Constant.Version);
        public static string LogDirectory => Path.Combine(DataDirectory(), Constant.Logs);

        public static readonly string CacheDirectory;
        public static readonly string SettingsDirectory;
        public static readonly string PluginsDirectory;
        public static readonly string ThemesDirectory;

        public static readonly string PluginSettingsDirectory;
        public static readonly string PluginCacheDirectory;

        public const string PythonEnvironmentName = "Python";
        public const string NodeEnvironmentName = "Node.js";
        public const string PluginEnvironments = "Environments";
        public const string PluginDeleteFile = "NeedDelete.txt";
        public static readonly string PluginEnvironmentsPath;

        private static bool IsUnderPath(string parentPath, string childPath)
        {
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentPath));
            var child = Path.GetFullPath(childPath);

            if (string.Equals(parent, child, StringComparison.OrdinalIgnoreCase))
                return true;

            var relativePath = Path.GetRelativePath(parent, child);

            return !string.IsNullOrEmpty(relativePath)
                && !relativePath.Equals(".", StringComparison.Ordinal)
                && !relativePath.StartsWith("..", StringComparison.Ordinal)
                && !Path.IsPathRooted(relativePath);
        }
    }
}
