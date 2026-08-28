using LocalMind.Agent;
using LocalMind.Application.Agents;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace LocalMind.Api;

internal sealed class ApplicationExceptionHandler : IExceptionHandler
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
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            _ => ((int?)null, (string?)null)
        };

        if (status is null)
            return false;

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
