using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Inertia.Net;

internal static class QueryOrdering
{
    internal static bool IsOrdered(Expression expression)
    {
        while (expression is MethodCallExpression call)
        {
            if (call.Method.DeclaringType == typeof(Queryable))
            {
                switch (call.Method.Name)
                {
                    case nameof(Queryable.OrderBy):
                    case nameof(Queryable.OrderByDescending):
                    case nameof(Queryable.ThenBy):
                    case nameof(Queryable.ThenByDescending):
                        return true;
                    case nameof(Queryable.Where):
                    case nameof(Queryable.Select):
                    case nameof(Queryable.Skip):
                    case nameof(Queryable.Take):
                    case nameof(Queryable.Reverse):
                    case nameof(Queryable.Cast):
                    case nameof(Queryable.OfType):
                    case nameof(Queryable.AsQueryable):
                        expression = call.Arguments[0];
                        continue;
                    default:
                        return false;
                }
            }

            if (call.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                || call.Method.DeclaringType?.FullName == "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions")
            {
                expression = call.Arguments[0];
                continue;
            }

            return false;
        }

        return false;
    }
}
