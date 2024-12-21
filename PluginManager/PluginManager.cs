using ExileCore2;
using ExileCore2.Shared;
using LibGit2Sharp;
using ImGuiNET;
using System.Numerics;
using ExileCore2.Shared.Interfaces;

namespace PluginManager
{
    public class PluginData
    {
        public string FolderName { get; set; }
        public string FolderPath { get; set; }
        public PluginWrapper Plugin { get; set; }
        public bool IsGitRepo { get; set; }
        public bool IsUpToDate { get; set; }
    }

    public class PluginManager : BaseSettingsPlugin<PluginManagerSettings>
    {
        private readonly Dictionary<string, PluginData> _plugins = new();
        private string _repoInput = string.Empty; // Format: https://github.com/owner/repo
        private bool _isProcessing = false;
        private string _statusMessage = string.Empty;
        private DateTime _statusMessageTime = DateTime.MinValue;

        public override bool Initialise()
        {
            Name = "Plugin Manager";
            RefreshPluginList();
            return true;
        }

        private void RefreshPluginList()
        {
            var newPlugins = new Dictionary<string, PluginData>();
            string sourcePath = Path.Combine(PluginManager.RootDirectory, "Plugins", "Source");

            if (!Directory.Exists(sourcePath))
            {
                _plugins.Clear();
                return;
            }

            foreach (var folderPath in Directory.GetDirectories(sourcePath))
            {
                string folderName = Path.GetFileName(folderPath);
                if (folderName == "PluginManager")
                {
                    continue;
                }

                var plugin = PluginManager.Plugins.FirstOrDefault(x => x.Plugin.DirectoryName == folderName);
                var pluginData = new PluginData
                {
                    FolderName = folderName,
                    FolderPath = folderPath,
                    Plugin = plugin,
                };

                UpdatePluginStatus(pluginData);
                newPlugins[folderName] = pluginData;
            }

            _plugins.Clear();
            foreach (var kvp in newPlugins)
            {
                _plugins[kvp.Key] = kvp.Value;
            }
        }

        private void UpdatePluginStatus(PluginData data)
        {
            try
            {
                using (var repo = new Repository(data.FolderPath))
                {
                    data.IsGitRepo = true;
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

        public override void DrawSettings()
        {
            base.DrawSettings();
            if (!GameController.Settings.CoreSettings.PluginSettings.AvoidLockingDllFiles)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.2f, 0.2f, 1)); // Red text
                ImGui.TextWrapped("Warning: 'Avoid Locking DLL Files' is disabled in Core Settings. This may prevent plugins from updating correctly.");
                ImGui.PopStyleColor();
            }

            ImGui.Separator();

            if (ImGui.Button("Refresh Plugins") && !_isProcessing)
            {
                _isProcessing = true;
                try
                {
                    RefreshPluginList();
                    ShowStatusMessage("Successfully refreshed plugin list");
                }
                catch (Exception ex)
                {
                    ShowStatusMessage($"Failed to refresh plugins: {ex.ToString()}");
                }
                finally
                {
                    _isProcessing = false;
                }
            }

            ImGui.SameLine();
            ImGui.Text("Install New Plugin");
            ImGui.Text("Format: https://github.com/owner/repo");
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - 100);
            if (ImGui.InputText("##repoinput", ref _repoInput, 256)) { }
            ImGui.PopItemWidth();

            ImGui.SameLine();
            if (ImGui.Button("Install") && !string.IsNullOrEmpty(_repoInput) && !_isProcessing)
            {
                _isProcessing = true;
                try
                {
                    InstallPlugin(_repoInput);
                    ShowStatusMessage($"Successfully installed plugin from {_repoInput}");
                    _repoInput = string.Empty;
                }
                catch (Exception ex)
                {
                    ShowStatusMessage($"Failed to install plugin: {ex.ToString()}");
                }
                finally
                {
                    _isProcessing = false;
                }
            }

            ImGui.Separator();
            ImGui.Text("Installed Plugins");

            if (ImGui.BeginTable("plugins_table", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 200f);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100f);
                ImGui.TableSetupColumn("Version Status", ImGuiTableColumnFlags.WidthFixed, 150f);
                ImGui.TableSetupColumn("Update", ImGuiTableColumnFlags.WidthFixed, 100f);
                ImGui.TableSetupColumn("Delete", ImGuiTableColumnFlags.WidthFixed, 100f);
                ImGui.TableHeadersRow();

                foreach (var pluginData in _plugins.Values)
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
                        if (ImGui.Button($"Update###{pluginData.FolderName}_update") && !_isProcessing)
                        {
                            _isProcessing = true;
                            try
                            {
                                UpdatePlugin(pluginData);
                                ShowStatusMessage($"Successfully updated {pluginData.FolderName}");
                            }
                            catch (Exception ex)
                            {
                                ShowStatusMessage($"Failed to update {pluginData.FolderName}: {ex.ToString()}");
                            }
                            finally
                            {
                                _isProcessing = false;
                            }
                        }
                    }
                    else
                    {
                        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
                        ImGui.Button($"Update###{pluginData.FolderName}_update");
                        ImGui.PopStyleVar();
                    }

                    ImGui.TableNextColumn();
                    if (ImGui.Button($"Delete###{pluginData.FolderName}_delete") && !_isProcessing)
                    {
                        _isProcessing = true;
                        try
                        {
                            if (pluginData.Plugin != null)
                            {
                                DeletePlugin(pluginData);
                            }
                            else
                            {
                                DeleteDirectory(pluginData.FolderPath);
                            }
                            ShowStatusMessage($"Successfully deleted {pluginData.FolderName}");
                        }
                        catch (Exception ex)
                        {
                            ShowStatusMessage($"Failed to delete {pluginData.FolderName}: {ex.ToString()}");
                        }
                        finally
                        {
                            _isProcessing = false;
                        }
                    }
                }
            }
            ImGui.EndTable();

            if (!string.IsNullOrEmpty(_statusMessage) &&
                (DateTime.Now - _statusMessageTime).TotalSeconds < 3.0)
            {
                ImGui.Separator();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), _statusMessage);
            }
        }

        public void InstallPlugin(string gitUrl)
        {
            if (string.IsNullOrEmpty(gitUrl))
                throw new ArgumentException("Git URL cannot be empty");

            string repoName = Path.GetFileNameWithoutExtension(gitUrl);
            if (repoName.EndsWith(".git"))
                repoName = repoName.Substring(0, repoName.Length - 4);

            string targetPath = Path.Combine(PluginManager.RootDirectory, "Plugins", "Source", repoName);

            if (Directory.Exists(targetPath))
            {
                DeleteDirectory(targetPath);
            }

            try
            {
                Repository.Clone(gitUrl, targetPath, new CloneOptions
                {
                    IsBare = false,
                    Checkout = false
                });

                using (var repo = new Repository(targetPath))
                {
                    var defaultBranch = repo.Head.FriendlyName;
                    Commands.Checkout(repo, defaultBranch, new CheckoutOptions
                    {
                    });
                }

                PluginManager.LoadFailedSourcePlugin(Path.Combine("Plugins", "Source", repoName));
            }
            catch (Exception ex)
            {
                if (Directory.Exists(targetPath))
                {
                    DeleteDirectory(targetPath);
                }
                LogMessage(ex.ToString());
            }

            RefreshPluginList();
        }

        public void UpdatePlugin(PluginData plugin)
        {
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
            RefreshPluginList();
        }

        public void DeletePlugin(PluginData plugin)
        {
            plugin.Plugin.Close();
            PluginManager.Plugins.Remove(plugin.Plugin);
            DeleteDirectory(Path.Combine(PluginManager.RootDirectory, "Plugins", "Source", plugin.FolderName));
            RefreshPluginList();
        }
    }
}