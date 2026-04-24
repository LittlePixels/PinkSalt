using Markdig;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using WinForms = System.Windows.Forms;
using Path = System.IO.Path; // This line fixes the ambiguity
using Microsoft.Win32;

namespace PinkSalt
{
    // MainWindow.xaml.cs
    public partial class MainWindow : Window
    {
        private NoteManager noteManager;
        private ObservableCollection<Note> notes;
        private ObservableCollection<string> folders;
        private Note? currentNote;

        // Debounce timer for the live preview. The WPF WebBrowser is heavy
        // (it's IE-based) and calling NavigateToString on every keystroke
        // causes visible stutter and dropped renders during fast typing.
        // 150ms feels instant to a human but lets bursts of keystrokes
        // collapse into a single render.
        private readonly DispatcherTimer previewTimer;

        // Suppresses the inline-title-rename logic while we're programmatically
        // setting TitleTextBox.Text inside SelectNote (otherwise switching
        // notes would look like a rename of the previous note).
        private bool suppressTitleSync;

        public MainWindow()
        {
            InitializeComponent();

            noteManager = new NoteManager();
            notes = new ObservableCollection<Note>(noteManager.LoadNotes());
            folders = new ObservableCollection<string>(noteManager.GetFolders());

            NotesList.ItemsSource = notes;
            FolderList.ItemsSource = folders;

            SearchBox.TextChanged += SearchBox_TextChanged;
            SearchBox.Text = "Search...";

            MarkdownEditor.TextChanged += MarkdownEditor_TextChanged;

            // Debounced preview rendering.
            previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            previewTimer.Tick += (s, e) =>
            {
                previewTimer.Stop();
                RenderMarkdownPreview();
            };

            // Inline rename wiring (the TitleTextBox already exists in XAML).
            TitleTextBox.TextChanged += TitleTextBox_TextChanged;
            TitleTextBox.LostFocus += TitleTextBox_LostFocus;
            TitleTextBox.KeyDown += TitleTextBox_KeyDown;

            // F2 to rename, from anywhere in the window.
            PreviewKeyDown += MainWindow_PreviewKeyDown;

            // Ensure keyboard shortcuts and text entered handlers are wired up
            ConfigureMarkdownEditor();

            MarkdownEditor.Visibility = Visibility.Visible;

            UpdateNoteGraph();
        }

        // Fires on every keystroke in the editor. We do the cheap work
        // synchronously (keep currentNote.Content in sync so saves use the
        // latest text) and schedule the expensive preview render via the
        // debounce timer.
        private void MarkdownEditor_TextChanged(object sender, EventArgs e)
        {
            if (currentNote != null)
            {
                currentNote.Content = MarkdownEditor.Text;
            }

            // Restart the debounce window. If the user keeps typing, the
            // timer keeps getting pushed out and we don't render until they
            // pause for 150ms.
            previewTimer.Stop();
            previewTimer.Start();
        }

        private void ConfigureMarkdownEditor()
        {
            // Keyboard shortcuts for Markdown formatting
            MarkdownEditor.TextArea.KeyDown += MarkdownEditor_KeyDown;
            MarkdownEditor.TextArea.TextEntered += MarkdownEditor_TextEntered;
        }

        private void NewNote_Click(object sender, RoutedEventArgs e)
        {
            var note = new Note
            {
                Id = Guid.NewGuid(),
                Title = "Untitled Note",
                Content = "",
                Created = DateTime.Now,
                Modified = DateTime.Now,
                Folder = FolderList.SelectedItem as string ?? "Default",
                Tags = new List<string>()
            };
            notes.Add(note);
            noteManager.SaveNote(note);
            SelectNote(note);
            UpdateNoteGraph();
        }

        private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NotesList.SelectedItem is Note note)
            {
                SelectNote(note);
            }
        }

        private void SelectNote(Note note)
        {
            SaveCurrentNote();

            // Programmatic UI updates below — don't treat them as user edits.
            suppressTitleSync = true;
            try
            {
                currentNote = note;
                TitleTextBox.Text = note.Title;
                MarkdownEditor.Text = note.Content;
                TagsTextBox.Text = string.Join(", ", note.Tags);
                FolderCombo.Text = note.Folder;
                EditorPanel.Visibility = Visibility.Visible;
                WelcomePanel.Visibility = Visibility.Collapsed;
            }
            finally
            {
                suppressTitleSync = false;
            }

            // Render immediately when switching notes (no debounce delay).
            previewTimer.Stop();
            RenderMarkdownPreview();
        }

        private void SaveCurrentNote()
        {
            if (currentNote != null)
            {
                currentNote.Title = TitleTextBox.Text;
                currentNote.Content = MarkdownEditor.Text;
                currentNote.Modified = DateTime.Now;
                currentNote.Folder = FolderCombo.Text;
                currentNote.Tags = TagsTextBox.Text
                    .Split(',')
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList();

                noteManager.SaveNote(currentNote);
                NotesList.Items.Refresh();
            }
        }

        // ---------------------------------------------------------------
        // Rename: inline edit
        // ---------------------------------------------------------------

        // Live-update the in-memory note title as the user types in the
        // title box, so the sidebar list reflects the new name immediately.
        // We do NOT save to disk on every keystroke — that happens on
        // LostFocus / Enter / SaveCurrentNote.
        private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (suppressTitleSync) return;
            if (currentNote == null) return;

            currentNote.Title = TitleTextBox.Text;
            NotesList.Items.Refresh();
        }

        private void TitleTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitTitleEdit();
        }

        private void TitleTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitTitleEdit();
                MarkdownEditor.Focus(); // jump to the editor, which feels natural
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && currentNote != null)
            {
                // Revert to the saved title.
                suppressTitleSync = true;
                TitleTextBox.Text = currentNote.Title;
                suppressTitleSync = false;
                MarkdownEditor.Focus();
                e.Handled = true;
            }
        }

        private void CommitTitleEdit()
        {
            if (currentNote == null) return;

            var newTitle = TitleTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newTitle))
            {
                // Refuse blank titles — restore previous.
                suppressTitleSync = true;
                TitleTextBox.Text = currentNote.Title;
                suppressTitleSync = false;
                return;
            }

            currentNote.Title = newTitle;
            noteManager.SaveNote(currentNote);
            NotesList.Items.Refresh();
        }

        // ---------------------------------------------------------------
        // Rename: button + F2 shortcut (dialog flow)
        // ---------------------------------------------------------------

        private void RenameNote_Click(object sender, RoutedEventArgs e)
        {
            ShowRenameDialog();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F2 && currentNote != null)
            {
                ShowRenameDialog();
                e.Handled = true;
            }
        }

        private void ShowRenameDialog()
        {
            if (currentNote == null) return;

            var dialog = new RenameDialog(currentNote.Title) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                var newTitle = dialog.NewTitle?.Trim();
                if (string.IsNullOrWhiteSpace(newTitle)) return;

                suppressTitleSync = true;
                TitleTextBox.Text = newTitle;
                suppressTitleSync = false;

                currentNote.Title = newTitle;
                noteManager.SaveNote(currentNote);
                NotesList.Items.Refresh();
                UpdateNoteGraph(); // titles are used for [[wiki link]] resolution
            }
        }

        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (currentNote != null)
            {
                var result = MessageBox.Show("Delete this note?", "Confirm Delete",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    noteManager.DeleteNote(currentNote.Id);
                    notes.Remove(currentNote);
                    currentNote = null;
                    EditorPanel.Visibility = Visibility.Collapsed;
                    WelcomePanel.Visibility = Visibility.Visible;
                    UpdateNoteGraph();
                }
            }
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentNote();
            MessageBox.Show("Note saved.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BackupToFolder_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentNote();

            var dialog = new WinForms.FolderBrowserDialog
            {
                Description = "Select a folder to export your Pink Salt backup to:"
            };

            var result = dialog.ShowDialog();
            if (result != WinForms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return; // user cancelled
            }

            // Create a timestamped folder inside the selected export directory
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportPath = Path.Combine(dialog.SelectedPath, $"PinkSaltExport_{timestamp}");
            Directory.CreateDirectory(exportPath);

            // Reuse the existing backup logic, then copy that backup to the export path
            var backupPath = noteManager.CreateBackup();

            foreach (var file in Directory.GetFiles(backupPath, "*.json"))
            {
                var fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(exportPath, fileName), overwrite: true);
            }

            MessageBox.Show($"Backup exported to:\n{exportPath}", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (notes == null || NotesList == null) return;

            var query = SearchBox.Text.ToLower();
            if (string.IsNullOrWhiteSpace(query) || query == "search...")
            {
                NotesList.ItemsSource = notes;
                return;
            }

            var filtered = notes.Where(n =>
                n.Title.ToLower().Contains(query) ||
                n.Content.ToLower().Contains(query) ||
                n.Tags.Any(t => t.ToLower().Contains(query))).ToList();
            NotesList.ItemsSource = filtered;
        }

        // Toolbar button handlers
        private void BoldBtn_Click(object sender, RoutedEventArgs e) => ApplyInlineFormatting("**", "**");

        private void ItalicBtn_Click(object sender, RoutedEventArgs e) => ApplyInlineFormatting("*", "*");

        private void CodeBtn_Click(object sender, RoutedEventArgs e) => ApplyInlineFormatting("`", "`");

        private void LinkBtn_Click(object sender, RoutedEventArgs e) => ApplyInlineFormatting("[[", "]]");

        private void HeadingBtn_Click(object sender, RoutedEventArgs e) => ApplyHeadingAtCurrentLine();

        private void ListBtn_Click(object sender, RoutedEventArgs e) => ToggleListAtCurrentLine();

        private void MarkdownEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.B:
                        ApplyInlineFormatting("**", "**");
                        e.Handled = true;
                        break;
                    case Key.I:
                        ApplyInlineFormatting("*", "*");
                        e.Handled = true;
                        break;
                    case Key.K:
                        ApplyInlineFormatting("`", "`");
                        e.Handled = true;
                        break;
                    case Key.L:
                        ApplyInlineFormatting("[[", "]]");
                        e.Handled = true;
                        break;
                }
            }
        }

        private void MarkdownEditor_TextEntered(object sender, TextCompositionEventArgs e)
        {
            if (e.Text == "\n")
            {
                AutoContinueList();
            }
        }

        private void ApplyInlineFormatting(string prefix, string suffix)
        {
            var editor = MarkdownEditor;
            var textArea = editor.TextArea;
            var selection = textArea.Selection;

            if (selection.IsEmpty)
            {
                int caretOffset = textArea.Caret.Offset;
                editor.Document.Insert(caretOffset, prefix + suffix);
                textArea.Caret.Offset = caretOffset + prefix.Length;
            }
            else
            {
                string selectedText = selection.GetText();
                var segment = selection.SurroundingSegment;
                editor.Document.Replace(segment, prefix + selectedText + suffix);
            }
        }

        private void ApplyHeadingAtCurrentLine()
        {
            var editor = MarkdownEditor;
            var doc = editor.Document;
            var line = doc.GetLineByNumber(editor.TextArea.Caret.Line);
            string text = doc.GetText(line).TrimStart();

            if (!text.StartsWith("# "))
            {
                doc.Insert(line.Offset, "# ");
            }
        }

        private void ToggleListAtCurrentLine()
        {
            var editor = MarkdownEditor;
            var doc = editor.Document;
            var line = doc.GetLineByNumber(editor.TextArea.Caret.Line);
            string lineText = doc.GetText(line);
            string trimmed = lineText.TrimStart();

            const string bullet = "- ";
            if (trimmed.StartsWith(bullet))
            {
                // remove bullet
                int index = lineText.IndexOf(bullet, StringComparison.Ordinal);
                if (index >= 0)
                {
                    doc.Remove(line.Offset + index, bullet.Length);
                }
            }
            else
            {
                doc.Insert(line.Offset, bullet);
            }
        }

        private void AutoContinueList()
        {
            var editor = MarkdownEditor;
            var doc = editor.Document;
            var caret = editor.TextArea.Caret;

            if (caret.Line <= 1) return;

            var prevLine = doc.GetLineByNumber(caret.Line - 1);
            string prevText = doc.GetText(prevLine);
            string trimmed = prevText.TrimStart();

            string prefix = string.Empty;

            // unordered lists
            if (trimmed.StartsWith("- ")) prefix = "- ";
            else if (trimmed.StartsWith("* ")) prefix = "* ";
            // numbered list: "1. ", "2. " etc.
            else
            {
                var match = System.Text.RegularExpressions.Regex.Match(trimmed, @"^(\d+)\. ");
                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out int num))
                    {
                        prefix = (num + 1) + ". ";
                    }
                }
            }

            if (!string.IsNullOrEmpty(prefix))
            {
                doc.Insert(caret.Offset, prefix);
            }
        }

        // The actual render call. Renamed from UpdateMarkdownPreview so the
        // call sites that want a "right now" render (SelectNote) and the
        // ones that want debounced rendering (typing) are clearly separated.
        // Pulls source-of-truth from the editor itself, not currentNote.Content
        // — that way it works even if the in-memory sync is ever out of step.
        private void RenderMarkdownPreview()
        {
            // Allow rendering even with no currentNote (e.g. a "scratch" view),
            // but if both are missing, bail.
            string source = MarkdownEditor?.Text ?? string.Empty;
            if (currentNote == null && string.IsNullOrEmpty(source)) return;

            var pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()   // tables, footnotes, task lists, etc.
                .UseEmojiAndSmiley()
                .UseTaskLists()
                .Build();

            var html = Markdown.ToHtml(source, pipeline);

            // Process [[note links]]
            html = Regex.Replace(html, @"\[\[([^\]]+)\]\]", match =>
            {
                var linkText = match.Groups[1].Value;
                return $"<a href='note://{linkText}' style='color: #FFB6C1; text-decoration: underline;'>{linkText}</a>";
            });

            var styledHtml = $@"
                <html>
                <head>
                    <style>
                        body {{ font-family: Segoe UI, Arial; padding: 20px; line-height: 1.6; background: #FFFFFF; color: #FF4B7F; }}
                        h1 {{ color: #FF4B7F; border-bottom: 1px solid #FFE0F0; padding-bottom: 4px; }}
                        h2 {{ color: #FF6C9B; margin-top: 18px; }}
                        h3 {{ color: #FF6C9B; margin-top: 16px; font-weight: 600; }}
                        a {{ color: #FF4B7F; text-decoration: underline; }}
                        ul, ol {{ margin-left: 22px; }}
                        li {{ margin: 2px 0; }}
                        /* GitHub-style checkboxes */
                        input[type='checkbox'] {{ margin-right: 6px; accent-color: #FF6C9B; }}
                        code {{ background: #FFFFF0F7; padding: 2px 6px; border-radius: 3px; font-family: Consolas, monospace; color: #FF4B7F; }}
                        pre {{ background: #FFFFF5FA; color: #FF4B7F; padding: 10px; border-radius: 6px; overflow-x: auto; }}
                        pre code {{ background: transparent; padding: 0; }}
                        table {{ border-collapse: collapse; margin-top: 10px; }}
                        th, td {{ border: 1px solid #FFE0F0; padding: 6px 10px; }}
                        th {{ background: #FFFFF0F7; }}
                        blockquote {{ border-left: 4px solid #FFB6C1; padding-left: 15px; margin-left: 0; color: #FF6C9B; background:#FFFFF5FA; }}
                    </style>
                </head>
                <body>{html}</body>
                </html>";

            // Fix image src to absolute file paths for local images
            styledHtml = Regex.Replace(styledHtml, @"<img src=""(Images/[^""]+)""", match =>
            {
                string rel = match.Groups[1].Value.Replace("/", "\\");
                string abs = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "PinkSaltNotes", rel);
                return $@"<img src=""file:///{abs.Replace("\\", "/")}""";
            });

            try
            {
                PreviewBrowser.NavigateToString(styledHtml);
            }
            catch
            {
                // NavigateToString can throw if called while a previous
                // navigation is still in flight. Swallow — the next debounce
                // tick will render the latest text anyway.
            }
        }

        // Kept for backwards compatibility with any other call sites.
        private void UpdateMarkdownPreview() => RenderMarkdownPreview();

        private void Backup_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentNote();
            var backupPath = noteManager.CreateBackup();
            MessageBox.Show($"Backup created at:\n{backupPath}", "Backup Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowGraph_Click(object sender, RoutedEventArgs e)
        {
            GraphPanel.Visibility = GraphPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (GraphPanel.Visibility == Visibility.Visible)
            {
                UpdateNoteGraph();
            }
        }

        private void UpdateNoteGraph()
        {
            GraphCanvas.Children.Clear();

            var links = new Dictionary<Guid, List<string>>();
            foreach (var note in notes)
            {
                var matches = Regex.Matches(note.Content, @"\[\[([^\]]+)\]\]");
                links[note.Id] = matches.Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToList();
            }

            double centerX = GraphCanvas.ActualWidth / 2;
            double centerY = GraphCanvas.ActualHeight / 2;
            double radius = Math.Min(centerX, centerY) * 0.7;

            var positions = new Dictionary<Guid, Point>();
            for (int i = 0; i < notes.Count; i++)
            {
                double angle = 2 * Math.PI * i / notes.Count;
                double x = centerX + radius * Math.Cos(angle);
                double y = centerY + radius * Math.Sin(angle);
                positions[notes[i].Id] = new Point(x, y);
            }

            // Draw links
            foreach (var note in notes)
            {
                if (!links.ContainsKey(note.Id)) continue;

                foreach (var linkName in links[note.Id])
                {
                    var targetNote = notes.FirstOrDefault(n =>
                        n.Title.Equals(linkName, StringComparison.OrdinalIgnoreCase));

                    if (targetNote != null && positions.ContainsKey(targetNote.Id))
                    {
                        var line = new Line
                        {
                            X1 = positions[note.Id].X,
                            Y1 = positions[note.Id].Y,
                            X2 = positions[targetNote.Id].X,
                            Y2 = positions[targetNote.Id].Y,
                            Stroke = new SolidColorBrush(Color.FromRgb(255, 182, 193)),
                            StrokeThickness = 2,
                            Opacity = 0.5
                        };
                        GraphCanvas.Children.Add(line);
                    }
                }
            }

            // Draw nodes
            foreach (var note in notes)
            {
                if (!positions.ContainsKey(note.Id)) continue;

                var ellipse = new Ellipse
                {
                    Width = 40,
                    Height = 40,
                    Fill = new SolidColorBrush(Color.FromRgb(255, 182, 193)),
                    Stroke = Brushes.White,
                    StrokeThickness = 2
                };

                Canvas.SetLeft(ellipse, positions[note.Id].X - 20);
                Canvas.SetTop(ellipse, positions[note.Id].Y - 20);
                ellipse.ToolTip = note.Title;

                ellipse.MouseDown += (s, e) =>
                {
                    NotesList.SelectedItem = note;
                };

                GraphCanvas.Children.Add(ellipse);
            }
        }

        private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FolderList.SelectedItem is string folder)
            {
                var filtered = notes.Where(n => n.Folder == folder).ToList();
                NotesList.ItemsSource = new ObservableCollection<Note>(filtered);
            }
        }

        private void ShowAllNotes_Click(object sender, RoutedEventArgs e)
        {
            NotesList.ItemsSource = notes;
            FolderList.SelectedIndex = -1;
        }

        private void GraphCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (GraphPanel.Visibility == Visibility.Visible)
            {
                UpdateNoteGraph();
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            SaveCurrentNote();
        }

        private void InsertImageBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Insert Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                string imagesDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "PinkSaltNotes", "Images");
                Directory.CreateDirectory(imagesDir);

                string fileName = Path.GetFileName(dialog.FileName);
                string destPath = Path.Combine(imagesDir, fileName);

                // Avoid overwriting existing images
                int count = 1;
                string nameOnly = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                while (File.Exists(destPath))
                {
                    destPath = Path.Combine(imagesDir, $"{nameOnly}_{count}{ext}");
                    count++;
                }
                File.Copy(dialog.FileName, destPath);

                // Insert Markdown image syntax at caret
                string relPath = $"Images/{Path.GetFileName(destPath)}";
                string markdown = $"![Image]({relPath})";
                var editor = MarkdownEditor;
                var caret = editor.TextArea.Caret.Offset;
                editor.Document.Insert(caret, markdown);
                editor.TextArea.Caret.Offset = caret + markdown.Length;
            }
        }
    }

    // Note.cs
    public class Note : INotifyPropertyChanged
    {
        private string title = string.Empty;
        private string content = string.Empty;

        public Guid Id { get; set; }

        public string Title
        {
            get => title;
            set
            {
                title = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        public string Content
        {
            get => content;
            set
            {
                content = value;
                OnPropertyChanged(nameof(Content));
            }
        }

        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public string Folder { get; set; } = "Default";
        public List<string> Tags { get; set; } = new List<string>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // NoteManager.cs
    public class NoteManager
    {
        private readonly string notesFolder;
        private readonly string backupFolder;

        public NoteManager() : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "PinkSaltNotes"))
        {
        }

        // Accepts a custom base path — used by tests to avoid touching Documents.
        public NoteManager(string notesFolder)
        {
            this.notesFolder = notesFolder;
            backupFolder = Path.Combine(notesFolder, "Backups");
            Directory.CreateDirectory(notesFolder);
            Directory.CreateDirectory(backupFolder);
        }

        public void SaveNote(Note note)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(note);
            var filePath = Path.Combine(notesFolder, $"{note.Id}.json");
            File.WriteAllText(filePath, json);
        }

        public Note[] LoadNotes()
        {
            var files = Directory.GetFiles(notesFolder, "*.json");
            return files.Select(file =>
            {
                try
                {
                    var json = File.ReadAllText(file);
                    return System.Text.Json.JsonSerializer.Deserialize<Note>(json);
                }
                catch
                {
                    return null;
                }
            })
            .Where(n => n != null)
            .OrderByDescending(n => n!.Modified)
            .ToArray()!;
        }

        public void DeleteNote(Guid id)
        {
            var filePath = Path.Combine(notesFolder, $"{id}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        public string[] GetFolders()
        {
            var notes = LoadNotes();
            return notes.Select(n => n.Folder)
                .Distinct()
                .OrderBy(f => f)
                .ToArray();
        }

        public string CreateBackup()
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(backupFolder, $"backup_{timestamp}");
            Directory.CreateDirectory(backupPath);

            var files = Directory.GetFiles(notesFolder, "*.json");
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(backupPath, fileName));
            }

            return backupPath;
        }
    }

    // ---------------------------------------------------------------
    // Small modal dialog used by the Rename button / F2 shortcut.
    // Code-only WPF window so you don't need a separate XAML file.
    // ---------------------------------------------------------------
    internal class RenameDialog : Window
    {
        private readonly TextBox input;
        public string? NewTitle { get; private set; }

        public RenameDialog(string currentTitle)
        {
            Title = "Rename Note";
            Width = 380;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = "New name:",
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x4B, 0x7F))
            };
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            input = new TextBox
            {
                Text = currentTitle,
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xF0)),
                BorderThickness = new Thickness(1)
            };
            input.SelectAll();
            input.Focus();
            input.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { Confirm(); e.Handled = true; }
                else if (e.Key == Key.Escape) { DialogResult = false; Close(); e.Handled = true; }
            };
            Grid.SetRow(input, 1);
            grid.Children.Add(input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };
            var ok = new Button
            {
                Content = "Rename",
                Width = 80,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            ok.Click += (s, e) => Confirm();
            var cancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                IsCancel = true
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            Content = grid;

            // Focus the textbox after the window is shown.
            Loaded += (s, e) =>
            {
                input.Focus();
                input.SelectAll();
            };
        }

        private void Confirm()
        {
            var t = input.Text?.Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                DialogResult = false;
            }
            else
            {
                NewTitle = t;
                DialogResult = true;
            }
            Close();
        }
    }
}
