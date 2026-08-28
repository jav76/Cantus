using System;

namespace Cantus.Core.Logging;

[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class TraceLogAttribute : Attribute
{
    public bool CaptureParameters { get; init; } = true;

    public bool CaptureReturnValue { get; init; } = true;

    public TraceLogAttribute()
    {
    }

    public TraceLogAttribute(bool captureParameters, bool captureReturnValue)
    {
        CaptureParameters = captureParameters;
        CaptureReturnValue = captureReturnValue;
    }
}
