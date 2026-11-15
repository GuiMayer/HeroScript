using API.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace API.Contracts;

public sealed class AuthoringDocumentFilter : IDocumentFilter
{
    public const string DocumentName = "authoring-v1";
    private const string AdminKey = "AuthoringAdminKey";

    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        var paths = document.Paths.Where(path =>
            path.Key.StartsWith("/api/v1/admin/authoring/", StringComparison.Ordinal)).ToArray();
        if (paths.Length == 0)
            return;

        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[AdminKey] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = AdminKeyMiddleware.HeaderName
        };

        var problemSchema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        var authoringProblem = new OpenApiSchema
        {
            AllOf = [problemSchema],
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["code"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["correlationId"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["validation"] = context.SchemaGenerator.GenerateSchema(typeof(CardAuthoringValidation), context.SchemaRepository),
                ["receipt"] = context.SchemaGenerator.GenerateSchema(typeof(CardAuthoringChangeReceipt), context.SchemaRepository)
            },
            Required = new HashSet<string> { "code" }
        };
        document.Components.Schemas!["AuthoringProblem"] = authoringProblem;
        var authoringProblemReference = new OpenApiSchemaReference("AuthoringProblem", document);
        foreach (var (path, item) in paths)
        foreach (var (method, operation) in item.Operations!)
        {
            operation.Security = [new() { [new OpenApiSecuritySchemeReference(AdminKey, document)] = [] }];
            if (method == HttpMethod.Post)
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "If-Match",
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "Strong workspace ETag returned by the authoring API.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                });
            }

            if (path.Contains("{settingId}", StringComparison.Ordinal) &&
                operation.Responses!.TryGetValue("200", out var declared) && declared is OpenApiResponse success)
            {
                success.Headers ??= new Dictionary<string, IOpenApiHeader>();
                success.Headers["ETag"] = new OpenApiHeader
                {
                    Description = "Workspace source version. For an applied change, this is the version in its receipt.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                };
            }

            var responses = operation.Responses!;
            responses.TryAdd("401", new OpenApiResponse { Description = "Administrative key is missing or invalid." });
            responses.TryAdd("403", new OpenApiResponse { Description = "The operation is disabled or denied by policy." });
            responses.TryAdd("404", new OpenApiResponse { Description = "The admin API or requested resource is unavailable." });
            responses.TryAdd("503", new OpenApiResponse { Description = "The workspace is unavailable or requires recovery." });
            foreach (var (status, response) in responses)
                if (status.StartsWith('4') || status.StartsWith('5'))
                    if (response is OpenApiResponse problem)
                        problem.Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/problem+json"] = new() { Schema = authoringProblemReference }
                        };
        }
    }
}
