using ExileCore2;
using LibGit2Sharp;
using ImGuiNET;
using System.Numerics;
using PluginManager.Models;
using System.Text.Json;

namespace PluginManager
{

    public class PluginManager : BaseSettingsPlugin<PluginManagerSettings>
    {
        private List<PluginData> _installed_plugins = new();
        private List<PluginDescription> _available_plugins = new();

        private bool _isProcessing = false;
        private string _repoInput = string.Empty; // Format: https://github.com/owner/repo

        private string _statusMessage = string.Empty;
        private DateTime _statusMessageTime = DateTime.MinValue;

        public override bool Initialise()
        {
            Name = "Plugin Manager";
            Task.Run(async () => await Task.WhenAll(RefreshPluginListAsync(), FetchAvailablePlugins())).Wait();
            return true;
        }

        private PluginDescription ExtractRepositoryIdentifier(string gitUrl)
        {
            var parts = gitUrl.Split(new[] { "github.com/" }, StringSplitOptions.None)[1]
                .Replace(".git", "")
                .Split('/');

            return new PluginDescription
            {
                Author = parts[0],
                Name = parts[1]
            };
        }

        private async Task RefreshPluginListAsync()
        {
            var newPlugins = new Dictionary<string, PluginData>();
            string sourcePath = Path.Combine(PluginManager.RootDirectory, "Plugins", "Source");

            _installed_plugins.Clear();

            await Task.Run(() => {
                foreach (var folderPath in Directory.GetDirectories(sourcePath))
                {
                    if (folderPath.ToLower().Contains("pluginmanager"))
                    {
                        continue;
                    }
                    string folderName = Path.GetFileName(folderPath);
                    var plugin = PluginManager.Plugins.FirstOrDefault(x => x.Plugin.DirectoryName == folderName);
                    var pluginData = new PluginData
                    {
                        FolderName = folderName,
                        FolderPath = folderPath,
                        Plugin = plugin,
                    };

                    UpdatePluginStatus(pluginData);
                    _installed_plugins.Add(pluginData);
                }
            });
        }

        private void UpdatePluginStatus(PluginData data)
        {
            try
            {
                using (var repo = new Repository(data.FolderPath))
                {
                    data.IsGitRepo = true;
                    data.Description = ExtractRepositoryIdentifier(repo.Network.Remotes["origin"].Url);
                    data.IsInstalled = true;
                    Commands.Fetch(repo, "origin", Array.Empty<string>(), null, null);

                    var currentBranch = repo.Head.FriendlyName;
                    var trackingBranch = repo.Branches[$"origin/{currentBranch}"];

                    if (trackingBranch != null)
                    {
                        var behind = repo.ObjectDatabase.CalculateHistoryDivergence(repo.Head.Tip, trackingBranch.Tip);
                        data.IsUpToDate = behind.BehindBy == 0;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error checking git status for {data.FolderName}: {ex.Message}");
                data.IsGitRepo = false;
                data.IsUpToDate = false;
            }
        }

        public void DeleteDirectory(string targetDir)
        {
            File.SetAttributes(targetDir, FileAttributes.Normal);

            string[] files = Directory.GetFiles(targetDir);
            string[] dirs = Directory.GetDirectories(targetDir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(targetDir, false);
        }

        private async void ShowStatusMessage(string message, float durationSeconds = 3.0f)
        {
            _statusMessage = message;
            _statusMessageTime = DateTime.Now;
            await Task.Delay(TimeSpan.FromSeconds(durationSeconds));
            if (_statusMessage == message)
                _statusMessage = string.Empty;
        }

    private async Task FetchAvailablePlugins()
        {
            using (var client = new HttpClient())
            {
                _available_plugins.Clear();
                var response = await client.GetStringAsync("https://raw.githubusercontent.com/exCore2/PluginBrowserData/refs/heads/data/output.json");
                LogMessage(response);

                var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                foreach (var plugin in root.GetProperty("PluginDescriptions").EnumerateArray())
                {
                    var fork = plugin.GetProperty("Forks")[0];
                    _available_plugins.Add(new PluginDescription
                    {
                        Name = fork.GetProperty("Name").GetString(),
                        Author = fork.GetProperty("Author").GetString(),
                        Description = plugin.GetProperty("Description").GetString(),
                    });
                }
            }
        }

        public override void DrawSettings()
        {
            base.DrawSettings();

            if (_isProcessing)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0, 1));
                ImGui.Text("Working...");
                ImGui.PopStyleColor();
            }

            if (!GameController.Settings.CoreSettings.PluginSettings.AvoidLockingDllFiles)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1));
                ImGui.TextWrapped("Warning: 'Avoid Locking DLL Files' is disabled in Core Settings. This may prevent plugins from updating correctly.");
                ImGui.PopStyleColor();
            }

            ImGui.Separator();

            ImGui.BeginDisabled(_isProcessing);

            if (ImGui.Button("Refresh All") && !_isProcessing)
            {
                _isProcessing = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.WhenAll(RefreshPluginListAsync(), FetchAvailablePlugins());
                        ShowStatusMessage("Successfully refreshed all plugins");
                    }
                    catch (Exception ex)
                    {
                        ShowStatusMessage($"Failed to refresh: {ex.Message}");
                    }
                    finally
                    {
                        _isProcessing = false;
                    }
                });
            }

            ImGui.Text("Install New Plugin");
            ImGui.Text("Format: https://github.com/owner/repo");
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 100);
            ImGui.InputText("##repoinput", ref _repoInput, 256);
            ImGui.PopItemWidth();

            ImGui.SameLine();
            if (ImGui.Button("Install") && !string.IsNullOrEmpty(_repoInput) && !_isProcessing)
            {
                _isProcessing = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await InstallPluginAsync(_repoInput);
                        ShowStatusMessage($"Successfully installed plugin from {_repoInput}");
                        _repoInput = string.Empty;
                    }
                    catch (Exception ex)
                    {
                        ShowStatusMessage($"Failed to install plugin: {ex.Message}");
                    }
                    finally
                    {
                        _isProcessing = false;
                    }
                });
            }

            ImGui.Separator();

            if (ImGui.BeginTabBar("plugins_tabbar"))
            {
                if (ImGui.BeginTabItem("Installed Plugins"))
                {
                    if (ImGui.BeginTable("plugins_table", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200f);
                        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100f);
                        ImGui.TableSetupColumn("Version Status", ImGuiTableColumnFlags.WidthFixed, 150f);
                        ImGui.TableSetupColumn("Update", ImGuiTableColumnFlags.WidthFixed, 100f);
                        ImGui.TableSetupColumn("Delete", ImGuiTableColumnFlags.WidthFixed, 100f);
                        ImGui.TableHeadersRow();

                        foreach (var pluginData in _installed_plugins)
                        {
                            DrawInstalledPluginRow(pluginData);
                        }
                        ImGui.EndTable();
                    }
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Available Plugins"))
                {
                    if (ImGui.BeginTable("available_plugins_table", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200f);
                        ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Install", ImGuiTableColumnFlags.WidthFixed, 100f);
                        ImGui.TableHeadersRow();

                        foreach(var plugin in _available_plugins) 
                        { 
                            DrawAvailablePluginRow(plugin); 
                        }
                        ImGui.EndTable();
                    }
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }

            ImGui.EndDisabled();

            if (!string.IsNullOrEmpty(_statusMessage) && (DateTime.Now - _statusMessageTime).TotalSeconds < 3.0)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), _statusMessage);
            }
        }

        private void DrawInstalledPluginRow(PluginData pluginData)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(pluginData.FolderName);

            ImGui.TableNextColumn();
            if (pluginData.Plugin != null)
            {
                ImGui.TextColored(new Vector4(0, 1, 0, 1), "Loaded");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0, 0, 1), "Not Loaded");
            }

            ImGui.TableNextColumn();
            if (pluginData.IsGitRepo)
            {
                if (pluginData.IsUpToDate)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "Latest");
                }
                else
                {
                    ImGui.TextColored(new Vector4(1, 1, 0, 1), "Update Available");
                }
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Not a repo");
            }

            ImGui.TableNextColumn();
            if (pluginData.IsGitRepo)
            {
                if (ImGui.Button($"Update###{pluginData.FolderName}_update"))
                {
                    _isProcessing = true;
                    Task.Run(async () =>
                    {
                        try
                        {
                            await UpdatePluginAsync(pluginData);
                            ShowStatusMessage($"Successfully updated {pluginData.FolderName}");
                        }
                        catch (Exception ex)
                        {
                            ShowStatusMessage($"Failed to update {pluginData.FolderName}: {ex.Message}");
                        }
                        finally
                        {
                            _isProcessing = false;
                        }
                    });
                }
            }
            else
            {
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
                ImGui.Button($"Update###{pluginData.FolderName}_update");
                ImGui.PopStyleVar();
            }

            ImGui.TableNextColumn();
            if (ImGui.Button($"Delete###{pluginData.FolderName}_delete"))
            {
                _isProcessing = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await DeletePluginAsync(pluginData);
                        ShowStatusMessage($"Successfully deleted {pluginData.FolderName}");
                    }
                    catch (Exception ex)
                    {
                        ShowStatusMessage($"Failed to delete {pluginData.FolderName}: {ex.Message}");
                    }
                    finally
                    {
                        _isProcessing = false;
                    }
                });
            }
        }

        private void DrawAvailablePluginRow(PluginDescription plugin)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.Text(plugin.Name);

            ImGui.TableNextColumn();
            ImGui.TextWrapped(plugin.Description);

            ImGui.TableNextColumn();

            var isInstalled = _installed_plugins.Any(p => p.Description.Equals(plugin));
            if (isInstalled)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0, 1, 0, 1));
                ImGui.Text("Installed");
                ImGui.PopStyleColor();
            }
            else if (ImGui.Button($"Install###{plugin.Name}") && !_isProcessing)
            {
                var repoUrl = $"https://github.com/{plugin.Author}/{plugin.Name}";
                ShowStatusMessage($"Installing {repoUrl}");
                _isProcessing = true;
                Task.Run(async () =>
                {
                    try
                    {
                        await InstallPluginAsync(repoUrl);
                        ShowStatusMessage($"Successfully installed {plugin.Name}");
                    }
                    catch (Exception ex)
                    {
                        ShowStatusMessage($"Failed to install {plugin.Name}: {ex.Message}");
                    }
                    finally
                    {
                        _isProcessing = false;
                    }
                });
            }
        }

        private async Task InstallPluginAsync(string gitUrl)
        {
            await Task.Run(() => {
                if (string.IsNullOrEmpty(gitUrl))
                    throw new ArgumentException("Git URL cannot be empty");

                string repoName = Path.GetFileNameWithoutExtension(gitUrl);
                if (repoName.EndsWith(".git"))
                    repoName = repoName.Substring(0, repoName.Length - 4);

                if (string.IsNullOrEmpty(repoName))
                    throw new ArgumentException("Invalid repository url");

                string targetPath = Path.Combine(PluginManager.RootDirectory, "Plugins", "Source", repoName);

                if (Directory.Exists(targetPath))
                {
                    DeleteDirectory(targetPath);
                }

                Repository.Clone(gitUrl, targetPath, new CloneOptions
                {
                    IsBare = false,
                    Checkout = false
                });

                using (var repo = new Repository(targetPath))
                {
                    var defaultBranch = repo.Head.FriendlyName;
                    Commands.Checkout(repo, defaultBranch, new CheckoutOptions());
                }

                PluginManager.LoadFailedSourcePlugin(Path.Combine("Plugins", "Source", repoName));
            });

            await RefreshPluginListAsync();
        }

        private async Task UpdatePluginAsync(PluginData plugin)
        {
            await Task.Run(() => {
                plugin.Plugin.Close();
                PluginManager.Plugins.Remove(plugin.Plugin);

                var dir = plugin.FolderPath;
                using (var repo = new Repository(dir))
                {
                    Commands.Fetch(repo, "origin", Array.Empty<string>(), null, null);
                    var defaultBranch = repo.Head.FriendlyName;
                    var remoteBranch = repo.Branches[$"origin/{defaultBranch}"];
                    repo.Reset(ResetMode.Hard, remoteBranch.Tip);
                }

                PluginManager.LoadFailedSourcePlugin(Path.Combine("Plugins", "Source", plugin.FolderName));
            });

            await RefreshPluginListAsync();
        }

        private async Task DeletePluginAsync(PluginData plugin)
        {
            await Task.Run(() => {
                plugin.Plugin.Close();
                PluginManager.Plugins.Remove(plugin.Plugin);
                DeleteDirectory(Path.Combine(PluginManager.RootDirectory, "Plugins", "Source", plugin.FolderName));
            });

            await RefreshPluginListAsync();
        }
    }
}