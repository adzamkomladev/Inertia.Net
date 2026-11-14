namespace Inertia.Net;

/// <summary>
/// A typed page-props object that names its own client component, so the name is written once:
/// <c>record TodosPage(List&lt;Todo&gt; Todos) : IInertiaPage { public static string Component =&gt; "Todos/Index"; }</c>, then
/// <c>Render(new TodosPage(todos))</c> and <c>o.TypeScript.Page&lt;TodosPage&gt;()</c>.
/// </summary>
public interface IInertiaPage
{
    /// <summary>The client-side page component name, e.g. <c>Todos/Index</c>.</summary>
    static abstract string Component { get; }
}
