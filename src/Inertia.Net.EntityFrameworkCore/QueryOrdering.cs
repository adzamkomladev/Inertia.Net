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
                || IsQuerySplittingAnnotation(call))
            {
                expression = call.Arguments[0];
                continue;
            }

            return false;
        }

        return false;
    }

    private static bool IsQuerySplittingAnnotation(MethodCallExpression call)
    {
        var method = call.Method;
        if (method.Name is not ("AsSplitQuery" or "AsSingleQuery") || !method.IsPublic || !method.IsStatic
            || !method.IsGenericMethod || method.GetGenericArguments() is not [var elementType]
            || call.Arguments.Count != 1)
            return false;

        var sourceType = typeof(IQueryable<>).MakeGenericType(elementType);
        if (method.ReturnType != sourceType || method.GetParameters() is not [var source] || source.ParameterType != sourceType)
            return false;

        // Relational annotations are optional: recognize their signed assembly and public signature without depending on that package.
        var coreAssembly = typeof(EntityFrameworkQueryableExtensions).Assembly.GetName();
        var origin = method.Module.Assembly.GetName();
        return origin.Name == coreAssembly.Name + ".Relational"
            && origin.GetPublicKeyToken().AsSpan().SequenceEqual(coreAssembly.GetPublicKeyToken());
    }
}
