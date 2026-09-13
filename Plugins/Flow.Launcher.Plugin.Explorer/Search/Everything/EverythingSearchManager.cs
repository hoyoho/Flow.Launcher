using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.Explorer.Exceptions;
using Flow.Launcher.Plugin.Explorer.Search.IProvider;

namespace Flow.Launcher.Plugin.Explorer.Search.Everything
{
    public class EverythingSearchManager : IIndexProvider, IContentIndexProvider, IPathIndexProvider
    {
        private Settings Settings { get; }
        private readonly Lock _syncRoot = new();
        private bool isApiInitialized;
        private IEverythingApi api;

        public EverythingSearchManager(Settings settings)
        {
            Settings = settings;
        }

        public async IAsyncEnumerable<SearchResult> SearchAsync(string search, [EnumeratorCancellation] CancellationToken token)
        {
            await EnsureAvailableAsync(token);

            if (token.IsCancellationRequested)
                yield break;

            // When full-path search is enabled, a bare keyword matches against the
            // entire path which can produce thousands of results and push the
            // exact file/folder name match out of the MaxResult window.
            // Run a name-only pass first so name matches are yielded first and
            // always fit within MaxResult; then append path-only matches.
            if (Settings.EverythingSearchFullPath)
            {
                var nameOption = new EverythingSearchOption(search,
                    Settings.SortOption,
                    MaxCount: Settings.MaxResult,
                    IsFullPathSearch: false,
                    IsRunCounterEnabled: Settings.EverythingEnableRunCount);

                var nameResults = new List<SearchResult>();
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                await foreach (var result in api.SearchAsync(nameOption, token))
                {
                    nameResults.Add(result);
                    seenPaths.Add(result.FullPath);
                    yield return result;
                }

                if (token.IsCancellationRequested)
                    yield break;

                var remaining = Settings.MaxResult - nameResults.Count;
                if (remaining > 0)
                {
                    var pathOption = new EverythingSearchOption(search,
                        Settings.SortOption,
                        MaxCount: Settings.MaxResult,
                        IsFullPathSearch: true,
                        IsRunCounterEnabled: Settings.EverythingEnableRunCount);

                    int yielded = 0;
                    await foreach (var result in api.SearchAsync(pathOption, token))
                    {
                        if (seenPaths.Contains(result.FullPath))
                            continue;
                        yield return result;
                        if (++yielded >= remaining)
                            break;
                    }
                }

                yield break;
            }

            var option = new EverythingSearchOption(search,
                Settings.SortOption,
                MaxCount: Settings.MaxResult,
                IsFullPathSearch: Settings.EverythingSearchFullPath,
                IsRunCounterEnabled: Settings.EverythingEnableRunCount);

            await foreach (var result in api.SearchAsync(option, token))
                yield return result;
        }

        public async IAsyncEnumerable<SearchResult> ContentSearchAsync(string plainSearch, string contentSearch,
            [EnumeratorCancellation] CancellationToken token)
        {
            await EnsureAvailableAsync(token);

            if (!Settings.EnableEverythingContentSearch)
            {
                throw new EngineNotAvailableException(Enum.GetName(Settings.IndexSearchEngineOption.Everything)!,
                    Localize.flowlauncher_plugin_everything_enable_content_search(),
                    Localize.flowlauncher_plugin_everything_enable_content_search_tips(),
                    Constants.EverythingErrorImagePath,
                    _ =>
                    {
                        Settings.EnableEverythingContentSearch = true;
                        return ValueTask.FromResult(true);
                    });
            }

            if (token.IsCancellationRequested)
                yield break;

            var option = new EverythingSearchOption(plainSearch,
                Settings.SortOption,
                IsContentSearch: true,
                ContentSearchKeyword: contentSearch,
                MaxCount: Settings.MaxResult,
                IsFullPathSearch: Settings.EverythingSearchFullPath,
                IsRunCounterEnabled: Settings.EverythingEnableRunCount);

            await foreach (var result in api.SearchAsync(option, token))
            {
                yield return result;
            }
        }

        public async IAsyncEnumerable<SearchResult> EnumerateAsync(string path, string search, bool recursive, [EnumeratorCancellation] CancellationToken token)
        {
            await EnsureAvailableAsync(token);

            if (token.IsCancellationRequested)
                yield break;

            var option = new EverythingSearchOption(search,
                Settings.SortOption,
                ParentPath: path,
                IsRecursive: recursive,
                MaxCount: Settings.MaxResult,
                IsFullPathSearch: Settings.EverythingSearchFullPath,
                IsRunCounterEnabled: Settings.EverythingEnableRunCount);

            await foreach (var result in api.SearchAsync(option, token))
                yield return result;
        }

        public void InitializeApi(string sdkDirectory)
        {
            lock (_syncRoot)
            {
                if (isApiInitialized)
                    return;

                if (Settings.EnableEverything15Support)
                {
                    if (!Everything3ApiDllImport.IsLoaded)
                        Everything3ApiDllImport.Load(sdkDirectory);
                }
                else
                {
                    if (!EverythingApiDllImport.IsLoaded)
                        EverythingApiDllImport.Load(sdkDirectory);
                }

                api = Settings.EnableEverything15Support
                    ? new EverythingApiV3(Settings.Everything15InstanceName)
                    : new LegacyEverythingApi();
                isApiInitialized = true;
            }
        }

        private async Task EnsureAvailableAsync(CancellationToken token)
        {
            var engineName = Enum.GetName(Settings.IndexSearchEngineOption.Everything)!;
            try
            {
                await api.CheckAvailableAsync(token);
            }
            catch (OperationCanceledException)
            {
                // ignore, the search was cancelled
            }
            catch (Exceptions.IPCErrorException) when (api is LegacyEverythingApi)
            {
                throw new EngineNotAvailableException(engineName,
                    Localize.flowlauncher_plugin_everything_click_to_launch_or_install(),
                    Localize.flowlauncher_plugin_everything_is_not_running(),
                    Constants.EverythingErrorImagePath,
                    ClickToInstallEverythingAsync);
            }
            catch (Exceptions.IPCErrorException)
            {
                throw new EngineNotAvailableException(engineName,
                    Localize.flowlauncher_plugin_everything_15_resolution(),
                    Localize.flowlauncher_plugin_everything_15_unavailable(),
                    Constants.EverythingErrorImagePath);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                throw new EngineNotAvailableException(engineName,
                    Localize.flowlauncher_plugin_everything_architecture_check(),
                    Constants.GeneralSearchErrorImagePath,
                    Localize.flowlauncher_plugin_everything_sdk_issue());
            }
        }

        private async ValueTask<bool> ClickToInstallEverythingAsync(ActionContext _)
        {
            try
            {
                var installedPath = await EverythingDownloadHelper.PromptDownloadIfNotInstallAsync(Settings.EverythingInstalledPath, Main.Context.API);

                if (installedPath == null)
                {
                    Main.Context.API.ShowMsgError(Localize.flowlauncher_plugin_everything_not_found());
                    Main.Context.API.LogError(nameof(EverythingSearchManager), "Unable to find Everything.exe");
                    return false;
                }

                Settings.EverythingInstalledPath = installedPath;
                Process.Start(installedPath, "-startup");
                return true;
            }
            catch (Exception e)
            {
                Main.Context.API.ShowMsgError(Localize.flowlauncher_plugin_everything_install_issue());
                Main.Context.API.LogException(nameof(EverythingSearchManager), "Failed to install Everything", e);
                return false;
            }
        }

        public bool IsFastSortOption(EverythingSortOption sortOption) => api.IsFastSortOption(sortOption);

        public Task IncrementRunCounterAsync(string fileOrFolder) => api.IncrementRunCounterAsync(fileOrFolder);
    }
}
