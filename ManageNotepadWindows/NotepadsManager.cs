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
using MaterialSkin;
using MaterialSkin.Controls;
using Microsoft.Win32;

namespace ManageNotepadWindows
{
    public partial class NotepadsManager : MaterialForm
    {
        // UI font sizing constraints and step
        private const float MinFontSize = 9F;
        private const float MaxFontSize = 20F;
        private const float FontStep = 1F;

        private float currentFontSize = 11F;

        // Sorting state for DataGridView (virtual mode)
        private int sortColumnIndex = -1;
        private bool sortAscending = true;

        private readonly string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotepadBackups");
        private Timer searchDebounceTimer;

        // Virtualized grid + model
        private DataGridView gridNotepadWindows;
        private List<NotepadWindowInfo> notepadWindows = new List<NotepadWindowInfo>();

        // Preview and UI
        private RichTextBox textBoxNotepadContent;
        private Panel panelTop;
        private Button refresh;
        private MaterialTextBox2 textBoxSearch;
        private SplitContainer splitContainer1;
        private Button btnFontIncrease;
        private Button btnFontDecrease;

        // Native helpers (consolidated)
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);
        [DllImport("user32.dll", EntryPoint = "SendMessage", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageInt(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
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

        // Model
        private class NotepadWindowInfo
        {
            public IntPtr Hwnd;
            public int ProcessId;
            public string Title;
            public string Preview;
            public string Diagnostic;
        }

        public NotepadsManager()
        {
            InitializeComponent();

            // DPI autoscale
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // MaterialSkin setup
            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.Blue500, Primary.Blue700, Primary.Blue200, Accent.LightBlue200, TextShade.WHITE);

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
            MakeWindowTopMost();
            this.Load += NotepadsManager_Load;

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
            if (gridNotepadWindows == null)
            {
                gridNotepadWindows = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    VirtualMode = true,
                    RowHeadersVisible = false,
                    AllowUserToResizeRows = false,
                    AllowUserToOrderColumns = true,
                    AutoGenerateColumns = false,
                    MultiSelect = false
                };

                var colPid = new DataGridViewTextBoxColumn { Name = "ProcessId", HeaderText = "PID", Width = 90 };
                var colName = new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill };
                var colPreview = new DataGridViewTextBoxColumn { Name = "Preview", HeaderText = "Text Preview", Width = 400 };
                var colDiag = new DataGridViewTextBoxColumn { Name = "Diag", HeaderText = "Diag", Width = 220 };

                // Make columns programmatic-sortable (virtual mode requires custom sorting)
                colPid.SortMode = DataGridViewColumnSortMode.Programmatic;
                colName.SortMode = DataGridViewColumnSortMode.Programmatic;
                colPreview.SortMode = DataGridViewColumnSortMode.Programmatic;
                colDiag.SortMode = DataGridViewColumnSortMode.Programmatic;

                gridNotepadWindows.Columns.AddRange(new DataGridViewColumn[] { colPid, colName, colPreview, colDiag });

                gridNotepadWindows.CellValueNeeded += GridNotepadWindows_CellValueNeeded;
                gridNotepadWindows.CellDoubleClick += GridNotepadWindows_CellDoubleClick;
                gridNotepadWindows.SelectionChanged += GridNotepadWindows_SelectionChanged;

                // hook header click for sorting
                gridNotepadWindows.ColumnHeaderMouseClick += GridNotepadWindows_ColumnHeaderMouseClick;
            }

            if (splitContainer1 != null)
            {
                splitContainer1.Panel1.Controls.Clear();
                splitContainer1.Panel1.Controls.Add(gridNotepadWindows);
            }
        }

        private void GridNotepadWindows_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= notepadWindows.Count) return;
            var item = notepadWindows[e.RowIndex];
            var colName = gridNotepadWindows.Columns[e.ColumnIndex].Name;
            switch (colName)
            {
                case "ProcessId": e.Value = item.ProcessId.ToString(); break;
                case "Name": e.Value = item.Title; break;
                case "Preview":
                    var p = item.Preview?.Replace(Environment.NewLine, " ").Trim();
                    if (!string.IsNullOrEmpty(p) && p.Length > 200) p = p.Substring(0, 200) + "...";
                    e.Value = p;
                    break;
                case "Diag": e.Value = item.Diagnostic?.Replace(Environment.NewLine, " ").Trim(); break;
            }
        }

        private void GridNotepadWindows_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= notepadWindows.Count) return;
            var item = notepadWindows[e.RowIndex];
            IntPtr hwnd = item.Hwnd;
            if (hwnd == IntPtr.Zero && item.ProcessId != 0) hwnd = FindWindowForProcess(item.ProcessId);
            if (hwnd != IntPtr.Zero) { ShowWindow(hwnd, SW_RESTORE); SetForegroundWindow(hwnd); }
        }

        private void GridNotepadWindows_SelectionChanged(object sender, EventArgs e)
        {
            if (gridNotepadWindows.CurrentCell == null) { textBoxNotepadContent.Text = string.Empty; return; }
            int row = gridNotepadWindows.CurrentCell.RowIndex;
            if (row < 0 || row >= notepadWindows.Count) return;
            var info = notepadWindows[row];
            string content = GetNotepadTextModern(info.Hwnd);
            if (string.IsNullOrWhiteSpace(content) || content.StartsWith("[")) content = GetNotepadText(info.Hwnd, -1);
            if (string.IsNullOrWhiteSpace(content) || content.StartsWith("Unable") || content.StartsWith("No content")) content = "Preview not available";
            textBoxNotepadContent.Text = content;
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

                        string preview = GetNotepadTextModern(hWnd);
                        bool modernFailed = string.IsNullOrWhiteSpace(preview) || preview.StartsWith("[");
                        if (modernFailed) preview = GetNotepadText(hWnd, 5);

                        string diag = preview;
                        if (string.IsNullOrWhiteSpace(preview) || preview.StartsWith("Unable") || preview.StartsWith("No content")) preview = "[Unable to retrieve content]";

                        notepadWindows.Add(new NotepadWindowInfo
                        {
                            Hwnd = hWnd,
                            ProcessId = (int)pid,
                            Title = title.ToString(),
                            Preview = preview,
                            Diagnostic = diag
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
                if (gridNotepadWindows != null)
                {
                    gridNotepadWindows.RowCount = notepadWindows.Count;
                    gridNotepadWindows.Invalidate();
                }
            }
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
                    notepad.WaitForInputIdle();
                    IntPtr hwnd = notepad.MainWindowHandle;
                    IntPtr edit = FindWindowEx(hwnd, IntPtr.Zero, "Edit", null);
                    if (edit != IntPtr.Zero)
                    {
                        SendMessage(edit, WM_SETTEXT, IntPtr.Zero, new StringBuilder(content));
                        SendMessageInt(edit, (int)WM_CHAR, new IntPtr(' '), IntPtr.Zero);
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
                        int len = (int)SendMessage(child, WM_GETTEXTLENGTH, IntPtr.Zero, null);
                        if (len > 0)
                        {
                            var sb = new StringBuilder(len + 1);
                            SendMessage(child, WM_GETTEXT, (IntPtr)sb.Capacity, sb);
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
                var windowElement = AutomationElement.FromHandle(windowHandle);
                if (windowElement == null) return "[UIA: window element null]";

                ControlType[] tryTypes = { ControlType.Edit, ControlType.Document, ControlType.Pane, ControlType.Custom, ControlType.Group, ControlType.Text };
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

                var textNodes = windowElement.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                if (textNodes != null && textNodes.Count > 0)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < textNodes.Count; i++)
                    {
                        try
                        {
                            var n = textNodes[i].Current.Name;
                            if (!string.IsNullOrWhiteSpace(n))
                            {
                                if (sb.Length > 0) sb.AppendLine();
                                sb.Append(n);
                            }
                        }
                        catch { }
                    }
                    if (sb.Length > 0) return sb.ToString();
                }

                var win32Text = TryGetTextFromChildClasses(windowHandle);
                if (!string.IsNullOrWhiteSpace(win32Text)) return win32Text;

                return "[No UIA text found]";
            }
            catch (Exception ex) { return $"[UIA error: {ex.Message}]"; }
        }

        static string GetNotepadText(IntPtr notepadHandle, int maxLines = 2)
        {
            IntPtr editHandle = FindWindowEx(notepadHandle, IntPtr.Zero, "Edit", null);
            if (editHandle == IntPtr.Zero) return "Unable to find text content.";
            int textLength = (int)SendMessage(editHandle, WM_GETTEXTLENGTH, IntPtr.Zero, null);
            if (textLength == 0) return "No content available.";
            var windowText = new StringBuilder(textLength + 1);
            SendMessage(editHandle, WM_GETTEXT, (IntPtr)windowText.Capacity, windowText);
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

        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            searchDebounceTimer.Stop();
            PerformSearch();
        }

        private void PerformSearch()
        {
            string searchText = textBoxSearch.Text.ToLower();
            notepadWindows.Clear();

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
                        string windowContent = GetNotepadTextModern(hWnd);
                        bool modernFailed = string.IsNullOrWhiteSpace(windowContent) || windowContent.StartsWith("[");
                        if (modernFailed) windowContent = GetNotepadText(hWnd, 5);
                        if (string.IsNullOrWhiteSpace(windowContent) || windowContent.StartsWith("Unable") || windowContent.StartsWith("No content"))
                            windowContent = "[Unable to retrieve content]";
                        if (windowContent.ToLower().Contains(searchText) || title.ToString().ToLower().Contains(searchText))
                        {
                            notepadWindows.Add(new NotepadWindowInfo { Hwnd = hWnd, ProcessId = (int)pid, Title = title.ToString(), Preview = windowContent, Diagnostic = windowContent });
                        }
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);

            // preserve sort
            if (sortColumnIndex != -1) SortByColumn(sortColumnIndex);
            else
            {
                if (gridNotepadWindows != null)
                {
                    gridNotepadWindows.RowCount = notepadWindows.Count;
                    gridNotepadWindows.Invalidate();
                    if (gridNotepadWindows.CurrentCell != null) GridNotepadWindows_SelectionChanged(this, EventArgs.Empty);
                }
            }
        }

        private void btnRefresh_Click(object sender, EventArgs e) => PopulateNotepadWindows();
        private void btnFontIncrease_Click(object sender, EventArgs e) { currentFontSize = Math.Min(48F, currentFontSize + 2F);                             UpdateFonts(); }
        private void btnFontDecrease_Click(object sender, EventArgs e) { currentFontSize = Math.Max(8F, currentFontSize - 2F); UpdateFonts(); }

        private void NotepadsManager_Load(object sender, EventArgs e)
        {
            try { this.Icon = ManageCMDWindows.Properties.Resources.notepad_manager_icon; } catch { }
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int screenHeight = Screen.PrimaryScreen.WorkingArea.Height;
            this.Width = (int)(screenWidth * 0.8);
            this.Height = (int)(screenHeight * 0.7);
            this.StartPosition = FormStartPosition.CenterScreen;
            if (splitContainer1 != null) splitContainer1.SplitterDistance = (int)(splitContainer1.Width * 0.4);
            PopulateNotepadWindows();
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

        // Custom sort handler for virtual DataGridView
        private void GridNotepadWindows_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.ColumnIndex < 0 || e.ColumnIndex >= gridNotepadWindows.Columns.Count) return;

            if (sortColumnIndex == e.ColumnIndex)
                sortAscending = !sortAscending;
            else
            {
                sortColumnIndex = e.ColumnIndex;
                sortAscending = true;
            }

            SortByColumn(sortColumnIndex);

            // update glyphs
            foreach (DataGridViewColumn col in gridNotepadWindows.Columns)
                col.HeaderCell.SortGlyphDirection = SortOrder.None;

            gridNotepadWindows.Columns[sortColumnIndex].HeaderCell.SortGlyphDirection = sortAscending ? SortOrder.Ascending : SortOrder.Descending;
        }

        // sorts the in-memory list and refreshes the virtual grid
        private void SortByColumn(int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= gridNotepadWindows.Columns.Count) return;
            var name = gridNotepadWindows.Columns[columnIndex].Name;
            IEnumerable<NotepadWindowInfo> sorted;

            switch (name)
            {
                case "ProcessId":
                    sorted = sortAscending ? notepadWindows.OrderBy(n => n.ProcessId) : notepadWindows.OrderByDescending(n => n.ProcessId);
                    break;
                case "Name":
                    sorted = sortAscending ? notepadWindows.OrderBy(n => n.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase) : notepadWindows.OrderByDescending(n => n.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "Preview":
                    sorted = sortAscending ? notepadWindows.OrderBy(n => n.Preview ?? string.Empty, StringComparer.CurrentCultureIgnoreCase) : notepadWindows.OrderByDescending(n => n.Preview ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                case "Diag":
                    sorted = sortAscending ? notepadWindows.OrderBy(n => n.Diagnostic ?? string.Empty, StringComparer.CurrentCultureIgnoreCase) : notepadWindows.OrderByDescending(n => n.Diagnostic ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                    break;
                default:
                    return;
            }

            notepadWindows = sorted.ToList();

            if (gridNotepadWindows != null)
            {
                gridNotepadWindows.RowCount = notepadWindows.Count;
                gridNotepadWindows.Invalidate();
                if (gridNotepadWindows.CurrentCell != null) GridNotepadWindows_SelectionChanged(this, EventArgs.Empty);
            }
        }

        // Minimal InitializeComponent - creates controls used by the class.
        private void InitializeComponent()
        {
            this.refresh = new Button();
            this.btnFontIncrease = new Button();
            this.btnFontDecrease = new Button();
            this.textBoxSearch = new MaterialTextBox2();
            this.splitContainer1 = new SplitContainer();
            this.textBoxNotepadContent = new RichTextBox();
            this.panelTop = new Panel();

            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.panelTop.SuspendLayout();

            // refresh
            this.refresh.Dock = DockStyle.Left;
            this.refresh.Size = new Size(120, 70);
            this.refresh.Text = "Refresh";
            this.refresh.Click += new EventHandler(this.btnRefresh_Click);

            // btnFontIncrease
            this.btnFontIncrease.Dock = DockStyle.Right;
            this.btnFontIncrease.Size = new Size(100, 70);
            this.btnFontIncrease.Text = "Zoom +";
            this.btnFontIncrease.Click += new EventHandler(this.btnFontIncrease_Click);

            // btnFontDecrease
            this.btnFontDecrease.Dock = DockStyle.Right;
            this.btnFontDecrease.Size = new Size(100, 70);
            this.btnFontDecrease.Text = "Zoom -";
            this.btnFontDecrease.Click += new EventHandler(this.btnFontDecrease_Click);

            // textBoxSearch
            this.textBoxSearch.Dock = DockStyle.Fill;
            this.textBoxSearch.Hint = "Search...";
            this.textBoxSearch.TrailingIconClick += new EventHandler(this.ClearSearchBox);
            this.textBoxSearch.TextChanged += new EventHandler(this.TextBoxSearch_TextChanged);

            // splitContainer1
            this.splitContainer1.Dock = DockStyle.Fill;
            this.splitContainer1.Location = new Point(0, 70);
            this.splitContainer1.Size = new Size(850, 507);
            this.splitContainer1.SplitterDistance = 350;
            this.splitContainer1.Panel2.Controls.Add(this.textBoxNotepadContent);

            // textBoxNotepadContent
            this.textBoxNotepadContent.Dock = DockStyle.Fill;
            this.textBoxNotepadContent.ReadOnly = true;
            this.textBoxNotepadContent.ScrollBars = RichTextBoxScrollBars.Vertical;

            // panelTop
            this.panelTop.Dock = DockStyle.Top;
            this.panelTop.Size = new Size(850, 70);
            this.panelTop.Controls.Add(this.textBoxSearch);
            this.panelTop.Controls.Add(this.btnFontDecrease);
            this.panelTop.Controls.Add(this.btnFontIncrease);
            this.panelTop.Controls.Add(this.refresh);

            // allow drag by panelTop
            this.panelTop.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessageInt(this.Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero); }
            };

            // Form
            this.ClientSize = new Size(850, 577);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.panelTop);
            this.Text = "Notepad Manager";
            this.Load += new EventHandler(this.NotepadsManager_Load);

            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.panelTop.ResumeLayout(false);
        }

        private void ClearSearchBox(object sender, EventArgs e) { textBoxSearch.Text = string.Empty; }
    }
}