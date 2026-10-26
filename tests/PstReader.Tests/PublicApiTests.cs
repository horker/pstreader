using PstReader.Models;

namespace PstReader.Tests;

public sealed class PublicApiTests
{
    [Fact]
    public void LibraryHasNoCliOrPowerShellDependency()
    {
        var references = typeof(PstStore).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name is "pstreader" or "System.Management.Automation");
        Assert.NotEqual(typeof(Cli.CliParser).Assembly, typeof(PstStore).Assembly);
    }

    [Fact]
    public void ConvertsCalendarWithoutChangingInstantOrSource()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var item = new CalendarItem("/Calendar", "Meeting", start, start.AddHours(1), "Room", false,
            "IPM.Appointment", "Body", "text");
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(9), "test", "test");
        var converted = ReaderDateTime.InTimeZone(item, zone);
        Assert.Equal(item.Start, converted.Start);
        Assert.Equal(TimeSpan.FromHours(9), converted.Start.Offset);
        Assert.Equal(TimeSpan.Zero, item.Start.Offset);
        Assert.Equal(item.Body, converted.Body);
    }

    [Fact]
    public void ConvertsNullableDatesAndPreservesMissingValues()
    {
        var due = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var task = new TaskItem("/Tasks", "Task", null, due, null, 0, 0, false, "IPM.Task");
        var mail = new MailItem("/Inbox", "Mail", "", "", "", "", "", due, null, false, false, "none", "IPM.Note");
        var zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-5), "test", "test");
        var convertedTask = ReaderDateTime.InTimeZone(task, zone);
        var convertedMail = ReaderDateTime.InTimeZone(mail, zone);
        Assert.Null(convertedTask.Start);
        Assert.Null(convertedTask.Completed);
        Assert.Equal(TimeSpan.FromHours(-5), convertedTask.Due!.Value.Offset);
        Assert.Null(convertedMail.Sent);
        Assert.Equal(TimeSpan.FromHours(-5), convertedMail.Received!.Value.Offset);
    }

    [Theory]
    [InlineData("2026-01-01T09:00:00+09:00")]
    [InlineData("2026-01-01T00:00:00Z")]
    public void ParsesOffsetsToUtc(string text) =>
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), ReaderDateTime.Parse(text));

    [Fact]
    public void RejectsInvalidDatesAndZones()
    {
        Assert.Throws<FormatException>(() => ReaderDateTime.Parse("not a date"));
        Assert.Throws<ArgumentException>(() => ReaderDateTime.ResolveTimeZone("invalid-zone-id"));
        Assert.Same(TimeZoneInfo.Utc, ReaderDateTime.ResolveTimeZone("UTC"));
        Assert.Equal(TimeZoneInfo.Local, ReaderDateTime.ResolveTimeZone("local"));
    }

    [Fact]
    public void ValidatesPublicOptionsBeforeReadingContents()
    {
        // A header alone is enough to construct a reader, but cannot supply any folders.
        var path = Path.GetTempFileName();
        try
        {
            var header = new byte[32];
            new byte[] { 0x21, 0x42, 0x44, 0x4E }.CopyTo(header, 0);
            header[8] = 0x53;
            header[9] = 0x4D;
            header[10] = 0x17;
            File.WriteAllBytes(path, header);
            var store = new PstStore(path);
            var now = DateTimeOffset.UtcNow;
            Assert.Equal(Path.GetFullPath(path), store.InputPath);
            Assert.Throws<ArgumentException>(() => store.ReadCalendar(now, now));
            Assert.Throws<ArgumentException>(() => store.ReadTasks(dateField: "received"));
            Assert.Throws<ArgumentException>(() => store.ReadMail(dateField: "due"));
            Assert.Throws<ArgumentException>(() => store.ReadMail(now, now.AddDays(-1)));
        }
        finally { File.Delete(path); }
    }
}
