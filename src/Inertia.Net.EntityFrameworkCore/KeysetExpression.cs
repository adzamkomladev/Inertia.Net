using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Inertia.Net;

internal readonly record struct KeysetKey(LambdaExpression Selector, object? Value, bool Descending);

internal static class KeysetExpression
{
    // Found once: Expression.Call by name searches every overload on each cursor page.
    private static readonly MethodInfo EfParameter = typeof(EF).GetMethod(nameof(EF.Parameter))!;
    private static readonly MethodInfo OrderBy = QueryableMethod(nameof(Queryable.OrderBy));
    private static readonly MethodInfo OrderByDescending = QueryableMethod(nameof(Queryable.OrderByDescending));
    private static readonly MethodInfo ThenBy = QueryableMethod(nameof(Queryable.ThenBy));
    private static readonly MethodInfo ThenByDescending = QueryableMethod(nameof(Queryable.ThenByDescending));
    private static readonly MethodInfo StringCompareTo = typeof(string).GetMethod(nameof(IComparable.CompareTo), [typeof(string)])!;
    private static readonly MethodInfo GuidCompareTo = typeof(Guid).GetMethod(nameof(IComparable.CompareTo), [typeof(Guid)])!;
    private static readonly MethodInfo DateTimeCompareTo = typeof(DateTime).GetMethod(nameof(IComparable.CompareTo), [typeof(DateTime)])!;
    private static readonly MethodInfo DateTimeOffsetCompareTo = typeof(DateTimeOffset).GetMethod(nameof(IComparable.CompareTo), [typeof(DateTimeOffset)])!;

    // The (source, keySelector) overload, without the IComparer one.
    private static MethodInfo QueryableMethod(string name) =>
        typeof(Queryable).GetMethods().Single(m => m.Name == name && m.GetParameters().Length == 2);

    internal static IQueryable<T> Apply<T>(IQueryable<T> query, IReadOnlyList<KeysetKey> keys, bool hasCursor, bool before)
    {
        if (hasCursor)
        {
            var parameter = Expression.Parameter(typeof(T), "row");
            Expression? predicate = null;
            Expression? prefix = null;
            foreach (var key in keys)
            {
                var left = new ReplaceParameter(key.Selector.Parameters[0], parameter).Visit(key.Selector.Body)!;
                var right = Expression.Call(EfParameter.MakeGenericMethod(left.Type), Expression.Constant(key.Value, left.Type));
                var comparison = Compare(left, right, key.Descending == before);
                var term = prefix is null ? comparison : Expression.AndAlso(prefix, comparison);
                predicate = predicate is null ? term : Expression.OrElse(predicate, term);
                var equal = Expression.Equal(left, right);
                prefix = prefix is null ? equal : Expression.AndAlso(prefix, equal);
            }

            query = query.Where(Expression.Lambda<Func<T, bool>>(predicate!, parameter));
        }

        for (var index = 0; index < keys.Count; index++)
        {
            var key = keys[index];
            var descending = key.Descending != before;
            var method = index == 0
                ? descending ? OrderByDescending : OrderBy
                : descending ? ThenByDescending : ThenBy;
            var expression = Expression.Call(method.MakeGenericMethod(typeof(T), key.Selector.ReturnType),
                query.Expression, Expression.Quote(key.Selector));
            query = query.Provider.CreateQuery<T>(expression);
        }

        return query;
    }

    private static BinaryExpression Compare(Expression left, Expression right, bool greater)
    {
        if (left.Type == typeof(bool))
        {
            left = Expression.Condition(left, Expression.Constant(1), Expression.Constant(0));
            right = Expression.Condition(right, Expression.Constant(1), Expression.Constant(0));
        }
        else if (CompareTo(left.Type) is { } compareTo)
        {
            left = Expression.Call(left, compareTo, right);
            right = Expression.Constant(0);
        }

        return greater ? Expression.GreaterThan(left, right) : Expression.LessThan(left, right);
    }

    private static MethodInfo? CompareTo(Type type) =>
        type == typeof(string) ? StringCompareTo
        : type == typeof(Guid) ? GuidCompareTo
        : type == typeof(DateTime) ? DateTimeCompareTo
        : type == typeof(DateTimeOffset) ? DateTimeOffsetCompareTo
        : null;

    private sealed class ReplaceParameter(ParameterExpression source, ParameterExpression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? replacement : node;
    }
}
