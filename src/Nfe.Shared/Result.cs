namespace Nfe.Shared;

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    private Result(T value)
    {
        IsSuccess = true;
        Value = value;
    }

    private Result(string code, string message)
    {
        IsSuccess = false;
        ErrorCode = code;
        ErrorMessage = message;
    }

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(string code, string message) => new(code, message);
}
