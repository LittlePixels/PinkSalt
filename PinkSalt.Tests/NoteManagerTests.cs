using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PinkSalt;
using Xunit;

namespace PinkSalt.Tests;

public class NoteManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly NoteManager _manager;

    public NoteManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"PinkSaltTests_{Guid.NewGuid():N}");
        _manager = new NoteManager(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---- SaveNote ----

    [Fact]
    public void SaveNote_CreatesJsonFileNamedByNoteId()
    {
        var note = MakeNote();
        _manager.SaveNote(note);
        Assert.True(File.Exists(Path.Combine(_tempDir, $"{note.Id}.json")));
    }

    [Fact]
    public void SaveNote_WritesValidDeserializableJson()
    {
        var note = MakeNote(title: "Test Title", content: "Hello world");
        _manager.SaveNote(note);

        var json = File.ReadAllText(Path.Combine(_tempDir, $"{note.Id}.json"));
        var loaded = JsonSerializer.Deserialize<Note>(json);

        Assert.NotNull(loaded);
        Assert.Equal(note.Id, loaded!.Id);
        Assert.Equal("Test Title", loaded.Title);
        Assert.Equal("Hello world", loaded.Content);
    }

    [Fact]
    public void SaveNote_OverwritesExistingFileOnResave()
    {
        var note = MakeNote(title: "Original");
        _manager.SaveNote(note);

        note.Title = "Updated";
        _manager.SaveNote(note);

        var json = File.ReadAllText(Path.Combine(_tempDir, $"{note.Id}.json"));
        var loaded = JsonSerializer.Deserialize<Note>(json);
        Assert.Equal("Updated", loaded!.Title);
    }

    [Fact]
    public void SaveNote_PreservesTags()
    {
        var note = MakeNote();
        note.Tags = new System.Collections.Generic.List<string> { "alpha", "beta", "gamma" };
        _manager.SaveNote(note);

        var json = File.ReadAllText(Path.Combine(_tempDir, $"{note.Id}.json"));
        var loaded = JsonSerializer.Deserialize<Note>(json);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, loaded!.Tags);
    }

    // ---- LoadNotes ----

    [Fact]
    public void LoadNotes_WhenNoFiles_ReturnsEmptyArray()
    {
        var notes = _manager.LoadNotes();
        Assert.Empty(notes);
    }

    [Fact]
    public void LoadNotes_ReturnsSavedNotes()
    {
        var a = MakeNote(title: "Note A");
        var b = MakeNote(title: "Note B");
        _manager.SaveNote(a);
        _manager.SaveNote(b);

        var loaded = _manager.LoadNotes();
        Assert.Equal(2, loaded.Length);
        Assert.Contains(loaded, n => n.Title == "Note A");
        Assert.Contains(loaded, n => n.Title == "Note B");
    }

    [Fact]
    public void LoadNotes_SkipsCorruptJsonFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, $"{Guid.NewGuid()}.json"), "NOT_VALID_JSON{{{{");
        var good = MakeNote(title: "Good Note");
        _manager.SaveNote(good);

        var loaded = _manager.LoadNotes();
        Assert.Single(loaded);
        Assert.Equal("Good Note", loaded[0].Title);
    }

    [Fact]
    public void LoadNotes_OrdersByModifiedDescending()
    {
        var older = MakeNote(title: "Older");
        older.Modified = new DateTime(2024, 1, 1);
        var newer = MakeNote(title: "Newer");
        newer.Modified = new DateTime(2025, 6, 1);

        _manager.SaveNote(older);
        _manager.SaveNote(newer);

        var loaded = _manager.LoadNotes();
        Assert.Equal("Newer", loaded[0].Title);
        Assert.Equal("Older", loaded[1].Title);
    }

    [Fact]
    public void LoadNotes_IgnoresNonJsonFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "not a note");
        var note = MakeNote();
        _manager.SaveNote(note);

        var loaded = _manager.LoadNotes();
        Assert.Single(loaded);
    }

    // ---- DeleteNote ----

    [Fact]
    public void DeleteNote_RemovesJsonFile()
    {
        var note = MakeNote();
        _manager.SaveNote(note);

        _manager.DeleteNote(note.Id);

        Assert.False(File.Exists(Path.Combine(_tempDir, $"{note.Id}.json")));
    }

    [Fact]
    public void DeleteNote_WhenFileDoesNotExist_DoesNotThrow()
    {
        var ex = Record.Exception(() => _manager.DeleteNote(Guid.NewGuid()));
        Assert.Null(ex);
    }

    [Fact]
    public void DeleteNote_LeavesOtherNotesIntact()
    {
        var keep = MakeNote(title: "Keep");
        var remove = MakeNote(title: "Remove");
        _manager.SaveNote(keep);
        _manager.SaveNote(remove);

        _manager.DeleteNote(remove.Id);

        var loaded = _manager.LoadNotes();
        Assert.Single(loaded);
        Assert.Equal("Keep", loaded[0].Title);
    }

    // ---- GetFolders ----

    [Fact]
    public void GetFolders_WhenNoNotes_ReturnsEmpty()
    {
        Assert.Empty(_manager.GetFolders());
    }

    [Fact]
    public void GetFolders_ReturnsDistinctFolderNames()
    {
        _manager.SaveNote(MakeNote(folder: "Work"));
        _manager.SaveNote(MakeNote(folder: "Work"));
        _manager.SaveNote(MakeNote(folder: "Personal"));

        var folders = _manager.GetFolders();
        Assert.Equal(2, folders.Length);
        Assert.Contains("Work", folders);
        Assert.Contains("Personal", folders);
    }

    [Fact]
    public void GetFolders_ReturnsAlphabeticalOrder()
    {
        _manager.SaveNote(MakeNote(folder: "Zebra"));
        _manager.SaveNote(MakeNote(folder: "Alpha"));
        _manager.SaveNote(MakeNote(folder: "Mango"));

        var folders = _manager.GetFolders();
        Assert.Equal(new[] { "Alpha", "Mango", "Zebra" }, folders);
    }

    // ---- CreateBackup ----

    [Fact]
    public void CreateBackup_ReturnsExistingDirectoryPath()
    {
        var note = MakeNote();
        _manager.SaveNote(note);

        var backupPath = _manager.CreateBackup();

        Assert.True(Directory.Exists(backupPath));
    }

    [Fact]
    public void CreateBackup_CopiesAllNoteFiles()
    {
        var a = MakeNote();
        var b = MakeNote();
        _manager.SaveNote(a);
        _manager.SaveNote(b);

        var backupPath = _manager.CreateBackup();

        var backedUpFiles = Directory.GetFiles(backupPath, "*.json");
        Assert.Equal(2, backedUpFiles.Length);
    }

    [Fact]
    public void CreateBackup_BackupPathIsInsideBackupsSubfolder()
    {
        _manager.SaveNote(MakeNote());
        var backupPath = _manager.CreateBackup();

        Assert.StartsWith(Path.Combine(_tempDir, "Backups"), backupPath);
    }

    [Fact]
    public void CreateBackup_PathContainsTimestamp()
    {
        _manager.SaveNote(MakeNote());
        var backupPath = _manager.CreateBackup();

        var folderName = Path.GetFileName(backupPath);
        Assert.StartsWith("backup_", folderName);
    }

    [Fact]
    public void CreateBackup_WhenNoNotes_CreatesEmptyBackupDirectory()
    {
        var backupPath = _manager.CreateBackup();
        Assert.True(Directory.Exists(backupPath));
        Assert.Empty(Directory.GetFiles(backupPath, "*.json"));
    }

    // ---- Helpers ----

    private static Note MakeNote(string title = "Untitled", string content = "", string folder = "Default")
        => new Note
        {
            Id = Guid.NewGuid(),
            Title = title,
            Content = content,
            Folder = folder,
            Created = DateTime.Now,
            Modified = DateTime.Now,
            Tags = new System.Collections.Generic.List<string>()
        };
}
