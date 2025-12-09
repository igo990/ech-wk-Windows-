using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ECHWorkersClient
{
    // ---------------------- 1. 入口点 ----------------------
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // 高 DPI 支持
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    // ---------------------- 2. 数据模型 ----------------------
    public class ServerConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "新服务器";
        public string Server { get; set; } = "";
        public string Listen { get; set; } = "";
        public string Token { get; set; } = "";
        public string Ip { get; set; } = "";
        public string Dns { get; set; } = "";
        public string Ech { get; set; } = "";
    }

    public class AppConfig
    {
        public List<ServerConfig> Servers { get; set; } = new List<ServerConfig>();
        public string CurrentServerId { get; set; }
    }

    // ---------------------- 3. 配置管理器 ----------------------
    public class ConfigManager
    {
        private readonly string configPath;
        public AppConfig Config { get; private set; }

        public ConfigManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = Path.Combine(appData, "ECHWorkersClient");
            Directory.CreateDirectory(configDir);
            configPath = Path.Combine(configDir, "config.json");
            LoadConfig();
        }

        public void LoadConfig()
        {
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    Config = JsonSerializer.Deserialize<AppConfig>(json);
                }
                catch { Config = new AppConfig(); }
            }
            else
            {
                Config = new AppConfig();
            }

            if (Config.Servers == null || Config.Servers.Count == 0)
            {
                AddDefaultServer();
            }
        }

        public void SaveConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}");
            }
        }

        private void AddDefaultServer()
        {
            Config.Servers.Add(new ServerConfig
            {
                Name = "默认服务器",
                Server = "example.com:443",
                Listen = "127.0.0.1:30000",
                Ip = "saas.sin.fan",
                Dns = "dns.alidns.com/dns-query",
                Ech = "cloudflare-ech.com"
            });
            Config.CurrentServerId = Config.Servers[0].Id;
        }

        public ServerConfig GetCurrentServer()
        {
            return Config.Servers.FirstOrDefault(s => s.Id == Config.CurrentServerId) ?? Config.Servers.FirstOrDefault();
        }
    }

    // ---------------------- 4. 系统代理设置工具 (P/Invoke) ----------------------
    public static class SystemProxyHelper
    {
        [DllImport("wininet.dll")]
        public static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
        public const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        public const int INTERNET_OPTION_REFRESH = 37;

        public static void SetProxy(bool enable, string address)
        {
            string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(keyPath, true))
            {
                if (key == null) return;

                if (enable)
                {
                    // 设置为 socks=127.0.0.1:port 格式
                    // 注意：Windows 全局代理对 SOCKS5 支持有限，通常这样写兼容性最好
                    key.SetValue("ProxyServer", $"socks={address}");
                    key.SetValue("ProxyEnable", 1);
                    key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>");
                }
                else
                {
                    key.SetValue("ProxyEnable", 0);
                }
            }

            // 刷新系统设置
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
    }

    // ---------------------- 5. 主窗口 UI ----------------------
    public class MainForm : Form
    {
        private ConfigManager configManager;
        private Process workerProcess;
        private bool isRunning = false;
        private bool isSystemProxyEnabled = false;

        // UI Controls
        private ComboBox cbServers;
        private TextBox txtServer, txtListen, txtToken, txtIp, txtDns, txtEch;
        private Button btnStart, btnStop, btnProxy, btnSave, btnAdd, btnDelete, btnRename, btnClearLog;
        private CheckBox chkAutoStart;
        private RichTextBox rtbLog;

        public MainForm()
        {
            this.Text = "ECH Workers 客户端 (C#版)";
            this.Size = new Size(900, 750);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Icon = SystemIcons.Application;

            configManager = new ConfigManager();

            InitializeUi();
            LoadConfigToUi();

            // 检查自动启动参数
            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("-autostart"))
            {
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = true; // 可选：是否隐藏
                StartProxy();
            }
        }

        private void InitializeUi()
        {
            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowStyles = { new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.AutoSize), new RowStyle(SizeType.Percent, 100) } };
            
            // 1. 服务器管理组
            var grpServer = new GroupBox { Text = "服务器管理", Dock = DockStyle.Top, Height = 60 };
            var pnlServer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(5) };
            pnlServer.Controls.Add(new Label { Text = "选择服务器:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 8, 0, 0) });
            
            cbServers = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cbServers.SelectedIndexChanged += CbServers_SelectedIndexChanged;
            pnlServer.Controls.Add(cbServers);

            btnAdd = new Button { Text = "新增", AutoSize = true }; btnAdd.Click += (s, e) => AddServer();
            btnSave = new Button { Text = "保存", AutoSize = true }; btnSave.Click += (s, e) => SaveServer();
            btnRename = new Button { Text = "重命名", AutoSize = true }; btnRename.Click += (s, e) => RenameServer();
            btnDelete = new Button { Text = "删除", AutoSize = true }; btnDelete.Click += (s, e) => DeleteServer();
            
            pnlServer.Controls.AddRange(new Control[] { btnAdd, btnSave, btnRename, btnDelete });
            grpServer.Controls.Add(pnlServer);

            // 2. 核心配置组
            var grpCore = new GroupBox { Text = "核心配置", Dock = DockStyle.Top, Height = 80 };
            var pnlCore = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            pnlCore.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            pnlCore.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            
            txtServer = new TextBox { Dock = DockStyle.Fill };
            txtListen = new TextBox { Dock = DockStyle.Fill };
            
            pnlCore.Controls.Add(new Label { Text = "服务地址:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
            pnlCore.Controls.Add(txtServer, 1, 0);
            pnlCore.Controls.Add(new Label { Text = "监听地址:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 1);
            pnlCore.Controls.Add(txtListen, 1, 1);
            grpCore.Controls.Add(pnlCore);

            // 3. 高级配置组
            var grpAdv = new GroupBox { Text = "高级选项", Dock = DockStyle.Top, Height = 140 };
            var pnlAdv = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
            pnlAdv.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            pnlAdv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            txtToken = new TextBox { Dock = DockStyle.Fill };
            txtIp = new TextBox { Dock = DockStyle.Fill };
            txtDns = new TextBox { Dock = DockStyle.Fill };
            txtEch = new TextBox { Dock = DockStyle.Fill };

            pnlAdv.Controls.Add(new Label { Text = "身份令牌:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
            pnlAdv.Controls.Add(txtToken, 1, 0);
            pnlAdv.Controls.Add(new Label { Text = "优选IP:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 1);
            pnlAdv.Controls.Add(txtIp, 1, 1);
            pnlAdv.Controls.Add(new Label { Text = "DoH服务器:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 2);
            pnlAdv.Controls.Add(txtDns, 1, 2);
            pnlAdv.Controls.Add(new Label { Text = "ECH域名:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 3);
            pnlAdv.Controls.Add(txtEch, 1, 3);
            grpAdv.Controls.Add(pnlAdv);

            // 4. 控制区域
            var grpCtrl = new GroupBox { Text = "控制", Dock = DockStyle.Top, Height = 60 };
            var pnlCtrl = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            
            btnStart = new Button { Text = "启动代理", Width = 100 }; btnStart.Click += (s, e) => StartProxy();
            btnStop = new Button { Text = "停止", Width = 80, Enabled = false }; btnStop.Click += (s, e) => StopProxy();
            btnProxy = new Button { Text = "设置系统代理", Width = 120, Enabled = false }; btnProxy.Click += (s, e) => ToggleSystemProxy();
            chkAutoStart = new CheckBox { Text = "开机启动", AutoSize = true, Margin = new Padding(10, 8, 0, 0) }; chkAutoStart.CheckedChanged += ChkAutoStart_CheckedChanged;
            btnClearLog = new Button { Text = "清空日志", Width = 80 }; btnClearLog.Click += (s, e) => rtbLog.Clear();

            CheckAutoStartStatus();

            pnlCtrl.Controls.AddRange(new Control[] { btnStart, btnStop, btnProxy, chkAutoStart, btnClearLog });
            grpCtrl.Controls.Add(pnlCtrl);

            // 5. 日志区域
            var grpLog = new GroupBox { Text = "运行日志", Dock = DockStyle.Fill };
            rtbLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.Black, ForeColor = Color.Lime, Font = new Font("Consolas", 9) };
            grpLog.Controls.Add(rtbLog);

            // 添加到主布局
            mainLayout.Controls.Add(grpServer);
            mainLayout.Controls.Add(grpCore);
            mainLayout.Controls.Add(grpAdv);
            mainLayout.Controls.Add(grpCtrl);
            mainLayout.Controls.Add(grpLog);
            this.Controls.Add(mainLayout);
        }

        // ---------------------- 逻辑实现 ----------------------

        private void RefreshServerCombo()
        {
            cbServers.SelectedIndexChanged -= CbServers_SelectedIndexChanged;
            cbServers.DataSource = null;
            cbServers.DisplayMember = "Name";
            cbServers.ValueMember = "Id";
            cbServers.DataSource = configManager.Config.Servers;
            
            var current = configManager.GetCurrentServer();
            if (current != null) cbServers.SelectedValue = current.Id;
            
            cbServers.SelectedIndexChanged += CbServers_SelectedIndexChanged;
        }

        private void LoadConfigToUi()
        {
            RefreshServerCombo();
            var server = configManager.GetCurrentServer();
            if (server != null)
            {
                txtServer.Text = server.Server;
                txtListen.Text = server.Listen;
                txtToken.Text = server.Token;
                txtIp.Text = server.Ip;
                txtDns.Text = server.Dns;
                txtEch.Text = server.Ech;
            }
        }

        private void CbServers_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (isRunning)
            {
                MessageBox.Show("请先停止当前代理后再切换服务器。");
                // 还原选择（略复杂，暂略）
                return;
            }

            if (cbServers.SelectedValue is string id)
            {
                configManager.Config.CurrentServerId = id;
                configManager.SaveConfig();
                LoadConfigToUi();
            }
        }

        private void UpdateServerFromUi()
        {
            var server = configManager.GetCurrentServer();
            if (server != null)
            {
                server.Server = txtServer.Text;
                server.Listen = txtListen.Text;
                server.Token = txtToken.Text;
                server.Ip = txtIp.Text;
                server.Dns = txtDns.Text;
                server.Ech = txtEch.Text;
            }
        }

        private void AddServer()
        {
            string name = Microsoft.VisualBasic.Interaction.InputBox("请输入服务器名称", "新增", "新服务器");
            if (!string.IsNullOrWhiteSpace(name))
            {
                UpdateServerFromUi(); // 保存当前状态
                var newServer = new ServerConfig 
                { 
                    Name = name,
                    Server = txtServer.Text, // 复制当前的配置
                    Listen = txtListen.Text,
                    Token = txtToken.Text,
                    Ip = txtIp.Text,
                    Dns = txtDns.Text,
                    Ech = txtEch.Text
                };
                configManager.Config.Servers.Add(newServer);
                configManager.Config.CurrentServerId = newServer.Id;
                configManager.SaveConfig();
                LoadConfigToUi();
                Log($"[系统] 已添加服务器: {name}");
            }
        }

        private void SaveServer()
        {
            UpdateServerFromUi();
            configManager.SaveConfig();
            Log($"[系统] 配置已保存");
        }

        private void RenameServer()
        {
            var server = configManager.GetCurrentServer();
            if (server != null)
            {
                string name = Microsoft.VisualBasic.Interaction.InputBox("新名称", "重命名", server.Name);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    server.Name = name;
                    configManager.SaveConfig();
                    RefreshServerCombo();
                }
            }
        }

        private void DeleteServer()
        {
            if (configManager.Config.Servers.Count <= 1)
            {
                MessageBox.Show("至少保留一个服务器配置");
                return;
            }

            var server = configManager.GetCurrentServer();
            if (MessageBox.Show($"确定删除 {server.Name} 吗？", "确认", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                configManager.Config.Servers.Remove(server);
                configManager.Config.CurrentServerId = configManager.Config.Servers[0].Id;
                configManager.SaveConfig();
                LoadConfigToUi();
                Log($"[系统] 已删除服务器");
            }
        }

        // ---------------------- 进程控制 ----------------------

        private string FindExecutable()
        {
            string exeName = "ech-workers.exe";
            string[] paths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName),
                Path.Combine(Directory.GetCurrentDirectory(), exeName)
            };

            foreach (var p in paths)
            {
                if (File.Exists(p)) return p;
            }

            // 检查 PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (pathEnv != null)
            {
                foreach (var p in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        var fullPath = Path.Combine(p, exeName);
                        if (File.Exists(fullPath)) return fullPath;
                    }
                    catch { }
                }
            }

            return null;
        }

        private void StartProxy()
        {
            UpdateServerFromUi();
            configManager.SaveConfig();

            string exePath = FindExecutable();
            if (string.IsNullOrEmpty(exePath))
            {
                Log("错误: 找不到 ech-workers.exe 文件！请将其放在程序同级目录下。");
                return;
            }

            var server = configManager.GetCurrentServer();
            var args = new StringBuilder();
            if (!string.IsNullOrEmpty(server.Server)) args.Append($"-f {server.Server} ");
            if (!string.IsNullOrEmpty(server.Listen)) args.Append($"-l {server.Listen} ");
            if (!string.IsNullOrEmpty(server.Token)) args.Append($"-token {server.Token} ");
            if (!string.IsNullOrEmpty(server.Ip)) args.Append($"-ip {server.Ip} ");
            if (!string.IsNullOrEmpty(server.Dns)) args.Append($"-dns {server.Dns} ");
            if (!string.IsNullOrEmpty(server.Ech)) args.Append($"-ech {server.Ech} ");

            try
            {
                workerProcess = new Process();
                workerProcess.StartInfo.FileName = exePath;
                workerProcess.StartInfo.Arguments = args.ToString();
                workerProcess.StartInfo.UseShellExecute = false;
                workerProcess.StartInfo.RedirectStandardOutput = true;
                workerProcess.StartInfo.RedirectStandardError = true;
                workerProcess.StartInfo.CreateNoWindow = true; // 隐藏黑框
                workerProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8; // 处理编码

                workerProcess.OutputDataReceived += (s, e) => Log(e.Data);
                workerProcess.ErrorDataReceived += (s, e) => Log(e.Data);

                workerProcess.Start();
                workerProcess.BeginOutputReadLine();
                workerProcess.BeginErrorReadLine();

                isRunning = true;
                UpdateUiState(true);
                Log($"[系统] 已启动 ech-workers, PID: {workerProcess.Id}");
            }
            catch (Exception ex)
            {
                Log($"启动失败: {ex.Message}");
            }
        }

        private void StopProxy()
        {
            if (workerProcess != null && !workerProcess.HasExited)
            {
                try
                {
                    workerProcess.Kill();
                    workerProcess.WaitForExit(1000);
                }
                catch { }
            }
            isRunning = false;
            
            // 自动关闭系统代理
            if (isSystemProxyEnabled) ToggleSystemProxy();
            
            UpdateUiState(false);
            Log("[系统] 进程已停止");
        }

        private void UpdateUiState(bool running)
        {
            this.Invoke((MethodInvoker)delegate {
                btnStart.Enabled = !running;
                btnStop.Enabled = running;
                btnProxy.Enabled = running;
                
                txtServer.Enabled = !running;
                txtListen.Enabled = !running;
                cbServers.Enabled = !running;
            });
        }

        // ---------------------- 系统集成 ----------------------

        private void ToggleSystemProxy()
        {
            if (!isRunning) return;

            isSystemProxyEnabled = !isSystemProxyEnabled;
            try
            {
                SystemProxyHelper.SetProxy(isSystemProxyEnabled, txtListen.Text);
                btnProxy.Text = isSystemProxyEnabled ? "关闭系统代理" : "设置系统代理";
                Log($"[系统] 系统代理已{(isSystemProxyEnabled ? "开启" : "关闭")}");
            }
            catch (Exception ex)
            {
                Log($"[系统] 设置代理失败: {ex.Message}");
                isSystemProxyEnabled = !isSystemProxyEnabled; // Revert
            }
        }

        private void CheckAutoStartStatus()
        {
            string keyName = "ECHWorkersClient";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
            {
                chkAutoStart.Checked = key?.GetValue(keyName) != null;
            }
        }

        private void ChkAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            string keyName = "ECHWorkersClient";
            string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (chkAutoStart.Checked)
                    {
                        string exePath = Process.GetCurrentProcess().MainModule.FileName;
                        // 这里添加 -autostart 参数，以便启动时自动开始连接
                        key.SetValue(keyName, $"\"{exePath}\" -autostart");
                        Log("[系统] 已开启开机自启");
                    }
                    else
                    {
                        key.DeleteValue(keyName, false);
                        Log("[系统] 已关闭开机自启");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"设置开机启动失败: {ex.Message}");
                // 恢复CheckBox状态（防止死循环需小心）
            }
        }

        private void Log(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            if (rtbLog.InvokeRequired)
            {
                rtbLog.BeginInvoke(new Action<string>(Log), msg);
                return;
            }

            rtbLog.AppendText(msg + Environment.NewLine);
            rtbLog.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isSystemProxyEnabled) SystemProxyHelper.SetProxy(false, "");
            StopProxy();
            base.OnFormClosing(e);
        }
    }
}