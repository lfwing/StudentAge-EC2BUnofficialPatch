using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;

namespace EC2BUnofficialPatch.Workshop
{
    internal sealed class ContentRootCatalog
    {
        private const string WorkshopAppId = "1991040";

        private ContentRootCatalog(IReadOnlyList<ContentRoot> roots)
        {
            Roots = roots;
        }

        internal IReadOnlyList<ContentRoot> Roots { get; }

        internal static ContentRootCatalog Discover()
        {
            List<ContentRoot> roots = new List<ContentRoot>();
            string workshopDirectory = FindWorkshopDirectory();
            if (!Directory.Exists(workshopDirectory))
            {
                return new ContentRootCatalog(roots);
            }

            foreach (string modDirectory in Directory
                .GetDirectories(workshopDirectory)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(new ContentRoot(
                    $"workshop-{Path.GetFileName(modDirectory)}",
                    Path.GetFullPath(modDirectory).TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)));
            }

            return new ContentRootCatalog(roots);
        }

        private static string FindWorkshopDirectory()
        {
            DirectoryInfo gameDirectory = new DirectoryInfo(Paths.GameRootPath);
            DirectoryInfo commonDirectory = gameDirectory.Parent;
            DirectoryInfo steamAppsDirectory = commonDirectory?.Parent;
            if (steamAppsDirectory == null)
            {
                return string.Empty;
            }

            return Path.Combine(
                steamAppsDirectory.FullName,
                "workshop",
                "content",
                WorkshopAppId);
        }
    }

    internal sealed class ContentRoot
    {
        internal ContentRoot(string id, string path)
        {
            Id = id;
            Path = path;
        }

        internal string Id { get; }

        internal string Path { get; }
    }
}
