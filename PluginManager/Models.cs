using ExileCore2.Shared;
using LibGit2Sharp;

namespace PluginManager.Models
{

    public class PluginData
    {
        public string FolderName { get; set; }
        public string FolderPath { get; set; }
        public PluginWrapper Plugin { get; set; }

        public bool IsGitRepo = false;
        public bool IsUpToDate = false;
        public bool IsInstalled = false;
        public PluginDescription Description { get; set; }
    }

    public class PluginDescription
    {
        public string Name { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public DateTime LastUpdated { get; set; }
        public string Location { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is PluginDescription other)
            {
                return Author.Equals(other.Author, StringComparison.OrdinalIgnoreCase) &&
                       Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }
    }
}