using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
//
using MaterialSkin;
using MaterialSkin.Controls;
//
using Microsoft.Win32;

namespace ManageNotepadWindows
{
    public partial class NotepadsManager : MaterialForm
    {
        private int sortColumn = -1;
        private string backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NotepadBackups");

        public NotepadsManager()
        {
            InitializeComponent();

            // Set up ListView columns programmatically
            SetupListViewColumns();
            MakeWindowTopMost();

            // Initialize MaterialSkinManager
            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);

            // You can choose between Themes: LIGHT or DARK
            materialSkinManager.Theme = MaterialSkinManager.Themes.LIGHT;

            // Customize the color scheme of the material form
            materialSkinManager.ColorScheme = new ColorScheme(
                Primary.Blue500, Primary.Blue700,
                Primary.Blue200, Accent.LightBlue200,
                TextShade.WHITE
            );

            // Ensure backup directory exists
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            // Restore backup files on startup
            RestoreBackupNotepadContent();

            // Start the backup timer (every 5 minutes)
            Timer backupTimer = new Timer();
            backupTimer.Interval = 5 * 60 * 1000; // 5 minutes
            backupTimer.Tick += (s, e) => BackupUnsavedNotepadContent();
            backupTimer.Start();

            // Hook into the system shutdown/logoff event
            SystemEvents.SessionEnding += new SessionEndingEventHandler(OnSessionEnding);

        } //EOC


        private void MakeWindowTopMost()
        {
            // Set the window as top-most
            SetWindowPos(this.Handle, HWND_TOPMOST, 0, 0, 0, 0, TOPMOST_FLAGS);
        }

        // Importing Windows API functions to interact with windows
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Import the SetWindowPos function from the Windows API
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint TOPMOST_FLAGS = SWP_NOMOVE | SWP_NOSIZE;



        // Constants for Windows API messages
        const uint WM_GETTEXT = 0x000D;
        const uint WM_GETTEXTLENGTH = 0x000E;
        const uint WM_CHAR = 0x0102;
        const uint WM_SETTEXT = 0x000C; // Define WM_SETTEXT

        private const int SW_RESTORE = 9;  // Restores a minimized window to its original size
        //
        private ListView listViewNotepadWindows;
        //private TextBox textBoxNotepadContent;
        private RichTextBox textBoxNotepadContent;
        private Panel panelTop;
        private Button refresh;
        //private TextBox textBoxSearch;
        private MaterialTextBox2 textBoxSearch;
        private PictureBox clearSearchButton;
        //
        private SplitContainer splitContainer1;

        // Event handler for Refresh button click
        private void btnRefresh_Click(object sender, EventArgs e)
        {
            PopulateNotepadWindows();
        }

        // Function to populate the Notepad windows in the ListView
        private void PopulateNotepadWindows()
        {
            // Clear the existing list before updating
            listViewNotepadWindows.Items.Clear();

            // Get all running Notepad processes
            Process[] processes = Process.GetProcessesByName("notepad");

            // Iterate over each Notepad process
            foreach (Process process in processes)
            {
                IntPtr hWnd = process.MainWindowHandle;

                if (hWnd != IntPtr.Zero && IsWindowVisible(hWnd))
                {
                    // Retrieve the window title
                    StringBuilder windowTitle = new StringBuilder(256);
                    GetWindowText(hWnd, windowTitle, windowTitle.Capacity);

                    // Get the actual text content from the Notepad child window (limited to the first two lines)
                    string windowContent = GetNotepadText(hWnd, 2); // Limit to 2 lines

                    // Add the window details to the ListView
                    //ListViewItem item = new ListViewItem(new[] { hWnd.ToInt64().ToString(), windowTitle.ToString(), windowContent });
                    ListViewItem item = new ListViewItem(new[] { process.Id.ToString(), windowTitle.ToString(), windowContent });
                    listViewNotepadWindows.Items.Add(item);
                }
            }

            // Sort the list after refreshing
            listViewNotepadWindows.Sort();

        }

        // Handle system shutdown/logoff event
        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            // Backup unsaved Notepad content before shutdown or logoff
            BackupUnsavedNotepadContent();
        }

        private void BackupUnsavedNotepadContent()
        {
            // Get all running Notepad processes
            Process[] processes = Process.GetProcessesByName("notepad");
            foreach (Process process in processes) {
                try {
                    IntPtr hWnd = process.MainWindowHandle;
                    if (hWnd != IntPtr.Zero && IsWindowVisible(hWnd)) {
                        // Retrieve the window title
                        StringBuilder windowTitle = new StringBuilder(256);
                        GetWindowText(hWnd, windowTitle, windowTitle.Capacity);

                        // If the window is unsaved (Untitled - Notepad), back it up
                        if (windowTitle.ToString().Contains("Untitled - Notepad") ){
                            // Get the content of the unsaved Notepad window (full content)
                            string windowContent = GetNotepadText(hWnd, -1); // Full content
                            // Create a unique backup file based on the process ID
                            string backupFilePath = Path.Combine(backupDir, string.Format("NotepadBackup_{0}.txt", process.Id) );
                            File.WriteAllText(backupFilePath, windowContent);
                            Console.WriteLine(string.Format("Added/Updated notepad backup of [{0}]", backupFilePath ) );
                        }
                        else {
                            // Optionally, delete existing backup if the file has been saved
                            string backupFilePath = Path.Combine(backupDir, string.Format("NotepadBackup_{0}.txt",process.Id ) );
                            if (File.Exists(backupFilePath)) {
                                File.Delete(backupFilePath);
                                Console.WriteLine(string.Format("Deteted notepad backup of [{0}]", backupFilePath ) );
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine(string.Format("Error backing up notepad process {0}: {1}", process.Id, ex.Message  ) );
                }
            }

            // Clean up backup files for processes that are no longer running
            CleanupObsoleteBackups();
        }

        // Helper method to delete backup files for processes that are no longer running
        private void CleanupObsoleteBackups()
        {
            string[] backupFiles = Directory.GetFiles(backupDir, "NotepadBackup_*.txt");
            var runningProcessIds = new HashSet<int>(Process.GetProcessesByName("notepad").Select(p => p.Id));
            foreach (string backupFile in backupFiles) {
                try {
                    // Extract the process ID from the backup file name
                    string fileName = Path.GetFileNameWithoutExtension(backupFile);
                    int processId;
                    string[] parts = fileName.Split('_');
                    if (parts.Length < 2 || !int.TryParse(parts[1], out processId)) {
                        continue; // Skip if the file name is not in the expected format
                    }
                    // If the process is no longer running, delete the backup file
                    if (!runningProcessIds.Contains(processId)) {
                        Console.WriteLine(string.Format("Process {0} is no longer running. Deleting notepad backup {1}.",processId, backupFile ) );
                        File.Delete(backupFile);
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine(string.Format("Error deleting notepad backup {0}: {1}", backupFile, ex.Message ) );
                }
            }
        }


        // Restore backups when the application starts
        private void RestoreBackupNotepadContent()
        {
            string[] backupFiles = Directory.GetFiles(backupDir, "NotepadBackup_*.txt");

            foreach (string backupFile in backupFiles) {
                try {
                    // Extract the process ID from the backup file name (e.g., NotepadBackup_39036.txt)
                    string fileName = Path.GetFileNameWithoutExtension(backupFile);
                    int processId;
                    string[] parts = fileName.Split('_');
                    if (parts.Length < 2 || !int.TryParse(parts[1], out processId) ) {
                        Console.WriteLine(string.Format("Notepad file name is not in an expected format. Restore skipped [{0}].", fileName) );
                        continue; // Skip if the file name is not in the expected format
                    }

                    // Check if the process with this ID is still running
                    bool isProcessRunning = Process.GetProcessesByName("notepad").Any(p => p.Id == processId);

                    if (isProcessRunning) {
                        // Skip restoration if the Notepad process with this ID is still running
                        Console.WriteLine(string.Format("Notepad process with ID {0} is still running. Skipping restore.", processId ) );
                        continue;
                    }

                    // If the process is not running, restore the content
                    string content = File.ReadAllText(backupFile);

                    // Restore it in a new Notepad window
                    Process notepad = Process.Start("notepad.exe");
                    notepad.WaitForInputIdle();

                    // Find the Notepad window and set its content
                    IntPtr notepadHandle = notepad.MainWindowHandle;
                    IntPtr editHandle = FindWindowEx(notepadHandle, IntPtr.Zero, "Edit", null);
                    if (editHandle != IntPtr.Zero) {
                        // Set the restored content
                        SendMessage(editHandle, WM_SETTEXT, IntPtr.Zero, new StringBuilder(content));
                        // Simulate typing a space to trigger the unsaved state (*)
                        SendMessage(editHandle, WM_CHAR, new IntPtr(' '), IntPtr.Zero); // Append space via typing simulation
                        Console.WriteLine(string.Format("Notepad restored new process ID {0}, content [{1}] ", processId, content ) );
                    }
                }
                catch (Exception ex) {
                    Console.WriteLine(string.Format("Error restoring backup: {0}.[{1}]", backupFile, ex.Message ) );
                }
            }
        } //EOF

        // Function to get the text content from the Notepad child window and limit it to the first few lines
        static string GetNotepadText(IntPtr notepadHandle, int maxLines = 2)
        {
            // Find the child window (edit control) of the Notepad window
            IntPtr editHandle = FindWindowEx(notepadHandle, IntPtr.Zero, "Edit", null);

            if (editHandle == IntPtr.Zero) {
                return "Unable to find text content.";
            }

            // Get the length of the text in the child window (edit control)
            int textLength = (int)SendMessage(editHandle, WM_GETTEXTLENGTH, IntPtr.Zero, null);

            // If there is no text, return an empty string
            if (textLength == 0) {
                return "No content available.";
            }

            // Create a StringBuilder to hold the text
            StringBuilder windowText = new StringBuilder(textLength + 1);

            // Send a message to the Notepad child window to retrieve the text
            SendMessage(editHandle, WM_GETTEXT, (IntPtr)windowText.Capacity, windowText);

            // Convert the text to string and split it into lines
            string[] lines = windowText.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            string[] sShowLines = lines;
            var nonEmptyLines = lines.SkipWhile(line => string.IsNullOrWhiteSpace(line)).ToArray();
            if (nonEmptyLines.Length != 0) {
                sShowLines = nonEmptyLines;
            }

            // Return only the first few lines (up to maxLines)
            if (maxLines == -1) {
                //return all lines
                return string.Join(Environment.NewLine, sShowLines); // Full content
            }
            return string.Join(Environment.NewLine, sShowLines, 0, Math.Min(maxLines, sShowLines.Length));
        } //EOF

        // Set up ListView columns
        private void SetupListViewColumns()
        {
            // Create the columns for ListView programmatically
            listViewNotepadWindows.Columns.Add("Process ID", 150, HorizontalAlignment.Left);
            listViewNotepadWindows.Columns.Add("Name", 250, HorizontalAlignment.Left);
            listViewNotepadWindows.Columns.Add("Text Preview", 400, HorizontalAlignment.Left);

            // Set ListView properties
            listViewNotepadWindows.View = View.Details;
            listViewNotepadWindows.FullRowSelect = true;
            listViewNotepadWindows.GridLines = true;

            // Attach column click event for sorting
            listViewNotepadWindows.ColumnClick += new ColumnClickEventHandler(ColumnClick);

            // Attach the double-click event handler for bringing the Notepad window to the front
            listViewNotepadWindows.DoubleClick += new EventHandler(ListViewItem_DoubleClick);
        }

        private void ListViewItem_DoubleClick_Windows_ID(object sender, EventArgs e)
        {
            if (listViewNotepadWindows.SelectedItems.Count > 0) {
                // Get the selected ListView item
                ListViewItem selectedItem = listViewNotepadWindows.SelectedItems[0];
                // The window handle (Windows ID) is stored in the first column (index 0)
                IntPtr notepadHandle = new IntPtr(long.Parse(selectedItem.SubItems[0].Text));

                // Check if the window handle is valid and bring the window to the front
                if (notepadHandle != IntPtr.Zero) {
                    // Restore the window if it is minimized
                    ShowWindow(notepadHandle, SW_RESTORE);
                    // Bring the Notepad window to the foreground
                    SetForegroundWindow(notepadHandle);
                }
            }
        }

        private void ListViewItem_DoubleClick(object sender, EventArgs e)
        {
            if (listViewNotepadWindows.SelectedItems.Count > 0) {
                // Get the selected ListView item
                ListViewItem selectedItem = listViewNotepadWindows.SelectedItems[0];
                // The process ID is stored in the first column (index 0)
                int processId = int.Parse(selectedItem.SubItems[0].Text);
                // Find the process with this ID
                Process process = Process.GetProcessesByName("notepad").FirstOrDefault(p => p.Id == processId);
                if (process != null) {
                    IntPtr notepadHandle = process.MainWindowHandle;
                    // Check if the window handle is valid and bring the window to the front
                    if (notepadHandle != IntPtr.Zero) {
                        // Restore the window if it is minimized
                        ShowWindow(notepadHandle, SW_RESTORE);
                        // Bring the Notepad window to the foreground
                        SetForegroundWindow(notepadHandle);
                    }
                }
            }
        } //EOF


        // Handle the column click event to sort by the clicked column
        private void ColumnClick(object o, ColumnClickEventArgs e)
        {
            // Determine if the clicked column is already the column that is being sorted.
            if (e.Column == sortColumn) {
                // Reverse the current sort direction for this column.
                if (listViewNotepadWindows.Sorting == SortOrder.Ascending ) {
                    listViewNotepadWindows.Sorting = SortOrder.Descending;
                }
                else {
                    listViewNotepadWindows.Sorting = SortOrder.Ascending;
                }
            }
            else {
                // Set the column number that is to be sorted; default to ascending.
                sortColumn = e.Column;
                listViewNotepadWindows.Sorting = SortOrder.Ascending;
            }

            // Set the ListViewItemSorter property to a new ListViewItemComparer
            listViewNotepadWindows.ListViewItemSorter = new ListViewItemComparer(e.Column, listViewNotepadWindows.Sorting);
            // Perform the sort with the new sort options.
            listViewNotepadWindows.Sort();
        }

        // Custom comparer for sorting ListView items by columns (including numeric sorting for Windows ID)
        class ListViewItemComparer : IComparer
        {
            private int col;
            private SortOrder order;

            public ListViewItemComparer(int column, SortOrder order) {
                col = column;
                this.order = order;
            }

            public int Compare(object x, object y) {
                int returnVal = -1;

                // Check if we are sorting by the "Windows/Process ID" column (index 0), which needs numeric sorting
                if (col == 0)
                {
                    // Parse the Windows ID as long and compare numerically
                    long id1 = long.Parse(((ListViewItem)x).SubItems[col].Text);
                    long id2 = long.Parse(((ListViewItem)y).SubItems[col].Text);
                    returnVal = id1.CompareTo(id2);
                }
                else
                {
                    // For other columns, perform string comparison
                    returnVal = String.Compare(((ListViewItem)x).SubItems[col].Text,
                                               ((ListViewItem)y).SubItems[col].Text);
                }

                // If descending order is required, reverse the result
                if (order == SortOrder.Descending)
                    returnVal *= -1;

                return returnVal;
            }
        } //EOC

        private void ListViewItem_SelectedIndexChanged_Windows_ID(object sender, EventArgs e)
        {
            if (listViewNotepadWindows.SelectedItems.Count > 0) {
                // Get the selected ListView item
                ListViewItem selectedItem = listViewNotepadWindows.SelectedItems[0];
                // The window handle (Windows ID) is stored in the first column (index 0)
                IntPtr notepadHandle = new IntPtr(long.Parse(selectedItem.SubItems[0].Text));

                if (notepadHandle != IntPtr.Zero) {
                    // Get the content of the Notepad window (limit to first 500 lines)
                    string notepadContent = GetNotepadText(notepadHandle, 500);

                    // Set the content to the TextBox
                    textBoxNotepadContent.Text = notepadContent;
                }
            }
        }

        private void ListViewItem_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listViewNotepadWindows.SelectedItems.Count > 0) {
                // Get the selected ListView item
                ListViewItem selectedItem = listViewNotepadWindows.SelectedItems[0];
                // The process ID is stored in the first column (index 0)
                int processId = int.Parse(selectedItem.SubItems[0].Text);
                // Find the process with this ID
                Process process = Process.GetProcessesByName("notepad").FirstOrDefault(p => p.Id == processId);
                if (process != null) {
                    IntPtr notepadHandle = process.MainWindowHandle;
                    if (notepadHandle != IntPtr.Zero)
                    {
                        // Get the content of the Notepad window (limit to first 500 lines)
                        string notepadContent = GetNotepadText(notepadHandle, 500);
                        // Set the content to the RichTextBox (or TextBox)
                        textBoxNotepadContent.Text = notepadContent;
                    }
                }
            } //EIF
        } //EOF


        private void TextBoxSearch_TextChanged(object sender, EventArgs e)
        {
            string searchText = textBoxSearch.Text.ToLower();

            // Clear the existing list before updating
            listViewNotepadWindows.Items.Clear();

            // Get all running Notepad processes
            Process[] processes = Process.GetProcessesByName("notepad");

            // Iterate over each Notepad process
            foreach (Process process in processes) {
                IntPtr hWnd = process.MainWindowHandle;

                if (hWnd != IntPtr.Zero && IsWindowVisible(hWnd)) {
                    // Retrieve the window title
                    StringBuilder windowTitle = new StringBuilder(256);
                    GetWindowText(hWnd, windowTitle, windowTitle.Capacity);
                    // Get the actual text content from the Notepad child window (limited to the first 500 lines)
                    string windowContent = GetNotepadText(hWnd, 500); // Search within the full content
                    // Check if the content or title contains the search text
                    if (windowContent.ToLower().Contains(searchText) || windowTitle.ToString().ToLower().Contains(searchText) ) {
                        // Add the window/process details to the ListView
                        //ListViewItem item = new ListViewItem(new[] { hWnd.ToInt64().ToString(), windowTitle.ToString(), windowContent });
                        ListViewItem item = new ListViewItem(new[] { process.Id.ToString(), windowTitle.ToString(), windowContent } );
                        item.Tag = windowContent;
                        listViewNotepadWindows.Items.Add(item);
                    }
                }
            } //EFOR

            // Sort the list after filtering
            listViewNotepadWindows.Sort();

            // If a Notepad window is selected, highlight the text in the content box
            if (listViewNotepadWindows.SelectedItems.Count > 0) {
                ListViewItem selectedItem = listViewNotepadWindows.SelectedItems[0];
                string notepadContent = selectedItem.Tag as string;
                textBoxNotepadContent.Text = notepadContent;
                HighlightSearchText(searchText);
            } //EIF

        } //EOF

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(NotepadsManager));
            this.refresh = new System.Windows.Forms.Button();
            //
            //this.textBoxSearch = new System.Windows.Forms.TextBox();
            this.textBoxSearch = new MaterialSkin.Controls.MaterialTextBox2();
            this.clearSearchButton = new System.Windows.Forms.PictureBox();
            //
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.listViewNotepadWindows = new System.Windows.Forms.ListView();
            //
            //this.textBoxNotepadContent = new System.Windows.Forms.TextBox();
            this.textBoxNotepadContent = new System.Windows.Forms.RichTextBox(); 
            //
            this.panelTop = new System.Windows.Forms.Panel();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.panelTop.SuspendLayout();
            this.SuspendLayout();
            // 
            // refresh
            // 
            this.refresh.Dock = System.Windows.Forms.DockStyle.Top;
            this.refresh.Location = new System.Drawing.Point(0, 0);
            this.refresh.Name = "refresh";
            this.refresh.Size = new System.Drawing.Size(850, 23);
            this.refresh.TabIndex = 0;
            this.refresh.Text = "Refresh";
            this.refresh.UseVisualStyleBackColor = true;
            this.refresh.Click += new System.EventHandler(this.btnRefresh_Click);
            // 
            // textBoxSearch
            //// 
            //this.textBoxSearch.Dock = System.Windows.Forms.DockStyle.Top;
            //this.textBoxSearch.Location = new System.Drawing.Point(0, 23);
            //this.textBoxSearch.Name = "textBoxSearch";
            //this.textBoxSearch.Size = new System.Drawing.Size(850, 20);
            //this.textBoxSearch.Hint = "Search...";
            //this.textBoxSearch.TabIndex = 1;
            //this.textBoxSearch.TextChanged += new System.EventHandler(this.TextBoxSearch_TextChanged);
            // NEW
            this.textBoxSearch.Dock = System.Windows.Forms.DockStyle.Top;
            this.textBoxSearch.Location = new System.Drawing.Point(0, 23);
            this.textBoxSearch.Name = "textBoxSearch";
            this.textBoxSearch.Font = new System.Drawing.Font("Roboto", 8F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
            this.textBoxSearch.Margin = new System.Windows.Forms.Padding(0);
            this.textBoxSearch.Size = new System.Drawing.Size(850, 18); // Adjusted height to reduce gap and align to top
            this.textBoxSearch.Hint = "Search...";
            this.textBoxSearch.LeadingIcon = null;  // Optional: Set this if you want a leading icon
            //this.textBoxSearch.TrailingIcon = Properties.Resources.clear_icon;  // Your clear icon image
            //this.textBoxSearch.MaxLength = 50;  // Adjust this as needed
            this.textBoxSearch.MouseState = MaterialSkin.MouseState.OUT;
            this.textBoxSearch.TabIndex = 1;
            this.textBoxSearch.TrailingIconClick += new System.EventHandler(this.ClearSearchBox);  // Event for clear button
            this.textBoxSearch.TextChanged += new System.EventHandler(this.TextBoxSearch_TextChanged);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 43);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.listViewNotepadWindows);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.textBoxNotepadContent);
            this.splitContainer1.Size = new System.Drawing.Size(850, 507);
            this.splitContainer1.SplitterDistance = 283;
            this.splitContainer1.TabIndex = 4;
            // 
            // listViewNotepadWindows
            // 
            this.listViewNotepadWindows.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listViewNotepadWindows.FullRowSelect = true;
            this.listViewNotepadWindows.GridLines = true;
            this.listViewNotepadWindows.Location = new System.Drawing.Point(0, 0);
            this.listViewNotepadWindows.Name = "listViewNotepadWindows";
            this.listViewNotepadWindows.Size = new System.Drawing.Size(283, 507);
            this.listViewNotepadWindows.TabIndex = 4;
            this.listViewNotepadWindows.UseCompatibleStateImageBehavior = false;
            this.listViewNotepadWindows.View = System.Windows.Forms.View.Details;
            this.listViewNotepadWindows.SelectedIndexChanged += new System.EventHandler(this.ListViewItem_SelectedIndexChanged);
            // 
            // textBoxNotepadContent
            // 
            this.textBoxNotepadContent.Dock = System.Windows.Forms.DockStyle.Fill;
            this.textBoxNotepadContent.Location = new System.Drawing.Point(0, 0);
            this.textBoxNotepadContent.Multiline = true;
            this.textBoxNotepadContent.Name = "textBoxNotepadContent";
            this.textBoxNotepadContent.ReadOnly = true;
            //this.textBoxNotepadContent.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBoxNotepadContent.ScrollBars = RichTextBoxScrollBars.Vertical;
            this.textBoxNotepadContent.Size = new System.Drawing.Size(563, 507);
            this.textBoxNotepadContent.TabIndex = 3;
            // 
            // panelTop
            // 
            this.panelTop.Controls.Add(this.textBoxSearch);
            this.panelTop.Controls.Add(this.refresh);
            this.panelTop.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelTop.Location = new System.Drawing.Point(0, 0);
            this.panelTop.Name = "panelTop";
            this.panelTop.Size = new System.Drawing.Size(850, 70);
            this.panelTop.TabIndex = 5;
            // 
            // NotepadsManager
            // 
            this.ClientSize = new System.Drawing.Size(850, 550);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.panelTop);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "NotepadsManager";
            this.Text = "Notepad Manager";
            this.Load += new System.EventHandler(this.NotepadManager_Load);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.panelTop.ResumeLayout(false);
            this.panelTop.PerformLayout();
            this.ResumeLayout(false);

        }

        private void ClearSearchBox(object sender, EventArgs e)
        {
            textBoxSearch.Text = string.Empty;  // Clears the search box
        }

        private void HighlightSearchText(string searchText)
        {
            // Reset all formatting
            textBoxNotepadContent.SelectAll();
            textBoxNotepadContent.SelectionBackColor = textBoxNotepadContent.BackColor;
            textBoxNotepadContent.SelectionColor = textBoxNotepadContent.ForeColor;
            textBoxNotepadContent.SelectionFont = textBoxNotepadContent.Font;

            if (string.IsNullOrEmpty(searchText))
                return;

            string content = textBoxNotepadContent.Text.ToLower();
            int startIndex = 0;

            // Search and highlight all occurrences of the search text
            while ((startIndex = content.IndexOf(searchText.ToLower(), startIndex)) != -1)
            {
                // Select the found text
                textBoxNotepadContent.Select(startIndex, searchText.Length);

                // Apply formatting (bold blue)
                textBoxNotepadContent.SelectionColor = Color.Blue;
                textBoxNotepadContent.SelectionFont = new Font(textBoxNotepadContent.Font, FontStyle.Bold);

                // Move past the current found item for further searching
                startIndex += searchText.Length;
            }

            // Reset selection to the start of the text
            textBoxNotepadContent.Select(0, 0);

            // Force the control to refresh to reflect the changes
            textBoxNotepadContent.Refresh();
        }


        private void NotepadManager_Load(object sender, EventArgs e)
        {
            this.Icon = ManageCMDWindows.Properties.Resources.notepad_manager_icon;

            // Get the screen dimensions
            int screenWidth = Screen.PrimaryScreen.WorkingArea.Width;
            int screenHeight = Screen.PrimaryScreen.WorkingArea.Height;

            // Set the form size to 60% of the screen's width and 50% of the screen's height
            this.Width = (int)(screenWidth * 0.8);
            this.Height = (int)(screenHeight * 0.7);

            // Center the form on the screen
            this.StartPosition = FormStartPosition.CenterScreen;

            // Set the split ratio (70% for Panel1, 30% for Panel2)
            splitContainer1.SplitterDistance = (int)(this.splitContainer1.Width * 0.6);

            // Automatically populate the list of Notepad windows on form load
            PopulateNotepadWindows();

            // Automatically sort by the "Name" column (index 1) in ascending order
            listViewNotepadWindows.ListViewItemSorter = new ListViewItemComparer(1, SortOrder.Ascending);
            listViewNotepadWindows.Sort();
            //Invalidate to redraw
            textBoxSearch.Invalidate();
        }

    }
}
