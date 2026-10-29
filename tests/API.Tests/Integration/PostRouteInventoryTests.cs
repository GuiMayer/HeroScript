using System.Reflection;
using API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;
using Xunit.Abstractions;

namespace API.Tests.Integration;

public sealed class PostRouteInventoryTests(ITestOutputHelper output)
{
    [Fact]
    public void EnumerateAllPostRoutesIncludingToolsInsteadOfHidingThemInGameplayPercentiles()
    {
        var routes = typeof(CombatCommandController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>().Select(post => new {
                    Controller = type.Name, Method = method.Name,
                    Prefix = type.GetCustomAttributes<RouteAttribute>().Select(route => route.Template).FirstOrDefault() ?? "",
                    Template = post.Template ?? "" }))).OrderBy(item => item.Controller).ThenBy(item => item.Method).ToArray();
        Assert.NotEmpty(routes);
        Assert.Contains(routes, route => route.Controller == nameof(CombatCommandController));
        Assert.Contains(routes, route => route.Controller == nameof(RunCommandController));
        foreach (var route in routes) output.WriteLine($"POST {route.Prefix}/{route.Template} [{route.Controller}.{route.Method}]");
        output.WriteLine("Command routes additionally require per-command measurements, not a single aggregate percentile.");
    }
}
