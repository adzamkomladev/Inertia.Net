using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Inertia.Net;

internal readonly record struct KeysetKey(LambdaExpression Selector, object? Value, bool Descending);

internal static class KeysetExpression
{
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
                var right = Expression.Call(typeof(EF), nameof(EF.Parameter), [left.Type], Expression.Constant(key.Value, left.Type));
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
                ? descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy)
                : descending ? nameof(Queryable.ThenByDescending) : nameof(Queryable.ThenBy);
            var expression = Expression.Call(typeof(Queryable), method, [typeof(T), key.Selector.ReturnType],
                query.Expression, Expression.Quote(key.Selector));
            query = query.Provider.CreateQuery<T>(expression);
        }

        return query;
    }

    private static Expression Compare(Expression left, Expression right, bool greater)
    {
        if (left.Type == typeof(bool))
        {
            left = Expression.Condition(left, Expression.Constant(1), Expression.Constant(0));
            right = Expression.Condition(right, Expression.Constant(1), Expression.Constant(0));
        }
        else if (left.Type == typeof(string) || left.Type == typeof(Guid)
            || left.Type == typeof(DateTime) || left.Type == typeof(DateTimeOffset))
        {
            left = Expression.Call(left, left.Type.GetMethod(nameof(IComparable.CompareTo), [left.Type])!, right);
            right = Expression.Constant(0);
        }

        return greater ? Expression.GreaterThan(left, right) : Expression.LessThan(left, right);
    }

    private sealed class ReplaceParameter(ParameterExpression source, ParameterExpression replacement) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == source ? replacement : node;
    }
}
