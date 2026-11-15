using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace QuickCommands.Server.Filters;

/// <summary>
/// Runs the FluentValidation validator of every action argument that has one and answers 400 with
/// ValidationProblemDetails (code "validation_failed"). Replaces the deprecated FluentValidation.AspNetCore auto-validation.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services, ProblemDetailsFactory problemDetailsFactory) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new ModelStateDictionary();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
                continue;
            if (services.GetService(typeof(IValidator<>).MakeGenericType(argument.GetType())) is not IValidator validator)
                continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                errors.AddModelError(ToCamelCasePath(failure.PropertyName), failure.ErrorMessage);
            }
        }

        if (errors.IsValid)
        {
            await next();
            return;
        }

        // The factory applies CustomizeProblemDetails (traceId, code) like every other error response.
        var problem = problemDetailsFactory.CreateValidationProblemDetails(
            context.HttpContext, errors, StatusCodes.Status400BadRequest, "The request is invalid.");
        problem.Extensions["code"] = "validation_failed";
        context.Result = new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }

    /// <summary>"Commands[0].Name" → "commands[0].name", matching the JSON the client sent.</summary>
    private static string ToCamelCasePath(string path) =>
        string.Join('.', path.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
