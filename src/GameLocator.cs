using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Heat5Music;

static class GameLocator
{
    public static string? Find()
    {
        foreach (var candidate in Candidates())
        {
            if (HasBank(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? Resolve(string picked)
    {
        if (string.IsNullOrWhiteSpace(picked))
        {
            return null;
        }

        if (HasBank(picked))
        {
            return picked;
        }

        var parent = Directory.GetParent(picked);
        if (parent != null && HasBank(parent.FullName))
        {
            return parent.FullName;
        }

        if (parent?.Parent != null && HasBank(parent.Parent.FullName))
        {
            return parent.Parent.FullName;
        }

        return null;
    }

    static bool HasBank(string root)
    {
        return File.Exists(MusicBank.StreamsPath(root));
    }

    static IEnumerable<string> Candidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var steamRoot in SteamRoots())
        {
            var direct = Path.Combine(steamRoot, "steamapps", "common", "NASCAR Heat 5");
            if (seen.Add(direct))
            {
                yield return direct;
            }

            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }

            var text = File.ReadAllText(vdf);
            foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
            {
                var lib = match.Groups[1].Value.Replace(@"\\", @"\");
                var extra = Path.Combine(lib, "steamapps", "common", "NASCAR Heat 5");
                if (seen.Add(extra))
                {
                    yield return extra;
                }
            }
        }

        yield return @"D:\SteamLibrary\steamapps\common\NASCAR Heat 5";
        yield return @"C:\Program Files (x86)\Steam\steamapps\common\NASCAR Heat 5";
        yield return @"C:\Program Files\Steam\steamapps\common\NASCAR Heat 5";

        foreach (var root in LocalRoots())
        {
            if (seen.Add(root))
            {
                yield return root;
            }
        }
    }

    static IEnumerable<string> LocalRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            })
            {
                RegistryKey? uninstall;
                try
                {
                    uninstall = hive.OpenSubKey(path, false);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (System.Security.SecurityException)
                {
                    continue;
                }

                if (uninstall == null)
                {
                    continue;
                }

                using (uninstall)
                {
                    foreach (var name in uninstall.GetSubKeyNames())
                    {
                        using var key = uninstall.OpenSubKey(name);
                        if (key == null)
                        {
                            continue;
                        }

                        var display = key.GetValue("DisplayName") as string;
                        if (display == null || display.IndexOf("NASCAR Heat 5", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        var location = (key.GetValue("InstallLocation") as string ?? "").Trim().TrimEnd('\\', '/');
                        if (location.Length > 0 && seen.Add(location))
                        {
                            yield return location;
                        }
                    }
                }
            }
        }

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            foreach (var rel in new[] { @"Games\NASCAR Heat 5", @"NASCAR Heat 5" })
            {
                var root = Path.Combine(drive.RootDirectory.FullName, rel);
                if (seen.Add(root))
                {
                    yield return root;
                }
            }
        }
    }

    static IEnumerable<string> SteamRoots()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrWhiteSpace(path))
        {
            yield return path.Replace('/', '\\');
        }
    }
}
