namespace PageNamespace;

// Register it in the app's JSON context (AppJsonContext in Program.cs): [JsonSerializable(typeof(RecordNamePage))].

/// <summary>The props of the PageName page: <c>return Render(new RecordNamePage("Hello"));</c></summary>
public sealed record RecordNamePage(string Message) : IInertiaPage
{
    public static string Component => "PageName";
}
