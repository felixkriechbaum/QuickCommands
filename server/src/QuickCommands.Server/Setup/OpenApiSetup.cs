using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace QuickCommands.Server.Setup;

/// <summary>Describes the Firebase ID token as a Bearer scheme so generated clients send it.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "QuickCommands Server",
            Version = "v1",
            Description = "Cloud save and store for QuickCommands. Authenticate with a Firebase ID token (Bearer).",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["firebase"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Firebase ID token",
        };
        // Version-independent output, so the checked-in file only changes when the API changes.
        document.Servers = [];
        return Task.CompletedTask;
    }
}

/// <summary>Marks endpoints that need a login with the "firebase" scheme.</summary>
internal sealed class BearerSecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requiresAuth = metadata.OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any() &&
                           !metadata.OfType<Microsoft.AspNetCore.Authorization.IAllowAnonymous>().Any();
        if (requiresAuth)
            operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("firebase", context.Document)] = [] }];
        return Task.CompletedTask;
    }
}
