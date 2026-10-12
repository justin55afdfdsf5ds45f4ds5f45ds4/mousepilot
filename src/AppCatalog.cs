using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MousePilot
{
    public class AppEntry
    {
        public string id;
        public string name;
        public string path;
    }

    /// <summary>Discovers local applications and keeps their launch targets out of client input.</summary>
    public class AppCatalog
    {
        private sealed class Target
        {
            public string Id;
            public string Name;
            public string Path;
            public string AppId;
            public bool Custom;
        }

        private readonly object sync = new object();
        private readonly object scanSync = new object();
        private readonly Dictionary<string, Target> targets =
            new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        private List<Target> cachedDiscovery;

        /// <summary>
        /// Runs discovery once, then returns fresh copies from the cache on later calls.
        /// The first Store-app discovery process has a ten-second timeout.
        /// </summary>
        public List<AppEntry> Scan()
        {
            lock (scanSync)
            {
                if (cachedDiscovery != null) return CurrentEntries();
                List<Target> discovered = new List<Target>();
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                AddKnownFile(discovered, Path.Combine(windows, "explorer.exe"), "File Explorer");
                AddKnownFile(discovered, Path.Combine(windows, "System32", "notepad.exe"), "Notepad");

                List<string> shortcuts = new List<string>();
                CollectShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.Programs), shortcuts, 0);
                CollectShortcuts(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), shortcuts, 0);
                shortcuts.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string shortcut in shortcuts)
                {
                    string name = Path.GetFileNameWithoutExtension(shortcut).Trim();
                    if (name.Length == 0 || IsMaintenanceShortcut(name)) continue;
                    discovered.Add(FileTarget(shortcut, name, false));
                }

                discovered.AddRange(ReadStoreApps());

                // Retain all targets by ID, including aliases, so a saved rule still resolves
                // even when another shortcut with the same display name wins the UI dedupe.
                lock (sync)
                {
                    foreach (Target target in discovered)
                    {
                        Target existing;
                        if (!targets.TryGetValue(target.Id, out existing) || !existing.Custom)
                            targets[target.Id] = target;
                    }

                    cachedDiscovery = discovered;
                    return CurrentEntries();
                }
            }
        }

        private List<AppEntry> CurrentEntries()
        {
            lock (sync)
            {
                HashSet<string> seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<AppEntry> result = new List<AppEntry>();
                foreach (Target target in cachedDiscovery)
                {
                    if (seenIds.Contains(target.Id) || !seenNames.Add(target.Name)) continue;
                    seenIds.Add(target.Id);
                    result.Add(Copy(target));
                }
                foreach (Target target in targets.Values)
                {
                    if (!target.Custom || !seenIds.Add(target.Id)) continue;
                    AppEntry entry = Copy(target);
                    if (!seenNames.Add(entry.name)) entry.name += " (custom)";
                    result.Add(entry);
                }
                result.Sort(delegate(AppEntry a, AppEntry b)
                {
                    int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(a.name, b.name);
                    return nameComparison != 0 ? nameComparison : StringComparer.Ordinal.Compare(a.id, b.id);
                });
                return result;
            }
        }

        /// <summary>Adds an explicitly selected executable or Windows shortcut.</summary>
        public AppEntry AddFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Choose an application or Windows shortcut.", "path");
            string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            string extension = Path.GetExtension(fullPath);
            if (!String.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an .exe application or .lnk shortcut.", "path");
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("The selected application could not be found.", fullPath);

            string name = Path.GetFileNameWithoutExtension(fullPath);
            if (String.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string description = FileVersionInfo.GetVersionInfo(fullPath).FileDescription;
                    if (!String.IsNullOrWhiteSpace(description)) name = description.Trim();
                }
                catch (Exception ex)
                {
                    if (!(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                        !(ex is System.ComponentModel.Win32Exception)) throw;
                }
            }
            Target target = FileTarget(fullPath, name, true);
            lock (sync)
            {
                Target existing;
                if (targets.TryGetValue(target.Id, out existing))
                {
                    existing.Custom = true;
                    return Copy(existing);
                }
                targets[target.Id] = target;
                return Copy(target);
            }
        }

        public AppEntry Find(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            lock (sync)
            {
                Target target;
                return targets.TryGetValue(id, out target) ? Copy(target) : null;
            }
        }

        /// <summary>Launches only an ID already discovered or added through the native picker.</summary>
        public void Launch(string id)
        {
            Target selected;
            lock (sync)
            {
                if (String.IsNullOrWhiteSpace(id) || !targets.TryGetValue(id, out selected))
                    throw new ArgumentException("Choose an available application before enabling this rule.", "id");
            }

            ProcessStartInfo start;
            if (selected.AppId != null)
            {
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                start = new ProcessStartInfo(explorer, "\"shell:AppsFolder\\" + selected.AppId + "\"");
            }
            else
            {
                if (!File.Exists(selected.Path))
                    throw new FileNotFoundException("This application has moved or was uninstalled. Choose it again.", selected.Path);
                start = new ProcessStartInfo(selected.Path);
                start.WorkingDirectory = Path.GetDirectoryName(selected.Path);
            }
            start.UseShellExecute = true;
            Process launched = Process.Start(start);
            if (launched != null) launched.Dispose();
        }

        private static void AddKnownFile(List<Target> result, string path, string name)
        {
            if (File.Exists(path)) result.Add(FileTarget(path, name, false));
        }

        private static Target FileTarget(string path, string name, bool custom)
        {
            string canonical = Path.GetFullPath(path);
            return new Target
            {
                Id = StableId("file:" + canonical.ToLowerInvariant()),
                Name = name,
                Path = canonical,
                Custom = custom
            };
        }

        private static string StableId(string canonical)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                StringBuilder hex = new StringBuilder(20);
                for (int i = 0; i < 10; i++) hex.Append(hash[i].ToString("x2"));
                return hex.ToString();
            }
        }

        private static AppEntry Copy(Target target)
        {
            return new AppEntry { id = target.Id, name = target.Name, path = target.Path };
        }

        private static bool IsMaintenanceShortcut(string name)
        {
            return name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Remove ", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("Readme", StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectShortcuts(string directory, List<string> result, int depth)
        {
            if (String.IsNullOrEmpty(directory) || depth > 12 || !Directory.Exists(directory)) return;
            try
            {
                result.AddRange(Directory.GetFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly));
                foreach (string child in Directory.GetDirectories(directory))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            CollectShortcuts(child, result, depth + 1);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static List<Target> ReadStoreApps()
        {
            List<Target> result = new List<Target>();
            try
            {
                string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(powershell)) return result;
                // This script is constant. No application names or client values become code.
                const string script = "$ErrorActionPreference='Stop'; [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding; @(Get-StartApps | Select-Object Name,AppID) | ConvertTo-Json -Compress";
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                ProcessStartInfo start = new ProcessStartInfo(powershell,
                    "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encoded);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = Encoding.UTF8;
                StringBuilder output = new StringBuilder();
                object outputSync = new object();
                using (Process process = new Process())
                {
                    process.StartInfo = start;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs args)
                    {
                        if (args.Data != null) lock (outputSync) output.AppendLine(args.Data);
                    };
                    process.ErrorDataReceived += delegate { };
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit(10000))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        process.WaitForExit(1000);
                        return result;
                    }
                    process.WaitForExit(); // Drain the already-exited process's output callbacks.
                    if (process.ExitCode != 0) return result;
                }
                string json;
                lock (outputSync) json = output.ToString().Trim().TrimStart('\uFEFF');
                if (json.Length == 0) return result;
                object parsed = new JavaScriptSerializer().DeserializeObject(json);
                object[] apps = parsed as object[];
                if (apps == null) apps = new object[] { parsed };
                foreach (object app in apps)
                {
                    Dictionary<string, object> fields = app as Dictionary<string, object>;
                    object rawName;
                    object rawId;
                    if (fields == null || !fields.TryGetValue("Name", out rawName) ||
                        !fields.TryGetValue("AppID", out rawId)) continue;
                    string name = rawName as string;
                    string appId = rawId as string;
                    if (String.IsNullOrWhiteSpace(name) || String.IsNullOrWhiteSpace(appId) ||
                        appId.IndexOf('!') < 0 || appId.IndexOfAny(new char[] { '"', '\r', '\n' }) >= 0) continue;
                    result.Add(new Target
                    {
                        Id = StableId("store:" + appId.ToLowerInvariant()),
                        Name = name.Trim(),
                        Path = "shell:AppsFolder\\" + appId,
                        AppId = appId
                    });
                }
            }
            catch (Exception ex)
            {
                // Start Menu discovery still works on systems without Get-StartApps.
                if (!(ex is IOException) && !(ex is UnauthorizedAccessException) &&
                    !(ex is System.ComponentModel.Win32Exception) && !(ex is InvalidOperationException) &&
                    !(ex is ArgumentException)) throw;
            }
            return result;
        }
    }
}
