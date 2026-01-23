using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Automation;
using AntdUI;
using Microsoft.Win32;

namespace ManageNotepadWindows
{
    public partial class NotepadsManager : Form
    {
        // UI font sizing constraints and step
        private const float MinFontSize = 9F;
        private const float MaxFontSize = 20F;
        private const float FontStep = 1F;

        private float currentFontSize = 11F;
        private float dpiScaleFactor = 1F;

        // Sorting state for AntdUI.Table
        private int sortColumnIndex = -1;
        private bool sortAscending = true;

        // Sorting state for Browser Tabs grid
        private int browserSortColumnIndex = -1;
        private bool browserSortAscending = true;

        private readonly string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotepadBackups");
        private readonly string settingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotepadBackups", "settings.json");
        private Timer searchDebounceTimer;

        // Virtualized grid + model - using AntdUI.Table for modern look
        private AntdUI.Table tableNotepadWindows;
        private List<NotepadWindowInfo> notepadWindows = new List<NotepadWindowInfo>();

        // Preview and UI
        private RichTextBox textBoxNotepadContent;
        private AntdUI.Panel panelNotepadPreview;
        private AntdUI.Panel panelBrowserPreview;
        private AntdUI.Panel panelTop;
        private AntdUI.Button refresh;
        private AntdUI.Input textBoxSearch;
        private AntdUI.Checkbox chkSearchContent;
        private SplitContainer splitContainer1;
        private AntdUI.Button btnFontIncrease;
        private AntdUI.Button btnFontDecrease;

        // Tab control - using AntdUI.Tabs for modern card-style tabs
        private AntdUI.Tabs tabs;

        // Browser tabs (for future implementation)
        private AntdUI.Table tableBrowserTabs;
        private List<BrowserTabInfo> browserTabs = new List<BrowserTabInfo>();
        private RichTextBox textBoxBrowserContent;
        private SplitContainer splitContainerBrowser;

        // Status bar
        private AntdUI.Panel panelStatus;
        private AntdUI.Label labelStatus;
        private int totalNotepadCount = 0;
        private int totalBrowserCount = 0;

        // Native helpers (consolidated)
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageInt(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint SMTO_BLOCK = 0x0001;
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        // Title dragging constants
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;

        // SVG Icons (full SVG markup required by AntdUI)
        private const string SvgSync = "<svg viewBox=\"0 0 1024 1024\"><path d=\"M168 504.2c1-43.7 10-86.1 26.9-126 17.3-41 42.1-77.7 73.7-109.4S337 212.3 378 195c42.4-17.9 87.4-27 133.9-27s91.5 9.1 133.8 27A341.5 341.5 0 0 1 755 268.8c9.9 9.9 19.2 20.4 27.8 31.4l-60.2 47c-5.3 4.1-3.5 12.5 3 14.1l175.6 43c5 1.2 9.9-2.6 9.9-7.7l.8-180.9c0-6.7-7.7-10.5-12.9-6.3l-56.4 44.1C765.8 155.1 646.2 92 511.8 92 282.7 92 96.3 275.6 92 504.2c0 3.3 2.7 6 6 6h60c3.3 0 6-2.7 6-6zm724 6h-60c-3.3 0-6 2.7-6 6-1 43.7-10 86.1-26.9 126-17.3 41-42.1 77.8-73.7 109.4A342.45 342.45 0 0 1 512 812.8c-47.3 0-93.1-9.6-135.5-27.8l-60.2 47c-5.3 4.1-3.5 12.5 3 14.1l175.6 43c5 1.2 9.9-2.6 9.9-7.7l.8-180.9c0-6.7-7.7-10.5-12.9-6.3l-56.4 44.1c-38.4-29.1-68.9-67.5-88.1-111.8-20.8-48-26-101.7-15-153.2 11-51.6 38.8-98.2 78.5-131.6 39.7-33.3 89.3-51.6 140.8-51.6 51.5 0 101.1 18.3 140.8 51.6 39.7 33.4 67.5 80 78.5 131.6 6.5 30.5 6.3 61.8-.8 92h62.1c3.3 0 6-2.7 6-6 .2-64.6-20.8-127.8-60-180.3z\"/></svg>";
        private const string SvgZoomIn = "<svg viewBox=\"0 0 1024 1024\"><path d=\"M637 443H519V309c0-4.4-3.6-8-8-8h-60c-4.4 0-8 3.6-8 8v134H325c-4.4 0-8 3.6-8 8v60c0 4.4 3.6 8 8 8h118v134c0 4.4 3.6 8 8 8h60c4.4 0 8-3.6 8-8V519h118c4.4 0 8-3.6 8-8v-60c0-4.4-3.6-8-8-8zm284 424L775 721c122.1-148.9 113.6-369.5-26-509-148-148.1-388.4-148.1-537 0-148.1 148.6-148.1 389 0 537 139.5 139.6 360.1 148.1 509 26l146 146c3.2 2.8 8.3 2.8 11 0l43-43c2.8-2.7 2.8-7.8 0-11zM696 696c-118.8 118.7-311.2 118.7-430 0-118.7-118.8-118.7-311.2 0-430 118.8-118.7 311.2-118.7 430 0 118.7 118.8 118.7 311.2 0 430z\"/></svg>";
        private const string SvgZoomOut = "<svg viewBox=\"0 0 1024 1024\"><path d=\"M637 443H325c-4.4 0-8 3.6-8 8v60c0 4.4 3.6 8 8 8h312c4.4 0 8-3.6 8-8v-60c0-4.4-3.6-8-8-8zm284 424L775 721c122.1-148.9 113.6-369.5-26-509-148-148.1-388.4-148.1-537 0-148.1 148.6-148.1 389 0 537 139.5 139.6 360.1 148.1 509 26l146 146c3.2 2.8 8.3 2.8 11 0l43-43c2.8-2.7 2.8-7.8 0-11zM696 696c-118.8 118.7-311.2 118.7-430 0-118.7-118.8-118.7-311.2 0-430 118.8-118.7 311.2-118.7 430 0 118.7 118.8 118.7 311.2 0 430z\"/></svg>";
        private const string SvgFile = "<svg viewBox=\"0 0 1024 1024\"><path d=\"M854.6 288.6L639.4 73.4c-6-6-14.1-9.4-22.6-9.4H192c-17.7 0-32 14.3-32 32v832c0 17.7 14.3 32 32 32h640c17.7 0 32-14.3 32-32V311.3c0-8.5-3.4-16.7-9.4-22.7zM790.2 326H602V137.8L790.2 326zm1.8 562H232V136h302v216a42 42 0 0 0 42 42h216v494z\"/></svg>";
        private const string SvgGlobal = "<svg viewBox=\"0 0 1024 1024\"><path d=\"M512 64C264.6 64 64 264.6 64 512s200.6 448 448 448 448-200.6 448-448S759.4 64 512 64zm0 820c-205.4 0-372-166.6-372-372s166.6-372 372-372 372 166.6 372 372-166.6 372-372 372zm5.6-532.7c53 0 89 33.8 93 83.4.3 4.2 3.8 7.4 8 7.4h56.7c2.6 0 4.7-2.1 4.7-4.7 0-86.7-68.4-147.4-162.7-147.4C407.4 290 344 364.2 344 486.8v52.3C344 660.8 407.4 734 517.3 734c94 0 162.7-58.8 162.7-141.4 0-2.6-2.1-4.7-4.7-4.7H618c-4.2 0-7.7 3.2-8 7.4-4.2 46.1-40.1 77.8-93 77.8-65.3 0-102.1-47.9-102.1-133.6v-52.6c.1-87 37-135.5 102.7-135.5z\"/></svg>";
        private const string SvgSearch = "<svg viewBox=\"0 0 1024 1024\"><path d=\"M909.6 854.5L649.9 594.8C690.2 542.7 714 479.9 714 412c0-158.2-128.8-287-287-287S140 253.8 140 412s128.8 287 287 287c67.9 0 130.7-23.8 182.8-63.5l259.7 259.6a8.2 8.2 0 0 0 11.6 0l28.5-28.5c3.2-3.2 3.2-8.4 0-11.6zM427 682c-150.2 0-272-121.8-272-272s121.8-272 272-272 272 121.8 272 272-121.8 272-272 272z\"/></svg>";

        // Other constants
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint TOPMOST_FLAGS = SWP_NOMOVE | SWP_NOSIZE;
        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;
        private const uint WM_CHAR = 0x0102;
        private const uint WM_SETTEXT = 0x000C;
        private const int SW_RESTORE = 9;

        // Model - properties for AntdUI.Table binding
        private class NotepadWindowInfo : AntdUI.NotifyProperty
        {
            public IntPtr Hwnd { get; set; }
            public int ProcessId { get; set; }
            public string Title { get; set; }
            public string Preview { get; set; }
            public string Diagnostic { get; set; }
        }

        private class BrowserTabInfo : AntdUI.NotifyProperty
        {
            public IntPtr Hwnd { get; set; }
            public int ProcessId { get; set; }
            public string Title { get; set; }
            public string Url { get; set; }
            public string Preview { get; set; }
        }

        // Settings for persistence
        private class AppSettings
        {
            public int NotepadSortColumn { get; set; } = -1;
            public bool NotepadSortAscending { get; set; } = true;
            public int BrowserSortColumn { get; set; } = -1;
            public bool BrowserSortAscending { get; set; } = true;
        }

        public NotepadsManager()
        {
            InitializeComponent();

            // DPI autoscale
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // AntdUI window configuration
            this.MaximizeBox = true;
            this.MinimizeBox = true;

            // Ensure backup dir + restore
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
            RestoreBackupNotepadContent();

            // Timers
            Timer backupTimer = new Timer { Interval = 5 * 60 * 1000 };
            backupTimer.Tick += (s, e) => BackupUnsavedNotepadContent();
            backupTimer.Start();

            searchDebounceTimer = new Timer { Interval = 300 };
            searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

            // Behavior
            SetupGrid();
            SetupBrowserGrid();
            MakeWindowTopMost();
            this.Load += NotepadsManager_Load;
            this.FormClosing += NotepadsManager_FormClosing;

            // keyboard shortcuts
            this.KeyPreview = true;
            this.KeyDown += NotepadsManager_KeyDown;
        }

        private void NotepadsManager_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.F) { textBoxSearch.Focus(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.Oemplus) { btnFontIncrease_Click(this, EventArgs.Empty); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.OemMinus) { btnFontDecrease_Click(this, EventArgs.Empty); e.Handled = true; }
        }

        private void MakeWindowTopMost()
        {
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, TOPMOST_FLAGS);
        }

        private void SetupGrid()
        {
            if (tableNotepadWindows == null)
            {
                tableNotepadWindows = new AntdUI.Table
                {
                    Dock = DockStyle.Fill,
                    Radius = 6,
                    FixedHeader = true,
                    EnableHeaderResizing = true,
                    RowSelectedBg = Color.FromArgb(230, 247, 255),
                    RowSelectedFore = Color.Black,
                    BorderColor = Color.FromArgb(217, 217, 217),
                    EmptyText = "No Notepad windows found"
                };

                // Define columns with sorting enabled
                tableNotepadWindows.Columns.Add(new AntdUI.Column("ProcessId", "PID") { Width = "80", SortOrder = true });
                tableNotepadWindows.Columns.Add(new AntdUI.Column("Title", "Name") { SortOrder = true });

                // Event handlers
                tableNotepadWindows.CellClick += TableNotepadWindows_CellClick;
                tableNotepadWindows.CellDoubleClick += TableNotepadWindows_CellDoubleClick;
                tableNotepadWindows.SelectIndexChanged += TableNotepadWindows_SelectIndexChanged;
                tableNotepadWindows.SortRows += TableNotepadWindows_SortRows;
            }

            if (splitContainer1 != null)
            {
                splitContainer1.Panel1.Controls.Clear();
                splitContainer1.Panel1.Controls.Add(tableNotepadWindows);
            }
        }

        private void SetupBrowserGrid()
        {
            if (tableBrowserTabs == null)
            {
                tableBrowserTabs = new AntdUI.Table
                {
                    Dock = DockStyle.Fill,
                    Radius = 6,
                    FixedHeader = true,
                    EnableHeaderResizing = true,
                    RowSelectedBg = Color.FromArgb(230, 247, 255),
                    RowSelectedFore = Color.Black,
                    BorderColor = Color.FromArgb(217, 217, 217),
                    EmptyText = "No browser tabs found"
                };

                // Define columns with sorting enabled - Title and URL share remaining width equally via "fill"
                tableBrowserTabs.Columns.Add(new AntdUI.Column("ProcessId", "PID") { Width = "80", SortOrder = true });
                tableBrowserTabs.Columns.Add(new AntdUI.Column("Title", "Title") { Width = "fill", SortOrder = true });
                tableBrowserTabs.Columns.Add(new AntdUI.Column("Url", "URL") { Width = "fill", SortOrder = true });

                // Event handlers
                tableBrowserTabs.CellClick += TableBrowserTabs_CellClick;
                tableBrowserTabs.CellDoubleClick += TableBrowserTabs_CellDoubleClick;
                tableBrowserTabs.SelectIndexChanged += TableBrowserTabs_SelectIndexChanged;
                tableBrowserTabs.SortRows += TableBrowserTabs_SortRows;
            }

            if (splitContainerBrowser != null)
            {
                splitContainerBrowser.Panel1.Controls.Clear();
                splitContainerBrowser.Panel1.Controls.Add(tableBrowserTabs);
            }
        }

        // Track selected row indices for AntdUI.Table
        private int selectedNotepadIndex = -1;
        private int selectedBrowserIndex = -1;

        private void TableNotepadWindows_CellClick(object sender, AntdUI.TableClickEventArgs e)
        {
            // Selection change is handled by SelectIndexChanged - this is just for reference
            // CellClick uses 0-based RowIndex, but we use SelectIndexChanged as single source of truth
        }

        private void TableNotepadWindows_SelectIndexChanged(object sender, EventArgs e)
        {
            // Single source of truth for selection changes (both click and keyboard)
            // SelectedIndex is 1-based per AntdUI documentation ("选中行（1开始）")
            int selectedIdx = tableNotepadWindows?.SelectedIndex ?? -1;
            if (selectedIdx > 0)
            {
                int rowIndex = selectedIdx - 1; // Convert to 0-based
                if (rowIndex >= 0 && rowIndex < notepadWindows.Count)
                {
                    selectedNotepadIndex = rowIndex;
                    LoadNotepadPreviewAsync(rowIndex);
                }
            }
        }

        private void TableNotepadWindows_CellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
        {
            // Use Record directly - no index conversion needed
            if (e.Record is not NotepadWindowInfo item) return;
            IntPtr hwnd = item.Hwnd;
            if (hwnd == IntPtr.Zero && item.ProcessId != 0) hwnd = FindWindowForProcess(item.ProcessId);
            if (hwnd != IntPtr.Zero) { ShowWindow(hwnd, SW_RESTORE); SetForegroundWindow(hwnd); }
        }

        private void TableBrowserTabs_CellClick(object sender, AntdUI.TableClickEventArgs e)
        {
            // Selection change is handled by SelectIndexChanged - this is just for reference
            // CellClick uses 0-based RowIndex, but we use SelectIndexChanged as single source of truth
        }

        private void TableBrowserTabs_SelectIndexChanged(object sender, EventArgs e)
        {
            // Single source of truth for selection changes (both click and keyboard)
            // SelectedIndex is 1-based per AntdUI documentation ("选中行（1开始）")
            int selectedIdx = tableBrowserTabs?.SelectedIndex ?? -1;
            if (selectedIdx > 0)
            {
                int rowIndex = selectedIdx - 1; // Convert to 0-based
                if (rowIndex >= 0 && rowIndex < browserTabs.Count)
                {
                    selectedBrowserIndex = rowIndex;
                    LoadBrowserPreview(rowIndex);
                }
            }
        }

        private void TableBrowserTabs_CellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
        {
            // Use Record directly - no index conversion needed
            if (e.Record is not BrowserTabInfo item) return;
            IntPtr hwnd = item.Hwnd;
            if (hwnd != IntPtr.Zero) { ShowWindow(hwnd, SW_RESTORE); SetForegroundWindow(hwnd); }
        }

        private void LoadBrowserPreview(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= browserTabs.Count) { textBoxBrowserContent.Text = string.Empty; return; }
            var info = browserTabs[rowIndex];

            // Display tab info
            var sb = new StringBuilder();
            sb.AppendLine($"Title: {info.Title}");
            sb.AppendLine($"URL: {info.Url ?? "Unknown"}");
            sb.AppendLine($"PID: {info.ProcessId}");
            sb.AppendLine($"Window Handle: 0x{info.Hwnd.ToString("X")}");

            if (!string.IsNullOrWhiteSpace(info.Preview))
            {
                sb.AppendLine();
                sb.AppendLine("Preview:");
                sb.AppendLine(info.Preview);
            }

            textBoxBrowserContent.Text = sb.ToString();
        }

        private async void LoadNotepadPreviewAsync(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= notepadWindows.Count) { textBoxNotepadContent.Text = string.Empty; return; }
            var info = notepadWindows[rowIndex];
            var hwnd = info.Hwnd;

            // If we already have preview content from search, use it
            if (!string.IsNullOrWhiteSpace(info.Preview) && !info.Preview.StartsWith("["))
            {
                textBoxNotepadContent.Text = info.Preview;
                return;
            }

            textBoxNotepadContent.Text = "Loading preview...";

            // Run content extraction on background thread to avoid UI hang
            string content = await System.Threading.Tasks.Task.Run(() =>
            {
                // Try multiple methods to get content - start with the fastest
                string text = GetNotepadText(hwnd, -1);
                if (string.IsNullOrWhiteSpace(text) || text.StartsWith("Unable") || text.StartsWith("No content"))
                {
                    text = GetNotepadTextModern(hwnd);
                }
                if (string.IsNullOrWhiteSpace(text) || text.StartsWith("["))
                {
                    text = TryGetTextFromChildClasses(hwnd);
                }
                if (string.IsNullOrWhiteSpace(text) || text.StartsWith("Unable") || text.StartsWith("No content") || text.StartsWith("["))
                {
                    text = "Preview not available";
                }
                return text;
            });

            // Verify selection hasn't changed while loading
            if (selectedNotepadIndex == rowIndex)
            {
                textBoxNotepadContent.Text = content;
            }
        }

        private void PopulateNotepadWindows()
        {
            notepadWindows.Clear();

            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;

                    StringBuilder className = new StringBuilder(256);
                    GetClassName(hWnd, className, className.Capacity);
                    var cls = className.ToString();

                    if (cls == "Notepad" || cls == "CascadiaWindow")
                    {
                        GetWindowThreadProcessId(hWnd, out uint pid);

                        StringBuilder title = new StringBuilder(256);
                        GetWindowText(hWnd, title, title.Capacity);

                        // No preview during initial load - keep it fast
                        notepadWindows.Add(new NotepadWindowInfo
                        {
                            Hwnd = hWnd,
                            ProcessId = (int)pid,
                            Title = title.ToString(),
                            Preview = "",
                            Diagnostic = ""
                        });
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            // preserve sorting if active
            if (sortColumnIndex != -1) SortByColumn(sortColumnIndex);
            else
            {
                RefreshNotepadTable();
            }
        }

        private void RefreshNotepadTable()
        {
            if (tableNotepadWindows != null)
            {
                tableNotepadWindows.DataSource = null;
                tableNotepadWindows.DataSource = new List<NotepadWindowInfo>(notepadWindows);
            }
        }

        private void PopulateBrowserTabs()
        {
            browserTabs.Clear();

            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;

                    StringBuilder className = new StringBuilder(256);
                    GetClassName(hWnd, className, className.Capacity);
                    var cls = className.ToString();

                    // Chrome, Edge, Brave use Chrome_WidgetWin_1
                    // Firefox uses MozillaWindowClass
                    if (cls == "Chrome_WidgetWin_1" || cls == "MozillaWindowClass")
                    {
                        GetWindowThreadProcessId(hWnd, out uint pid);

                        StringBuilder title = new StringBuilder(256);
                        GetWindowText(hWnd, title, title.Capacity);

                        // Skip windows with no title or browser window titles
                        string titleStr = title.ToString();
                        if (string.IsNullOrWhiteSpace(titleStr)) return true;

                        // Clean up title - remove browser suffixes
                        titleStr = CleanBrowserTitle(titleStr);

                        // Try to extract URL from UI Automation
                        string url = ExtractBrowserUrl(hWnd);

                        // Try to extract preview content for ChatGPT/Grok pages
                        string preview = "";
                        if (url != null && (url.Contains("chatgpt.com") || url.Contains("grok.com") || url.Contains("x.com/i/grok")))
                        {
                            preview = ExtractBrowserPageContent(hWnd);
                        }

                        browserTabs.Add(new BrowserTabInfo
                        {
                            Hwnd = hWnd,
                            ProcessId = (int)pid,
                            Title = titleStr,
                            Url = url,
                            Preview = preview
                        });
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            // preserve sorting if active
            if (browserSortColumnIndex != -1) SortBrowserTabsByColumn(browserSortColumnIndex);
            else
            {
                RefreshBrowserTable();
            }
        }

        private void RefreshBrowserTable()
        {
            if (tableBrowserTabs != null)
            {
                tableBrowserTabs.DataSource = null;
                tableBrowserTabs.DataSource = new List<BrowserTabInfo>(browserTabs);
            }
        }

        private string ExtractBrowserUrl(IntPtr windowHandle)
        {
            try
            {
                // Use a timeout to prevent hanging on unresponsive windows
                var task = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var windowElement = AutomationElement.FromHandle(windowHandle);
                        if (windowElement == null) return "";

                        // Look for address bar (Edit control with automation ID containing "address" or "url")
                        var condition = new AndCondition(
                            new PropertyCondition(AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit),
                            new OrCondition(
                                new PropertyCondition(AutomationElement.AutomationIdProperty, "addressbar"),
                                new PropertyCondition(AutomationElement.NameProperty, "Address and search bar")
                            )
                        );

                        var addressBar = windowElement.FindFirst(TreeScope.Descendants, condition);
                        if (addressBar != null)
                        {
                            object patternObj;
                            if (addressBar.TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
                            {
                                var valuePattern = (ValuePattern)patternObj;
                                return valuePattern.Current.Value;
                            }
                        }

                        // Fallback: try to find any Edit control that looks like a URL (limit search)
                        var editCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, System.Windows.Automation.ControlType.Edit);
                        var edits = windowElement.FindAll(TreeScope.Children, editCondition);

                        for (int i = 0; i < Math.Min(edits.Count, 10); i++)
                        {
                            try
                            {
                                object patternObj;
                                if (edits[i].TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
                                {
                                    var valuePattern = (ValuePattern)patternObj;
                                    var value = valuePattern.Current.Value;

                                    // Check if it looks like a URL
                                    if (!string.IsNullOrWhiteSpace(value) &&
                                        (value.StartsWith("http://") || value.StartsWith("https://") ||
                                         value.StartsWith("file://") || value.Contains(".")))
                                    {
                                        return value;
                                    }
                                }
                            }
                            catch { }
                        }
                        return "";
                    }
                    catch { return ""; }
                });

                // Wait max 500ms for URL extraction (reduced from 2s for faster startup)
                if (task.Wait(500))
                    return task.Result;
            }
            catch { }

            return "";
        }

        private string CleanBrowserTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title;

            // Remove common browser suffixes
            string[] suffixes = {
                " - Google Chrome",
                " - Microsoft Edge",
                " - Mozilla Firefox",
                " - Brave",
                " - Opera",
                " - Vivaldi",
                " — Mozilla Firefox",
                " – Google Chrome"
            };

            foreach (var suffix in suffixes)
            {
                if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    title = title.Substring(0, title.Length - suffix.Length);
                    break;
                }
            }

            return title.Trim();
        }

        private string ExtractBrowserPageContent(IntPtr windowHandle)
        {
            // Disable browser content extraction - too slow and causes hangs
            // Browser UI Automation can scan thousands of elements and freeze the system
            return "";
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e) => BackupUnsavedNotepadContent();

        private void BackupUnsavedNotepadContent()
        {
            var processes = Process.GetProcessesByName("notepad");
            foreach (var process in processes)
            {
                try
                {
                    IntPtr hWnd = process.MainWindowHandle;
                    if (hWnd == IntPtr.Zero || !IsWindowVisible(hWnd)) continue;
                    var title = new StringBuilder(256);
                    GetWindowText(hWnd, title, title.Capacity);
                    if (title.ToString().Contains("Untitled - Notepad"))
                    {
                        string content = GetNotepadText(hWnd, -1);
                        string backupFilePath = Path.Combine(backupDir, $"NotepadBackup_{process.Id}.txt");
                        File.WriteAllText(backupFilePath, content);
                    }
                    else
                    {
                        string backupFilePath = Path.Combine(backupDir, $"NotepadBackup_{process.Id}.txt");
                        if (File.Exists(backupFilePath)) File.Delete(backupFilePath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error backing up notepad process {process.Id}: {ex.Message}");
                }
            }
            CleanupObsoleteBackups();
        }

        private void CleanupObsoleteBackups()
        {
            string[] backupFiles = Directory.GetFiles(backupDir, "NotepadBackup_*.txt");
            var running = new HashSet<int>(Process.GetProcessesByName("notepad").Select(p => p.Id));
            foreach (var f in backupFiles)
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    var parts = name.Split('_');
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int pid)) continue;
                    if (!running.Contains(pid)) File.Delete(f);
                }
                catch (Exception ex) { Debug.WriteLine($"Error deleting backup {f}: {ex.Message}"); }
            }
        }

        private void RestoreBackupNotepadContent()
        {
            string[] backupFiles = Directory.GetFiles(backupDir, "NotepadBackup_*.txt");
            foreach (var backupFile in backupFiles)
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(backupFile);
                    var parts = name.Split('_');
                    if (parts.Length < 2 || !int.TryParse(parts[1], out int pid)) continue;
                    bool running = Process.GetProcessesByName("notepad").Any(p => p.Id == pid);
                    if (running) continue;

                    string content = File.ReadAllText(backupFile);
                    var notepad = Process.Start("notepad.exe");
                    if (!notepad.WaitForInputIdle(5000)) continue; // 5 second timeout
                    IntPtr hwnd = notepad.MainWindowHandle;
                    IntPtr edit = FindWindowEx(hwnd, IntPtr.Zero, "Edit", null);
                    if (edit != IntPtr.Zero)
                    {
                        IntPtr result;
                        SendMessageTimeout(edit, WM_SETTEXT, IntPtr.Zero, new StringBuilder(content), SMTO_ABORTIFHUNG, 2000, out result);
                        SendMessageTimeout(edit, WM_CHAR, new IntPtr(' '), IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out result);
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"Error restoring backup {backupFile}: {ex.Message}"); }
            }
        }

        private string TryGetTextFromChildClasses(IntPtr parentHandle)
        {
            if (parentHandle == IntPtr.Zero) return null;
            string[] classNames = { "Edit", "RichEdit20W", "RichEdit20A", "RICHEDIT50W", "RichEditD2DPT", "RichEdit50W" };
            string result = null;
            EnumChildWindows(parentHandle, (child, lparam) =>
            {
                try
                {
                    var cls = new StringBuilder(256);
                    GetClassName(child, cls, cls.Capacity);
                    string cn = cls.ToString();
                    if (classNames.Contains(cn))
                    {
                        IntPtr lenResult;
                        // Use 1000ms timeout to prevent hanging on unresponsive windows
                        IntPtr ret = SendMessageTimeout(child, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out lenResult);
                        if (ret == IntPtr.Zero) return true; // Timed out or failed, skip this window

                        int len = (int)lenResult;
                        if (len > 0 && len < 10_000_000) // Sanity check on length
                        {
                            var sb = new StringBuilder(Math.Min(len + 1, 1_000_000)); // Cap at 1MB
                            IntPtr textResult;
                            ret = SendMessageTimeout(child, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 2000, out textResult);
                            if (ret == IntPtr.Zero) return true; // Timed out

                            var text = sb.ToString();
                            if (!string.IsNullOrWhiteSpace(text)) { result = text; return false; }
                        }
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private string GetNotepadTextModern(IntPtr windowHandle)
        {
            try
            {
                // Use a timeout to prevent hanging on unresponsive windows
                var task = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var windowElement = AutomationElement.FromHandle(windowHandle);
                        if (windowElement == null) return "[UIA: window element null]";

                        // Only try Edit and Document types - most common for Notepad
                        System.Windows.Automation.ControlType[] tryTypes = { System.Windows.Automation.ControlType.Edit, System.Windows.Automation.ControlType.Document };
                        foreach (var ct in tryTypes)
                        {
                            var element = windowElement.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ct));
                            if (element == null) continue;

                            object patternObj;
                            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
                            {
                                var valuePattern = (ValuePattern)patternObj;
                                var v = valuePattern.Current.Value;
                                if (!string.IsNullOrWhiteSpace(v)) return v;
                            }

                            if (element.TryGetCurrentPattern(TextPattern.Pattern, out patternObj))
                            {
                                var textPattern = (TextPattern)patternObj;
                                string doc = textPattern.DocumentRange.GetText(-1);
                                if (!string.IsNullOrWhiteSpace(doc)) return doc;
                            }

                            var name = element.Current.Name;
                            if (!string.IsNullOrWhiteSpace(name)) return name;
                        }

                        // Try Win32 fallback
                        var win32Text = TryGetTextFromChildClasses(windowHandle);
                        if (!string.IsNullOrWhiteSpace(win32Text)) return win32Text;

                        return "[No UIA text found]";
                    }
                    catch (Exception ex) { return $"[UIA error: {ex.Message}]"; }
                });

                // Wait max 3 seconds for text extraction
                if (task.Wait(3000))
                    return task.Result;
                else
                    return "[Timeout extracting text]";
            }
            catch (Exception ex) { return $"[UIA error: {ex.Message}]"; }
        }

        private string GetNotepadText(IntPtr notepadHandle, int maxLines = 2)
        {
            IntPtr editHandle = FindWindowEx(notepadHandle, IntPtr.Zero, "Edit", null);
            if (editHandle == IntPtr.Zero) return "Unable to find text content.";

            IntPtr lenResult;
            // Use 1000ms timeout to prevent hanging
            IntPtr ret = SendMessageTimeout(editHandle, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 1000, out lenResult);
            if (ret == IntPtr.Zero) return "Window not responding.";

            int textLength = (int)lenResult;
            if (textLength == 0) return "No content available.";
            if (textLength > 10_000_000) return "Content too large."; // Sanity check

            var windowText = new StringBuilder(Math.Min(textLength + 1, 1_000_000)); // Cap at 1MB
            IntPtr textResult;
            ret = SendMessageTimeout(editHandle, WM_GETTEXT, (IntPtr)windowText.Capacity, windowText, SMTO_ABORTIFHUNG, 2000, out textResult);
            if (ret == IntPtr.Zero) return "Window not responding.";

            var lines = windowText.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var nonEmpty = lines.SkipWhile(line => string.IsNullOrWhiteSpace(line)).ToArray();
            var use = nonEmpty.Length != 0 ? nonEmpty : lines;
            if (maxLines == -1) return string.Join(Environment.NewLine, use);
            return string.Join(Environment.NewLine, use, 0, Math.Min(maxLines, use.Length));
        }

        private IntPtr FindWindowForProcess(int processId)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid == (uint)processId) { found = hWnd; return false; }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private void TextBoxSearch_TextChanged(object sender, EventArgs e)
        {
            searchDebounceTimer.Stop();
            searchDebounceTimer.Start();
        }

        private async void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            searchDebounceTimer.Stop();

            // Search based on active tab
            if (tabs != null && tabs.SelectedIndex == 1)
            {
                await PerformBrowserSearchAsync();
            }
            else
            {
                await PerformNotepadSearchAsync();
            }
        }

        private async System.Threading.Tasks.Task PerformBrowserSearchAsync()
        {
            string searchText = textBoxSearch.Text.ToLower();

            // If search is empty, just populate all browser tabs
            if (string.IsNullOrWhiteSpace(searchText))
            {
                PopulateBrowserTabs();
                RefreshBrowserTable();
                return;
            }

            var browserResults = await System.Threading.Tasks.Task.Run(() =>
            {
                var tempResults = new List<BrowserTabInfo>();

                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hWnd)) return true;

                        var cls = new StringBuilder(256);
                        GetClassName(hWnd, cls, cls.Capacity);
                        var clsStr = cls.ToString();

                        if (clsStr == "Chrome_WidgetWin_1" || clsStr == "MozillaWindowClass")
                        {
                            GetWindowThreadProcessId(hWnd, out uint pid);

                            var titleSb = new StringBuilder(256);
                            GetWindowText(hWnd, titleSb, titleSb.Capacity);
                            string titleStr = titleSb.ToString();
                            if (string.IsNullOrWhiteSpace(titleStr)) return true;
                            titleStr = CleanBrowserTitle(titleStr);

                            string url = "";
                            try { url = ExtractBrowserUrl(hWnd) ?? ""; } catch { }

                            // Match against title or URL
                            if (titleStr.ToLower().Contains(searchText) ||
                                url.ToLower().Contains(searchText))
                            {
                                tempResults.Add(new BrowserTabInfo
                                {
                                    Hwnd = hWnd,
                                    ProcessId = (int)pid,
                                    Title = titleStr,
                                    Url = url,
                                    Preview = ""
                                });
                            }
                        }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);

                return tempResults;
            });

            browserTabs.Clear();
            browserTabs.AddRange(browserResults);

            if (browserSortColumnIndex != -1) SortBrowserTabsByColumn(browserSortColumnIndex);
            else RefreshBrowserTable();

            UpdateStatus();
        }

        private async System.Threading.Tasks.Task PerformNotepadSearchAsync()
        {
            string searchText = textBoxSearch.Text.ToLower();

            // If search is empty, just populate all windows without extracting content
            if (string.IsNullOrWhiteSpace(searchText))
            {
                PopulateNotepadWindows();
                return;
            }

            bool searchContent = chkSearchContent?.Checked ?? false;

            var results = await System.Threading.Tasks.Task.Run(() =>
            {
                var tempResults = new List<NotepadWindowInfo>();

                EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    var cls = new StringBuilder(256);
                    GetClassName(hWnd, cls, cls.Capacity);
                    if (cls.ToString() == "Notepad" || cls.ToString() == "CascadiaWindow")
                    {
                        GetWindowThreadProcessId(hWnd, out uint pid);
                        var title = new StringBuilder(256);
                        GetWindowText(hWnd, title, title.Capacity);

                        // Always check title first (fast)
                        if (title.ToString().ToLower().Contains(searchText))
                        {
                            tempResults.Add(new NotepadWindowInfo { Hwnd = hWnd, ProcessId = (int)pid, Title = title.ToString(), Preview = "", Diagnostic = "" });
                            return true;
                        }

                        // Only search content if checkbox is checked
                        if (searchContent)
                        {
                            // Get full content with fallback methods
                            string windowContent = GetNotepadText(hWnd, -1);
                            if (string.IsNullOrWhiteSpace(windowContent) || windowContent.StartsWith("Unable") || windowContent.StartsWith("No content"))
                            {
                                windowContent = GetNotepadTextModern(hWnd);
                            }
                            if (string.IsNullOrWhiteSpace(windowContent) || windowContent.StartsWith("["))
                            {
                                windowContent = TryGetTextFromChildClasses(hWnd);
                            }
                            if (string.IsNullOrWhiteSpace(windowContent) || windowContent.StartsWith("["))
                            {
                                windowContent = "[Unable to retrieve content]";
                            }
                            if (windowContent.ToLower().Contains(searchText))
                            {
                                tempResults.Add(new NotepadWindowInfo { Hwnd = hWnd, ProcessId = (int)pid, Title = title.ToString(), Preview = windowContent, Diagnostic = windowContent });
                            }
                        }
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

                return tempResults;
            });

            // Update UI on main thread
            notepadWindows.Clear();
            notepadWindows.AddRange(results);

            // preserve sort
            if (sortColumnIndex != -1) SortByColumn(sortColumnIndex);
            else
            {
                RefreshNotepadTable();
                if (selectedNotepadIndex >= 0 && selectedNotepadIndex < notepadWindows.Count)
                {
                    LoadNotepadPreviewAsync(selectedNotepadIndex);
                }
            }

            UpdateStatus();
        }

        private async void btnRefresh_Click(object sender, EventArgs e)
        {
            refresh.Enabled = false;
            refresh.Text = "Refreshing...";
            UpdateStatus("Refreshing...");

            // Check which tab is active and refresh accordingly, preserving current filter
            if (tabs != null && tabs.SelectedIndex == 0)
            {
                // Notepads tab - use search if filter is active
                await PerformNotepadSearchAsync();
                if (string.IsNullOrWhiteSpace(textBoxSearch.Text))
                    totalNotepadCount = notepadWindows.Count;
            }
            else if (tabs != null && tabs.SelectedIndex == 1)
            {
                // Browser Tabs tab - use search if filter is active
                await PerformBrowserSearchAsync();
                if (string.IsNullOrWhiteSpace(textBoxSearch.Text))
                    totalBrowserCount = browserTabs.Count;
            }

            refresh.Enabled = true;
            refresh.Text = "Refresh";
            UpdateStatus();
        }
        private void btnFontIncrease_Click(object sender, EventArgs e)
        {
            currentFontSize = Math.Min(MaxFontSize, currentFontSize + FontStep);
            UpdateFonts();
        }
        private void btnFontDecrease_Click(object sender, EventArgs e)
        {
            currentFontSize = Math.Max(MinFontSize, currentFontSize - FontStep);
            UpdateFonts();
        }

        private void NotepadsManager_Load(object sender, EventArgs e)
        {
            try { this.Icon = ManageCMDWindows.Properties.Resources.notepad_manager_icon; } catch { }
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int screenHeight = Screen.PrimaryScreen.WorkingArea.Height;

            // Calculate DPI scale factor
            using (Graphics g = this.CreateGraphics())
            {
                float dpi = g.DpiX;
                dpiScaleFactor = dpi / 96F;  // 96 DPI = 100%, 192 DPI = 200%, etc.
                currentFontSize = 11F;
            }

            // Apply DPI scaling to all controls
            ApplyDpiScaling();
            UpdateFonts();

            this.Width = (int)(screenWidth * 0.8);
            this.Height = (int)(screenHeight * 0.7);
            this.StartPosition = FormStartPosition.CenterScreen;
            if (splitContainer1 != null) splitContainer1.SplitterDistance = (int)(splitContainer1.Width * 0.60);
            if (splitContainerBrowser != null) splitContainerBrowser.SplitterDistance = (int)(splitContainerBrowser.Width * 0.60);

            // Load saved settings (including sort order)
            LoadSettings();

            // Load data asynchronously to show UI immediately
            LoadDataAsync();
        }

        private async void LoadDataAsync()
        {
            UpdateStatus("Loading Notepad windows...");

            // Load notepad windows (fast - no URL extraction)
            await System.Threading.Tasks.Task.Run(() => PopulateNotepadWindows());
            totalNotepadCount = notepadWindows.Count;
            RefreshNotepadTable();

            UpdateStatus("Loading browser tabs...");

            // Load browser tabs (slower due to URL extraction)
            await System.Threading.Tasks.Task.Run(() => PopulateBrowserTabs());
            totalBrowserCount = browserTabs.Count;
            RefreshBrowserTable();

            // Apply saved sort order after data is loaded
            ApplySavedSortOrder();
            UpdateStatus();
        }

        private void UpdateStatus(string message = null)
        {
            if (labelStatus == null) return;

            if (!string.IsNullOrEmpty(message))
            {
                labelStatus.Text = message;
                return;
            }

            // Show counts based on active tab
            if (tabs != null && tabs.SelectedIndex == 1)
            {
                // Browser tab
                if (browserTabs.Count == totalBrowserCount || totalBrowserCount == 0)
                    labelStatus.Text = $"{browserTabs.Count} browser tab{(browserTabs.Count != 1 ? "s" : "")}";
                else
                    labelStatus.Text = $"{browserTabs.Count} of {totalBrowserCount} browser tabs";
            }
            else
            {
                // Notepads tab
                if (notepadWindows.Count == totalNotepadCount || totalNotepadCount == 0)
                    labelStatus.Text = $"{notepadWindows.Count} Notepad window{(notepadWindows.Count != 1 ? "s" : "")}";
                else
                    labelStatus.Text = $"{notepadWindows.Count} of {totalNotepadCount} Notepad windows";
            }
        }

        private void NotepadsManager_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
        }

        private void SetFontRecursive(Control control, Font font)
        {
            control.Font = font;
            foreach (Control child in control.Controls) SetFontRecursive(child, font);
        }

        private void UpdateFonts()
        {
            var font = new Font(this.Font.FontFamily, currentFontSize, this.Font.Style);
            SetFontRecursive(this, font);
        }

        private void ApplyDpiScaling()
        {
            // Scale button sizes
            this.refresh.Size = new Size((int)(150 * dpiScaleFactor), (int)(70 * dpiScaleFactor));
            this.btnFontIncrease.Size = new Size((int)(130 * dpiScaleFactor), (int)(70 * dpiScaleFactor));
            this.btnFontDecrease.Size = new Size((int)(130 * dpiScaleFactor), (int)(70 * dpiScaleFactor));

            // Scale panel height
            this.panelTop.Height = (int)(70 * dpiScaleFactor);

            // Scale splitter width
            this.splitContainer1.SplitterWidth = (int)(16 * dpiScaleFactor);
            this.splitContainerBrowser.SplitterWidth = (int)(16 * dpiScaleFactor);
        }

        private void TableNotepadWindows_SortRows(object sender, AntdUI.IntEventArgs e)
        {
            int columnIndex = e.Value;
            if (columnIndex < 0 || tableNotepadWindows.Columns.Count <= columnIndex) return;

            var column = tableNotepadWindows.Columns[columnIndex];
            bool ascending = column.SortMode == AntdUI.SortMode.ASC;

            // Track sort state for persistence
            sortColumnIndex = columnIndex;
            sortAscending = ascending;

            // Sort based on column key
            IEnumerable<NotepadWindowInfo> sorted;
            switch (column.Key)
            {
                case "ProcessId":
                    sorted = ascending ? notepadWindows.OrderBy(n => n.ProcessId) : notepadWindows.OrderByDescending(n => n.ProcessId);
                    break;
                case "Title":
                    sorted = ascending ? notepadWindows.OrderBy(n => n.Title ?? "", StringComparer.CurrentCultureIgnoreCase) : notepadWindows.OrderByDescending(n => n.Title ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            notepadWindows = sorted.ToList();
            RefreshNotepadTable();
        }

        private void TableBrowserTabs_SortRows(object sender, AntdUI.IntEventArgs e)
        {
            int columnIndex = e.Value;
            if (columnIndex < 0 || tableBrowserTabs.Columns.Count <= columnIndex) return;

            var column = tableBrowserTabs.Columns[columnIndex];
            bool ascending = column.SortMode == AntdUI.SortMode.ASC;

            // Track sort state for persistence
            browserSortColumnIndex = columnIndex;
            browserSortAscending = ascending;

            // Sort based on column key
            IEnumerable<BrowserTabInfo> sorted;
            switch (column.Key)
            {
                case "ProcessId":
                    sorted = ascending ? browserTabs.OrderBy(b => b.ProcessId) : browserTabs.OrderByDescending(b => b.ProcessId);
                    break;
                case "Title":
                    sorted = ascending ? browserTabs.OrderBy(b => b.Title ?? "", StringComparer.CurrentCultureIgnoreCase) : browserTabs.OrderByDescending(b => b.Title ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "Url":
                    sorted = ascending ? browserTabs.OrderBy(b => b.Url ?? "", StringComparer.CurrentCultureIgnoreCase) : browserTabs.OrderByDescending(b => b.Url ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            browserTabs = sorted.ToList();
            RefreshBrowserTable();
        }

        // Sorts the in-memory notepad list and refreshes the table (legacy - used by search)
        private void SortByColumn(int columnIndex)
        {
            string[] columnNames = { "ProcessId", "Title" };
            if (columnIndex < 0 || columnIndex >= columnNames.Length) return;
            var name = columnNames[columnIndex];
            IEnumerable<NotepadWindowInfo> sorted;

            switch (name)
            {
                case "ProcessId":
                    sorted = sortAscending ? notepadWindows.OrderBy(n => n.ProcessId) : notepadWindows.OrderByDescending(n => n.ProcessId);
                    break;
                case "Title":
                    sorted = sortAscending ? notepadWindows.OrderBy(n => n.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase) : notepadWindows.OrderByDescending(n => n.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            notepadWindows = sorted.ToList();
            RefreshNotepadTable();
        }

        // Sorts the browser tabs in-memory list and refreshes the table
        private void SortBrowserTabsByColumn(int columnIndex)
        {
            string[] columnNames = { "ProcessId", "Title", "Url" };
            if (columnIndex < 0 || columnIndex >= columnNames.Length) return;
            var name = columnNames[columnIndex];
            IEnumerable<BrowserTabInfo> sorted;

            switch (name)
            {
                case "ProcessId":
                    sorted = browserSortAscending ? browserTabs.OrderBy(b => b.ProcessId) : browserTabs.OrderByDescending(b => b.ProcessId);
                    break;
                case "Title":
                    sorted = browserSortAscending ? browserTabs.OrderBy(b => b.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase) : browserTabs.OrderByDescending(b => b.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "Url":
                    sorted = browserSortAscending ? browserTabs.OrderBy(b => b.Url ?? string.Empty, StringComparer.CurrentCultureIgnoreCase) : browserTabs.OrderByDescending(b => b.Url ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            browserTabs = sorted.ToList();
            RefreshBrowserTable();
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new AppSettings
                {
                    NotepadSortColumn = sortColumnIndex,
                    NotepadSortAscending = sortAscending,
                    BrowserSortColumn = browserSortColumnIndex,
                    BrowserSortAscending = browserSortAscending
                };

                string json = System.Text.Json.JsonSerializer.Serialize(settings);
                File.WriteAllText(settingsFile, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving settings: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsFile)) return;

                string json = File.ReadAllText(settingsFile);
                var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
                if (settings == null) return;

                sortColumnIndex = settings.NotepadSortColumn;
                sortAscending = settings.NotepadSortAscending;
                browserSortColumnIndex = settings.BrowserSortColumn;
                browserSortAscending = settings.BrowserSortAscending;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        private void ApplySavedSortOrder()
        {
            // Apply notepad table sort if saved
            if (sortColumnIndex >= 0 && sortColumnIndex < tableNotepadWindows?.Columns.Count)
            {
                var col = tableNotepadWindows.Columns[sortColumnIndex];
                col.SortMode = sortAscending ? AntdUI.SortMode.ASC : AntdUI.SortMode.DESC;
                SortByColumn(sortColumnIndex);
            }

            // Apply browser table sort if saved
            if (browserSortColumnIndex >= 0 && browserSortColumnIndex < tableBrowserTabs?.Columns.Count)
            {
                var col = tableBrowserTabs.Columns[browserSortColumnIndex];
                col.SortMode = browserSortAscending ? AntdUI.SortMode.ASC : AntdUI.SortMode.DESC;
                SortBrowserTabsByColumn(browserSortColumnIndex);
            }
        }

        // Minimal InitializeComponent - creates controls used by the class.
        private void InitializeComponent()
        {
            this.refresh = new AntdUI.Button();
            this.btnFontIncrease = new AntdUI.Button();
            this.btnFontDecrease = new AntdUI.Button();
            this.textBoxSearch = new AntdUI.Input();
            this.chkSearchContent = new AntdUI.Checkbox();
            this.splitContainer1 = new SplitContainer();
            this.textBoxNotepadContent = new RichTextBox();
            this.panelTop = new AntdUI.Panel();
            this.panelNotepadPreview = new AntdUI.Panel();
            this.panelBrowserPreview = new AntdUI.Panel();
            this.tabs = new AntdUI.Tabs();
            this.splitContainerBrowser = new SplitContainer();
            this.textBoxBrowserContent = new RichTextBox();
            this.panelStatus = new AntdUI.Panel();
            this.labelStatus = new AntdUI.Label();

            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerBrowser)).BeginInit();
            this.splitContainerBrowser.Panel2.SuspendLayout();
            this.splitContainerBrowser.SuspendLayout();
            this.panelTop.SuspendLayout();

            // refresh - styled AntdUI button with icon
            this.refresh.Dock = DockStyle.Left;
            this.refresh.Size = new Size(150, 70);
            this.refresh.Text = "Refresh";
            this.refresh.Type = AntdUI.TTypeMini.Primary;
            this.refresh.Radius = 6;
            this.refresh.IconSvg = SvgSync;
            this.refresh.Click += new EventHandler(this.btnRefresh_Click);

            // btnFontIncrease - styled AntdUI button with icon
            this.btnFontIncrease.Dock = DockStyle.Right;
            this.btnFontIncrease.Size = new Size(130, 70);
            this.btnFontIncrease.Text = "Zoom +";
            this.btnFontIncrease.Radius = 6;
            this.btnFontIncrease.IconSvg = SvgZoomIn;
            this.btnFontIncrease.Click += new EventHandler(this.btnFontIncrease_Click);

            // btnFontDecrease - styled AntdUI button with icon
            this.btnFontDecrease.Dock = DockStyle.Right;
            this.btnFontDecrease.Size = new Size(130, 70);
            this.btnFontDecrease.Text = "Zoom -";
            this.btnFontDecrease.Radius = 6;
            this.btnFontDecrease.IconSvg = SvgZoomOut;
            this.btnFontDecrease.Click += new EventHandler(this.btnFontDecrease_Click);

            // textBoxSearch - styled input with rounded corners and search icon
            this.textBoxSearch.Dock = DockStyle.Fill;
            this.textBoxSearch.PlaceholderText = "Search titles...";
            this.textBoxSearch.AllowClear = true;
            this.textBoxSearch.Radius = 6;
            this.textBoxSearch.PrefixSvg = SvgSearch;
            this.textBoxSearch.TextChanged += new EventHandler(this.TextBoxSearch_TextChanged);

            // chkSearchContent
            this.chkSearchContent.Dock = DockStyle.Right;
            this.chkSearchContent.Text = "Search content";
            this.chkSearchContent.AutoSize = true;
            this.chkSearchContent.CheckedChanged += (s, e) =>
            {
                this.textBoxSearch.PlaceholderText = chkSearchContent.Checked ? "Search titles and content..." : "Search titles...";
                if (!string.IsNullOrWhiteSpace(textBoxSearch.Text))
                {
                    searchDebounceTimer.Stop();
                    searchDebounceTimer.Start();
                }
            };

            // panelNotepadPreview - wraps the preview textbox with shadow and rounded corners
            this.panelNotepadPreview.Dock = DockStyle.Fill;
            this.panelNotepadPreview.Shadow = 4;
            this.panelNotepadPreview.Radius = 6;
            this.panelNotepadPreview.Padding = new Padding(8);
            this.panelNotepadPreview.Back = Color.White;
            this.panelNotepadPreview.Controls.Add(this.textBoxNotepadContent);

            // panelBrowserPreview - wraps the browser preview textbox with shadow and rounded corners
            this.panelBrowserPreview.Dock = DockStyle.Fill;
            this.panelBrowserPreview.Shadow = 4;
            this.panelBrowserPreview.Radius = 6;
            this.panelBrowserPreview.Padding = new Padding(8);
            this.panelBrowserPreview.Back = Color.White;
            this.panelBrowserPreview.Controls.Add(this.textBoxBrowserContent);

            // splitContainer1 (Notepads tab)
            this.splitContainer1.Dock = DockStyle.Fill;
            this.splitContainer1.Size = new Size(850, 437);
            this.splitContainer1.SplitterDistance = 350;
            this.splitContainer1.SplitterWidth = 16;
            this.splitContainer1.BorderStyle = BorderStyle.None;
            this.splitContainer1.Panel2.Controls.Add(this.panelNotepadPreview);

            // textBoxNotepadContent
            this.textBoxNotepadContent.Dock = DockStyle.Fill;
            this.textBoxNotepadContent.ReadOnly = true;
            this.textBoxNotepadContent.ScrollBars = RichTextBoxScrollBars.Vertical;
            this.textBoxNotepadContent.BorderStyle = BorderStyle.None;

            // splitContainerBrowser (Browser Tabs tab)
            this.splitContainerBrowser.Dock = DockStyle.Fill;
            this.splitContainerBrowser.Size = new Size(850, 437);
            this.splitContainerBrowser.SplitterDistance = 350;
            this.splitContainerBrowser.SplitterWidth = 16;
            this.splitContainerBrowser.BorderStyle = BorderStyle.None;
            this.splitContainerBrowser.Panel2.Controls.Add(this.panelBrowserPreview);

            // textBoxBrowserContent
            this.textBoxBrowserContent.Dock = DockStyle.Fill;
            this.textBoxBrowserContent.ReadOnly = true;
            this.textBoxBrowserContent.ScrollBars = RichTextBoxScrollBars.Vertical;
            this.textBoxBrowserContent.BorderStyle = BorderStyle.None;
            this.textBoxBrowserContent.Text = "Browser tab preview will appear here...";

            // tabs - AntdUI card-style tabs
            this.tabs.Dock = DockStyle.Fill;
            this.tabs.Type = AntdUI.TabType.Card;

            // Create tab pages using AntdUI.TabPage with icons
            var tabNotepads = new AntdUI.TabPage();
            tabNotepads.Text = "Notepads";
            tabNotepads.IconSvg = SvgFile;
            tabNotepads.Controls.Add(this.splitContainer1);

            var tabBrowser = new AntdUI.TabPage();
            tabBrowser.Text = "Browser Tabs";
            tabBrowser.IconSvg = SvgGlobal;
            tabBrowser.Controls.Add(this.splitContainerBrowser);

            this.tabs.Pages.Add(tabNotepads);
            this.tabs.Pages.Add(tabBrowser);
            this.tabs.SelectedIndexChanged += (s, e) => UpdateStatus();

            // panelTop - styled toolbar with shadow
            this.panelTop.Dock = DockStyle.Top;
            this.panelTop.Size = new Size(850, 70);
            this.panelTop.Shadow = 2;
            this.panelTop.Radius = 0;
            this.panelTop.Padding = new Padding(8);
            this.panelTop.Back = Color.FromArgb(250, 250, 250);
            this.panelTop.Controls.Add(this.textBoxSearch);
            this.panelTop.Controls.Add(this.chkSearchContent);
            this.panelTop.Controls.Add(this.btnFontDecrease);
            this.panelTop.Controls.Add(this.btnFontIncrease);
            this.panelTop.Controls.Add(this.refresh);

            // allow drag by panelTop
            this.panelTop.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessageInt(this.Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero); }
            };

            // labelStatus - inside panel for text display
            this.labelStatus.Dock = DockStyle.Fill;
            this.labelStatus.Text = "Ready";
            this.labelStatus.Padding = new Padding(16, 0, 0, 0);
            this.labelStatus.ForeColor = Color.White;
            this.labelStatus.BackColor = Color.FromArgb(64, 169, 255);

            // panelStatus - styled status bar at bottom with light blue background
            this.panelStatus.Dock = DockStyle.Bottom;
            this.panelStatus.Height = 64;
            this.panelStatus.Back = Color.FromArgb(64, 169, 255);
            this.panelStatus.Radius = 0;
            this.panelStatus.Padding = new Padding(0);
            this.panelStatus.Controls.Add(this.labelStatus);

            // Form - WinForms docking: last added = first processed
            // So add Fill first, then edge-docked controls
            this.ClientSize = new Size(850, 577);
            this.Controls.Add(this.tabs);         // Fill - processed last
            this.Controls.Add(this.panelStatus);  // Bottom - processed second
            this.Controls.Add(this.panelTop);     // Top - processed first
            this.Text = "Notepad Manager";
            this.BackColor = Color.FromArgb(245, 245, 245);
            this.Load += new EventHandler(this.NotepadsManager_Load);

            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.splitContainerBrowser.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerBrowser)).EndInit();
            this.splitContainerBrowser.ResumeLayout(false);
            this.panelTop.ResumeLayout(false);
        }

        private void ClearSearchBox(object sender, EventArgs e) { textBoxSearch.Text = string.Empty; }
    }
}