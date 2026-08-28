using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Cantus.Generators;

[Generator]
public class TraceLogDecoratorGenerator : IIncrementalGenerator
{
    private const string TraceLogAttributeName = "Cantus.Core.Logging.TraceLogAttribute";
    private const string RedactAttributeName = "Cantus.Core.Logging.RedactAttribute";

    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<InterfaceToDecorate?> interfacesToDecorate = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is InterfaceDeclarationSyntax ids && ids.AttributeLists.Count > 0,
                transform: static (ctx, ct) => GetInterfaceToDecorate(ctx, ct))
            .Where(static m => m is not null);

        context.RegisterSourceOutput(interfacesToDecorate, static (spc, interfaceModel) =>
        {
            if (interfaceModel is null)
            {
                return;
            }

            string source = GenerateDecoratorClass(interfaceModel);
            spc.AddSource($"{interfaceModel.DecoratorClassName}.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static InterfaceToDecorate? GetInterfaceToDecorate(GeneratorSyntaxContext context, System.Threading.CancellationToken cancellationToken)
    {
        InterfaceDeclarationSyntax interfaceDeclaration = (InterfaceDeclarationSyntax)context.Node;
        ISymbol? symbol = context.SemanticModel.GetDeclaredSymbol(interfaceDeclaration, cancellationToken);
        if (symbol is not INamedTypeSymbol typeSymbol || typeSymbol.TypeKind != TypeKind.Interface)
        {
            return null;
        }

        AttributeData? traceLogAttr = typeSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == TraceLogAttributeName
                              || a.AttributeClass?.Name == "TraceLogAttribute"
                              || a.AttributeClass?.Name == "TraceLog");

        if (traceLogAttr is null)
        {
            return null;
        }

        bool captureParams = true;
        bool captureReturn = true;

        foreach (KeyValuePair<string, TypedConstant> namedArg in traceLogAttr.NamedArguments)
        {
            if (namedArg.Key == "CaptureParameters" && namedArg.Value.Value is bool cp)
            {
                captureParams = cp;
            }
            else if (namedArg.Key == "CaptureReturnValue" && namedArg.Value.Value is bool cr)
            {
                captureReturn = cr;
            }
        }

        if (traceLogAttr.ConstructorArguments.Length >= 2)
        {
            if (traceLogAttr.ConstructorArguments[0].Value is bool cp)
            {
                captureParams = cp;
            }
            if (traceLogAttr.ConstructorArguments[1].Value is bool cr)
            {
                captureReturn = cr;
            }
        }

        string fullInterfaceName = typeSymbol.ToDisplayString(TypeDisplayFormat);
        string interfaceName = typeSymbol.Name;
        string namespaceName = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : typeSymbol.ContainingNamespace.ToDisplayString();

        string decoratorClassName = interfaceName.StartsWith("I") && interfaceName.Length > 1
            ? $"TraceLogging{interfaceName.Substring(1)}Decorator"
            : $"TraceLogging{interfaceName}Decorator";

        List<MethodModel> methods = new List<MethodModel>();
        List<PropertyModel> properties = new List<PropertyModel>();
        List<EventModel> events = new List<EventModel>();

        IEnumerable<INamedTypeSymbol> allInterfaces = new[] { typeSymbol }.Concat(typeSymbol.AllInterfaces);
        foreach (INamedTypeSymbol iface in allInterfaces)
        {
            foreach (ISymbol member in iface.GetMembers())
            {
                if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
                {
                    List<ParameterModel> parameters = new List<ParameterModel>();
                    foreach (IParameterSymbol param in method.Parameters)
                    {
                        bool isRedacted = param.GetAttributes().Any(a =>
                            a.AttributeClass?.ToDisplayString() == RedactAttributeName
                            || a.AttributeClass?.Name == "RedactAttribute"
                            || a.AttributeClass?.Name == "Redact");

                        string? defaultValue = null;
                        if (param.HasExplicitDefaultValue)
                        {
                            defaultValue = param.ExplicitDefaultValue switch
                            {
                                null => "default",
                                string s => $"\"{s}\"",
                                bool b => b ? "true" : "false",
                                _ => param.ExplicitDefaultValue.ToString()
                            };
                        }

                        parameters.Add(new ParameterModel(
                            param.Name,
                            param.Type.ToDisplayString(TypeDisplayFormat),
                            param.RefKind,
                            isRedacted,
                            defaultValue));
                    }

                    string returnTypeStr = method.ReturnType.ToDisplayString(TypeDisplayFormat);
                    bool isAsync = returnTypeStr.StartsWith("global::System.Threading.Tasks.Task")
                                || returnTypeStr.StartsWith("global::System.Threading.Tasks.ValueTask");
                    bool hasReturnValue = returnTypeStr != "void"
                                       && returnTypeStr != "global::System.Threading.Tasks.Task"
                                       && returnTypeStr != "global::System.Threading.Tasks.ValueTask";

                    methods.Add(new MethodModel(
                        method.Name,
                        returnTypeStr,
                        isAsync,
                        hasReturnValue,
                        parameters.ToImmutableArray()));
                }
                else if (member is IPropertySymbol prop)
                {
                    properties.Add(new PropertyModel(
                        prop.Name,
                        prop.Type.ToDisplayString(TypeDisplayFormat),
                        prop.GetMethod is not null,
                        prop.SetMethod is not null));
                }
                else if (member is IEventSymbol evt)
                {
                    events.Add(new EventModel(
                        evt.Name,
                        evt.Type.ToDisplayString(TypeDisplayFormat)));
                }
            }
        }

        return new InterfaceToDecorate(
            namespaceName,
            interfaceName,
            fullInterfaceName,
            decoratorClassName,
            captureParams,
            captureReturn,
            methods.ToImmutableArray(),
            properties.ToImmutableArray(),
            events.ToImmutableArray());
    }

    private static string GenerateDecoratorClass(InterfaceToDecorate model)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Diagnostics;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using Microsoft.Extensions.Logging;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(model.NamespaceName))
        {
            sb.AppendLine($"namespace {model.NamespaceName};");
            sb.AppendLine();
        }

        sb.AppendLine($"/// <summary>");
        sb.AppendLine($"/// Auto-generated trace logging decorator for <see cref=\"{model.FullInterfaceName}\"/>.");
        sb.AppendLine($"/// Logs entry, exit, duration, and exceptions at TRACE level.");
        sb.AppendLine($"/// </summary>");
        sb.AppendLine($"public sealed class {model.DecoratorClassName} : {model.FullInterfaceName}");
        sb.AppendLine("{");
        sb.AppendLine($"    private readonly {model.FullInterfaceName} _inner;");
        sb.AppendLine($"    private readonly global::Microsoft.Extensions.Logging.ILogger<{model.DecoratorClassName}> _logger;");
        sb.AppendLine();
        sb.AppendLine($"    public {model.DecoratorClassName}({model.FullInterfaceName} inner, global::Microsoft.Extensions.Logging.ILogger<{model.DecoratorClassName}> logger)");
        sb.AppendLine("    {");
        sb.AppendLine("        _inner = inner ?? throw new global::System.ArgumentNullException(nameof(inner));");
        sb.AppendLine("        _logger = logger ?? throw new global::System.ArgumentNullException(nameof(logger));");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Implement events
        foreach (EventModel evt in model.Events)
        {
            sb.AppendLine($"    public event {evt.Type} {evt.Name}");
            sb.AppendLine("    {");
            sb.AppendLine($"        add => _inner.{evt.Name} += value;");
            sb.AppendLine($"        remove => _inner.{evt.Name} -= value;");
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Implement properties
        foreach (PropertyModel prop in model.Properties)
        {
            sb.AppendLine($"    public {prop.Type} {prop.Name}");
            sb.AppendLine("    {");
            if (prop.HasGetter)
            {
                sb.AppendLine($"        get => _inner.{prop.Name};");
            }
            if (prop.HasSetter)
            {
                sb.AppendLine($"        set => _inner.{prop.Name} = value;");
            }
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        // Implement methods
        foreach (MethodModel method in model.Methods)
        {
            GenerateMethodImplementation(sb, model, method);
        }

        sb.AppendLine("}");
        sb.AppendLine();

        // Generate DI extension methods
        sb.AppendLine($"public static class {model.DecoratorClassName}Extensions");
        sb.AppendLine("{");
        sb.AppendLine($"    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddTraceDecorated<TImpl>(");
        sb.AppendLine($"        this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,");
        sb.AppendLine($"        global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime = global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)");
        sb.AppendLine($"        where TImpl : class, {model.FullInterfaceName}");
        sb.AppendLine("    {");
        sb.AppendLine("        services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(");
        sb.AppendLine($"            typeof(TImpl), typeof(TImpl), lifetime));");
        sb.AppendLine("        services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(");
        sb.AppendLine($"            typeof({model.FullInterfaceName}),");
        sb.AppendLine("            sp => new " + model.DecoratorClassName + "(");
        sb.AppendLine("                sp.GetRequiredService<TImpl>(),");
        sb.AppendLine($"                sp.GetRequiredService<global::Microsoft.Extensions.Logging.ILogger<{model.DecoratorClassName}>>()),");
        sb.AppendLine("            lifetime));");
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateMethodImplementation(StringBuilder sb, InterfaceToDecorate model, MethodModel method)
    {
        string paramList = string.Join(", ", method.Parameters.Select(p =>
        {
            string prefix = p.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => string.Empty
            };
            string defaultSuffix = p.DefaultValue != null ? $" = {p.DefaultValue}" : string.Empty;
            return $"{prefix}{p.Type} {p.Name}{defaultSuffix}";
        }));

        string innerArgList = string.Join(", ", method.Parameters.Select(p =>
        {
            string prefix = p.RefKind switch
            {
                RefKind.Ref => "ref ",
                RefKind.Out => "out ",
                RefKind.In => "in ",
                _ => string.Empty
            };
            return $"{prefix}{p.Name}";
        }));

        string asyncKeyword = method.IsAsync ? "async " : string.Empty;
        sb.AppendLine($"    public {asyncKeyword}{method.ReturnType} {method.Name}({paramList})");
        sb.AppendLine("    {");

        // Format arguments string
        if (model.CaptureParameters && method.Parameters.Length > 0)
        {
            List<string> argFormatParts = new List<string>();
            foreach (ParameterModel p in method.Parameters)
            {
                if (p.RefKind == RefKind.Out)
                {
                    argFormatParts.Add($"{p.Name}=<out>");
                }
                else if (p.IsRedacted)
                {
                    argFormatParts.Add($"{p.Name}=[REDACTED]");
                }
                else
                {
                    argFormatParts.Add($"{p.Name}={{{p.Name}}}");
                }
            }
            string formattedArgs = string.Join(", ", argFormatParts);
            sb.AppendLine($"        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Trace))");
            sb.AppendLine("        {");
            sb.AppendLine($"            _logger.LogTrace(\"[TRACE] Entering {model.InterfaceName}.{method.Name}({formattedArgs})\");");
            sb.AppendLine("        }");
        }
        else
        {
            sb.AppendLine($"        if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Trace))");
            sb.AppendLine("        {");
            sb.AppendLine($"            _logger.LogTrace(\"[TRACE] Entering {model.InterfaceName}.{method.Name}()\");");
            sb.AppendLine("        }");
        }

        sb.AppendLine("        global::System.Diagnostics.Stopwatch stopwatch = global::System.Diagnostics.Stopwatch.StartNew();");
        sb.AppendLine("        try");
        sb.AppendLine("        {");

        if (method.ReturnType == "void")
        {
            sb.AppendLine($"            _inner.{method.Name}({innerArgList});");
            sb.AppendLine("            stopwatch.Stop();");
            sb.AppendLine("            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Trace))");
            sb.AppendLine("            {");
            sb.AppendLine($"                _logger.LogTrace(\"[TRACE] Exiting {model.InterfaceName}.{method.Name} completed in {{ElapsedMs}}ms\", stopwatch.ElapsedMilliseconds);");
            sb.AppendLine("            }");
        }
        else if (method.ReturnType == "global::System.Threading.Tasks.Task" || method.ReturnType == "global::System.Threading.Tasks.ValueTask")
        {
            sb.AppendLine($"            await _inner.{method.Name}({innerArgList});");
            sb.AppendLine("            stopwatch.Stop();");
            sb.AppendLine("            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Trace))");
            sb.AppendLine("            {");
            sb.AppendLine($"                _logger.LogTrace(\"[TRACE] Exiting {model.InterfaceName}.{method.Name} completed in {{ElapsedMs}}ms\", stopwatch.ElapsedMilliseconds);");
            sb.AppendLine("            }");
        }
        else
        {
            string awaitPrefix = method.IsAsync ? "await " : string.Empty;
            sb.AppendLine($"            var result = {awaitPrefix}_inner.{method.Name}({innerArgList});");
            sb.AppendLine("            stopwatch.Stop();");
            sb.AppendLine("            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Trace))");
            sb.AppendLine("            {");
            if (model.CaptureReturnValue)
            {
                sb.AppendLine($"                _logger.LogTrace(\"[TRACE] Exiting {model.InterfaceName}.{method.Name} completed in {{ElapsedMs}}ms => {{Result}}\", stopwatch.ElapsedMilliseconds, result);");
            }
            else
            {
                sb.AppendLine($"                _logger.LogTrace(\"[TRACE] Exiting {model.InterfaceName}.{method.Name} completed in {{ElapsedMs}}ms\", stopwatch.ElapsedMilliseconds);");
            }
            sb.AppendLine("            }");
            sb.AppendLine("            return result;");
        }

        sb.AppendLine("        }");
        sb.AppendLine("        catch (global::System.Exception ex)");
        sb.AppendLine("        {");
        sb.AppendLine("            stopwatch.Stop();");
        sb.AppendLine("            if (_logger.IsEnabled(global::Microsoft.Extensions.Logging.LogLevel.Trace))");
        sb.AppendLine("            {");
        sb.AppendLine($"                _logger.LogTrace(ex, \"[TRACE] Exception in {model.InterfaceName}.{method.Name} after {{ElapsedMs}}ms: {{Message}}\", stopwatch.ElapsedMilliseconds, ex.Message);");
        sb.AppendLine("            }");
        sb.AppendLine("            throw;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
    }
}

internal sealed class InterfaceToDecorate : IEquatable<InterfaceToDecorate>
{
    public string NamespaceName { get; }
    public string InterfaceName { get; }
    public string FullInterfaceName { get; }
    public string DecoratorClassName { get; }
    public bool CaptureParameters { get; }
    public bool CaptureReturnValue { get; }
    public ImmutableArray<MethodModel> Methods { get; }
    public ImmutableArray<PropertyModel> Properties { get; }
    public ImmutableArray<EventModel> Events { get; }

    public InterfaceToDecorate(
        string namespaceName,
        string interfaceName,
        string fullInterfaceName,
        string decoratorClassName,
        bool captureParameters,
        bool captureReturnValue,
        ImmutableArray<MethodModel> methods,
        ImmutableArray<PropertyModel> properties,
        ImmutableArray<EventModel> events)
    {
        NamespaceName = namespaceName;
        InterfaceName = interfaceName;
        FullInterfaceName = fullInterfaceName;
        DecoratorClassName = decoratorClassName;
        CaptureParameters = captureParameters;
        CaptureReturnValue = captureReturnValue;
        Methods = methods;
        Properties = properties;
        Events = events;
    }

    public bool Equals(InterfaceToDecorate? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return NamespaceName == other.NamespaceName
            && InterfaceName == other.InterfaceName
            && FullInterfaceName == other.FullInterfaceName
            && DecoratorClassName == other.DecoratorClassName
            && CaptureParameters == other.CaptureParameters
            && CaptureReturnValue == other.CaptureReturnValue
            && Methods.SequenceEqual(other.Methods)
            && Properties.SequenceEqual(other.Properties)
            && Events.SequenceEqual(other.Events);
    }

    public override bool Equals(object? obj) => Equals(obj as InterfaceToDecorate);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = NamespaceName.GetHashCode();
            hash = (hash * 397) ^ InterfaceName.GetHashCode();
            hash = (hash * 397) ^ DecoratorClassName.GetHashCode();
            return hash;
        }
    }
}

internal sealed class MethodModel : IEquatable<MethodModel>
{
    public string Name { get; }
    public string ReturnType { get; }
    public bool IsAsync { get; }
    public bool HasReturnValue { get; }
    public ImmutableArray<ParameterModel> Parameters { get; }

    public MethodModel(string name, string returnType, bool isAsync, bool hasReturnValue, ImmutableArray<ParameterModel> parameters)
    {
        Name = name;
        ReturnType = returnType;
        IsAsync = isAsync;
        HasReturnValue = hasReturnValue;
        Parameters = parameters;
    }

    public bool Equals(MethodModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && ReturnType == other.ReturnType
            && IsAsync == other.IsAsync
            && HasReturnValue == other.HasReturnValue
            && Parameters.SequenceEqual(other.Parameters);
    }

    public override bool Equals(object? obj) => Equals(obj as MethodModel);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Name.GetHashCode();
            hash = (hash * 397) ^ ReturnType.GetHashCode();
            return hash;
        }
    }
}

internal sealed class ParameterModel : IEquatable<ParameterModel>
{
    public string Name { get; }
    public string Type { get; }
    public RefKind RefKind { get; }
    public bool IsRedacted { get; }
    public string? DefaultValue { get; }

    public ParameterModel(string name, string type, RefKind refKind, bool isRedacted, string? defaultValue)
    {
        Name = name;
        Type = type;
        RefKind = refKind;
        IsRedacted = isRedacted;
        DefaultValue = defaultValue;
    }

    public bool Equals(ParameterModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && Type == other.Type
            && RefKind == other.RefKind
            && IsRedacted == other.IsRedacted
            && DefaultValue == other.DefaultValue;
    }

    public override bool Equals(object? obj) => Equals(obj as ParameterModel);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Name.GetHashCode();
            hash = (hash * 397) ^ Type.GetHashCode();
            hash = (hash * 397) ^ (int)RefKind;
            hash = (hash * 397) ^ IsRedacted.GetHashCode();
            if (DefaultValue != null)
            {
                hash = (hash * 397) ^ DefaultValue.GetHashCode();
            }
            return hash;
        }
    }
}

internal sealed class PropertyModel : IEquatable<PropertyModel>
{
    public string Name { get; }
    public string Type { get; }
    public bool HasGetter { get; }
    public bool HasSetter { get; }

    public PropertyModel(string name, string type, bool hasGetter, bool hasSetter)
    {
        Name = name;
        Type = type;
        HasGetter = hasGetter;
        HasSetter = hasSetter;
    }

    public bool Equals(PropertyModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name
            && Type == other.Type
            && HasGetter == other.HasGetter
            && HasSetter == other.HasSetter;
    }

    public override bool Equals(object? obj) => Equals(obj as PropertyModel);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Name.GetHashCode();
            hash = (hash * 397) ^ Type.GetHashCode();
            return hash;
        }
    }
}

internal sealed class EventModel : IEquatable<EventModel>
{
    public string Name { get; }
    public string Type { get; }

    public EventModel(string name, string type)
    {
        Name = name;
        Type = type;
    }

    public bool Equals(EventModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name && Type == other.Type;
    }

    public override bool Equals(object? obj) => Equals(obj as EventModel);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = Name.GetHashCode();
            hash = (hash * 397) ^ Type.GetHashCode();
            return hash;
        }
    }
}
