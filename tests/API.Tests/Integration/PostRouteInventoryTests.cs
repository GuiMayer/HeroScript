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
                .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>().SelectMany(post =>
                    type.GetCustomAttributes<RouteAttribute>().Select(route => route.Template).DefaultIfEmpty("")
                        .Select(prefix => new { Controller = type.Name, Method = method.Name,
                            Route = ResolveRoute(prefix, post.Template ?? "") }))))
            .Distinct().OrderBy(item => item.Controller).ThenBy(item => item.Method).ThenBy(item => item.Route).ToArray();
        Assert.NotEmpty(routes);
        Assert.Contains(routes, route => route.Controller == nameof(CombatCommandController));
        Assert.Contains(routes, route => route.Controller == nameof(RunCommandController));
        foreach (var route in routes) output.WriteLine($"POST /{route.Route} [{route.Controller}.{route.Method}]");
        output.WriteLine("Command routes additionally require per-command measurements, not a single aggregate percentile.");
    }
    [Theory]
    [InlineData("api/v1/runs", "", "api/v1/runs")]
    [InlineData("api/v1/runs", "commands", "api/v1/runs/commands")]
    [InlineData("api/v1/runs", "/api/v1/tools", "api/v1/tools")]
    [InlineData("api/v1/runs", "~/api/v1/tools", "api/v1/tools")]
    public void AbsoluteMethodRoutesReplaceTheControllerPrefix(string prefix, string template, string expected) =>
        Assert.Equal(expected, ResolveRoute(prefix, template));

    private static string ResolveRoute(string prefix, string template) =>
        template.StartsWith('/') || template.StartsWith("~/", StringComparison.Ordinal)
            ? template.TrimStart('~', '/')
            : string.Join('/', new[] { prefix.Trim('/'), template.Trim('/') }.Where(part => part.Length > 0));

}
