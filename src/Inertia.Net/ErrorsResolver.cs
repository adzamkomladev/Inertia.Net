namespace Inertia.Net;

/// <summary>Builds the <c>errors</c> prop from error bags (protocol §6).</summary>
internal static class ErrorsResolver
{
    public static InertiaProps Resolve(Dictionary<string, Dictionary<string, string[]>>? bags, bool withAllErrors, string? errorBagHeader)
    {
        var result = new InertiaProps();
        if (bags is null || bags.Count == 0)
        {
            return result;
        }

        if (bags.TryGetValue("default", out var defaultBag))
        {
            var flattened = Flatten(defaultBag, withAllErrors);
            if (errorBagHeader is null)
            {
                return flattened;
            }

            result[errorBagHeader] = flattened;
            return result;
        }

        foreach (var (bag, errors) in bags)
        {
            result[bag] = Flatten(errors, withAllErrors);
        }

        return result;
    }

    private static InertiaProps Flatten(Dictionary<string, string[]> errors, bool withAllErrors)
    {
        var result = new InertiaProps(errors.Count);
        foreach (var (field, messages) in errors)
        {
            if (messages.Length > 0)
            {
                result[field] = withAllErrors ? messages : messages[0];
            }
        }

        return result;
    }
}
