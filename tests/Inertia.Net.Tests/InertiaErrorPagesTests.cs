using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Inertia.Net.Tests;

public class InertiaErrorPagesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Invalid_options_fail_validation(int scenario)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInertiaErrorPages(o =>
        {
            switch (scenario)
            {
                case 0: o.Component = " "; break;
                case 1: o.StatusCodes = []; break;
                case 2: o.StatusCodes = [399]; break;
                case 3: o.Components[401] = "Unauthorized"; break;
                case 4: o.Components[404] = ""; break;
                case 5: o.StatusCodes = [600]; break;
            }
        });
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<InertiaErrorPagesOptions>>().Value);
    }

    [Fact]
    public void Middleware_requires_registration()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        var error = Assert.Throws<InvalidOperationException>(() => app.UseInertiaErrorPages());
        Assert.Contains("AddInertiaErrorPages", error.Message);
    }
}
