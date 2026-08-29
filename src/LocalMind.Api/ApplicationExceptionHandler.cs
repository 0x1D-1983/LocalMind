using LocalMind.Agent;
using LocalMind.Application.Agents;
using LocalMind.Prompts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LocalMind.Api;

internal sealed class ApplicationExceptionHandler(ILogger<ApplicationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            UnknownAgentException => (StatusCodes.Status404NotFound, "Unknown agent"),
            AgentException => (StatusCodes.Status502BadGateway, "Agent failed"),
            PromptNotFoundException => (StatusCodes.Status500InternalServerError, "Prompt not found"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => ((int?)null, (string?)null)
        };

        if (status is null)
            return false;

        if (status.Value >= StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "{Title}: {Detail}", title, exception.Message);
        else
            logger.LogWarning(exception, "{Title}: {Detail}", title, exception.Message);

        httpContext.Response.StatusCode = status.Value;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status.Value,
            Title = title,
            Detail = exception.Message
        }, cancellationToken);

        return true;
    }
}
