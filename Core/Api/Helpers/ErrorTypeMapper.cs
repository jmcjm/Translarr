using ErrorOr;

namespace Translarr.Core.Api.Helpers;


public static class ErrorTypeMapper
{
    /// <summary>
    /// Maps <see cref="ErrorOr{T}"/> to <see cref="IResult"/> Problem Response.
    /// </summary>
    /// <param name="errors">The ErrorOr result containing errors</param>
    /// <returns>Problem Response IResult</returns>
    public static IResult MapErrorsToProblemResponse<T>(ErrorOr<T> errors)
    {
        var firstError = errors.FirstError;
        var statusCode = MapErrorTypeToStatusCode(firstError.Type);
        var title = string.Join(", ", errors.Errors.Select(e => e.Code));
        var detail = string.Join(", ", errors.Errors.Select(e => e.Description));
        
        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode
        );
    }
    
    /// <summary>
    /// Maps <see cref="ErrorType"/> to <see cref="int"/> HTTP status code.
    /// </summary>
    /// <param name="errorType"></param>
    /// <returns></returns>
    private static int MapErrorTypeToStatusCode(ErrorType errorType)
    {
        return errorType switch
        {
            ErrorType.Failure => 400,
            ErrorType.Unexpected => 500,
            ErrorType.Validation => 400,
            ErrorType.Conflict => 409,
            ErrorType.NotFound => 404,
            ErrorType.Unauthorized => 401,
            ErrorType.Forbidden => 403,
            _ => 500
        };
    }
}