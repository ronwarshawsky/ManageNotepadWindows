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
using System.Net.Http;
using System.Text.Json;
using System.Management;
using AntdUI;
using Microsoft.Win32;
using Microsoft.Data.Sqlite;

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

        // Sorting state for Shells grid
        private int shellSortColumnIndex = -1;
        private bool shellSortAscending = true;

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

        // Browser tabs
        private AntdUI.Table tableBrowserTabs;
        private List<BrowserTabInfo> browserTabs = new List<BrowserTabInfo>();
        private RichTextBox textBoxBrowserContent;
        private SplitContainer splitContainerBrowser;

        // Shell windows (cmd, PowerShell, bash, etc.)
        private AntdUI.Table tableShells;
        private List<ShellWindowInfo> shellWindows = new List<ShellWindowInfo>();
        private RichTextBox textBoxShellContent;
        private SplitContainer splitContainerShells;
        private AntdUI.Panel panelShellPreview;

        // Browser History
        private AntdUI.Table tableHistory;
        private List<BrowserHistoryInfo> browserHistory = new List<BrowserHistoryInfo>();
        private SplitContainer splitContainerHistory;
        private RichTextBox textBoxHistoryDetails;
        private int selectedHistoryIndex = -1;
        private int historySortColumnIndex = -1;
        private bool historySortAscending = true;
        // private System.Windows.Forms.ToolTip historyToolTip; // Disabled for now
        private System.Windows.Forms.ContextMenuStrip historyContextMenu;

        // History table column widths (single source of truth)
        private const int HistoryColBrowserWidth = 70;
        private const int HistoryColLastVisitWidth = 130;

        // Notepad Tab Cache (Win11 TabState)
        private AntdUI.Table tableTabState;
        private List<TabStateInfo> tabStateFiles = new List<TabStateInfo>();
        private SplitContainer splitContainerTabState;
        private RichTextBox textBoxTabStateContent;
        private int selectedTabStateIndex = -1;

        // Status bar
        private AntdUI.Panel panelStatus;
        private AntdUI.Label labelStatus;
        private int totalNotepadCount = 0;
        private int totalBrowserCount = 0;
        private int totalShellCount = 0;
        private int totalHistoryCount = 0;
        private int totalTabStateCount = 0;

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
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        // Virtual key codes for keyboard simulation
        private const byte VK_CONTROL = 0x11;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_A = 0x41;
        private const byte VK_C = 0x43;
        private const byte VK_ESCAPE = 0x1B;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Console API for reading shell buffer
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ReadConsoleOutputCharacter(IntPtr hConsoleOutput, StringBuilder lpCharacter, uint nLength, COORD dwReadCoord, out uint lpNumberOfCharsRead);

        private const int STD_OUTPUT_HANDLE = -11;

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
            public COORD(short x, short y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SMALL_RECT
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CONSOLE_SCREEN_BUFFER_INFO
        {
            public COORD dwSize;
            public COORD dwCursorPosition;
            public short wAttributes;
            public SMALL_RECT srWindow;
            public COORD dwMaximumWindowSize;
        }

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
        private const string SvgTerminal = "<svg viewBox=\"0 0 1024 1024\"><path d=\"M928 140H96c-17.7 0-32 14.3-32 32v680c0 17.7 14.3 32 32 32h832c17.7 0 32-14.3 32-32V172c0-17.7-14.3-32-32-32zm-40 632H136V212h752v560zM304 460l152 120-152 120v-92H200v-56h104v-92zm216 180h200v56H520v-56z\"/></svg>";
        private const string SvgHistory = "<svg viewBox=\"0 0 1024 1024\"><path d=\"M536 128c-221 0-400 179-400 400s179 400 400 400 400-179 400-400-179-400-400-400zm0 720c-176.7 0-320-143.3-320-320s143.3-320 320-320 320 143.3 320 320-143.3 320-320 320zm62-320h158c4.4 0 8-3.6 8-8v-48c0-4.4-3.6-8-8-8H560V296c0-4.4-3.6-8-8-8h-48c-4.4 0-8 3.6-8 8v232c0 4.4 3.6 8 8 8h94z\"/></svg>";

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
            public long MemoryBytes { get; set; }
            public string MemoryFormatted
            {
                get
                {
                    if (MemoryBytes <= 0) return "";
                    double mb = MemoryBytes / (1024.0 * 1024.0);
                    if (mb >= 1024)
                        return $"{mb / 1024:F1} GB";
                    return $"{mb:F0} MB";
                }
            }
        }

        private class ShellWindowInfo : AntdUI.NotifyProperty
        {
            public IntPtr Hwnd { get; set; }
            public int ProcessId { get; set; }
            public string Title { get; set; }
            public string ShellType { get; set; }  // cmd, PowerShell, Bash, etc.
            public string ProcessName { get; set; }
        }

        private class BrowserHistoryInfo : AntdUI.NotifyProperty
        {
            public string Title { get; set; }
            public string Url { get; set; }
            public DateTime LastVisit { get; set; }
            public int VisitCount { get; set; }
            public string Browser { get; set; }  // Chrome, Edge, Firefox
            public string LastVisitFormatted => LastVisit.ToString("yyyy-MM-dd HH:mm");
        }

        private class TabStateInfo : AntdUI.NotifyProperty
        {
            public string FileName { get; set; }
            public string FilePath { get; set; }
            public string Content { get; set; }
            public int ContentLength { get; set; }
            public DateTime LastModified { get; set; }
            public string Preview { get; set; }
            public string LastModifiedFormatted => LastModified.ToString("MM/dd/yy HH:mm");
        }

        // Settings for persistence
        private class AppSettings
        {
            public int NotepadSortColumn { get; set; } = -1;
            public bool NotepadSortAscending { get; set; } = true;
            public int BrowserSortColumn { get; set; } = -1;
            public bool BrowserSortAscending { get; set; } = true;
            public int ShellSortColumn { get; set; } = -1;
            public bool ShellSortAscending { get; set; } = true;
            public int HistorySortColumn { get; set; } = -1;
            public bool HistorySortAscending { get; set; } = true;
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
            SetupShellsGrid();
            SetupHistoryGrid();
            SetupTabStateGrid();
            MakeWindowTopMost();
            // Load event is registered in InitializeComponent
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
                tableBrowserTabs.Columns.Add(new AntdUI.Column("ProcessId", "PID") { Width = "70", SortOrder = true });
                tableBrowserTabs.Columns.Add(new AntdUI.Column("MemoryFormatted", "Memory") { Width = "80", SortOrder = true });
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

        private void SetupShellsGrid()
        {
            if (tableShells == null)
            {
                tableShells = new AntdUI.Table
                {
                    Dock = DockStyle.Fill,
                    Radius = 6,
                    FixedHeader = true,
                    EnableHeaderResizing = true,
                    RowSelectedBg = Color.FromArgb(230, 247, 255),
                    RowSelectedFore = Color.Black,
                    BorderColor = Color.FromArgb(217, 217, 217),
                    EmptyText = "No shell windows found"
                };

                // Define columns with sorting enabled
                tableShells.Columns.Add(new AntdUI.Column("ProcessId", "PID") { Width = "80", SortOrder = true });
                tableShells.Columns.Add(new AntdUI.Column("ShellType", "Type") { Width = "120", SortOrder = true });
                tableShells.Columns.Add(new AntdUI.Column("Title", "Title") { SortOrder = true });

                // Event handlers
                tableShells.CellClick += TableShells_CellClick;
                tableShells.CellDoubleClick += TableShells_CellDoubleClick;
                tableShells.SelectIndexChanged += TableShells_SelectIndexChanged;
                tableShells.SortRows += TableShells_SortRows;
            }

            if (splitContainerShells != null)
            {
                splitContainerShells.Panel1.Controls.Clear();
                splitContainerShells.Panel1.Controls.Add(tableShells);
            }
        }

        private void SetupHistoryGrid()
        {
            if (tableHistory == null)
            {
                tableHistory = new AntdUI.Table
                {
                    Dock = DockStyle.Fill,
                    Radius = 6,
                    FixedHeader = true,
                    EnableHeaderResizing = true,
                    RowSelectedBg = Color.FromArgb(230, 247, 255),
                    RowSelectedFore = Color.Black,
                    BorderColor = Color.FromArgb(217, 217, 217),
                    EmptyText = "No browser history found"
                };

                // Define columns with sorting enabled
                tableHistory.Columns.Add(new AntdUI.Column("Browser", "Browser") { Width = HistoryColBrowserWidth.ToString(), SortOrder = true });
                tableHistory.Columns.Add(new AntdUI.Column("LastVisitFormatted", "Last Visit") { Width = HistoryColLastVisitWidth.ToString(), SortOrder = true });
                tableHistory.Columns.Add(new AntdUI.Column("Title", "Title") { Width = "fill", SortOrder = true, Ellipsis = true });
                tableHistory.Columns.Add(new AntdUI.Column("Url", "URL") { Width = "fill", SortOrder = true, Ellipsis = true });

                // Event handlers
                tableHistory.CellClick += TableHistory_CellClick;
                tableHistory.CellDoubleClick += TableHistory_CellDoubleClick;
                tableHistory.SelectIndexChanged += TableHistory_SelectIndexChanged;
                tableHistory.SortRows += TableHistory_SortRows;

                // Tooltip for Title and URL columns - disabled for now
                // historyToolTip = new System.Windows.Forms.ToolTip();
                // historyToolTip.AutoPopDelay = 10000;
                // historyToolTip.InitialDelay = 500;
                // historyToolTip.ReshowDelay = 200;
                // historyToolTip.ShowAlways = true;
                // tableHistory.MouseMove += TableHistory_MouseMove;
                // tableHistory.MouseLeave += (s, e) => HideHistoryTooltip();

                // Context menu for right-click
                historyContextMenu = new System.Windows.Forms.ContextMenuStrip();
                var deleteMenuItem = new System.Windows.Forms.ToolStripMenuItem("Delete from history...");
                deleteMenuItem.Click += HistoryDeleteMenuItem_Click;
                historyContextMenu.Items.Add(deleteMenuItem);
                tableHistory.MouseUp += TableHistory_MouseUp;
            }

            if (splitContainerHistory != null)
            {
                splitContainerHistory.Panel1.Controls.Clear();
                splitContainerHistory.Panel1.Controls.Add(tableHistory);
            }
        }

        private void SetupTabStateGrid()
        {
            if (tableTabState == null)
            {
                tableTabState = new AntdUI.Table
                {
                    Dock = DockStyle.Fill,
                    Radius = 6,
                    FixedHeader = true,
                    EnableHeaderResizing = true,
                    RowSelectedBg = Color.FromArgb(230, 247, 255),
                    RowSelectedFore = Color.Black,
                    BorderColor = Color.FromArgb(217, 217, 217),
                    EmptyText = "No cached Notepad tabs found"
                };
                tableTabState.Columns.Add(new AntdUI.Column("LastModifiedFormatted", "Modified") { Width = "120", SortOrder = true });
                tableTabState.Columns.Add(new AntdUI.Column("ContentLength", "Chars") { Width = "60", SortOrder = true });
                tableTabState.Columns.Add(new AntdUI.Column("Preview", "Content Preview") { Width = "fill", Ellipsis = true });
                tableTabState.CellClick += TableTabState_CellClick;
                tableTabState.CellDoubleClick += TableTabState_CellDoubleClick;
                tableTabState.SelectIndexChanged += TableTabState_SelectIndexChanged;
            }

            if (splitContainerTabState != null)
            {
                splitContainerTabState.Panel1.Controls.Clear();
                splitContainerTabState.Panel1.Controls.Add(tableTabState);
            }
        }

        private void PopulateTabState()
        {
            var tabStateDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", "Microsoft.WindowsNotepad_8wekyb3d8bbwe", "LocalState", "TabState");

            tabStateFiles.Clear();
            if (!Directory.Exists(tabStateDir)) return;

            var files = Directory.GetFiles(tabStateDir, "*.bin")
                .Where(f => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f), @"\.\d+\.bin(\.tmp)?$"))
                .OrderByDescending(File.GetLastWriteTime);

            foreach (var file in files)
            {
                var info = ParseTabStateFile(file);
                if (info != null) tabStateFiles.Add(info);
            }
            totalTabStateCount = tabStateFiles.Count;
        }

        private static TabStateInfo ParseTabStateFile(string path)
        {
            try
            {
                byte[] bytes;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytes = new byte[fs.Length];
                    fs.ReadExactly(bytes, 0, bytes.Length);
                }

                if (bytes.Length < 6 || bytes[0] != 0x4E || bytes[1] != 0x50) return null;
                if (bytes[3] != 0x00) return null; // saved file — content is on disk

                // Find the fixed content marker 03 01 01 01
                int marker = -1;
                for (int i = 4; i < bytes.Length - 4; i++)
                    if (bytes[i] == 0x03 && bytes[i + 1] == 0x01 && bytes[i + 2] == 0x01 && bytes[i + 3] == 0x01) { marker = i; break; }
                if (marker < 0) return null;

                int pos = marker + 4;
                int contentLen = ReadTabStateVarint(bytes, ref pos);
                if (contentLen <= 0 || pos + contentLen * 2 > bytes.Length) return null;

                string content = Encoding.Unicode.GetString(bytes, pos, contentLen * 2);
                string preview = content.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
                if (preview.Length > 150) preview = preview.Substring(0, 150) + "…";

                return new TabStateInfo
                {
                    FileName = Path.GetFileNameWithoutExtension(path),
                    FilePath = path,
                    Content = content,
                    ContentLength = contentLen,
                    LastModified = File.GetLastWriteTime(path),
                    Preview = preview
                };
            }
            catch { return null; }
        }

        private static int ReadTabStateVarint(byte[] bytes, ref int pos)
        {
            int val = 0, shift = 0;
            byte b;
            do { b = bytes[pos++]; val |= (b & 0x7F) << shift; shift += 7; }
            while ((b & 0x80) != 0 && pos < bytes.Length);
            return val;
        }

        private void RefreshTabStateTable()
        {
            if (tableTabState == null) return;
            tableTabState.DataSource = null;
            tableTabState.DataSource = new List<TabStateInfo>(tabStateFiles);
        }

        private void TableTabState_SelectIndexChanged(object sender, EventArgs e)
        {
            int idx = tableTabState?.SelectedIndex ?? -1;
            if (idx > 0)
            {
                selectedTabStateIndex = idx - 1;
                if (selectedTabStateIndex < tabStateFiles.Count)
                    textBoxTabStateContent.Text = tabStateFiles[selectedTabStateIndex].Content ?? string.Empty;
            }
        }

        private void TableTabState_CellClick(object sender, AntdUI.TableClickEventArgs e) { }

        private void TableTabState_CellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
        {
            if (e.Record is TabStateInfo item) RestoreTabState(item);
        }

        private void RestoreTabState(TabStateInfo item)
        {
            if (string.IsNullOrWhiteSpace(item?.Content)) return;
            try
            {
                var notepad = Process.Start("notepad.exe");
                if (notepad == null || !notepad.WaitForInputIdle(5000)) return;
                IntPtr hwnd = IntPtr.Zero;
                for (int i = 0; i < 10 && hwnd == IntPtr.Zero; i++)
                {
                    System.Threading.Thread.Sleep(200);
                    notepad.Refresh();
                    hwnd = notepad.MainWindowHandle;
                }
                if (hwnd != IntPtr.Zero) TrySetNotepadText(hwnd, item.Content);
            }
            catch (Exception ex) { Debug.WriteLine($"RestoreTabState error: {ex.Message}"); }
        }

        // Track selected row indices for AntdUI.Table
        private int selectedNotepadIndex = -1;
        private int selectedBrowserIndex = -1;
        private int selectedShellIndex = -1;

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

        private async void LoadBrowserPreview(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= browserTabs.Count) { textBoxBrowserContent.Text = string.Empty; return; }
            var info = browserTabs[rowIndex];

            // Display tab info header
            var sb = new StringBuilder();
            sb.AppendLine($"Title: {info.Title}");
            sb.AppendLine($"URL: {info.Url ?? "Unknown"}");
            sb.AppendLine($"PID: {info.ProcessId}");
            sb.AppendLine($"Window Handle: 0x{info.Hwnd.ToString("X")}");
            sb.AppendLine();
            sb.AppendLine("--- Page Content (loading...) ---");

            textBoxBrowserContent.Text = sb.ToString();

            // Try to read browser content via clipboard
            string pageContent = await System.Threading.Tasks.Task.Run(() => TryReadBrowserViaClipboard(info.Hwnd));

            // Verify selection hasn't changed while loading
            if (selectedBrowserIndex == rowIndex)
            {
                sb.Clear();
                sb.AppendLine($"Title: {info.Title}");
                sb.AppendLine($"URL: {info.Url ?? "Unknown"}");
                sb.AppendLine($"PID: {info.ProcessId}");
                sb.AppendLine($"Window Handle: 0x{info.Hwnd.ToString("X")}");
                sb.AppendLine();
                sb.AppendLine("--- Page Content ---");
                sb.AppendLine(pageContent);
                textBoxBrowserContent.Text = sb.ToString();
            }
        }

        private string TryReadBrowserViaClipboard(IntPtr hwnd)
        {
            try
            {
                string originalClipboard = null;
                string browserContent = null;

                // Capture our window handle on the UI thread
                IntPtr ourWindow = IntPtr.Zero;
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() => ourWindow = this.Handle));
                }
                else
                {
                    ourWindow = this.Handle;
                }

                // Must run on STA thread for clipboard operations
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        // Save original clipboard content
                        if (System.Windows.Forms.Clipboard.ContainsText())
                        {
                            originalClipboard = System.Windows.Forms.Clipboard.GetText();
                        }

                        // Clear clipboard to detect if copy worked
                        System.Windows.Forms.Clipboard.Clear();

                        // Activate browser window - needs real focus for Ctrl+A/C to work
                        ShowWindow(hwnd, SW_RESTORE);
                        SetForegroundWindow(hwnd);
                        System.Threading.Thread.Sleep(150); // Give browser time to activate

                        // Send Ctrl+A using keybd_event (more reliable than SendKeys)
                        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_A, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_A, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(150);

                        // Send Ctrl+C using keybd_event
                        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_C, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                        System.Threading.Thread.Sleep(150);

                        // Press Escape to deselect
                        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
                        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                        // Restore focus to our window
                        System.Threading.Thread.Sleep(50);
                        SetForegroundWindow(ourWindow);

                        // Read clipboard content
                        System.Threading.Thread.Sleep(50);
                        if (System.Windows.Forms.Clipboard.ContainsText())
                        {
                            browserContent = System.Windows.Forms.Clipboard.GetText();
                        }

                        // Restore original clipboard
                        if (originalClipboard != null)
                        {
                            System.Windows.Forms.Clipboard.SetText(originalClipboard);
                        }
                        else
                        {
                            System.Windows.Forms.Clipboard.Clear();
                        }
                    }
                    catch { }
                });

                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join(3000); // Max 3 second timeout for browsers

                if (!string.IsNullOrWhiteSpace(browserContent))
                {
                    // Limit to last 50 lines to avoid huge content
                    var lines = browserContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    int startIdx = Math.Max(0, lines.Length - 50);
                    var lastLines = new StringBuilder();
                    for (int i = startIdx; i < lines.Length; i++)
                    {
                        lastLines.AppendLine(lines[i]);
                    }
                    return lastLines.ToString().TrimEnd();
                }

                return "[Unable to read browser content]";
            }
            catch (Exception ex)
            {
                return $"[Error: {ex.Message}]";
            }
        }

        private void TableShells_CellClick(object sender, AntdUI.TableClickEventArgs e)
        {
            // Selection change is handled by SelectIndexChanged
        }

        private void TableShells_SelectIndexChanged(object sender, EventArgs e)
        {
            // Single source of truth for selection changes (both click and keyboard)
            int selectedIdx = tableShells?.SelectedIndex ?? -1;
            if (selectedIdx > 0)
            {
                int rowIndex = selectedIdx - 1; // Convert to 0-based
                if (rowIndex >= 0 && rowIndex < shellWindows.Count)
                {
                    selectedShellIndex = rowIndex;
                    LoadShellPreview(rowIndex);
                }
            }
        }

        private void TableShells_CellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
        {
            // Use Record directly - no index conversion needed
            if (e.Record is not ShellWindowInfo item) return;
            IntPtr hwnd = item.Hwnd;
            if (hwnd != IntPtr.Zero) { ShowWindow(hwnd, SW_RESTORE); SetForegroundWindow(hwnd); }
        }

        private void TableHistory_CellClick(object sender, AntdUI.TableClickEventArgs e)
        {
            // Selection change is handled by SelectIndexChanged
        }

        private void TableHistory_SelectIndexChanged(object sender, EventArgs e)
        {
            // Single source of truth for selection changes (both click and keyboard)
            int selectedIdx = tableHistory?.SelectedIndex ?? -1;
            if (selectedIdx > 0)
            {
                int rowIndex = selectedIdx - 1; // Convert to 0-based
                if (rowIndex >= 0 && rowIndex < browserHistory.Count)
                {
                    selectedHistoryIndex = rowIndex;
                    LoadHistoryPreview(rowIndex);
                }
            }
        }

        private void TableHistory_CellDoubleClick(object sender, AntdUI.TableClickEventArgs e)
        {
            // Open URL in default browser
            if (e.Record is not BrowserHistoryInfo item) return;
            if (!string.IsNullOrWhiteSpace(item.Url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void LoadHistoryPreview(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= browserHistory.Count) { textBoxHistoryDetails.Text = string.Empty; return; }
            var info = browserHistory[rowIndex];

            var sb = new StringBuilder();
            sb.AppendLine($"Title: {info.Title}");
            sb.AppendLine($"URL: {info.Url}");
            sb.AppendLine($"Browser: {info.Browser}");
            sb.AppendLine($"Last Visit: {info.LastVisit:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Visit Count: {info.VisitCount}");
            sb.AppendLine();
            sb.AppendLine("Double-click to open in browser");

            textBoxHistoryDetails.Text = sb.ToString();
        }

        // Tooltip tracking - disabled for now
        // private int lastHistoryTooltipRow = -1;
        // private int lastHistoryTooltipCol = -1;
        // private void TableHistory_MouseMove(object sender, MouseEventArgs e) { ... }
        // private void HideHistoryTooltip() { ... }

        private void TableHistory_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                // Calculate row from Y position
                int headerHeight = 42;
                int rowHeight = 42;
                int rowIndex = (e.Y - headerHeight) / rowHeight;

                if (rowIndex >= 0 && rowIndex < browserHistory.Count)
                {
                    // Select the row under cursor
                    tableHistory.SelectedIndex = rowIndex + 1; // 1-based
                    selectedHistoryIndex = rowIndex;
                    LoadHistoryPreview(rowIndex);

                    // Show context menu
                    historyContextMenu.Show(tableHistory, e.Location);
                }
            }
        }

        private void HistoryDeleteMenuItem_Click(object sender, EventArgs e)
        {
            if (selectedHistoryIndex < 0 || selectedHistoryIndex >= browserHistory.Count)
                return;

            var item = browserHistory[selectedHistoryIndex];

            var result = MessageBox.Show(
                $"Delete this entry from browser history?\n\nTitle: {item.Title}\nURL: {item.Url}",
                "Delete History Entry",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                // Delete from database
                bool deleted = DeleteHistoryEntry(item);

                if (deleted)
                {
                    // Remove from list and refresh
                    browserHistory.RemoveAt(selectedHistoryIndex);
                    totalHistoryCount = browserHistory.Count;
                    RefreshHistoryTable();
                    UpdateStatus();

                    // Clear preview
                    textBoxHistoryDetails.Text = "Entry deleted.";
                    selectedHistoryIndex = -1;
                }
                else
                {
                    MessageBox.Show("Failed to delete entry from browser history.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private bool DeleteHistoryEntry(BrowserHistoryInfo item)
        {
            try
            {
                string historyPath = item.Browser == "Chrome"
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data\Default\History")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data\Default\History");

                if (!File.Exists(historyPath)) return false;

                // Copy to temp file (database is locked while browser is running)
                string tempPath = Path.Combine(Path.GetTempPath(), $"history_delete_{Guid.NewGuid()}.db");
                File.Copy(historyPath, tempPath, true);

                try
                {
                    using (var connection = new SqliteConnection($"Data Source={tempPath}"))
                    {
                        connection.Open();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "DELETE FROM urls WHERE url = @url";
                            command.Parameters.AddWithValue("@url", item.Url);
                            int rowsAffected = command.ExecuteNonQuery();

                            if (rowsAffected > 0)
                            {
                                // Copy back to original (browser must be closed for this to work)
                                try
                                {
                                    File.Copy(tempPath, historyPath, true);
                                    return true;
                                }
                                catch
                                {
                                    // Browser is probably running - can't update the file
                                    MessageBox.Show(
                                        $"Please close {item.Browser} and try again.\nThe browser's history file is locked.",
                                        "Browser Running",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Warning);
                                    return false;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting history entry: {ex.Message}");
                return false;
            }
        }

        private string WrapTooltipText(string text, int maxLineLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLineLength)
                return text;

            var sb = new StringBuilder();
            int pos = 0;
            while (pos < text.Length)
            {
                int len = Math.Min(maxLineLength, text.Length - pos);
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(text.Substring(pos, len));
                pos += len;
            }
            return sb.ToString();
        }

        private async void LoadShellPreview(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= shellWindows.Count) { textBoxShellContent.Text = string.Empty; return; }
            var info = shellWindows[rowIndex];

            // Display shell info header
            var sb = new StringBuilder();
            sb.AppendLine($"Title: {info.Title}");
            sb.AppendLine($"Type: {info.ShellType}");
            sb.AppendLine($"Process: {info.ProcessName}");
            sb.AppendLine($"PID: {info.ProcessId}");
            sb.AppendLine($"Window Handle: 0x{info.Hwnd.ToString("X")}");
            sb.AppendLine();
            sb.AppendLine("--- Console Buffer (last 20 lines) ---");
            sb.AppendLine("Loading...");

            textBoxShellContent.Text = sb.ToString();

            // Try to read console buffer asynchronously
            string bufferContent = await System.Threading.Tasks.Task.Run(() => ReadConsoleBuffer(info.ProcessId, info.Hwnd, info.ProcessName));

            // Verify selection hasn't changed while loading
            if (selectedShellIndex == rowIndex)
            {
                sb.Clear();
                sb.AppendLine($"Title: {info.Title}");
                sb.AppendLine($"Type: {info.ShellType}");
                sb.AppendLine($"Process: {info.ProcessName}");
                sb.AppendLine($"PID: {info.ProcessId}");
                sb.AppendLine($"Window Handle: 0x{info.Hwnd.ToString("X")}");
                sb.AppendLine();
                sb.AppendLine("--- Console Buffer (last 20 lines) ---");
                sb.AppendLine(bufferContent);
                textBoxShellContent.Text = sb.ToString();
            }
        }

        private string ReadConsoleBuffer(int processId, IntPtr hwnd, string processName)
        {
            // For Windows Terminal, skip directly to clipboard approach (Console API and UIA don't work)
            if (processName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
            {
                return TryReadTerminalViaClipboard(hwnd);
            }

            // First try classic Console API (works for cmd.exe, powershell.exe with conhost)
            string classicResult = TryReadClassicConsole(processId);
            if (!classicResult.StartsWith("["))
                return classicResult;

            // Fallback to UI Automation (works for some terminals)
            string uiaResult = TryReadTerminalViaUIA(hwnd);
            if (!uiaResult.StartsWith("["))
                return uiaResult;

            // Last resort: clipboard approach
            string clipboardResult = TryReadTerminalViaClipboard(hwnd);
            if (!clipboardResult.StartsWith("["))
                return clipboardResult;

            return "[Unable to read terminal content]";
        }

        private string TryReadClassicConsole(int processId)
        {
            try
            {
                // First, detach from any existing console
                FreeConsole();

                // Try to attach to the target process's console
                if (!AttachConsole((uint)processId))
                {
                    int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    if (error == 5) // Access denied
                        return "[Access denied - process may be elevated]";
                    if (error == 6) // Invalid handle
                        return "[No console attached to this process]";
                    if (error == 31) // Device not functioning
                        return "[Console not available - may be Windows Terminal]";
                    return $"[Unable to attach to console: error {error}]";
                }

                try
                {
                    IntPtr hConsole = GetStdHandle(STD_OUTPUT_HANDLE);
                    if (hConsole == IntPtr.Zero || hConsole == new IntPtr(-1))
                    {
                        return "[Unable to get console handle]";
                    }

                    if (!GetConsoleScreenBufferInfo(hConsole, out CONSOLE_SCREEN_BUFFER_INFO csbi))
                    {
                        return "[Unable to get console buffer info]";
                    }

                    int bufferWidth = csbi.dwSize.X;
                    int cursorY = csbi.dwCursorPosition.Y;

                    // Read last 20 lines (or fewer if buffer is smaller)
                    int linesToRead = Math.Min(20, cursorY + 1);
                    int startLine = Math.Max(0, cursorY - linesToRead + 1);

                    var result = new StringBuilder();
                    for (int y = startLine; y <= cursorY; y++)
                    {
                        var lineBuffer = new StringBuilder(bufferWidth);
                        COORD coord = new COORD(0, (short)y);
                        if (ReadConsoleOutputCharacter(hConsole, lineBuffer, (uint)bufferWidth, coord, out uint charsRead))
                        {
                            string line = lineBuffer.ToString(0, (int)Math.Min(charsRead, bufferWidth)).TrimEnd();
                            result.AppendLine(line);
                        }
                    }

                    string content = result.ToString().TrimEnd();
                    return string.IsNullOrWhiteSpace(content) ? "[Console buffer is empty]" : content;
                }
                finally
                {
                    FreeConsole();
                }
            }
            catch (Exception ex)
            {
                return $"[Error reading console: {ex.Message}]";
            }
        }

        private string TryReadTerminalViaUIA(IntPtr hwnd)
        {
            try
            {
                var windowElement = AutomationElement.FromHandle(hwnd);
                if (windowElement == null) return "[UIA: window element null]";

                // Try to find terminal/text control - Windows Terminal uses custom control
                // Look for elements with TextPattern support
                var walker = TreeWalker.ContentViewWalker;
                var textContent = FindTerminalTextRecursive(windowElement, 0);

                if (!string.IsNullOrWhiteSpace(textContent))
                {
                    // Get last 20 lines
                    var lines = textContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    int startIdx = Math.Max(0, lines.Length - 20);
                    var lastLines = new StringBuilder();
                    for (int i = startIdx; i < lines.Length; i++)
                    {
                        lastLines.AppendLine(lines[i]);
                    }
                    return lastLines.ToString().TrimEnd();
                }

                // UIA failed, try clipboard approach for Windows Terminal
                return TryReadTerminalViaClipboard(hwnd);
            }
            catch (Exception ex)
            {
                return $"[UIA error: {ex.Message}]";
            }
        }

        private string TryReadTerminalViaClipboard(IntPtr hwnd)
        {
            try
            {
                string originalClipboard = null;
                string terminalContent = null;

                // Capture our window handle on the calling thread (which may be a background thread)
                // We need to get it from the UI thread
                IntPtr ourWindow = IntPtr.Zero;
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() => ourWindow = this.Handle));
                }
                else
                {
                    ourWindow = this.Handle;
                }

                // Must run on STA thread for clipboard operations
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        // Save original clipboard content
                        if (System.Windows.Forms.Clipboard.ContainsText())
                        {
                            originalClipboard = System.Windows.Forms.Clipboard.GetText();
                        }

                        // Clear clipboard to detect if copy worked
                        System.Windows.Forms.Clipboard.Clear();

                        // Make our window topmost temporarily so terminal doesn't visually come to front
                        SetWindowPos(ourWindow, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

                        // Activate terminal window (it gets focus but stays behind our window visually)
                        IntPtr currentForeground = GetForegroundWindow();
                        SetForegroundWindow(hwnd);
                        System.Threading.Thread.Sleep(30);

                        // Send Ctrl+Shift+A (select all in Windows Terminal) then Ctrl+Shift+C (copy)
                        System.Windows.Forms.SendKeys.SendWait("^+a"); // Ctrl+Shift+A
                        System.Threading.Thread.Sleep(30);
                        System.Windows.Forms.SendKeys.SendWait("^+c"); // Ctrl+Shift+C
                        System.Threading.Thread.Sleep(30);

                        // Send Escape to deselect
                        System.Windows.Forms.SendKeys.SendWait("{ESC}");

                        // Remove topmost flag and restore focus
                        SetWindowPos(ourWindow, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
                        SetForegroundWindow(ourWindow);

                        // Read clipboard content
                        System.Threading.Thread.Sleep(20);
                        if (System.Windows.Forms.Clipboard.ContainsText())
                        {
                            terminalContent = System.Windows.Forms.Clipboard.GetText();
                        }

                        // Restore original clipboard
                        if (originalClipboard != null)
                        {
                            System.Windows.Forms.Clipboard.SetText(originalClipboard);
                        }
                        else
                        {
                            System.Windows.Forms.Clipboard.Clear();
                        }
                    }
                    catch { }
                });

                thread.SetApartmentState(System.Threading.ApartmentState.STA);
                thread.Start();
                thread.Join(2000); // Max 2 second timeout

                if (!string.IsNullOrWhiteSpace(terminalContent))
                {
                    // Get last 30 lines
                    var lines = terminalContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    int startIdx = Math.Max(0, lines.Length - 30);
                    var lastLines = new StringBuilder();
                    for (int i = startIdx; i < lines.Length; i++)
                    {
                        lastLines.AppendLine(lines[i]);
                    }
                    return lastLines.ToString().TrimEnd();
                }

                return "[Unable to read terminal content]";
            }
            catch (Exception ex)
            {
                return $"[Clipboard error: {ex.Message}]";
            }
        }

        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private string FindTerminalTextRecursive(AutomationElement element, int depth)
        {
            if (depth > 10) return null; // Prevent infinite recursion

            try
            {
                // Try TextPattern first (best for terminals)
                object patternObj;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out patternObj))
                {
                    var textPattern = (TextPattern)patternObj;
                    string text = textPattern.DocumentRange.GetText(-1);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }

                // Try ValuePattern
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out patternObj))
                {
                    var valuePattern = (ValuePattern)patternObj;
                    string val = valuePattern.Current.Value;
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }

                // Check element name (sometimes contains text)
                var name = element.Current.Name;
                if (!string.IsNullOrWhiteSpace(name) && name.Length > 50) // Likely terminal content
                    return name;

                // Search children
                var children = element.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
                foreach (AutomationElement child in children)
                {
                    var result = FindTerminalTextRecursive(child, depth + 1);
                    if (!string.IsNullOrWhiteSpace(result)) return result;
                }
            }
            catch { }

            return null;
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

        // Browser process info including command line
        private class BrowserProcessInfo
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; }
            public string CommandLine { get; set; }
            public long MemoryBytes { get; set; }
            public string ProcessType { get; set; } // browser, renderer, gpu, utility, etc.
            public string SiteOrigin { get; set; } // extracted from command line for renderers
        }

        // Cache of browser process info
        private List<BrowserProcessInfo> _browserProcessCache = new List<BrowserProcessInfo>();

        // Get all browser processes with their command lines using WMI
        private void RefreshBrowserProcessInfo()
        {
            _browserProcessCache.Clear();

            try
            {
                // Query WMI for processes with command lines
                string query = "SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE Name LIKE 'chrome%' OR Name LIKE 'msedge%' OR Name LIKE 'brave%' OR Name LIKE 'firefox%'";

                using (var searcher = new ManagementObjectSearcher(query))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject obj in results)
                    {
                        try
                        {
                            int pid = Convert.ToInt32(obj["ProcessId"]);
                            string name = obj["Name"]?.ToString() ?? "";
                            string cmdLine = obj["CommandLine"]?.ToString() ?? "";

                            // Get memory for this process
                            long memory = 0;
                            try
                            {
                                var proc = Process.GetProcessById(pid);
                                memory = proc.WorkingSet64;
                            }
                            catch { }

                            // Determine process type from command line
                            string processType = "browser";
                            string siteOrigin = "";

                            if (cmdLine.Contains("--type=renderer"))
                            {
                                processType = "renderer";
                                // Try to extract site origin from command line
                                siteOrigin = ExtractSiteOrigin(cmdLine);
                            }
                            else if (cmdLine.Contains("--type=gpu"))
                                processType = "gpu";
                            else if (cmdLine.Contains("--type=utility"))
                                processType = "utility";
                            else if (cmdLine.Contains("--type=crashpad"))
                                processType = "crashpad";
                            else if (cmdLine.Contains("--extension-process"))
                                processType = "extension";

                            _browserProcessCache.Add(new BrowserProcessInfo
                            {
                                ProcessId = pid,
                                ProcessName = name.Replace(".exe", ""),
                                CommandLine = cmdLine,
                                MemoryBytes = memory,
                                ProcessType = processType,
                                SiteOrigin = siteOrigin
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        // Extract site origin from renderer command line (e.g., --site-per-process-site=https://example.com)
        private string ExtractSiteOrigin(string cmdLine)
        {
            // Look for site isolation flags
            var patterns = new[]
            {
                "--site-per-process-site=",
                "--isolation-by-site-origin=",
                "--renderer-client-id="
            };

            foreach (var pattern in patterns)
            {
                int idx = cmdLine.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    int start = idx + pattern.Length;
                    int end = cmdLine.IndexOf(' ', start);
                    if (end < 0) end = cmdLine.Length;
                    string value = cmdLine.Substring(start, end - start).Trim('"');

                    // Try to extract domain from URL
                    if (Uri.TryCreate(value, UriKind.Absolute, out Uri uri))
                    {
                        return uri.Host.ToLowerInvariant();
                    }
                    return value.ToLowerInvariant();
                }
            }

            return "";
        }

        // Get per-tab memory by matching tabs to renderer processes
        private void CalculatePerTabMemory()
        {
            // Refresh browser process info
            RefreshBrowserProcessInfo();

            // Get only renderer processes
            var renderers = _browserProcessCache
                .Where(p => p.ProcessType == "renderer")
                .ToList();

            // Group tabs by browser type
            var tabsByBrowser = browserTabs
                .GroupBy(t =>
                {
                    try
                    {
                        var proc = Process.GetProcessById(t.ProcessId);
                        return proc.ProcessName.ToLowerInvariant();
                    }
                    catch { return "unknown"; }
                })
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in tabsByBrowser)
            {
                string browserName = kvp.Key;
                var tabs = kvp.Value;

                // Get renderers for this browser
                var browserRenderers = renderers
                    .Where(r => r.ProcessName.ToLowerInvariant().Contains(browserName) ||
                                browserName.Contains(r.ProcessName.ToLowerInvariant()))
                    .ToList();

                if (browserRenderers.Count == 0)
                {
                    // No renderer info, use main process memory
                    foreach (var tab in tabs)
                    {
                        try
                        {
                            var proc = Process.GetProcessById(tab.ProcessId);
                            tab.MemoryBytes = proc.WorkingSet64;
                        }
                        catch { }
                    }
                    continue;
                }

                // Try to match tabs to renderers by site origin
                var unmatchedTabs = new List<BrowserTabInfo>();
                var usedRenderers = new HashSet<int>();

                foreach (var tab in tabs)
                {
                    bool matched = false;

                    if (!string.IsNullOrEmpty(tab.Url))
                    {
                        // Extract domain from tab URL
                        string tabDomain = "";
                        if (Uri.TryCreate(tab.Url, UriKind.Absolute, out Uri uri))
                        {
                            tabDomain = uri.Host.ToLowerInvariant();
                        }

                        if (!string.IsNullOrEmpty(tabDomain))
                        {
                            // Find a renderer with matching site origin
                            var matchingRenderer = browserRenderers
                                .Where(r => !usedRenderers.Contains(r.ProcessId) &&
                                           !string.IsNullOrEmpty(r.SiteOrigin) &&
                                           (r.SiteOrigin.Contains(tabDomain) || tabDomain.Contains(r.SiteOrigin)))
                                .OrderByDescending(r => r.MemoryBytes)
                                .FirstOrDefault();

                            if (matchingRenderer != null)
                            {
                                tab.MemoryBytes = matchingRenderer.MemoryBytes;
                                usedRenderers.Add(matchingRenderer.ProcessId);
                                matched = true;
                            }
                        }
                    }

                    if (!matched)
                    {
                        unmatchedTabs.Add(tab);
                    }
                }

                // For unmatched tabs, distribute remaining renderer memory
                var unusedRenderers = browserRenderers
                    .Where(r => !usedRenderers.Contains(r.ProcessId))
                    .ToList();

                if (unmatchedTabs.Count > 0 && unusedRenderers.Count > 0)
                {
                    // Sort both by some criteria and assign
                    long avgMemory = unusedRenderers.Sum(r => r.MemoryBytes) / Math.Max(unmatchedTabs.Count, 1);

                    foreach (var tab in unmatchedTabs)
                    {
                        if (unusedRenderers.Count > 0)
                        {
                            var renderer = unusedRenderers[0];
                            tab.MemoryBytes = renderer.MemoryBytes;
                            unusedRenderers.RemoveAt(0);
                        }
                        else
                        {
                            tab.MemoryBytes = avgMemory;
                        }
                    }
                }
                else if (unmatchedTabs.Count > 0)
                {
                    // No unused renderers, calculate average from all renderers
                    long avgMemory = browserRenderers.Count > 0
                        ? browserRenderers.Sum(r => r.MemoryBytes) / browserRenderers.Count
                        : 0;

                    foreach (var tab in unmatchedTabs)
                    {
                        tab.MemoryBytes = avgMemory;
                    }
                }
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
                            Preview = preview,
                            MemoryBytes = 0 // Will be calculated after enumeration
                        });
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            // Calculate per-tab average memory
            CalculatePerTabMemory();

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

        private void PopulateShells()
        {
            shellWindows.Clear();

            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;

                    StringBuilder className = new StringBuilder(256);
                    GetClassName(hWnd, className, className.Capacity);
                    var cls = className.ToString();

                    // Check for shell window classes
                    // ConsoleWindowClass: cmd.exe, PowerShell (legacy)
                    // CASCADIA_HOSTING_WINDOW_CLASS: Windows Terminal
                    // mintty: Git Bash, MSYS2
                    // VirtualConsoleClass: ConEmu
                    if (cls == "ConsoleWindowClass" || cls == "CASCADIA_HOSTING_WINDOW_CLASS" ||
                        cls == "mintty" || cls == "VirtualConsoleClass")
                    {
                        GetWindowThreadProcessId(hWnd, out uint pid);

                        StringBuilder title = new StringBuilder(256);
                        GetWindowText(hWnd, title, title.Capacity);
                        string titleStr = title.ToString();
                        if (string.IsNullOrWhiteSpace(titleStr)) return true;

                        // Determine shell type from title or process name
                        string shellType = "Unknown";
                        string processName = "";
                        try
                        {
                            var proc = Process.GetProcessById((int)pid);
                            processName = proc.ProcessName;
                            shellType = DetermineShellType(titleStr, processName, cls);
                        }
                        catch { }

                        shellWindows.Add(new ShellWindowInfo
                        {
                            Hwnd = hWnd,
                            ProcessId = (int)pid,
                            Title = titleStr,
                            ShellType = shellType,
                            ProcessName = processName
                        });
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            // preserve sorting if active
            if (shellSortColumnIndex != -1) SortShellsByColumn(shellSortColumnIndex);
            else
            {
                RefreshShellsTable();
            }
        }

        private string DetermineShellType(string title, string processName, string className)
        {
            string lowerTitle = title.ToLower();
            string lowerProc = processName.ToLower();

            // Windows Terminal
            if (className == "CASCADIA_HOSTING_WINDOW_CLASS" || lowerProc == "windowsterminal")
            {
                // Try to determine what's inside Windows Terminal from title
                if (lowerTitle.Contains("powershell") || lowerTitle.Contains("pwsh")) return "PowerShell";
                if (lowerTitle.Contains("cmd")) return "CMD";
                if (lowerTitle.Contains("bash") || lowerTitle.Contains("ubuntu") || lowerTitle.Contains("wsl")) return "Bash/WSL";
                return "Terminal";
            }

            // Git Bash / MSYS
            if (className == "mintty" || lowerProc == "mintty")
            {
                if (lowerTitle.Contains("mingw") || lowerTitle.Contains("msys")) return "MSYS2";
                return "Git Bash";
            }

            // ConEmu
            if (className == "VirtualConsoleClass") return "ConEmu";

            // Legacy console
            if (lowerProc == "powershell" || lowerProc == "pwsh") return "PowerShell";
            if (lowerProc == "cmd") return "CMD";
            if (lowerProc == "bash" || lowerProc == "sh") return "Bash";
            if (lowerProc == "wsl") return "WSL";

            // Fallback - check title
            if (lowerTitle.Contains("powershell")) return "PowerShell";
            if (lowerTitle.Contains("command prompt") || lowerTitle.StartsWith("c:\\")) return "CMD";
            if (lowerTitle.Contains("bash")) return "Bash";

            return "Console";
        }

        private void RefreshShellsTable()
        {
            if (tableShells != null)
            {
                tableShells.DataSource = null;
                tableShells.DataSource = new List<ShellWindowInfo>(shellWindows);
            }
        }

        private void PopulateHistory()
        {
            browserHistory.Clear();

            // Read Chrome history
            ReadChromeHistory();

            // Read Edge history
            ReadEdgeHistory();

            // Sort by last visit (most recent first) by default
            browserHistory = browserHistory.OrderByDescending(h => h.LastVisit).ToList();

            // Limit to last 500 entries for performance
            if (browserHistory.Count > 2000)
            {
                browserHistory = browserHistory.Take(2000).ToList();
            }

            totalHistoryCount = browserHistory.Count;

            // preserve sorting if active
            if (historySortColumnIndex != -1) SortHistoryByColumn(historySortColumnIndex);
            else
            {
                RefreshHistoryTable();
            }
        }

        private void ReadChromeHistory()
        {
            try
            {
                string historyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Google\Chrome\User Data\Default\History");

                if (!File.Exists(historyPath)) return;

                // Copy to temp file (database is locked while Chrome is running)
                string tempPath = Path.Combine(Path.GetTempPath(), $"chrome_history_{Guid.NewGuid()}.db");
                File.Copy(historyPath, tempPath, true);

                try
                {
                    using (var connection = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly"))
                    {
                        connection.Open();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                SELECT url, title, visit_count, last_visit_time
                                FROM urls
                                ORDER BY last_visit_time DESC
                                LIMIT 1000";

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string url = reader.GetString(0);
                                    string title = reader.IsDBNull(1) ? url : reader.GetString(1);
                                    int visitCount = reader.GetInt32(2);
                                    long lastVisitTime = reader.GetInt64(3);

                                    // Chrome stores time as microseconds since 1601-01-01
                                    DateTime lastVisit = DateTime.FromFileTimeUtc(lastVisitTime * 10).ToLocalTime();

                                    browserHistory.Add(new BrowserHistoryInfo
                                    {
                                        Title = string.IsNullOrWhiteSpace(title) ? url : title,
                                        Url = url,
                                        VisitCount = visitCount,
                                        LastVisit = lastVisit,
                                        Browser = "Chrome"
                                    });
                                }
                            }
                        }
                    }
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading Chrome history: {ex.Message}");
            }
        }

        private void ReadEdgeHistory()
        {
            try
            {
                string historyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\User Data\Default\History");

                if (!File.Exists(historyPath)) return;

                // Copy to temp file (database is locked while Edge is running)
                string tempPath = Path.Combine(Path.GetTempPath(), $"edge_history_{Guid.NewGuid()}.db");
                File.Copy(historyPath, tempPath, true);

                try
                {
                    using (var connection = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly"))
                    {
                        connection.Open();
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = @"
                                SELECT url, title, visit_count, last_visit_time
                                FROM urls
                                ORDER BY last_visit_time DESC
                                LIMIT 1000";

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    string url = reader.GetString(0);
                                    string title = reader.IsDBNull(1) ? url : reader.GetString(1);
                                    int visitCount = reader.GetInt32(2);
                                    long lastVisitTime = reader.GetInt64(3);

                                    // Edge stores time as microseconds since 1601-01-01 (same as Chrome)
                                    DateTime lastVisit = DateTime.FromFileTimeUtc(lastVisitTime * 10).ToLocalTime();

                                    browserHistory.Add(new BrowserHistoryInfo
                                    {
                                        Title = string.IsNullOrWhiteSpace(title) ? url : title,
                                        Url = url,
                                        VisitCount = visitCount,
                                        LastVisit = lastVisit,
                                        Browser = "Edge"
                                    });
                                }
                            }
                        }
                    }
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading Edge history: {ex.Message}");
            }
        }

        private void RefreshHistoryTable()
        {
            if (tableHistory != null)
            {
                tableHistory.DataSource = null;
                tableHistory.DataSource = new List<BrowserHistoryInfo>(browserHistory);
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
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;

                    var cls = new StringBuilder(256);
                    GetClassName(hWnd, cls, cls.Capacity);
                    var clsStr = cls.ToString();
                    if (clsStr != "Notepad" && clsStr != "CascadiaWindow") return true;

                    string content = ExtractNotepadContentForBackup(hWnd, clsStr);
                    if (IsNotepadTextError(content)) return true;

                    string backupPath = Path.Combine(backupDir, $"NotepadBackup_{hWnd.ToInt64()}.txt");
                    File.WriteAllText(backupPath, content);
                }
                catch (Exception ex) { Debug.WriteLine($"Backup error for hWnd {hWnd}: {ex.Message}"); }
                return true;
            }, IntPtr.Zero);

            CleanupObsoleteBackups();
        }

        private void CleanupObsoleteBackups()
        {
            var openHwnds = new HashSet<long>();
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    var cls = new StringBuilder(256);
                    GetClassName(hWnd, cls, cls.Capacity);
                    var clsStr = cls.ToString();
                    if (clsStr == "Notepad" || clsStr == "CascadiaWindow")
                        openHwnds.Add(hWnd.ToInt64());
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            foreach (var f in Directory.GetFiles(backupDir, "NotepadBackup_*.txt"))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    var parts = name.Split('_');
                    if (parts.Length < 2 || !long.TryParse(parts[1], out long hwndVal)) continue;
                    if (!openHwnds.Contains(hwndVal)) File.Delete(f);
                }
                catch (Exception ex) { Debug.WriteLine($"Error deleting backup {f}: {ex.Message}"); }
            }
        }

        private void RestoreBackupNotepadContent()
        {
            var openHwnds = new HashSet<long>();
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    var cls = new StringBuilder(256);
                    GetClassName(hWnd, cls, cls.Capacity);
                    var clsStr = cls.ToString();
                    if (clsStr == "Notepad" || clsStr == "CascadiaWindow")
                        openHwnds.Add(hWnd.ToInt64());
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            foreach (var backupFile in Directory.GetFiles(backupDir, "NotepadBackup_*.txt"))
            {
                try
                {
                    var name = Path.GetFileNameWithoutExtension(backupFile);
                    var parts = name.Split('_');
                    if (parts.Length < 2 || !long.TryParse(parts[1], out long hwndVal)) continue;
                    if (openHwnds.Contains(hwndVal)) continue;

                    string content = File.ReadAllText(backupFile);
                    if (string.IsNullOrWhiteSpace(content)) { File.Delete(backupFile); continue; }

                    var notepad = Process.Start("notepad.exe");
                    if (notepad == null || !notepad.WaitForInputIdle(5000)) continue;

                    IntPtr hwnd = IntPtr.Zero;
                    for (int i = 0; i < 10 && hwnd == IntPtr.Zero; i++)
                    {
                        System.Threading.Thread.Sleep(200);
                        notepad.Refresh();
                        hwnd = notepad.MainWindowHandle;
                    }
                    if (hwnd == IntPtr.Zero) continue;

                    if (TrySetNotepadText(hwnd, content))
                        File.Delete(backupFile);
                }
                catch (Exception ex) { Debug.WriteLine($"Error restoring backup {backupFile}: {ex.Message}"); }
            }
        }

        private string ExtractNotepadContentForBackup(IntPtr hWnd, string windowClass)
        {
            if (windowClass == "CascadiaWindow")
                return GetNotepadTextModern(hWnd);

            string text = GetNotepadText(hWnd, -1);
            if (!IsNotepadTextError(text)) return text;

            return GetNotepadTextModern(hWnd);
        }

        private static bool IsNotepadTextError(string text) =>
            string.IsNullOrWhiteSpace(text) ||
            text.StartsWith("[") ||
            text == "Unable to find text content." ||
            text == "Window not responding." ||
            text == "No content available." ||
            text == "Content too large.";

        private bool TrySetNotepadText(IntPtr hwnd, string content)
        {
            // Classic Notepad: Edit child control
            IntPtr edit = FindWindowEx(hwnd, IntPtr.Zero, "Edit", null);
            if (edit != IntPtr.Zero)
            {
                IntPtr result;
                SendMessageTimeout(edit, WM_SETTEXT, IntPtr.Zero, new StringBuilder(content), SMTO_ABORTIFHUNG, 2000, out result);
                return true;
            }

            // Win11 Notepad: search for RichEdit child classes
            string[] richEditClasses = { "RichEdit20W", "RichEdit20A", "RICHEDIT50W", "RichEditD2DPT", "RichEdit50W" };
            bool setViaChild = false;
            EnumChildWindows(hwnd, (child, lparam) =>
            {
                var cls = new StringBuilder(256);
                GetClassName(child, cls, cls.Capacity);
                if (richEditClasses.Contains(cls.ToString()))
                {
                    IntPtr result;
                    SendMessageTimeout(child, WM_SETTEXT, IntPtr.Zero, new StringBuilder(content), SMTO_ABORTIFHUNG, 2000, out result);
                    setViaChild = true;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (setViaChild) return true;

            // UIA ValuePattern fallback
            try
            {
                var task = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        var windowElement = AutomationElement.FromHandle(hwnd);
                        if (windowElement == null) return false;
                        System.Windows.Automation.ControlType[] tryTypes = {
                            System.Windows.Automation.ControlType.Edit,
                            System.Windows.Automation.ControlType.Document
                        };
                        foreach (var ct in tryTypes)
                        {
                            var element = windowElement.FindFirst(TreeScope.Descendants,
                                new PropertyCondition(AutomationElement.ControlTypeProperty, ct));
                            if (element == null) continue;
                            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object patternObj))
                            {
                                ((ValuePattern)patternObj).SetValue(content);
                                return true;
                            }
                        }
                        return false;
                    }
                    catch { return false; }
                });
                return task.Wait(3000) && task.Result;
            }
            catch { return false; }
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
            else if (tabs != null && tabs.SelectedIndex == 2)
            {
                await PerformShellsSearchAsync();
            }
            else if (tabs != null && tabs.SelectedIndex == 3)
            {
                await PerformHistorySearchAsync();
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
                                    Preview = "",
                                    MemoryBytes = 0 // Will be calculated after enumeration
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

            // Calculate per-tab memory using WMI process info
            CalculatePerTabMemory();

            if (browserSortColumnIndex != -1) SortBrowserTabsByColumn(browserSortColumnIndex);
            else RefreshBrowserTable();

            UpdateStatus();
        }

        private async System.Threading.Tasks.Task PerformShellsSearchAsync()
        {
            string searchText = textBoxSearch.Text.ToLower();

            // If search is empty, just populate all shells
            if (string.IsNullOrWhiteSpace(searchText))
            {
                PopulateShells();
                RefreshShellsTable();
                return;
            }

            var shellResults = await System.Threading.Tasks.Task.Run(() =>
            {
                var tempResults = new List<ShellWindowInfo>();

                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hWnd)) return true;

                        var cls = new StringBuilder(256);
                        GetClassName(hWnd, cls, cls.Capacity);
                        var clsStr = cls.ToString();

                        if (clsStr == "ConsoleWindowClass" || clsStr == "CASCADIA_HOSTING_WINDOW_CLASS" ||
                            clsStr == "mintty" || clsStr == "VirtualConsoleClass")
                        {
                            GetWindowThreadProcessId(hWnd, out uint pid);

                            var titleSb = new StringBuilder(256);
                            GetWindowText(hWnd, titleSb, titleSb.Capacity);
                            string titleStr = titleSb.ToString();
                            if (string.IsNullOrWhiteSpace(titleStr)) return true;

                            string shellType = "Unknown";
                            string processName = "";
                            try
                            {
                                var proc = Process.GetProcessById((int)pid);
                                processName = proc.ProcessName;
                                shellType = DetermineShellType(titleStr, processName, clsStr);
                            }
                            catch { }

                            // Match against title, shell type, or process name
                            if (titleStr.ToLower().Contains(searchText) ||
                                shellType.ToLower().Contains(searchText) ||
                                processName.ToLower().Contains(searchText))
                            {
                                tempResults.Add(new ShellWindowInfo
                                {
                                    Hwnd = hWnd,
                                    ProcessId = (int)pid,
                                    Title = titleStr,
                                    ShellType = shellType,
                                    ProcessName = processName
                                });
                            }
                        }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);

                return tempResults;
            });

            shellWindows.Clear();
            shellWindows.AddRange(shellResults);

            if (shellSortColumnIndex != -1) SortShellsByColumn(shellSortColumnIndex);
            else RefreshShellsTable();

            UpdateStatus();
        }

        private async System.Threading.Tasks.Task PerformHistorySearchAsync()
        {
            string searchText = textBoxSearch.Text.ToLower();

            // If search is empty, reload all history
            if (string.IsNullOrWhiteSpace(searchText))
            {
                PopulateHistory();
                RefreshHistoryTable();
                return;
            }

            var historyResults = await System.Threading.Tasks.Task.Run(() =>
            {
                // Filter the existing history list (don't reload from database)
                return browserHistory.Where(h =>
                    (h.Title?.ToLower().Contains(searchText) ?? false) ||
                    (h.Url?.ToLower().Contains(searchText) ?? false) ||
                    (h.Browser?.ToLower().Contains(searchText) ?? false)
                ).ToList();
            });

            browserHistory = historyResults;

            if (historySortColumnIndex != -1) SortHistoryByColumn(historySortColumnIndex);
            else RefreshHistoryTable();

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
            else if (tabs != null && tabs.SelectedIndex == 2)
            {
                // Shells tab - use search if filter is active
                await PerformShellsSearchAsync();
                if (string.IsNullOrWhiteSpace(textBoxSearch.Text))
                    totalShellCount = shellWindows.Count;
            }
            else if (tabs != null && tabs.SelectedIndex == 3)
            {
                // History tab - reload from database
                await System.Threading.Tasks.Task.Run(() => PopulateHistory());
                if (!string.IsNullOrWhiteSpace(textBoxSearch.Text))
                    await PerformHistorySearchAsync();
                RefreshHistoryTable();
            }
            else if (tabs != null && tabs.SelectedIndex == 4)
            {
                // Notepad Cache tab - reload from TabState directory
                await System.Threading.Tasks.Task.Run(() => PopulateTabState());
                RefreshTabStateTable();
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
            if (splitContainerShells != null) splitContainerShells.SplitterDistance = (int)(splitContainerShells.Width * 0.60);

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

            UpdateStatus("Loading shell windows...");

            // Load shell windows (fast)
            await System.Threading.Tasks.Task.Run(() => PopulateShells());
            totalShellCount = shellWindows.Count;
            RefreshShellsTable();

            UpdateStatus("Loading browser history...");

            // Load browser history (reads SQLite database)
            await System.Threading.Tasks.Task.Run(() => PopulateHistory());
            RefreshHistoryTable();

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
            else if (tabs != null && tabs.SelectedIndex == 2)
            {
                // Shells tab
                if (shellWindows.Count == totalShellCount || totalShellCount == 0)
                    labelStatus.Text = $"{shellWindows.Count} shell window{(shellWindows.Count != 1 ? "s" : "")}";
                else
                    labelStatus.Text = $"{shellWindows.Count} of {totalShellCount} shell windows";
            }
            else if (tabs != null && tabs.SelectedIndex == 3)
            {
                // History tab
                if (browserHistory.Count == totalHistoryCount || totalHistoryCount == 0)
                    labelStatus.Text = $"{browserHistory.Count} history entr{(browserHistory.Count != 1 ? "ies" : "y")}";
                else
                    labelStatus.Text = $"{browserHistory.Count} of {totalHistoryCount} history entries";
            }
            else if (tabs != null && tabs.SelectedIndex == 4)
            {
                // Notepad Cache tab
                labelStatus.Text = $"{tabStateFiles.Count} cached Notepad tab{(tabStateFiles.Count != 1 ? "s" : "")} (Win11 TabState)";
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
            this.splitContainerShells.SplitterWidth = (int)(16 * dpiScaleFactor);
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
                case "MemoryFormatted":
                    sorted = ascending ? browserTabs.OrderBy(b => b.MemoryBytes) : browserTabs.OrderByDescending(b => b.MemoryBytes);
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
            string[] columnNames = { "ProcessId", "MemoryFormatted", "Title", "Url" };
            if (columnIndex < 0 || columnIndex >= columnNames.Length) return;
            var name = columnNames[columnIndex];
            IEnumerable<BrowserTabInfo> sorted;

            switch (name)
            {
                case "ProcessId":
                    sorted = browserSortAscending ? browserTabs.OrderBy(b => b.ProcessId) : browserTabs.OrderByDescending(b => b.ProcessId);
                    break;
                case "MemoryFormatted":
                    sorted = browserSortAscending ? browserTabs.OrderBy(b => b.MemoryBytes) : browserTabs.OrderByDescending(b => b.MemoryBytes);
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

        private void TableShells_SortRows(object sender, AntdUI.IntEventArgs e)
        {
            int columnIndex = e.Value;
            if (columnIndex < 0 || tableShells.Columns.Count <= columnIndex) return;

            var column = tableShells.Columns[columnIndex];
            bool ascending = column.SortMode == AntdUI.SortMode.ASC;

            // Track sort state for persistence
            shellSortColumnIndex = columnIndex;
            shellSortAscending = ascending;

            // Sort based on column key
            IEnumerable<ShellWindowInfo> sorted;
            switch (column.Key)
            {
                case "ProcessId":
                    sorted = ascending ? shellWindows.OrderBy(s => s.ProcessId) : shellWindows.OrderByDescending(s => s.ProcessId);
                    break;
                case "ShellType":
                    sorted = ascending ? shellWindows.OrderBy(s => s.ShellType ?? "", StringComparer.CurrentCultureIgnoreCase) : shellWindows.OrderByDescending(s => s.ShellType ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "Title":
                    sorted = ascending ? shellWindows.OrderBy(s => s.Title ?? "", StringComparer.CurrentCultureIgnoreCase) : shellWindows.OrderByDescending(s => s.Title ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            shellWindows = sorted.ToList();
            RefreshShellsTable();
        }

        // Sorts the shell windows in-memory list and refreshes the table
        private void SortShellsByColumn(int columnIndex)
        {
            string[] columnNames = { "ProcessId", "ShellType", "Title" };
            if (columnIndex < 0 || columnIndex >= columnNames.Length) return;
            var name = columnNames[columnIndex];
            IEnumerable<ShellWindowInfo> sorted;

            switch (name)
            {
                case "ProcessId":
                    sorted = shellSortAscending ? shellWindows.OrderBy(s => s.ProcessId) : shellWindows.OrderByDescending(s => s.ProcessId);
                    break;
                case "ShellType":
                    sorted = shellSortAscending ? shellWindows.OrderBy(s => s.ShellType ?? string.Empty, StringComparer.CurrentCultureIgnoreCase) : shellWindows.OrderByDescending(s => s.ShellType ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "Title":
                    sorted = shellSortAscending ? shellWindows.OrderBy(s => s.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase) : shellWindows.OrderByDescending(s => s.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            shellWindows = sorted.ToList();
            RefreshShellsTable();
        }

        private void TableHistory_SortRows(object sender, AntdUI.IntEventArgs e)
        {
            int columnIndex = e.Value;
            if (columnIndex < 0 || tableHistory.Columns.Count <= columnIndex) return;

            var column = tableHistory.Columns[columnIndex];
            bool ascending = column.SortMode == AntdUI.SortMode.ASC;

            // Track sort state for persistence
            historySortColumnIndex = columnIndex;
            historySortAscending = ascending;

            // Sort based on column key
            IEnumerable<BrowserHistoryInfo> sorted;
            switch (column.Key)
            {
                case "Title":
                    sorted = ascending ? browserHistory.OrderBy(h => h.Title ?? "", StringComparer.CurrentCultureIgnoreCase) : browserHistory.OrderByDescending(h => h.Title ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "Url":
                    sorted = ascending ? browserHistory.OrderBy(h => h.Url ?? "", StringComparer.CurrentCultureIgnoreCase) : browserHistory.OrderByDescending(h => h.Url ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "LastVisitFormatted":
                    sorted = ascending ? browserHistory.OrderBy(h => h.LastVisit) : browserHistory.OrderByDescending(h => h.LastVisit);
                    break;
                case "Browser":
                    sorted = ascending ? browserHistory.OrderBy(h => h.Browser ?? "", StringComparer.CurrentCultureIgnoreCase) : browserHistory.OrderByDescending(h => h.Browser ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            browserHistory = sorted.ToList();
            RefreshHistoryTable();
        }

        private void SortHistoryByColumn(int columnIndex)
        {
            string[] columnNames = { "Browser", "LastVisitFormatted", "Title", "Url" };
            if (columnIndex < 0 || columnIndex >= columnNames.Length) return;
            var name = columnNames[columnIndex];
            IEnumerable<BrowserHistoryInfo> sorted;

            switch (name)
            {
                case "Browser":
                    sorted = historySortAscending ? browserHistory.OrderBy(h => h.Browser ?? "", StringComparer.CurrentCultureIgnoreCase) : browserHistory.OrderByDescending(h => h.Browser ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "LastVisitFormatted":
                    sorted = historySortAscending ? browserHistory.OrderBy(h => h.LastVisit) : browserHistory.OrderByDescending(h => h.LastVisit);
                    break;
                case "Title":
                    sorted = historySortAscending ? browserHistory.OrderBy(h => h.Title ?? "", StringComparer.CurrentCultureIgnoreCase) : browserHistory.OrderByDescending(h => h.Title ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "Url":
                    sorted = historySortAscending ? browserHistory.OrderBy(h => h.Url ?? "", StringComparer.CurrentCultureIgnoreCase) : browserHistory.OrderByDescending(h => h.Url ?? "", StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            browserHistory = sorted.ToList();
            RefreshHistoryTable();
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
                    BrowserSortAscending = browserSortAscending,
                    ShellSortColumn = shellSortColumnIndex,
                    ShellSortAscending = shellSortAscending,
                    HistorySortColumn = historySortColumnIndex,
                    HistorySortAscending = historySortAscending
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
                shellSortColumnIndex = settings.ShellSortColumn;
                shellSortAscending = settings.ShellSortAscending;
                historySortColumnIndex = settings.HistorySortColumn;
                historySortAscending = settings.HistorySortAscending;
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

            // Apply shells table sort if saved
            if (shellSortColumnIndex >= 0 && shellSortColumnIndex < tableShells?.Columns.Count)
            {
                var col = tableShells.Columns[shellSortColumnIndex];
                col.SortMode = shellSortAscending ? AntdUI.SortMode.ASC : AntdUI.SortMode.DESC;
                SortShellsByColumn(shellSortColumnIndex);
            }

            // Apply history table sort if saved
            if (historySortColumnIndex >= 0 && historySortColumnIndex < tableHistory?.Columns.Count)
            {
                var col = tableHistory.Columns[historySortColumnIndex];
                col.SortMode = historySortAscending ? AntdUI.SortMode.ASC : AntdUI.SortMode.DESC;
                SortHistoryByColumn(historySortColumnIndex);
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
            this.splitContainerShells = new SplitContainer();
            this.textBoxShellContent = new RichTextBox();
            this.panelShellPreview = new AntdUI.Panel();
            this.splitContainerHistory = new SplitContainer();
            this.textBoxHistoryDetails = new RichTextBox();
            this.splitContainerTabState = new SplitContainer();
            this.textBoxTabStateContent = new RichTextBox();
            this.panelStatus = new AntdUI.Panel();
            this.labelStatus = new AntdUI.Label();

            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerBrowser)).BeginInit();
            this.splitContainerBrowser.Panel2.SuspendLayout();
            this.splitContainerBrowser.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerShells)).BeginInit();
            this.splitContainerShells.Panel2.SuspendLayout();
            this.splitContainerShells.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerHistory)).BeginInit();
            this.splitContainerHistory.Panel2.SuspendLayout();
            this.splitContainerHistory.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerTabState)).BeginInit();
            this.splitContainerTabState.Panel2.SuspendLayout();
            this.splitContainerTabState.SuspendLayout();
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

            // panelShellPreview - wraps the shell preview textbox with shadow and rounded corners
            this.panelShellPreview.Dock = DockStyle.Fill;
            this.panelShellPreview.Shadow = 4;
            this.panelShellPreview.Radius = 6;
            this.panelShellPreview.Padding = new Padding(8);
            this.panelShellPreview.Back = Color.White;
            this.panelShellPreview.Controls.Add(this.textBoxShellContent);

            // splitContainerShells (Shells tab)
            this.splitContainerShells.Dock = DockStyle.Fill;
            this.splitContainerShells.Size = new Size(850, 437);
            this.splitContainerShells.SplitterDistance = 350;
            this.splitContainerShells.SplitterWidth = 16;
            this.splitContainerShells.BorderStyle = BorderStyle.None;
            this.splitContainerShells.Panel2.Controls.Add(this.panelShellPreview);

            // textBoxShellContent
            this.textBoxShellContent.Dock = DockStyle.Fill;
            this.textBoxShellContent.ReadOnly = true;
            this.textBoxShellContent.ScrollBars = RichTextBoxScrollBars.Vertical;
            this.textBoxShellContent.BorderStyle = BorderStyle.None;
            this.textBoxShellContent.Text = "Shell window info will appear here...";

            // panelHistoryPreview - wraps the history details textbox
            var panelHistoryPreview = new AntdUI.Panel();
            panelHistoryPreview.Dock = DockStyle.Fill;
            panelHistoryPreview.Shadow = 4;
            panelHistoryPreview.Radius = 6;
            panelHistoryPreview.Padding = new Padding(8);
            panelHistoryPreview.Back = Color.White;
            panelHistoryPreview.Controls.Add(this.textBoxHistoryDetails);

            // splitContainerHistory (Browser History tab)
            this.splitContainerHistory.Dock = DockStyle.Fill;
            this.splitContainerHistory.Size = new Size(850, 437);
            this.splitContainerHistory.SplitterDistance = 350;
            this.splitContainerHistory.SplitterWidth = 16;
            this.splitContainerHistory.BorderStyle = BorderStyle.None;
            this.splitContainerHistory.Panel2.Controls.Add(panelHistoryPreview);

            // textBoxHistoryDetails
            this.textBoxHistoryDetails.Dock = DockStyle.Fill;
            this.textBoxHistoryDetails.ReadOnly = true;
            this.textBoxHistoryDetails.ScrollBars = RichTextBoxScrollBars.Vertical;
            this.textBoxHistoryDetails.BorderStyle = BorderStyle.None;
            this.textBoxHistoryDetails.Text = "Select a history entry to see details...";

            // splitContainerTabState (Notepad Cache tab)
            this.splitContainerTabState.Dock = DockStyle.Fill;
            this.splitContainerTabState.Orientation = Orientation.Horizontal;
            this.splitContainerTabState.SplitterDistance = 300;
            this.splitContainerTabState.SplitterWidth = 6;
            this.splitContainerTabState.BorderStyle = BorderStyle.None;

            // textBoxTabStateContent - preview pane
            this.textBoxTabStateContent.Dock = DockStyle.Fill;
            this.textBoxTabStateContent.ReadOnly = true;
            this.textBoxTabStateContent.ScrollBars = RichTextBoxScrollBars.Both;
            this.textBoxTabStateContent.BorderStyle = BorderStyle.None;
            this.textBoxTabStateContent.Font = new Font("Consolas", 9f);
            this.textBoxTabStateContent.WordWrap = false;
            this.textBoxTabStateContent.Text = "Select a tab to preview content. Double-click to open in Notepad.";

            // Restore button below preview
            var btnRestoreTab = new AntdUI.Button();
            btnRestoreTab.Dock = DockStyle.Bottom;
            btnRestoreTab.Height = 36;
            btnRestoreTab.Text = "Open in Notepad";
            btnRestoreTab.Type = AntdUI.TTypeMini.Primary;
            btnRestoreTab.Radius = 6;
            btnRestoreTab.Click += (s, e) =>
            {
                if (selectedTabStateIndex >= 0 && selectedTabStateIndex < tabStateFiles.Count)
                    RestoreTabState(tabStateFiles[selectedTabStateIndex]);
            };

            this.splitContainerTabState.Panel2.Controls.Add(this.textBoxTabStateContent);
            this.splitContainerTabState.Panel2.Controls.Add(btnRestoreTab);

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

            var tabShells = new AntdUI.TabPage();
            tabShells.Text = "Shells";
            tabShells.IconSvg = SvgTerminal;
            tabShells.Controls.Add(this.splitContainerShells);

            var tabHistory = new AntdUI.TabPage();
            tabHistory.Text = "Browser History";
            tabHistory.IconSvg = SvgHistory;
            tabHistory.Controls.Add(this.splitContainerHistory);

            var tabCache = new AntdUI.TabPage();
            tabCache.Text = "Notepad Cache";
            tabCache.IconSvg = SvgFile;
            tabCache.Controls.Add(this.splitContainerTabState);

            this.tabs.Pages.Add(tabNotepads);
            this.tabs.Pages.Add(tabBrowser);
            this.tabs.Pages.Add(tabShells);
            this.tabs.Pages.Add(tabHistory);
            this.tabs.Pages.Add(tabCache);
            this.tabs.SelectedIndexChanged += (s, e) =>
            {
                UpdateStatus();
                if (tabs.SelectedIndex == 4 && tabStateFiles.Count == 0)
                {
                    PopulateTabState();
                    RefreshTabStateTable();
                    UpdateStatus();
                }
            };

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
            this.splitContainerShells.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerShells)).EndInit();
            this.splitContainerShells.ResumeLayout(false);
            this.splitContainerHistory.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerHistory)).EndInit();
            this.splitContainerHistory.ResumeLayout(false);
            this.splitContainerTabState.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerTabState)).EndInit();
            this.splitContainerTabState.ResumeLayout(false);
            this.panelTop.ResumeLayout(false);
        }

        private void ClearSearchBox(object sender, EventArgs e) { textBoxSearch.Text = string.Empty; }
    }
}