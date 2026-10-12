using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MousePilot
{
    public class Configuration
    {
        public bool enabled = true;
        public List<GestureRule> rules = new List<GestureRule>();
        public List<string> customApps = new List<string>();
    }
    public class ActivityEntry
    {
        public string id, time, message, kind;
    }
    internal class MouseInput
    {
        public string token;
        public long time;
    }
    internal static class Program
    {
        internal static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MousePilot");
        internal static readonly string SessionFile = Path.Combine(DataDirectory, "session.json");
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            string sid = WindowsIdentity.GetCurrent().User.Value;
            using (Mutex mutex = new Mutex(true, "Local\\MousePilot-" + sid, out created))
            {
                if (!created)
                {
                    if (!args.Contains("--background") && File.Exists(SessionFile))
                    {
                        try
                        {
                            var session = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(File.ReadAllText(SessionFile));
                            Process.Start(new ProcessStartInfo(session["url"]) { UseShellExecute = true });
                        }
                        catch { MessageBox.Show("MousePilot is already running. Open its dashboard from the system tray.", "MousePilot"); }
                    }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    using (PilotContext context = new PilotContext(args.Contains("--background") || args.Contains("--no-browser")))
                        Application.Run(context);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("MousePilot could not start: " + ex.Message, "MousePilot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
    internal sealed class PilotContext : ApplicationContext
    {
        private readonly object gate = new object();
        private readonly AppCatalog catalog = new AppCatalog();
        private readonly GestureMatcher matcher = new GestureMatcher();
        private readonly BlockingCollection<MouseInput> inputs = new BlockingCollection<MouseInput>(512);
        private readonly List<ActivityEntry> activity = new List<ActivityEntry>();
        private readonly string configFile = Path.Combine(Program.DataDirectory, "rules.json");
        private readonly string webRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web");
        private readonly Control dispatcher = new Control();
        private readonly NativeMouseHook hook = new NativeMouseHook();
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem pauseItem;
        private Configuration config = new Configuration();
        private HttpListener listener;
        private string origin, token, dashboardUrl;
        private long presenceUntil;
        private long inputBoundary;
        private volatile bool stopping;

        internal PilotContext(bool quiet)
        {
            Directory.CreateDirectory(Program.DataDirectory);
            if (!File.Exists(Path.Combine(webRoot, "index.html"))) throw new IOException("The web folder is missing. Keep it beside MousePilot.exe.");
            IntPtr unused = dispatcher.Handle;
            catalog.Scan();
            LoadConfig();
            token = NewToken();
            StartServer();
            dashboardUrl = origin + "/#token=" + token;
            File.WriteAllText(Program.SessionFile, Json(new { url = dashboardUrl, pid = Process.GetCurrentProcess().Id }), Encoding.UTF8);
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open dashboard", null, delegate { OpenDashboard(); });
            pauseItem = new ToolStripMenuItem("Pause all rules", null, delegate { SetEnabled(!config.enabled); });
            menu.Items.Add(pauseItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Quit MousePilot", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Text = "MousePilot", Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath), ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { OpenDashboard(); };
            UpdateTray();
            hook.Input += delegate(string value, long time) { inputs.TryAdd(new MouseInput { token = value, time = time }); };
            hook.Start();
            AddActivity("MousePilot is ready. Rules apply as soon as you save.", "info");
            new Thread(ProcessInputs) { IsBackground = true, Name = "MousePilot gestures" }.Start();
            if (!quiet) OpenDashboard();
        }
        private static string Json(object value) { return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.Serialize(value); }
        private static string NewToken()
        {
            byte[] bytes = new byte[32];
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
        private void OpenDashboard() { Process.Start(new ProcessStartInfo(dashboardUrl) { UseShellExecute = true }); }
        private void LoadConfig()
        {
            if (!File.Exists(configFile)) return;
            try
            {
                var loaded = new JavaScriptSerializer().Deserialize<Configuration>(File.ReadAllText(configFile));
                if (loaded == null || loaded.rules == null || loaded.customApps == null) throw new InvalidDataException("Invalid settings");
                foreach (string path in loaded.customApps) { try { catalog.AddFile(path); } catch { } }
                config = loaded;
                var ids = new HashSet<string>();
                foreach (var rule in config.rules)
                {
                    // A removed application should leave its rule visible for repair.
                    try { ValidateRule(rule, false); if (!ids.Add(rule.id)) throw new InvalidDataException("Duplicate rule ID"); }
                    catch { rule.enabled = false; }
                }
            }
            catch (Exception ex)
            {
                string backup = configFile + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Copy(configFile, backup, false);
                config = new Configuration();
                AddActivity("Settings could not be read. A backup was kept: " + ex.Message, "error");
            }
        }
        private Configuration CloneConfig() { return new JavaScriptSerializer().Deserialize<Configuration>(Json(config)); }
        private void Commit(Configuration next)
        {
            string temporary = configFile + ".tmp";
            File.WriteAllText(temporary, Json(next), Encoding.UTF8);
            if (File.Exists(configFile)) File.Replace(temporary, configFile, configFile + ".bak");
            else File.Move(temporary, configFile);
            config = next;
            matcher.Reset();
            inputBoundary = MonotonicMilliseconds();
        }
        private static long MonotonicMilliseconds()
        {
            long ticks = Stopwatch.GetTimestamp(), frequency = Stopwatch.Frequency;
            return (ticks / frequency) * 1000 + (ticks % frequency) * 1000 / frequency;
        }
        private void SetEnabled(bool enabled)
        {
            lock (gate)
            {
                var next = CloneConfig(); next.enabled = enabled; Commit(next);
                AddActivity(enabled ? "All rules resumed." : "All rules paused.", "info");
            }
            dispatcher.BeginInvoke(new Action(UpdateTray));
        }
        private void UpdateTray()
        {
            pauseItem.Text = config.enabled ? "Pause all rules" : "Resume all rules";
            tray.Text = config.enabled ? "MousePilot - rules active" : "MousePilot - paused";
        }
        private void AddActivity(string message, string kind)
        {
            lock (gate)
            {
                activity.Insert(0, new ActivityEntry { id = Guid.NewGuid().ToString("N"), time = DateTime.UtcNow.ToString("o"), message = message, kind = kind });
                if (activity.Count > 40) activity.RemoveAt(activity.Count - 1);
            }
        }
        private void ProcessInputs()
        {
            try
            {
                foreach (var input in inputs.GetConsumingEnumerable())
                {
                    if (stopping) return;
                    string match = null;
                    lock (gate)
                    {
                        if (input.time <= inputBoundary || MonotonicMilliseconds() - input.time > 3000) continue;
                        if (!config.enabled || Stopwatch.GetTimestamp() < presenceUntil) matcher.Reset();
                        else match = matcher.Feed(input.token, input.time, config.rules);
                    }
                    if (match != null)
                    {
                        try { LaunchRule(match, false); }
                        catch (Exception ex) { AddActivity("Could not run a matched rule: " + ex.Message, "error"); }
                    }
                }
            }
            catch (Exception ex) { if (!stopping) AddActivity("Gesture processing stopped: " + ex.Message, "error"); }
        }
        private void LaunchRule(string id, bool test)
        {
            lock (gate)
            {
                if (stopping) return;
                var rule = config.rules.Find(r => r.id == id);
                if (rule == null)
                {
                    if (test) throw new ArgumentException("This rule no longer exists.");
                    return;
                }
                if (!test && (!config.enabled || !rule.enabled)) return;
                try
                {
                    if (rule.action.kind == "app") catalog.Launch(rule.action.appId);
                    else Process.Start(new ProcessStartInfo(rule.action.url) { UseShellExecute = true });
                    rule.lastRun = DateTime.UtcNow.ToString("o"); rule.runCount++;
                    // Persist counts without resetting the matcher's post-launch cooldown.
                    string temp = configFile + ".tmp";
                    File.WriteAllText(temp, Json(config), Encoding.UTF8);
                    if (File.Exists(configFile)) File.Replace(temp, configFile, configFile + ".bak"); else File.Move(temp, configFile);
                    AddActivity((test ? "Tested: " : "Triggered: ") + rule.name, "success");
                }
                catch (Exception ex)
                {
                    AddActivity("Could not run " + rule.name + ": " + ex.Message, "error");
                    if (test) throw new InvalidOperationException("Could not run the action: " + ex.Message);
                }
            }
        }
        private void ValidateRule(GestureRule rule, bool checkApp)
        {
            if (rule == null) throw new ArgumentException("A rule is required.");
            rule.name = (rule.name ?? "").Trim();
            if (rule.name.Length < 1 || rule.name.Length > 80) throw new ArgumentException("Give your rule a name (up to 80 characters).");
            if (rule.sequence == null || rule.sequence.Count < 1 || rule.sequence.Count > 8) throw new ArgumentException("Choose 1 to 8 mouse inputs.");
            string[] allowed = { "left", "right", "middle", "wheelUp", "wheelDown", "back", "forward" };
            if (rule.sequence.Any(s => !allowed.Contains(s))) throw new ArgumentException("Unknown mouse input.");
            if (rule.gapMs < 300 || rule.gapMs > 3000) throw new ArgumentException("Time between inputs must be between 300 and 3000 milliseconds.");
            if (rule.action == null) throw new ArgumentException("Choose an action.");
            if (rule.action.kind == "app")
            {
                if (checkApp && catalog.Find(rule.action.appId) == null) throw new ArgumentException("Choose an installed application or browse for one.");
            }
            else if (rule.action.kind == "url")
            {
                Uri uri;
                if (!Uri.TryCreate(rule.action.url, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https") || !String.IsNullOrEmpty(uri.UserInfo) || rule.action.url.Length > 2048)
                    throw new ArgumentException("Enter a complete http:// or https:// website address.");
            }
            else throw new ArgumentException("Choose an application or a website action.");
            if (rule.enabled)
            {
                foreach (var other in config.rules.Where(r => r.enabled && r.id != rule.id && r.sequence != null))
                {
                    int length = Math.Min(rule.sequence.Count, other.sequence.Count);
                    if (rule.sequence.Take(length).SequenceEqual(other.sequence.Take(length)))
                        throw new ArgumentException("This sequence overlaps with \"" + other.name + "\". Change the sequence or pause that rule first.");
                }
            }
        }
        private bool StartupEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                return key != null && key.GetValue("MousePilot") != null;
        }
        private object State()
        {
            lock (gate)
            {
                return new { enabled = config.enabled, hookActive = hook.Active, rules = config.rules.ToArray(), apps = catalog.Scan(), activity = activity.ToArray(), startup = StartupEnabled(), version = "1.0.0" };
            }
        }
        private void StartServer()
        {
            for (int port = 47831; port <= 47841; port++)
            {
                var candidate = new HttpListener();
                string address = "http://localhost:" + port;
                candidate.Prefixes.Add(address + "/");
                try { candidate.Start(); listener = candidate; origin = address; break; }
                catch (HttpListenerException) { candidate.Close(); }
            }
            if (listener == null) throw new IOException("Could not open the local dashboard port (47831-47841).");
            new Thread(delegate()
            {
                while (!stopping)
                {
                    try { var context = listener.GetContext(); ThreadPool.QueueUserWorkItem(delegate { HandleRequest(context); }); }
                    catch (HttpListenerException) { break; }
                    catch (ObjectDisposedException) { break; }
                }
            }) { IsBackground = true, Name = "MousePilot dashboard" }.Start();
        }
        private static bool SecureEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int difference = 0; for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i]; return difference == 0;
        }
        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request; var response = context.Response;
            try
            {
                response.Headers["Cache-Control"] = "no-store";
                response.Headers["X-Content-Type-Options"] = "nosniff";
                response.Headers["X-Frame-Options"] = "DENY";
                response.Headers["Referrer-Policy"] = "no-referrer";
                response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
                if (!IPAddress.IsLoopback(request.RemoteEndPoint.Address) || request.Url.Authority != new Uri(origin).Authority)
                { Respond(response, 403, new { error = "Local access only." }); return; }
                string path = request.Url.AbsolutePath;
                if (path.StartsWith("/api/"))
                {
                    if (!SecureEquals(request.Headers["X-MousePilot-Token"], token)) { Respond(response, 401, new { error = "Open MousePilot.exe or use the tray menu to connect this dashboard." }); return; }
                    string suppliedOrigin = request.Headers["Origin"];
                    if (suppliedOrigin != null && suppliedOrigin != origin) { Respond(response, 403, new { error = "This request came from another website." }); return; }
                    if (request.HttpMethod == "GET" && path == "/api/state") { lock (gate) { Respond(response, 200, State()); } return; }
                    if (request.HttpMethod != "POST") { Respond(response, 405, new { error = "Method not supported." }); return; }
                    if (request.ContentType == null || !request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) { Respond(response, 415, new { error = "Use JSON." }); return; }
                    if (request.ContentLength64 < 0 || request.ContentLength64 > 65536) { Respond(response, 413, new { error = "Request too large." }); return; }
                    string body;
                    using (var reader = new StreamReader(request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
                    var serializer = new JavaScriptSerializer();
                    var values = serializer.Deserialize<Dictionary<string, object>>(body);
                    if (values == null) throw new ArgumentException("A JSON object is required.");
                    if (path == "/api/presence")
                    {
                        bool focused = Convert.ToBoolean(values["focused"]);
                        lock (gate)
                        {
                            bool wasFocused = Stopwatch.GetTimestamp() < presenceUntil;
                            presenceUntil = focused ? Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * 2.5) : 0;
                            if (focused || wasFocused) { matcher.Reset(); inputBoundary = MonotonicMilliseconds(); }
                        }
                        Respond(response, 200, new { ok = true }); return;
                    }
                    if (path == "/api/browse")
                    {
                        AppEntry entry = null;
                        dispatcher.Invoke(new Action(delegate()
                        {
                            using (var dialog = new OpenFileDialog { Title = "Choose an app for MousePilot", Filter = "Applications and shortcuts (*.exe;*.lnk)|*.exe;*.lnk", CheckFileExists = true, Multiselect = false, DereferenceLinks = false })
                                if (dialog.ShowDialog() == DialogResult.OK) entry = catalog.AddFile(dialog.FileName);
                        }));
                        if (entry != null) lock (gate) { var next = CloneConfig(); if (!next.customApps.Contains(entry.path)) next.customApps.Add(entry.path); Commit(next); }
                        Respond(response, 200, new { app = entry }); return;
                    }
                    if (path == "/api/quit")
                    {
                        Respond(response, 200, new { ok = true }); dispatcher.BeginInvoke(new Action(ExitThread)); return;
                    }
                    lock (gate)
                    {
                        if (path == "/api/rules")
                        {
                            GestureRule rule = serializer.Deserialize<GestureRule>(body);
                            if (String.IsNullOrWhiteSpace(rule.id)) rule.id = Guid.NewGuid().ToString("N");
                            else if (!config.rules.Any(r => r.id == rule.id)) throw new ArgumentException("This rule no longer exists. Create a new rule.");
                            ValidateRule(rule, true);
                            var next = CloneConfig(); int index = next.rules.FindIndex(r => r.id == rule.id);
                            if (index >= 0) { rule.runCount = next.rules[index].runCount; rule.lastRun = next.rules[index].lastRun; next.rules[index] = rule; }
                            else { if (next.rules.Count >= 100) throw new ArgumentException("You can have up to 100 rules."); rule.runCount = 0; rule.lastRun = null; next.rules.Add(rule); }
                            Commit(next); AddActivity("Applied: " + rule.name, "success");
                        }
                        else if (path == "/api/delete")
                        {
                            string id = Convert.ToString(values["id"]); var next = CloneConfig();
                            var removed = next.rules.Find(r => r.id == id);
                            if (removed == null) throw new ArgumentException("This rule no longer exists.");
                            next.rules.Remove(removed); Commit(next); AddActivity("Deleted: " + removed.name, "info");
                        }
                        else if (path == "/api/toggle") SetEnabled(Convert.ToBoolean(values["enabled"]));
                        else if (path == "/api/test") LaunchRule(Convert.ToString(values["id"]), true);
                        else if (path == "/api/startup")
                        {
                            bool enabled = Convert.ToBoolean(values["enabled"]);
                            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                            {
                                if (enabled) key.SetValue("MousePilot", "\"" + Application.ExecutablePath + "\" --background");
                                else key.DeleteValue("MousePilot", false);
                            }
                            AddActivity(enabled ? "MousePilot will start when you sign in." : "Start with Windows turned off.", "info");
                        }
                        else { Respond(response, 404, new { error = "Unknown endpoint." }); return; }
                        Respond(response, 200, State());
                    }
                    return;
                }
                if (request.HttpMethod != "GET") { Respond(response, 405, new { error = "Method not supported." }); return; }
                string filename = path == "/" ? "index.html" : path.TrimStart('/');
                if (filename != "index.html" && filename != "style.css" && filename != "app.js") { Respond(response, 404, new { error = "Not found." }); return; }
                string file = Path.Combine(webRoot, filename);
                if (!File.Exists(file)) { Respond(response, 404, new { error = "Not found." }); return; }
                byte[] data = File.ReadAllBytes(file);
                response.ContentType = filename.EndsWith(".css") ? "text/css; charset=utf-8" : filename.EndsWith(".js") ? "text/javascript; charset=utf-8" : "text/html; charset=utf-8";
                response.StatusCode = 200; response.ContentLength64 = data.Length; response.OutputStream.Write(data, 0, data.Length);
            }
            catch (ArgumentException ex) { TryError(response, 400, ex.Message); }
            catch (KeyNotFoundException) { TryError(response, 400, "A required field is missing."); }
            catch (Exception ex) { TryError(response, 500, ex.Message); }
            finally { try { response.Close(); } catch { } }
        }
        private static void Respond(HttpListenerResponse response, int status, object value)
        {
            byte[] data = Encoding.UTF8.GetBytes(Json(value)); response.StatusCode = status; response.ContentType = "application/json; charset=utf-8"; response.ContentLength64 = data.Length; response.OutputStream.Write(data, 0, data.Length);
        }
        private static void TryError(HttpListenerResponse response, int status, string error) { try { Respond(response, status, new { error = error }); } catch { } }
        protected override void ExitThreadCore()
        {
            if (stopping) return;
            stopping = true; hook.Dispose(); inputs.CompleteAdding(); listener.Close(); tray.Visible = false; tray.Dispose(); dispatcher.Dispose();
            try { File.Delete(Program.SessionFile); } catch { }
            base.ExitThreadCore();
        }
    }
}
