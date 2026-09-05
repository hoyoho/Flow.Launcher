using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Flow.Launcher.Infrastructure;
using Flow.Launcher.Infrastructure.UserSettings;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.SharedCommands;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Flow.Launcher.Core
{
    public class Updater
    {
        public string GitHubRepository { get; init; }

        private static readonly string ClassName = nameof(Updater);

        private readonly IPublicAPI _api;

        public Updater(IPublicAPI publicAPI, string gitHubRepository)
        {
            _api = publicAPI;
            GitHubRepository = gitHubRepository;
        }

        private SemaphoreSlim UpdateLock { get; } = new SemaphoreSlim(1);

        public async Task UpdateAppAsync(bool silentUpdate = true)
        {
            await UpdateLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!silentUpdate)
                    _api.ShowMsg(Localize.pleaseWait(),
                        Localize.update_flowlauncher_update_check());

                var updateManager = new UpdateManager(new GithubSource(GitHubRepository, null, false));

                var newUpdateInfo = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);

                if (newUpdateInfo == null)
                {
                    if (!silentUpdate)
                        _api.ShowMsgBox(Localize.update_flowlauncher_already_on_latest());
                    return;
                }

                var newRelease = newUpdateInfo.TargetFullRelease;

                _api.LogInfo(ClassName, $"Future Release <{Formatted(newRelease)}>");

                if (!silentUpdate)
                    _api.ShowMsg(Localize.update_flowlauncher_update_found(),
                        Localize.update_flowlauncher_updating());

                await updateManager.DownloadUpdatesAsync(newUpdateInfo).ConfigureAwait(false);

                if (DataLocation.PortableDataLocationInUse())
                {
                    var targetDestination = Path.Combine(RootAppDir(), DataLocation.PortableFolderName);
                    FilesFolders.CopyAll(DataLocation.PortableDataPath, targetDestination, (s) => _api.ShowMsgBox(s));
                    if (!FilesFolders.VerifyBothFolderFilesEqual(DataLocation.PortableDataPath, targetDestination,
                            (s) => _api.ShowMsgBox(s)))
                        _api.ShowMsgBox(Localize.update_flowlauncher_fail_moving_portable_user_profile_data(DataLocation.PortableDataPath, targetDestination));
                }

                var newVersionTips = NewVersionTips(newRelease.Version.ToNormalizedString());

                _api.LogInfo(ClassName, $"Update success:{newVersionTips}");

                if (_api.ShowMsgBox(newVersionTips, Localize.update_flowlauncher_new_update(),
                        MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    updateManager.ApplyUpdatesAndRestart(newRelease);
                }
                else
                {
                    updateManager.WaitExitThenApplyUpdates(newRelease);
                }
            }
            catch (Exception e)
            {
                if (e is HttpRequestException or WebException or SocketException ||
                    e.InnerException is TimeoutException)
                {
                    _api.LogException(ClassName,
                        $"Check your connection and proxy settings to github-cloud.s3.amazonaws.com.", e);
                }
                else
                {
                    _api.LogException(ClassName, $"Error Occurred", e);
                }

                if (!silentUpdate)
                    _api.ShowMsgError(Localize.update_flowlauncher_fail(),
                        Localize.update_flowlauncher_check_connection());
            }
            finally
            {
                UpdateLock.Release();
            }
        }

        public static void RecoverPortableData()
        {
            try
            {
                var locator = CurrentLocator();
                var stagedPortableDataPath = Path.Combine(locator.RootAppDir!, DataLocation.PortableFolderName);

                if (!Directory.Exists(stagedPortableDataPath))
                    return;

                FilesFolders.CopyAll(stagedPortableDataPath,
                    Path.Combine(locator.AppContentDir!, DataLocation.PortableFolderName), null);
                Directory.Delete(stagedPortableDataPath, true);
            }
            catch (Exception)
            {
            }
        }

        private static IVelopackLocator CurrentLocator()
        {
            if (VelopackLocator.IsCurrentSet)
                return VelopackLocator.Current;

            return VelopackLocator.CreateDefaultForPlatform();
        }

        private static string RootAppDir()
        {
            return CurrentLocator().RootAppDir ?? Constant.RootDirectory;
        }

        private static string NewVersionTips(string version)
        {
            var tips = Localize.newVersionTips(version);

            return tips;
        }

        private static string Formatted<T>(T t)
        {
            var formatted = JsonSerializer.Serialize(t, new JsonSerializerOptions { WriteIndented = true });

            return formatted;
        }
    }
}
