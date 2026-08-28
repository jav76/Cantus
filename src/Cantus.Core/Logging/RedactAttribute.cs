using System;

namespace Cantus.Core.Logging;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class RedactAttribute : Attribute
{
}
