using System.Text.Json.Nodes;
using Core.Run;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace API.Contracts;

/// <summary>The generic command envelope's typed payloads come from the same canonical codec registry.</summary>
public sealed class CanonicalCommandDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        var payloads = new JsonObject();
        foreach (var descriptor in GameplayCommandDescriptors.All.OrderBy(value => value.Type, StringComparer.Ordinal))
        {
            context.SchemaGenerator.GenerateSchema(descriptor.PayloadType, context.SchemaRepository);
            payloads[descriptor.Type] = "#/components/schemas/" + descriptor.PayloadType.Name;
        }
        document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        document.Extensions["x-heroscript-command-payloads"] = new JsonNodeExtension(payloads);
    }
}
