using System.Text.Json;
using PstReader.Cli;
using PstReader.Models;
using PstReader.Output;
using PstReader.Services;
using XstReader;

namespace PstReader.Tests;

public sealed class TaskAndMailTests
{
    private static readonly DateTimeOffset Date = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
    private static Property Prop(ushort tag, object value) => new() { Tag = (EpropertyTag)tag, Value = value };
    private static Property Named(uint lid, object value) => new()
    {
        Tag = (EpropertyTag)0x8001, Guid = "00062003-0000-0000-c000-000000000046", Lid = lid, Value = value,
    };
    private static Message Message(string messageClass)
    {
        var message = new Message { Subject = "Subject", Body = "Body" };
        message.Properties.Add(Prop(0x001A, messageClass));
        return message;
    }

    [Theory]
    [InlineData("tasks", "due")]
    [InlineData("mail", "received")]
    public void DefaultsAndCommonOptions(string command, string field)
    {
        var parsed = CliParser.Parse([command, "a.pst", "--include-body", "--format", "csv",
            "--from", "2026-01-01", "--to", "2026-02-01", "--time-zone", "utc", "--output", "a.csv"]);
        Assert.True(parsed.IsSuccess);
        Assert.Equal(field, parsed.Options!.DateField);
        Assert.True(parsed.Options.IncludeBody);
        Assert.Equal(TimeZoneInfo.Utc, parsed.Options.OutputTimeZone);
    }

    [Theory]
    [InlineData("tasks", "start", true)]
    [InlineData("tasks", "due", true)]
    [InlineData("tasks", "completed", true)]
    [InlineData("mail", "received", true)]
    [InlineData("mail", "sent", true)]
    [InlineData("mail", "due", false)]
    [InlineData("tasks", "sent", false)]
    [InlineData("calendar", "start", false)]
    [InlineData("folders", "due", false)]
    [InlineData("info", "sent", false)]
    public void DateFieldValidation(string command, string field, bool valid) =>
        Assert.Equal(valid, CliParser.Parse([command, "a.pst", "--date-field", field]).IsSuccess);

    [Theory]
    [InlineData("mail", true)]
    [InlineData("tasks", false)]
    [InlineData("calendar", false)]
    [InlineData("folders", false)]
    [InlineData("info", false)]
    public void FlagOptionValidation(string command, bool valid) =>
        Assert.Equal(valid, CliParser.Parse([command, "a.pst", "--flagged-only"]).IsSuccess);

    [Theory]
    [InlineData("IPM.Task", true, false)]
    [InlineData("ipm.task.Custom", true, false)]
    [InlineData("IPM.TaskRequest", false, false)]
    [InlineData("IPM.Note", false, true)]
    [InlineData("ipm.note.Custom", false, true)]
    [InlineData("IPM.Noteworthy", false, false)]
    [InlineData("IPM.Appointment", false, false)]
    public void ClassMatching(string value, bool task, bool mail)
    {
        Assert.Equal(task, XstStoreService.CreateTaskItem(new Folder(), Message(value)) is not null);
        Assert.Equal(mail, XstStoreService.CreateMailItem(new Folder(), Message(value)) is not null);
    }

    [Fact]
    public void TaskPropertiesAndMissingValues()
    {
        var message = Message("IPM.Task");
        var missing = XstStoreService.CreateTaskItem(new Folder(), message)!;
        Assert.Null(missing.Start);
        Assert.Null(missing.Status);
        Assert.Null(missing.IsComplete);
        Assert.Null(missing.PercentComplete);
        message.Properties.AddRange([Named(0x8104, Date.UtcDateTime), Named(0x8105, Date.AddDays(1)),
            Named(0x810F, Date.AddDays(2)), Named(0x8101, 1), Named(0x8102, 0.25), Named(0x811C, false)]);
        var item = XstStoreService.CreateTaskItem(new Folder(), message, true)!;
        Assert.Equal(25d, item.PercentComplete);
        Assert.Equal(1, item.Status);
        Assert.False(item.IsComplete);
        Assert.Equal("Body", item.Body);
        Assert.Equal(Date, XstStoreService.TaskDate(item, "start"));
        Assert.Equal(Date.AddDays(1), XstStoreService.TaskDate(item, "due"));
        Assert.Equal(Date.AddDays(2), XstStoreService.TaskDate(item, "completed"));
        message.Properties.First(p => p.Lid == 0x8105).Guid = "other";
        Assert.Null(XstStoreService.CreateTaskItem(new Folder(), message)!.Due);
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData(0, "none")]
    [InlineData(1, "complete")]
    [InlineData(2, "flagged")]
    [InlineData(3, "unknown")]
    public void FlagStatesAndCombinedFilter(int? flag, string expected)
    {
        var message = Message("IPM.Note");
        if (flag is not null) message.Properties.Add(Prop(0x1090, flag.Value));
        message.Properties.Add(Prop(0x0E06, Date));
        var item = XstStoreService.CreateMailItem(new Folder(), message)!;
        Assert.Equal(expected, item.FlagStatus);
        Assert.Equal(flag == 2 ? 1 : 0,
            XstStoreService.FilterMail([item], Date, Date.AddDays(1), "received", true).Count);
        Assert.Empty(XstStoreService.FilterMail([item], null, Date, "received", true));
        Assert.Single(XstStoreService.FilterMail([item], null, null, "received", false));
    }

    [Fact]
    public void MailAddressesDatesAndFlags()
    {
        var message = Message("IPM.Note");
        message.Properties.AddRange([Prop(0x0C1A, "Sender"), Prop(0x0C1F, "/EX/sender"),
            Prop(0x5D01, "sender@example.com"), Prop(0x0039, Date), Prop(0x0E07, 0x11)]);
        foreach (var type in new[] { RecipientType.To, RecipientType.Cc, RecipientType.Bcc })
        {
            var recipient = new Recipient { RecipientType = type, DisplayName = type.ToString(), EmailAddress = "/EX/user" };
            recipient.Properties.Add(Prop(0x39FE, "user@example.com"));
            message.Recipients.Add(recipient);
        }
        var item = XstStoreService.CreateMailItem(new Folder(), message)!;
        Assert.Equal("sender@example.com", item.SenderAddress);
        Assert.Equal("Sender", item.SenderName);
        Assert.Equal("To <user@example.com>", item.To);
        Assert.Equal("Cc <user@example.com>", item.Cc);
        Assert.Equal("Bcc <user@example.com>", item.Bcc);
        Assert.True(item.IsRead);
        Assert.True(item.HasAttachments);
        Assert.Null(XstStoreService.MailDate(item, "received"));
        Assert.Equal(Date, XstStoreService.MailDate(item, "sent"));
        Assert.False(XstStoreService.InPeriod(item.Received, Date, null));
        message.Properties.RemoveAll(p => (ushort)p.Tag is 0x5D01 or 0x0E07);
        message.Recipients.Clear();
        message.Properties.Add(Prop(0x0E04, "Display recipient"));
        item = XstStoreService.CreateMailItem(new Folder(), message)!;
        Assert.Equal("/EX/sender", item.SenderAddress);
        Assert.Equal("Display recipient", item.To);
        Assert.Null(item.IsRead);
        Assert.Null(item.HasAttachments);
    }

    [Fact]
    public void PeriodAndSorting()
    {
        Assert.True(XstStoreService.InPeriod(null, null, null));
        Assert.False(XstStoreService.InPeriod(null, Date, null));
        Assert.False(XstStoreService.InPeriod(null, null, Date));
        Assert.True(XstStoreService.InPeriod(Date, Date, null));
        Assert.False(XstStoreService.InPeriod(Date, null, Date));
        Assert.True(XstStoreService.InPeriod(Date, null, Date.AddDays(1)));
        var items = new[] { (At: (DateTimeOffset?)null, Subject: "a"), (At: (DateTimeOffset?)Date, Subject: "z"),
            (At: (DateTimeOffset?)Date, Subject: "a"), (At: (DateTimeOffset?)Date.AddDays(-1), Subject: "b") };
        Assert.Equal(new[] { "b", "a", "z", "a" },
            XstStoreService.FilterAndSort(items, i => i.At, i => i.Subject, null, null).Select(i => i.Subject));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutputSchemaBodyAndTimeZone(bool body)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(9), "Test", "Test");
        var task = new TaskItem("Root", "Subject", Date, null, null, null, null, null, "IPM.Task", "Body,\"Quote\"\nNext line", "text");
        var mail = new MailItem("Root", "Subject", "", "", "", "", "", Date, null, null, null, "none", "IPM.Note",
            task.Body, "text");
        foreach (var isTask in new[] { true, false })
        {
            using var json = new StringWriter();
            using var csv = new StringWriter();
            if (isTask)
            {
                OutputSerializer.WriteTasks(json, [task], OutputFormat.Json, zone, body);
                OutputSerializer.WriteTasks(csv, [task], OutputFormat.Csv, zone, body);
            }
            else
            {
                OutputSerializer.WriteMail(json, [mail], OutputFormat.Json, zone, body);
                OutputSerializer.WriteMail(csv, [mail], OutputFormat.Csv, zone, body);
            }
            using var document = JsonDocument.Parse(json.ToString());
            var item = document.RootElement[0];
            Assert.Equal((isTask ? 9 : 13) + (body ? 2 : 0), item.EnumerateObject().Count());
            Assert.Equal(body, item.TryGetProperty("body", out var outputBody));
            if (body) Assert.Equal(task.Body, outputBody.GetString());
            Assert.Equal(TimeSpan.FromHours(9), item.GetProperty(isTask ? "start" : "received").GetDateTimeOffset().Offset);
            Assert.Equal(JsonValueKind.Null, item.GetProperty(isTask ? "due" : "sent").ValueKind);
            Assert.Contains("Subject", json.ToString());
            Assert.Contains("+09:00", csv.ToString());
            Assert.Equal(body, csv.ToString().Contains("\"body\""));
            if (body) Assert.Contains("Body,\"\"Quote\"\"\nNext line", csv.ToString());
        }
    }
}
