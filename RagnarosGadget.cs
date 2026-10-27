using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RagnarosGadget
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "RagnarosGadget_SingleInstance_Mutex", out createdNew);

            if (!createdNew)
            {
                Process current = Process.GetCurrentProcess();
                foreach (Process p in Process.GetProcessesByName(current.ProcessName))
                {
                    if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindowAsync(p.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(p.MainWindowHandle);
                        return;
                    }
                }
                MessageBox.Show("Aviso de REINOS (Ragnaros Gadget) ya se encuentra en ejecución en la bandeja del sistema (junto al reloj).",
                    "Aviso de REINOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainApp());
        }
    }

    internal enum RealmStatus
    {
        Unknown,
        Online,
        Offline,
        Maintenance,
        Locked
    }

    internal class Result
    {
        public RealmStatus Status = RealmStatus.Unknown;
        public string RealmName = "";
        public string Population = "";
        public string Category = "";
        public string Timezone = "";
        public string RealmType = "";
        public bool IsLocked = false;
        public string RawError = null;
    }

    internal class Config
    {
        public string RealmName { get; set; }
        public string Region { get; set; }
        public string Locale { get; set; }
        public int IntervalMinutes { get; set; }
        public bool NotifyOn { get; set; }
        public bool NotifyOff { get; set; }
        public bool PlaySound { get; set; }
        public bool NotifyMaintenanceReminder { get; set; }
        public int MaintenanceReminderMinutes { get; set; }
        public int TypicalMaintenanceMinutes { get; set; }

        public Config()
        {
            RealmName = "Ragnaros";
            Region = "us";
            Locale = "es-es";
            IntervalMinutes = 2; // Comprobación cada 2 minutos
            NotifyOn = true;
            NotifyOff = true;
            PlaySound = true;
            NotifyMaintenanceReminder = true;
            MaintenanceReminderMinutes = 15; // Aviso cada 15 minutos durante mantenimiento
            TypicalMaintenanceMinutes = 60;  // Promedio habitual (60 min) si no hay historial
        }

        public bool IsValid
        {
            get { return !string.IsNullOrEmpty(RealmName); }
        }

        public static string ConfigDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RagnarosGadget");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDir, "config.json"); }
        }

        public static string LastStatusPath
        {
            get { return Path.Combine(ConfigDir, "laststatus.txt"); }
        }

        public static Config Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var ser = new JavaScriptSerializer();
                    Config c = ser.Deserialize<Config>(json);
                    if (c != null)
                    {
                        if (string.IsNullOrEmpty(c.RealmName)) c.RealmName = "Ragnaros";
                        if (string.IsNullOrEmpty(c.Region)) c.Region = "us";
                        if (string.IsNullOrEmpty(c.Locale)) c.Locale = "es-es";
                        if (c.IntervalMinutes < 1) c.IntervalMinutes = 2;
                        if (c.MaintenanceReminderMinutes < 1) c.MaintenanceReminderMinutes = 15;
                        if (c.TypicalMaintenanceMinutes < 10) c.TypicalMaintenanceMinutes = 60;
                        return c;
                    }
                }
            }
            catch
            {
            }
            return new Config();
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(ConfigPath, ser.Serialize(this));
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo guardar la configuración: " + ex.Message, "Aviso de REINOS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    internal class MaintenanceState
    {
        public bool IsActive { get; set; }
        public DateTime StartTimeUtc { get; set; }
        public DateTime LastReminderUtc { get; set; }

        public MaintenanceState()
        {
            IsActive = false;
            StartTimeUtc = DateTime.MinValue;
            LastReminderUtc = DateTime.MinValue;
        }
    }

    internal static class MaintenanceTracker
    {
        public static string HistoryPath
        {
            get { return Path.Combine(Config.ConfigDir, "maintenance_history.json"); }
        }

        public static string StatePath
        {
            get { return Path.Combine(Config.ConfigDir, "maintenance_state.json"); }
        }

        public static int GetEstimatedDurationMinutes(int defaultMinutes = 60)
        {
            try
            {
                if (File.Exists(HistoryPath))
                {
                    string json = File.ReadAllText(HistoryPath);
                    var ser = new JavaScriptSerializer();
                    var list = ser.Deserialize<ArrayList>(json);
                    if (list != null && list.Count > 0)
                    {
                        int sum = 0;
                        int count = 0;
                        foreach (object item in list)
                        {
                            var dict = item as Dictionary<string, object>;
                            if (dict != null && dict.ContainsKey("DurationMinutes"))
                            {
                                sum += Convert.ToInt32(dict["DurationMinutes"]);
                                count++;
                            }
                        }
                        if (count > 0)
                        {
                            int avg = (int)Math.Round((double)sum / count);
                            return Math.Max(25, Math.Min(240, avg));
                        }
                    }
                }
            }
            catch { }
            return defaultMinutes;
        }

        public static MaintenanceState LoadState()
        {
            try
            {
                if (File.Exists(StatePath))
                {
                    string json = File.ReadAllText(StatePath);
                    var ser = new JavaScriptSerializer();
                    var s = ser.Deserialize<MaintenanceState>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new MaintenanceState();
        }

        public static void SaveState(MaintenanceState state)
        {
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(StatePath, ser.Serialize(state));
            }
            catch { }
        }

        public static void RecordMaintenanceFinished(DateTime startUtc, DateTime endUtc)
        {
            try
            {
                int duration = Math.Max(5, (int)Math.Round((endUtc - startUtc).TotalMinutes));
                var ser = new JavaScriptSerializer();
                var history = new ArrayList();
                if (File.Exists(HistoryPath))
                {
                    try
                    {
                        var existing = ser.Deserialize<ArrayList>(File.ReadAllText(HistoryPath));
                        if (existing != null) history = existing;
                    }
                    catch { }
                }

                var newRecord = new Dictionary<string, object>();
                newRecord["Date"] = startUtc.ToString("yyyy-MM-dd HH:mm");
                newRecord["DurationMinutes"] = duration;
                history.Add(newRecord);

                while (history.Count > 20) history.RemoveAt(0);

                File.WriteAllText(HistoryPath, ser.Serialize(history));
            }
            catch { }
        }
    }

    internal static class StatusFetcher
    {
        private const string QueryHash = "b37e546366a58e211e922b8c96cd1ff74249f564a49029cc9737fef3300ff175";

        static StatusFetcher()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | (SecurityProtocolType)768 | SecurityProtocolType.Tls;
            }
            catch
            {
            }
        }

        public static Result GetStatus(string realmName, string region, string locale)
        {
            if (string.IsNullOrEmpty(region)) region = "us";
            if (string.IsNullOrEmpty(locale)) locale = "es-es";

            try
            {
                return QueryGraphQL(realmName, region, locale);
            }
            catch (Exception ex1)
            {
                if (!locale.Equals("en-us", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        return QueryGraphQL(realmName, region, "en-us");
                    }
                    catch
                    {
                    }
                }
                throw ex1;
            }
        }

        private static Result QueryGraphQL(string realmName, string region, string locale)
        {
            string url = string.Format("https://worldofwarcraft.blizzard.com/{0}/graphql", locale.ToLowerInvariant());
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
            req.Headers.Add("accept-language", locale);
            req.Headers.Add("x-static", "false");
            req.Timeout = 12000;
            req.ReadWriteTimeout = 12000;

            string payload = "{\"operationName\":\"GetRealmStatusData\",\"variables\":{\"input\":{\"compoundRegionGameVersionSlug\":\"" +
                             region.ToLowerInvariant() + "\"}},\"extensions\":{\"persistedQuery\":{\"version\":1,\"sha256Hash\":\"" +
                             QueryHash + "\"}}}";

            byte[] bytes = Encoding.UTF8.GetBytes(payload);
            req.ContentLength = bytes.Length;

            using (var stream = req.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string json = reader.ReadToEnd();
                var ser = new JavaScriptSerializer();
                var root = ser.Deserialize<Dictionary<string, object>>(json);

                if (root == null || !root.ContainsKey("data"))
                    throw new Exception("La respuesta de Blizzard no contiene datos válidos.");

                var data = root["data"] as Dictionary<string, object>;
                if (data == null || !data.ContainsKey("Realms"))
                    throw new Exception("No se encontró la lista de reinos en la respuesta de Blizzard.");

                var realmsList = data["Realms"] as ArrayList;
                if (realmsList == null)
                    throw new Exception("El formato de la lista de reinos no es el esperado.");

                string wanted = realmName.Trim();
                string wantedSlug = wanted.ToLowerInvariant().Replace(" ", "-").Replace("'", "");

                foreach (object item in realmsList)
                {
                    var r = item as Dictionary<string, object>;
                    if (r == null) continue;

                    string name = r.ContainsKey("name") && r["name"] != null ? r["name"].ToString().Trim() : "";
                    string slug = r.ContainsKey("slug") && r["slug"] != null ? r["slug"].ToString().Trim() : "";

                    if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase) ||
                        slug.Equals(wantedSlug, StringComparison.OrdinalIgnoreCase))
                    {
                        var res = new Result();
                        res.RealmName = name;

                        bool isOnline = false;
                        if (r.ContainsKey("online") && r["online"] != null)
                        {
                            bool.TryParse(r["online"].ToString(), out isOnline);
                        }

                        bool isLocked = false;
                        if (r.ContainsKey("realmLockStatus") && r["realmLockStatus"] != null)
                        {
                            var lockStatus = r["realmLockStatus"] as Dictionary<string, object>;
                            if (lockStatus != null)
                            {
                                bool lockedNew = lockStatus.ContainsKey("isLockedForNewCharacters") && (bool)lockStatus["isLockedForNewCharacters"];
                                bool lockedPct = lockStatus.ContainsKey("isLockedForPct") && (bool)lockStatus["isLockedForPct"];
                                isLocked = lockedNew || lockedPct;
                            }
                        }
                        res.IsLocked = isLocked;

                        if (isOnline)
                        {
                            res.Status = isLocked ? RealmStatus.Locked : RealmStatus.Online;
                        }
                        else
                        {
                            res.Status = RealmStatus.Offline;
                        }

                        if (r.ContainsKey("population") && r["population"] != null)
                        {
                            var popDict = r["population"] as Dictionary<string, object>;
                            if (popDict != null && popDict.ContainsKey("name") && popDict["name"] != null)
                                res.Population = popDict["name"].ToString();
                        }
                        if (string.IsNullOrEmpty(res.Population)) res.Population = isOnline ? "Media" : "Offline";

                        if (r.ContainsKey("category") && r["category"] != null)
                            res.Category = r["category"].ToString();

                        if (r.ContainsKey("timezone") && r["timezone"] != null)
                            res.Timezone = r["timezone"].ToString();

                        if (r.ContainsKey("type") && r["type"] != null)
                        {
                            var typeDict = r["type"] as Dictionary<string, object>;
                            if (typeDict != null && typeDict.ContainsKey("name") && typeDict["name"] != null)
                                res.RealmType = typeDict["name"].ToString();
                        }

                        return res;
                    }
                }

                throw new Exception(string.Format("No se encontró el reino '{0}' en la región '{1}'. Verifica el nombre en Configuración.", realmName, region.ToUpperInvariant()));
            }
        }
    }

    internal class MainApp : ApplicationContext
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private NotifyIcon _icon;
        private System.Threading.Timer _timer;
        private Config _config;
        private StatusForm _statusForm;
        private SetupForm _setupForm;
        private RealmStatus _lastStatus;
        private int _checking;
        private volatile bool _shuttingDown;
        private DateTime _nextCheckUtc;
        private string _lastError;
        private string _lastCheckTime = "";
        private string _lastPopulation = "";
        private string _lastCategory = "";
        private Icon _iconOnline;
        private Icon _iconOffline;
        private Icon _iconUnknown;
        private ToolStripMenuItem _menuStatus;
        private ToolStripMenuItem _menuAutoStart;
        private MaintenanceState _maintState;

        private volatile bool _manualCheckRequested = false;
        private bool _initialStatusNotified = false;

        public MainApp()
        {
            _config = Config.Load();
            _maintState = MaintenanceTracker.LoadState();

            CreateIcons();
            BuildTray();

            string lastFile = Config.LastStatusPath;
            if (File.Exists(lastFile))
            {
                try
                {
                    string s = File.ReadAllText(lastFile).Trim();
                    _lastStatus = ParseStatus(s);
                }
                catch
                {
                }
            }

            if (!_config.IsValid)
            {
                ShowSetup();
            }
            else
            {
                SetIcon(RealmStatus.Unknown);
                Schedule(0);
            }
        }

        private static void PostUi(Action action)
        {
            SynchronizationContext ctx = SynchronizationContext.Current;
            if (ctx != null)
                ctx.Post(delegate { action(); }, null);
            else
                action();
        }

        private static RealmStatus ParseStatus(string s)
        {
            switch (s)
            {
                case "Online": return RealmStatus.Online;
                case "Maintenance": return RealmStatus.Maintenance;
                case "Locked": return RealmStatus.Locked;
                case "Offline": return RealmStatus.Offline;
                default: return RealmStatus.Unknown;
            }
        }

        private void CreateIcons()
        {
            // VERDE brillante para Online
            _iconOnline = CreateStatusIcon(Color.FromArgb(46, 204, 113), true);
            // ROJO brillante para Offline / Mantenimiento
            _iconOffline = CreateStatusIcon(Color.FromArgb(231, 76, 60), false);
            // Gris para Desconocido / Consultando
            _iconUnknown = CreateStatusIcon(Color.FromArgb(149, 165, 166), false);
        }

        private static Icon CreateStatusIcon(Color circleColor, bool isOnline)
        {
            var bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // Círculo de estado (Verde / Rojo / Gris)
                using (var brush = new SolidBrush(circleColor))
                    g.FillEllipse(brush, 2, 2, 28, 28);

                // Borde blanco sutil
                using (var pen = new Pen(Color.FromArgb(240, 255, 255, 255), 2.2f))
                    g.DrawEllipse(pen, 2, 2, 28, 28);

                // Letra "R" en blanco centrada
                using (var brush = new SolidBrush(Color.White))
                using (var font = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    string text = "R";
                    SizeF sz = g.MeasureString(text, font);
                    g.DrawString(text, font, brush, (32 - sz.Width) / 2f + 0.5f, (32 - sz.Height) / 2f);
                }
            }
            IntPtr h = bmp.GetHicon();
            Icon ic = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            bmp.Dispose();
            return ic;
        }

        private void BuildTray()
        {
            _icon = new NotifyIcon();
            _icon.Visible = true;
            _icon.Text = "Aviso de REINOS - WoW";

            var menu = new ContextMenuStrip();
            _menuStatus = new ToolStripMenuItem("Estado: Consultando...") { Enabled = false };
            menu.Items.Add(_menuStatus);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Ver ventana de estado", null, (s, e) => ShowStatus()));
            menu.Items.Add(new ToolStripMenuItem("Comprobar ahora", null, (s, e) => CheckNow()));
            menu.Items.Add(new ToolStripMenuItem("Probar aviso de servidor ACTIVO (Verde)", null, (s, e) => TestActiveNotification()));
            menu.Items.Add(new ToolStripMenuItem("Probar aviso estimado de mantenimiento (~32 min)", null, (s, e) => TestMaintenancePrediction()));
            menu.Items.Add(new ToolStripMenuItem("Configuración...", null, (s, e) => ShowSetup()));
            menu.Items.Add(new ToolStripMenuItem("Abrir web oficial de Blizzard", null, (s, e) =>
            {
                try { Process.Start("https://worldofwarcraft.blizzard.com/es-es/game/status/us"); }
                catch { }
            }));
            menu.Items.Add(new ToolStripSeparator());
            _menuAutoStart = new ToolStripMenuItem("Iniciar con Windows", null, (s, e) =>
            {
                SetAutoStart(_menuAutoStart.Checked);
            }) { CheckOnClick = true, Checked = IsAutoStart() };
            menu.Items.Add(_menuAutoStart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Salir", null, (s, e) => Exit()));

            _icon.ContextMenuStrip = menu;
            _icon.Click += (s, e) =>
            {
                var me = e as MouseEventArgs;
                if (me == null || me.Button == MouseButtons.Left)
                    ShowStatus();
            };
            _icon.DoubleClick += (s, e) => ShowStatus();
            _icon.BalloonTipClicked += (s, e) => ShowStatus();
        }

        private void Schedule(int delaySeconds)
        {
            if (_shuttingDown) return;
            _nextCheckUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
            int ms = delaySeconds <= 0 ? 300 : Math.Max(1000, delaySeconds * 1000);
            if (_timer == null) _timer = new System.Threading.Timer(TimerTick, null, Timeout.Infinite, Timeout.Infinite);
            try { _timer.Change(ms, Timeout.Infinite); }
            catch { }
        }

        private void TimerTick(object state)
        {
            Check();
        }

        internal void CheckNow()
        {
            _manualCheckRequested = true;
            Schedule(0);
        }

        private void Check()
        {
            if (Interlocked.Exchange(ref _checking, 1) != 0) return;
            try
            {
                Config cfg = _config;
                if (cfg == null || !cfg.IsValid) return;

                Result r = StatusFetcher.GetStatus(cfg.RealmName, cfg.Region, cfg.Locale);
                _lastError = null;
                _lastCheckTime = DateTime.Now.ToString("HH:mm:ss");
                _lastPopulation = r.Population;
                _lastCategory = r.Category;
                OnStatus(r.Status, cfg, r);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _lastCheckTime = DateTime.Now.ToString("HH:mm:ss");
                PostUi(delegate
                {
                    SetIcon(RealmStatus.Unknown);
                    RefreshMenu();
                    RefreshStatusWindow();
                });
            }
            finally
            {
                Interlocked.Exchange(ref _checking, 0);
                if (_lastError != null) Schedule(30);
                else Schedule(cfgInterval());
            }
        }

        private int cfgInterval()
        {
            Config c = _config;
            if (c == null) return 120; // 2 minutos por defecto
            return Math.Max(1, c.IntervalMinutes) * 60;
        }

        private void OnStatus(RealmStatus status, Config cfg, Result result)
        {
            bool notify = false;
            string title = "";
            string msg = "";

            bool isOfflineOrMaint = (status == RealmStatus.Offline || status == RealmStatus.Maintenance || status == RealmStatus.Locked);

            // Gestión del cronómetro y estado de mantenimiento
            if (isOfflineOrMaint)
            {
                if (!_maintState.IsActive)
                {
                    _maintState.IsActive = true;
                    _maintState.StartTimeUtc = DateTime.UtcNow;
                    _maintState.LastReminderUtc = DateTime.UtcNow; // Registrar inicio
                    MaintenanceTracker.SaveState(_maintState);
                }
                else
                {
                    // Comprobar si han pasado 15 minutos desde el último aviso de mantenimiento
                    int reminderMins = Math.Max(1, cfg.MaintenanceReminderMinutes);
                    double elapsedSinceLastReminder = (DateTime.UtcNow - _maintState.LastReminderUtc).TotalMinutes;

                    if (cfg.NotifyMaintenanceReminder && elapsedSinceLastReminder >= reminderMins)
                    {
                        // Disparar aviso periódico cada 15 minutos
                        int typicalDuration = MaintenanceTracker.GetEstimatedDurationMinutes(cfg.TypicalMaintenanceMinutes);
                        double totalElapsed = (DateTime.UtcNow - _maintState.StartTimeUtc).TotalMinutes;
                        int remaining = (int)Math.Round(typicalDuration - totalElapsed);

                        string predMsg;
                        if (remaining > 0)
                        {
                            predMsg = string.Format("Normalmente este mantenimiento termina en ~{0} min.", remaining);
                        }
                        else
                        {
                            predMsg = string.Format("El mantenimiento lleva {0} min (se ha extendido más de lo habitual). Comprobando cada 2 min...", (int)totalElapsed);
                        }

                        _maintState.LastReminderUtc = DateTime.UtcNow;
                        MaintenanceTracker.SaveState(_maintState);

                        // Mostrar notificación de mantenimiento
                        PostUi(delegate
                        {
                            ShowNotification(string.Format("{0} en mantenimiento", cfg.RealmName), predMsg, RealmStatus.Offline);
                            if (cfg.PlaySound)
                            {
                                try { SystemSounds.Asterisk.Play(); } catch { }
                            }
                        });
                    }
                }
            }
            else if (status == RealmStatus.Online)
            {
                if (_maintState.IsActive)
                {
                    // El mantenimiento ha finalizado
                    _maintState.IsActive = false;
                    MaintenanceTracker.RecordMaintenanceFinished(_maintState.StartTimeUtc, DateTime.UtcNow);
                    MaintenanceTracker.SaveState(_maintState);
                }
            }

            // NOTIFICACIONES:
            // 1. Al iniciar la aplicación (primer chequeo completado):
            if (!_initialStatusNotified)
            {
                _initialStatusNotified = true;
                notify = true;
                if (status == RealmStatus.Online)
                {
                    title = string.Format("¡{0} está ACTIVO! (VERDE)", cfg.RealmName);
                    msg = string.Format("El servidor está activo y listo para jugar. Población: {0}.", result.Population);
                }
                else
                {
                    int typical = MaintenanceTracker.GetEstimatedDurationMinutes(cfg.TypicalMaintenanceMinutes);
                    double elapsed = _maintState.IsActive ? (DateTime.UtcNow - _maintState.StartTimeUtc).TotalMinutes : 0;
                    int remaining = Math.Max(5, (int)Math.Round(typical - elapsed));
                    title = string.Format("{0} está OFFLINE (ROJO)", cfg.RealmName);
                    msg = string.Format("El servidor está en mantenimiento. Normalmente termina en ~{0} min.", remaining);
                }
            }
            // 2. Si el usuario solicitó comprobar manualmente:
            else if (_manualCheckRequested)
            {
                _manualCheckRequested = false;
                notify = true;
                if (status == RealmStatus.Online)
                {
                    title = string.Format("¡{0} está ACTIVO! (VERDE)", cfg.RealmName);
                    msg = string.Format("Comprobación manual: El servidor está disponible para jugar. ({0})", result.Population);
                }
                else
                {
                    title = string.Format("{0} está OFFLINE (ROJO)", cfg.RealmName);
                    msg = "Comprobación manual: El servidor continúa inactivo o en mantenimiento.";
                }
            }
            // 3. Cuando cambia el estado (ej. de Offline a Online):
            else if (_lastStatus != status)
            {
                if (status == RealmStatus.Online && cfg.NotifyOn)
                {
                    notify = true;
                    title = string.Format("¡{0} YA ESTÁ EN LÍNEA! (VERDE)", cfg.RealmName);
                    msg = "¡El servidor ha vuelto a subir! Ya puedes entrar al juego.";
                }
                else if (isOfflineOrMaint && cfg.NotifyOff)
                {
                    int typical = MaintenanceTracker.GetEstimatedDurationMinutes(cfg.TypicalMaintenanceMinutes);
                    int remaining = typical;
                    notify = true;
                    title = string.Format("{0} OFFLINE (ROJO)", cfg.RealmName);
                    msg = string.Format("El servidor no está disponible. Normalmente este mantenimiento termina en ~{0} min.", remaining);
                }
            }

            _lastStatus = status;
            try { File.WriteAllText(Config.LastStatusPath, status.ToString()); }
            catch { }

            string tooltipStatus = status == RealmStatus.Online ? "EN LÍNEA (Verde)" : "OFFLINE (Rojo)";
            string tooltip = string.Format("{0}: {1}", cfg.RealmName, tooltipStatus);
            if (_lastCheckTime.Length > 0) tooltip += " [" + _lastCheckTime + "]";
            if (tooltip.Length >= 64) tooltip = tooltip.Substring(0, 63);

            RealmStatus st = status;
            bool doNotify = notify;
            PostUi(delegate
            {
                try { _icon.Text = tooltip; } catch { }
                SetIcon(st);
                RefreshMenu();
                RefreshStatusWindow();
                if (doNotify)
                {
                    ShowNotification(title, msg, st);
                    if (cfg.PlaySound)
                    {
                        try
                        {
                            if (st == RealmStatus.Online)
                                SystemSounds.Asterisk.Play();
                            else
                                SystemSounds.Exclamation.Play();
                        }
                        catch { }
                    }
                }
            });
        }

        private static string StatusMessage(RealmStatus status)
        {
            switch (status)
            {
                case RealmStatus.Online: return "EN LÍNEA (Verde)";
                case RealmStatus.Offline: return "OFFLINE (Rojo)";
                case RealmStatus.Maintenance: return "En MANTENIMIENTO (Rojo)";
                case RealmStatus.Locked: return "BLOQUEADO (Rojo)";
                default: return "Sin datos / Comprobando...";
            }
        }

        private void SetIcon(RealmStatus status)
        {
            Icon ic;
            switch (status)
            {
                case RealmStatus.Online:
                    ic = _iconOnline;
                    break;
                case RealmStatus.Offline:
                case RealmStatus.Maintenance:
                case RealmStatus.Locked:
                    ic = _iconOffline;
                    break;
                default:
                    ic = _iconUnknown;
                    break;
            }
            _icon.Icon = ic;
        }

        // Notificación 100% nativa y segura para Windows
        // Renderizada directamente por el Shell de Windows (explorer.exe).
        // No crea ventanas TopMost transparentes, NO deja barras invisibles y NO interfiere con DirectX ni cuelga WoW.
        private void ShowNotification(string title, string message, RealmStatus status)
        {
            try
            {
                ToolTipIcon iconType = ToolTipIcon.Info;
                if (status == RealmStatus.Offline || status == RealmStatus.Maintenance || status == RealmStatus.Locked)
                    iconType = ToolTipIcon.Warning;

                _icon.ShowBalloonTip(7000, title, message, iconType);
            }
            catch { }
        }

        internal void TestActiveNotification()
        {
            string title = string.Format("¡{0} está ACTIVO! (VERDE)", _config.RealmName);
            string msg = "El servidor de WoW Retail está activo y disponible para jugar.";
            ShowNotification(title, msg, RealmStatus.Online);
            try { SystemSounds.Asterisk.Play(); } catch { }
        }

        internal void TestMaintenancePrediction()
        {
            int remaining = 32;
            string title = string.Format("{0} en mantenimiento", _config.RealmName);
            string msg = string.Format("Notificación: “Normalmente este mantenimiento termina en ~{0} min.”", remaining);
            ShowNotification(title, msg, RealmStatus.Offline);
            try { SystemSounds.Asterisk.Play(); } catch { }
        }

        private void RefreshMenu()
        {
            if (_menuStatus != null)
            {
                string text = _config.RealmName + ": " + StatusMessage(_lastStatus);
                if (_lastError != null) text += " [Error de red]";
                _menuStatus.Text = text;
            }
        }

        internal void ShowSetup()
        {
            PostUi(delegate
            {
                if (_setupForm == null || _setupForm.IsDisposed)
                {
                    _setupForm = new SetupForm(_config, OnConfigSaved);
                    _setupForm.Show();
                }
                else
                {
                    _setupForm.Activate();
                }
            });
        }

        private void OnConfigSaved(Config newConfig)
        {
            _config = newConfig;
            _config.Save();
            _lastStatus = RealmStatus.Unknown;
            _initialStatusNotified = false;
            try { if (File.Exists(Config.LastStatusPath)) File.Delete(Config.LastStatusPath); }
            catch { }
            _lastError = null;
            SetIcon(RealmStatus.Unknown);
            Schedule(0);
        }

        internal void ShowStatus()
        {
            PostUi(delegate
            {
                if (_statusForm == null || _statusForm.IsDisposed)
                    _statusForm = new StatusForm();
                _statusForm.Attach(this, _config);
                RefreshStatusWindow();
                if (!_statusForm.Visible) _statusForm.Show();
                _statusForm.Activate();
                _statusForm.BringToFront();
            });
        }

        internal void RefreshStatusWindow()
        {
            if (_statusForm == null || _statusForm.IsDisposed) return;
            _statusForm.UpdateStatus(_config, _lastStatus, _lastCheckTime, _nextCheckUtc, _lastError, _lastPopulation, _lastCategory, _maintState);
        }

        private static bool IsAutoStart()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && key.GetValue("RagnarosGadget") != null;
            }
            catch { return false; }
        }

        private static void SetAutoStart(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (enabled) key.SetValue("RagnarosGadget", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("RagnarosGadget", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo cambiar el inicio automático: " + ex.Message, "Aviso de REINOS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Exit()
        {
            _shuttingDown = true;
            try { if (_timer != null) _timer.Dispose(); }
            catch { }
            _icon.Visible = false;
            _icon.Dispose();
            Application.Exit();
        }
    }

    internal class SetupForm : Form
    {
        private readonly Config _config;
        private readonly Action<Config> _onSaved;
        private TextBox _txtRealm;
        private ComboBox _cmbRegion;
        private TextBox _txtLocale;
        private NumericUpDown _numInterval;
        private NumericUpDown _numReminderInterval;
        private NumericUpDown _numTypicalDuration;
        private CheckBox _chkNotifyOn;
        private CheckBox _chkNotifyOff;
        private CheckBox _chkReminder;
        private CheckBox _chkSound;

        public SetupForm(Config config, Action<Config> onSaved)
        {
            _config = config;
            _onSaved = onSaved;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Configuración - Aviso de REINOS (WoW)";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Width = 490;
            Height = 520;
            BackColor = Color.FromArgb(248, 249, 250);
            Font = new Font("Segoe UI", 9f);

            var pnlMain = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20) };

            var lblTitle = new Label
            {
                Text = "Configuración del Monitor de Reinos",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41),
                Dock = DockStyle.Top,
                Height = 28
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 230,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(0, 8, 0, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            Func<string, Control> makeLabel = delegate(string text)
            {
                return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 8, 0), Font = new Font("Segoe UI", 9.2f) };
            };

            _txtRealm = new TextBox { Dock = DockStyle.Fill, Text = _config.RealmName, Font = new Font("Segoe UI", 9.2f) };
            
            _cmbRegion = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 9.2f) };
            _cmbRegion.Items.AddRange(new object[] { "us (América y Oceanía)", "eu (Europa)", "kr (Corea)", "tw (Taiwán)" });
            _cmbRegion.SelectedIndex = 0;
            if (_config.Region == "eu") _cmbRegion.SelectedIndex = 1;
            else if (_config.Region == "kr") _cmbRegion.SelectedIndex = 2;
            else if (_config.Region == "tw") _cmbRegion.SelectedIndex = 3;

            _txtLocale = new TextBox { Dock = DockStyle.Fill, Text = _config.Locale, Font = new Font("Segoe UI", 9.2f) };
            
            _numInterval = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = 1,
                Maximum = 60,
                Value = Math.Max(1, Math.Min(60, _config.IntervalMinutes)),
                Font = new Font("Segoe UI", 9.2f)
            };

            _numReminderInterval = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = 1,
                Maximum = 60,
                Value = Math.Max(1, Math.Min(60, _config.MaintenanceReminderMinutes)),
                Font = new Font("Segoe UI", 9.2f)
            };

            _numTypicalDuration = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = 15,
                Maximum = 300,
                Value = Math.Max(15, Math.Min(300, _config.TypicalMaintenanceMinutes)),
                Font = new Font("Segoe UI", 9.2f)
            };

            layout.Controls.Add(makeLabel("Nombre del reino:"), 0, 0);
            layout.Controls.Add(_txtRealm, 1, 0);

            layout.Controls.Add(makeLabel("Región:"), 0, 1);
            layout.Controls.Add(_cmbRegion, 1, 1);

            layout.Controls.Add(makeLabel("Idioma de consulta:"), 0, 2);
            layout.Controls.Add(_txtLocale, 1, 2);

            layout.Controls.Add(makeLabel("Comprobar estado cada (min):"), 0, 3);
            layout.Controls.Add(_numInterval, 1, 3);

            layout.Controls.Add(makeLabel("Avisar mantenimiento cada (min):"), 0, 4);
            layout.Controls.Add(_numReminderInterval, 1, 4);

            layout.Controls.Add(makeLabel("Duración habitual mantenimiento (min):"), 0, 5);
            layout.Controls.Add(_numTypicalDuration, 1, 5);

            var pnlChecks = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 110,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(0, 6, 0, 0)
            };

            _chkNotifyOn = new CheckBox
            {
                Text = "Notificar cuando el servidor esté ACTIVO / EN LÍNEA (Verde)",
                Checked = _config.NotifyOn,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2)
            };
            _chkNotifyOff = new CheckBox
            {
                Text = "Notificar cuando entre en mantenimiento / OFFLINE (Rojo)",
                Checked = _config.NotifyOff,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2)
            };
            _chkReminder = new CheckBox
            {
                Text = "Avisar cada 15 min del tiempo restante estimado de mantenimiento",
                Checked = _config.NotifyMaintenanceReminder,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2)
            };
            _chkSound = new CheckBox
            {
                Text = "Reproducir sonido de alerta con las notificaciones",
                Checked = _config.PlaySound,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2)
            };

            pnlChecks.Controls.Add(_chkNotifyOn);
            pnlChecks.Controls.Add(_chkNotifyOff);
            pnlChecks.Controls.Add(_chkReminder);
            pnlChecks.Controls.Add(_chkSound);

            var btnSave = new Button
            {
                Text = "Probar y Guardar",
                BackColor = Color.FromArgb(46, 204, 113),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Height = 34,
                Width = 150
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += delegate { TrySave(); };

            var btnCancel = new Button
            {
                Text = "Cancelar",
                BackColor = Color.FromArgb(220, 224, 230),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f),
                Height = 34,
                Width = 100
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += delegate { Close(); };

            var pnlButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 45,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(0, 6, 0, 0)
            };
            pnlButtons.Controls.Add(btnSave);
            pnlButtons.Controls.Add(btnCancel);

            pnlMain.Controls.Add(pnlButtons);
            pnlMain.Controls.Add(pnlChecks);
            pnlMain.Controls.Add(layout);
            pnlMain.Controls.Add(lblTitle);

            Controls.Add(pnlMain);
            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private Config ReadFields()
        {
            string realm = _txtRealm.Text.Trim();
            if (string.IsNullOrEmpty(realm))
            {
                MessageBox.Show("Debes escribir el nombre del reino de WoW (por ejemplo: Ragnaros).", "Aviso de REINOS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }

            string region = "us";
            if (_cmbRegion.SelectedIndex == 1) region = "eu";
            else if (_cmbRegion.SelectedIndex == 2) region = "kr";
            else if (_cmbRegion.SelectedIndex == 3) region = "tw";

            var cfg = new Config
            {
                RealmName = realm,
                Region = region,
                Locale = string.IsNullOrEmpty(_txtLocale.Text.Trim()) ? "es-es" : _txtLocale.Text.Trim(),
                IntervalMinutes = (int)_numInterval.Value,
                MaintenanceReminderMinutes = (int)_numReminderInterval.Value,
                TypicalMaintenanceMinutes = (int)_numTypicalDuration.Value,
                NotifyOn = _chkNotifyOn.Checked,
                NotifyOff = _chkNotifyOff.Checked,
                NotifyMaintenanceReminder = _chkReminder.Checked,
                PlaySound = _chkSound.Checked
            };
            return cfg;
        }

        private void TrySave()
        {
            Config cfg = ReadFields();
            if (cfg == null) return;

            Cursor = Cursors.WaitCursor;
            try
            {
                Result r = StatusFetcher.GetStatus(cfg.RealmName, cfg.Region, cfg.Locale);
                string estadoTexto = r.Status == RealmStatus.Online ? "EN LÍNEA (Verde)" : "OFFLINE (Rojo)";
                MessageBox.Show(string.Format("¡Conexión exitosa con la API de Blizzard!\n\nReino: {0}\nEstado: {1}\nPoblación: {2}\nRegión: {3}\n\nConfiguración guardada correctamente.",
                    r.RealmName, estadoTexto, r.Population, cfg.Region.ToUpperInvariant()),
                    "Aviso de REINOS", MessageBoxButtons.OK, MessageBoxIcon.Information);

                _onSaved(cfg);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al conectar o consultar el reino:\n" + ex.Message, "Aviso de REINOS", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }
    }

    internal class StatusForm : Form
    {
        private MainApp _app;
        private Config _config;
        private Panel _pnlBanner;
        private Label _lblStatusTitle;
        private Label _lblStatusSub;
        private Label _lblLast;
        private Label _lblNext;
        private Label _lblMaintTime;
        private Label _lblDetails;
        private Label _lblError;
        private System.Windows.Forms.Timer _countdownTimer;
        private DateTime _targetNextUtc;
        private MaintenanceState _maintState;

        public StatusForm()
        {
            BuildUi();
        }

        public void Attach(MainApp app, Config config)
        {
            _app = app;
            _config = config;
            Text = "Estado de Reino - " + (config != null ? config.RealmName : "WoW");
        }

        private void BuildUi()
        {
            Text = "Aviso de REINOS - WoW Retail";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.Manual;
            Width = 430;
            Height = 360;
            TopMost = false; // Sin TopMost para no interferir jamás con DirectX ni WoW
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Segoe UI", 9f);

            var mainContainer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16) };

            // Banner superior de estado (Verde o Rojo)
            _pnlBanner = new Panel
            {
                Dock = DockStyle.Top,
                Height = 65,
                BackColor = Color.FromArgb(149, 165, 166),
                Padding = new Padding(12)
            };

            _lblStatusTitle = new Label
            {
                Text = "COMPROBANDO ESTADO...",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.White,
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _lblStatusSub = new Label
            {
                Text = "Consultando Blizzard API...",
                Font = new Font("Segoe UI", 9f),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Bottom,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _pnlBanner.Controls.Add(_lblStatusSub);
            _pnlBanner.Controls.Add(_lblStatusTitle);

            // Contenedor de información
            var pnlInfo = new Panel
            {
                Dock = DockStyle.Top,
                Height = 150,
                Padding = new Padding(4, 10, 4, 0)
            };

            _lblLast = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(70, 80, 95) };
            _lblNext = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(70, 80, 95), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _lblMaintTime = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 24, ForeColor = Color.FromArgb(192, 57, 43), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _lblDetails = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 22, ForeColor = Color.FromArgb(70, 80, 95) };
            _lblError = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 36, ForeColor = Color.FromArgb(220, 53, 69), AutoEllipsis = true, Visible = false };

            pnlInfo.Controls.Add(_lblError);
            pnlInfo.Controls.Add(_lblDetails);
            pnlInfo.Controls.Add(_lblMaintTime);
            pnlInfo.Controls.Add(_lblNext);
            pnlInfo.Controls.Add(_lblLast);

            // Botones de acción inferiores
            var btnNow = new Button
            {
                Text = "Comprobar ahora",
                AutoSize = true,
                BackColor = Color.FromArgb(41, 128, 185),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Height = 32
            };
            btnNow.FlatAppearance.BorderSize = 0;
            btnNow.Click += delegate { if (_app != null) _app.CheckNow(); };

            var btnTestMaint = new Button
            {
                Text = "Aviso (~32m)",
                AutoSize = true,
                BackColor = Color.FromArgb(230, 235, 240),
                FlatStyle = FlatStyle.Flat,
                Height = 32
            };
            btnTestMaint.FlatAppearance.BorderSize = 0;
            btnTestMaint.Click += delegate { if (_app != null) _app.TestMaintenancePrediction(); };

            var btnConfig = new Button
            {
                Text = "Configurar",
                AutoSize = true,
                BackColor = Color.FromArgb(230, 235, 240),
                FlatStyle = FlatStyle.Flat,
                Height = 32
            };
            btnConfig.FlatAppearance.BorderSize = 0;
            btnConfig.Click += delegate { if (_app != null) _app.ShowSetup(); };

            var btnPage = new Button
            {
                Text = "Web WoW",
                AutoSize = true,
                BackColor = Color.FromArgb(230, 235, 240),
                FlatStyle = FlatStyle.Flat,
                Height = 32
            };
            btnPage.FlatAppearance.BorderSize = 0;
            btnPage.Click += delegate
            {
                try { Process.Start("https://worldofwarcraft.blizzard.com/es-es/game/status/us"); }
                catch { }
            };

            var pnlButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0, 4, 0, 0)
            };
            pnlButtons.Controls.Add(btnNow);
            pnlButtons.Controls.Add(btnTestMaint);
            pnlButtons.Controls.Add(btnConfig);
            pnlButtons.Controls.Add(btnPage);

            mainContainer.Controls.Add(pnlButtons);
            mainContainer.Controls.Add(pnlInfo);
            mainContainer.Controls.Add(_pnlBanner);

            Controls.Add(mainContainer);

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 16);

            // Timer de conteo regresivo: solo corre cuando la ventana está visible
            _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _countdownTimer.Tick += CountdownTick;

            VisibleChanged += (s, e) =>
            {
                if (Visible)
                {
                    CountdownTick(null, null);
                    _countdownTimer.Start();
                }
                else
                {
                    _countdownTimer.Stop();
                }
            };

            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        private void CountdownTick(object sender, EventArgs e)
        {
            if (_targetNextUtc != DateTime.MinValue)
            {
                TimeSpan left = _targetNextUtc - DateTime.UtcNow;
                if (left.TotalSeconds <= 0)
                {
                    _lblNext.Text = "Próxima comprobación: en curso...";
                }
                else
                {
                    int min = (int)left.TotalMinutes;
                    int sec = left.Seconds;
                    _lblNext.Text = string.Format("Próxima comprobación: en {0}m {1:D2}s (cada {2} min)",
                        min, sec, _config != null ? _config.IntervalMinutes : 2);
                }
            }

            // Actualizar cronología en vivo si está en mantenimiento
            if (_maintState != null && _maintState.IsActive && _maintState.StartTimeUtc != DateTime.MinValue)
            {
                double elapsed = (DateTime.UtcNow - _maintState.StartTimeUtc).TotalMinutes;
                int typical = MaintenanceTracker.GetEstimatedDurationMinutes(_config != null ? _config.TypicalMaintenanceMinutes : 60);
                int remaining = (int)Math.Round(typical - elapsed);

                if (remaining > 0)
                {
                    _lblMaintTime.Text = string.Format("⏳ En mantenimiento: lleva {0} min | Termina en ~{1} min",
                        (int)elapsed, remaining);
                }
                else
                {
                    _lblMaintTime.Text = string.Format("⏳ En mantenimiento: lleva {0} min (se ha extendido)",
                        (int)elapsed);
                }
                _lblMaintTime.Visible = true;
            }
            else
            {
                int avg = MaintenanceTracker.GetEstimatedDurationMinutes(_config != null ? _config.TypicalMaintenanceMinutes : 60);
                _lblMaintTime.Text = string.Format("⏳ Historial: mantenimiento habitual suele durar ~{0} min.", avg);
                _lblMaintTime.Visible = true;
            }
        }

        public void UpdateStatus(Config cfg, RealmStatus status, string lastCheck, DateTime nextCheckUtc, string error, string population, string category, MaintenanceState maintState)
        {
            if (_lblStatusTitle == null || _lblStatusTitle.IsDisposed) return;

            _targetNextUtc = nextCheckUtc;
            _maintState = maintState;
            string realmName = cfg != null ? cfg.RealmName : "Ragnaros";

            if (status == RealmStatus.Online)
            {
                _pnlBanner.BackColor = Color.FromArgb(46, 204, 113); // VERDE
                _lblStatusTitle.Text = "● " + realmName.ToUpperInvariant() + ": EN LÍNEA";
                _lblStatusSub.Text = "Servidor disponible para jugar.";
            }
            else if (status == RealmStatus.Offline || status == RealmStatus.Maintenance || status == RealmStatus.Locked)
            {
                _pnlBanner.BackColor = Color.FromArgb(231, 76, 60); // ROJO
                _lblStatusTitle.Text = "● " + realmName.ToUpperInvariant() + ": OFFLINE";
                _lblStatusSub.Text = "Servidor inactivo (mantenimiento de los martes).";
            }
            else
            {
                _pnlBanner.BackColor = Color.FromArgb(149, 165, 166); // Gris
                _lblStatusTitle.Text = "● " + realmName.ToUpperInvariant() + ": CONSULTANDO...";
                _lblStatusSub.Text = "Esperando respuesta de Blizzard.";
            }

            _lblLast.Text = "Última comprobación: " + (lastCheck.Length > 0 ? lastCheck : "Pendiente");

            string pop = population.Length > 0 ? population : "Desconocida";
            string cat = category.Length > 0 ? " (" + category + ")" : "";
            _lblDetails.Text = string.Format("Población: {0}{1} | Región: {2}", pop, cat, cfg != null ? cfg.Region.ToUpperInvariant() : "US");

            if (error != null)
            {
                _lblError.Text = "Aviso: " + error;
                _lblError.Visible = true;
            }
            else
            {
                _lblError.Visible = false;
            }

            if (Visible) CountdownTick(null, null);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { if (_countdownTimer != null) _countdownTimer.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}
