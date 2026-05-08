using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Core.Common;

/// <summary>
/// Represents the result of an operation that can either succeed with a value or fail with an error.
/// Provides a functional approach to error handling without exceptions.
/// </summary>
/// <typeparam name="T">The type of the success value</typeparam>
public class Result<T>
{
    private readonly T? _value;
    private readonly string? _error;
    private readonly Exception? _exception;

    /// <summary>
    /// Indicates whether the operation was successful.
    /// </summary>
    [MemberNotNullWhen(true, nameof(_value))]
    [MemberNotNullWhen(false, nameof(_error))]
    public bool IsSuccess { get; }

    /// <summary>
    /// Indicates whether the operation failed.
    /// </summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// Gets the success value. Throws if the result is a failure.
    /// </summary>
    public T Value
    {
        get
        {
            if (IsFailure)
                throw new InvalidOperationException($"Cannot access Value of a failed result. Error: {_error}");
            return _value;
        }
    }

    /// <summary>
    /// Gets the error message. Throws if the result is a success.
    /// </summary>
    public string Error
    {
        get
        {
            if (IsSuccess)
                throw new InvalidOperationException("Cannot access Error of a successful result.");
            return _error;
        }
    }

    /// <summary>
    /// Gets the exception if one was provided with the failure.
    /// </summary>
    public Exception? Exception => _exception;

    private Result(T value)
    {
        IsSuccess = true;
        _value = value;
        _error = null;
        _exception = null;
    }

    private Result(string error, Exception? exception = null)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
        _exception = exception;
    }

    /// <summary>
    /// Creates a successful result with the given value.
    /// </summary>
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a failed result with the given error message.
    /// </summary>
    public static Result<T> Failure(string error) => new(error);

    /// <summary>
    /// Creates a failed result with an error message and exception.
    /// </summary>
    public static Result<T> Failure(string error, Exception exception) => new(error, exception);

    /// <summary>
    /// Executes an action if the result is successful.
    /// </summary>
    public Result<T> OnSuccess(Action<T> action)
    {
        if (IsSuccess)
            action(_value);
        return this;
    }

    /// <summary>
    /// Executes an action if the result is a failure.
    /// </summary>
    public Result<T> OnFailure(Action<string> action)
    {
        if (IsFailure)
            action(_error);
        return this;
    }

    /// <summary>
    /// Transforms the success value using the provided function.
    /// </summary>
    public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
    {
        if (IsFailure)
            return Result<TNew>.Failure(_error, _exception);
        
        try
        {
            return Result<TNew>.Success(mapper(_value));
        }
        catch (Exception ex)
        {
            return Result<TNew>.Failure($"Mapping failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Chains another operation that returns a Result.
    /// </summary>
    public Result<TNew> Bind<TNew>(Func<T, Result<TNew>> binder)
    {
        if (IsFailure)
            return Result<TNew>.Failure(_error, _exception);
        
        try
        {
            return binder(_value);
        }
        catch (Exception ex)
        {
            return Result<TNew>.Failure($"Binding failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Returns the value if successful, otherwise returns the provided default value.
    /// </summary>
    public T ValueOr(T defaultValue) => IsSuccess ? _value : defaultValue;

    /// <summary>
    /// Returns the value if successful, otherwise computes and returns a default value.
    /// </summary>
    public T ValueOr(Func<T> defaultValueProvider) => IsSuccess ? _value : defaultValueProvider();

    /// <summary>
    /// Matches the result to one of two functions based on success or failure.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess ? onSuccess(_value) : onFailure(_error);
    }

    /// <summary>
    /// Implicit conversion from value to successful Result.
    /// </summary>
    public static implicit operator Result<T>(T value) => Success(value);

    public override string ToString()
    {
        return IsSuccess 
            ? $"Success({_value})" 
            : $"Failure({_error})";
    }
}

/// <summary>
/// Represents the result of an operation that can succeed or fail without a return value.
/// </summary>
public class Result
{
    private readonly string? _error;
    private readonly Exception? _exception;

    [MemberNotNullWhen(false, nameof(_error))]
    public bool IsSuccess { get; }
    
    public bool IsFailure => !IsSuccess;

    public string Error
    {
        get
        {
            if (IsSuccess)
                throw new InvalidOperationException("Cannot access Error of a successful result.");
            return _error;
        }
    }

    public Exception? Exception => _exception;

    private Result(bool isSuccess, string? error = null, Exception? exception = null)
    {
        IsSuccess = isSuccess;
        _error = error;
        _exception = exception;
    }

    public static Result Success() => new(true);
    
    public static Result Failure(string error) => new(false, error);
    
    public static Result Failure(string error, Exception exception) => new(false, error, exception);

    public Result OnSuccess(Action action)
    {
        if (IsSuccess)
            action();
        return this;
    }

    public Result OnFailure(Action<string> action)
    {
        if (IsFailure)
            action(_error);
        return this;
    }

    public Result<T> Map<T>(Func<T> mapper)
    {
        if (IsFailure)
            return Result<T>.Failure(_error, _exception);
        
        try
        {
            return Result<T>.Success(mapper());
        }
        catch (Exception ex)
        {
            return Result<T>.Failure($"Mapping failed: {ex.Message}", ex);
        }
    }

    public TResult Match<TResult>(Func<TResult> onSuccess, Func<string, TResult> onFailure)
    {
        return IsSuccess ? onSuccess() : onFailure(_error);
    }

    public override string ToString()
    {
        return IsSuccess ? "Success" : $"Failure({_error})";
    }
}

/// <summary>
/// Extension methods for working with Results.
/// </summary>
public static class ResultExtensions
{
    /// <summary>
    /// Combines multiple results into a single result containing a list of values.
    /// Fails if any individual result fails.
    /// </summary>
    public static Result<IReadOnlyList<T>> Combine<T>(this IEnumerable<Result<T>> results)
    {
        var resultList = results.ToList();
        var values = new List<T>();
        var errors = new List<string>();

        foreach (var result in resultList)
        {
            if (result.IsSuccess)
                values.Add(result.Value);
            else
                errors.Add(result.Error);
        }

        if (errors.Count > 0)
            return Result<IReadOnlyList<T>>.Failure($"Multiple failures: {string.Join("; ", errors)}");

        return Result<IReadOnlyList<T>>.Success(values);
    }

    /// <summary>
    /// Executes an action that may throw and wraps it in a Result.
    /// </summary>
    public static Result<T> Try<T>(Func<T> action, string? errorMessage = null)
    {
        try
        {
            return Result<T>.Success(action());
        }
        catch (Exception ex)
        {
            var message = errorMessage ?? $"Operation failed: {ex.Message}";
            return Result<T>.Failure(message, ex);
        }
    }

    /// <summary>
    /// Executes an action that may throw and wraps it in a Result.
    /// </summary>
    public static Result Try(Action action, string? errorMessage = null)
    {
        try
        {
            action();
            return Result.Success();
        }
        catch (Exception ex)
        {
            var message = errorMessage ?? $"Operation failed: {ex.Message}";
            return Result.Failure(message, ex);
        }
    }
}
