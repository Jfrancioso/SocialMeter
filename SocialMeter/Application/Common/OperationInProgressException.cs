namespace SocialMeter.Application.Common;

/// <summary>A long-running operation (rescore, deletion sync) is already running; the caller should try again later. Maps to 409.</summary>
public sealed class OperationInProgressException : Exception
{
    public OperationInProgressException(string operation) : base($"{operation} is already running.") { }
}
