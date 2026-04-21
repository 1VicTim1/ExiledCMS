using Microsoft.AspNetCore.Mvc;

namespace ExiledCms.TicketsService.Api.Infrastructure;

public sealed class ApiException : Exception
{
    public ApiException(int statusCode, string title, string detail, string errorCode, object? details = null)
        : base(detail)
    {
        StatusCode = statusCode;
        Title = title;
        ErrorCode = errorCode;
        DetailsPayload = details;
    }

    public int StatusCode { get; }

    public string Title { get; }

    public string ErrorCode { get; }

    public object? DetailsPayload { get; }

    public ProblemDetails ToProblemDetails(HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCode,
            Title = Title,
            Detail = Message,
            Instance = httpContext.Request.Path,
        };

        problem.Extensions["errorCode"] = ErrorCode;

        if (DetailsPayload is not null)
        {
            problem.Extensions["details"] = DetailsPayload;
        }

        return problem;
    }

    public static ApiException BadRequest(string detail, string errorCode = "bad_request", object? details = null) =>
        new(StatusCodes.Status400BadRequest, "Bad request", detail, errorCode, details);

    public static ApiException Unauthorized(string detail, string errorCode = "unauthorized") =>
        new(StatusCodes.Status401Unauthorized, "Unauthorized", detail, errorCode);

    public static ApiException Forbidden(string detail, string errorCode = "forbidden") =>
        new(StatusCodes.Status403Forbidden, "Forbidden", detail, errorCode);

    public static ApiException NotFound(string detail, string errorCode = "not_found") =>
        new(StatusCodes.Status404NotFound, "Not found", detail, errorCode);

    public static ApiException Conflict(string detail, string errorCode = "conflict") =>
        new(StatusCodes.Status409Conflict, "Conflict", detail, errorCode);
}
