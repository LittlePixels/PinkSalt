using System.Collections.Generic;
using System.ComponentModel;
using PinkSalt;
using Xunit;

namespace PinkSalt.Tests;

public class NoteTests
{
    // ---- Default values ----

    [Fact]
    public void Note_DefaultTitle_IsEmptyString()
    {
        var note = new Note();
        Assert.Equal(string.Empty, note.Title);
    }

    [Fact]
    public void Note_DefaultContent_IsEmptyString()
    {
        var note = new Note();
        Assert.Equal(string.Empty, note.Content);
    }

    [Fact]
    public void Note_DefaultFolder_IsDefault()
    {
        var note = new Note();
        Assert.Equal("Default", note.Folder);
    }

    [Fact]
    public void Note_DefaultTags_IsEmptyList()
    {
        var note = new Note();
        Assert.NotNull(note.Tags);
        Assert.Empty(note.Tags);
    }

    // ---- Property setters ----

    [Fact]
    public void Title_Set_UpdatesValue()
    {
        var note = new Note { Title = "My Note" };
        Assert.Equal("My Note", note.Title);
    }

    [Fact]
    public void Content_Set_UpdatesValue()
    {
        var note = new Note { Content = "Some **markdown** content" };
        Assert.Equal("Some **markdown** content", note.Content);
    }

    // ---- INotifyPropertyChanged ----

    [Fact]
    public void Title_Set_RaisesPropertyChangedEvent()
    {
        var note = new Note();
        var raised = new List<string>();
        note.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        note.Title = "New Title";

        Assert.Contains(nameof(Note.Title), raised);
    }

    [Fact]
    public void Content_Set_RaisesPropertyChangedEvent()
    {
        var note = new Note();
        var raised = new List<string>();
        note.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        note.Content = "New Content";

        Assert.Contains(nameof(Note.Content), raised);
    }

    [Fact]
    public void Title_SetToSameValue_StillRaisesPropertyChanged()
    {
        var note = new Note { Title = "Same" };
        var count = 0;
        note.PropertyChanged += (_, _) => count++;

        note.Title = "Same";

        Assert.Equal(1, count);
    }

    [Fact]
    public void Note_ImplementsINotifyPropertyChanged()
    {
        Assert.IsAssignableFrom<INotifyPropertyChanged>(new Note());
    }
}
