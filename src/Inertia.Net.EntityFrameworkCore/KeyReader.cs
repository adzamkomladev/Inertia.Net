using System.Linq.Expressions;
using System.Reflection;

namespace Inertia.Net;

/// <summary>Reads a keyset selector's value from a loaded row, to encode the page's cursors.</summary>
internal static class KeyReader
{
    /// <summary>
    /// A page reads its key from two rows at most, so compiling the selector, which a caller's lambda rebuilds on every request, costs far
    /// more than walking its members. Selectors other than a member chain on the row (<c>x => x.Id</c>, <c>x => x.Author.Name</c>) are compiled.
    /// </summary>
    public static Func<T, TKey> For<T, TKey>(Expression<Func<T, TKey>> selector)
    {
        var members = new List<MemberInfo>();
        var node = selector.Body;
        while (node is MemberExpression member)
        {
            members.Add(member.Member);
            node = member.Expression;
        }

        if (members.Count == 0 || node != selector.Parameters[0])
        {
            return selector.Compile();
        }

        members.Reverse();
        var chain = members.ToArray();
        return row =>
        {
            object? value = row;
            foreach (var member in chain)
            {
                value = member is PropertyInfo property ? property.GetValue(value) : ((FieldInfo)member).GetValue(value);
            }

            return (TKey)value!;
        };
    }
}
