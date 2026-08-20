using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Adk.Analyzer;

[Generator]
public sealed class NetworkGenerator : IIncrementalGenerator
{
    const string PAYLOAD_ATTRIBUTE = "Generated.NetworkPayloadAttribute";
    const string CALLBACK_ATTRIBUTE = "Generated.NetworkCallbackAttribute";
    const string EVENT_ARGS_TYPE = "Generated.ReceivedPacketEventArgs";
    const string SINGLETON_INTERFACE = "Generated.ISingleton<T>";

    static readonly DiagnosticDescriptor InvalidPayload = Error(
        "ADKNET001",
        "Invalid network payload",
        "[NetworkPayload] must target a top-level, non-static partial class");

    static readonly DiagnosticDescriptor InvalidPayloadId = Error(
        "ADKNET002",
        "Invalid network payload ID",
        "Network payload ID must be a positive integer other than the reserved transport ID 127");

    static readonly DiagnosticDescriptor DuplicatePayloadId = Error(
        "ADKNET003",
        "Duplicate network payload ID",
        "Network payload ID {0} is also used by {1}");

    static readonly DiagnosticDescriptor InvalidCallback = Error(
        "ADKNET004",
        "Invalid network callback",
        "[NetworkCallback] must target an accessible static or singleton instance void method with one parameter matching either ReceivedPacketEventArgs or the declared payload type");

    static readonly DiagnosticDescriptor InvalidCallbackId = Error(
        "ADKNET005",
        "Invalid network callback ID",
        "Network callback ID must be a positive integer other than the reserved transport ID 127");

    static readonly DiagnosticDescriptor InvalidCallbackPayload = Error(
        "ADKNET006",
        "Invalid network callback payload",
        "[NetworkCallback] payload type must be decorated with [NetworkPayload]");

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource("NetworkContracts.g.cs", ContractSource));

        var payloads = context.SyntaxProvider.ForAttributeWithMetadataName(
                PAYLOAD_ATTRIBUTE,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) => ReadPayload(attributeContext))
            .Collect();

        var callbacks = context.SyntaxProvider.ForAttributeWithMetadataName(
                CALLBACK_ATTRIBUTE,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, _) => ReadCallback(attributeContext))
            .Collect();

        var networkUsage = payloads.Combine(callbacks);
        context.RegisterSourceOutput(
            context.CompilationProvider.Combine(networkUsage),
            static (output, input) =>
            {
                var compilation = input.Left;
                var usages = input.Right;
                if ((!usages.Left.IsDefaultOrEmpty || !usages.Right.IsDefaultOrEmpty) &&
                    ShouldEmitNetworkManager(compilation))
                {
                    output.AddSource("NetworkManager.g.cs", NetworkManagerSource.Source);
                }
            });

        context.RegisterSourceOutput(payloads, static (output, values) => EmitPayloads(output, values));
        context.RegisterSourceOutput(callbacks, static (output, values) => EmitCallbacks(output, values));
    }

    static PayloadInput ReadPayload(GeneratorAttributeSyntaxContext context)
    {
        var attribute = context.Attributes[0];
        var id = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int value
            ? value
            : 0;
        return new PayloadInput(context.TargetSymbol as INamedTypeSymbol, id, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
    }

    static CallbackInput ReadCallback(GeneratorAttributeSyntaxContext context)
    {
        var attribute = context.Attributes[0];
        var payloadType = attribute.ConstructorArguments.Length >= 1
            ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol
            : null;
        var payloadAttribute = payloadType?.GetAttributes().FirstOrDefault(candidate =>
            candidate.AttributeClass?.ToDisplayString() == PAYLOAD_ATTRIBUTE);
        var id = payloadAttribute?.ConstructorArguments.Length == 1 &&
                 payloadAttribute.ConstructorArguments[0].Value is int value
            ? value
            : 0;
        var filter = attribute.ConstructorArguments.Length >= 2 && attribute.ConstructorArguments[1].Value is int flags
            ? flags
            : 0;
        return new CallbackInput(
            context.TargetSymbol as IMethodSymbol,
            payloadType,
            id,
            filter,
            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
    }

    static void EmitPayloads(SourceProductionContext output, ImmutableArray<PayloadInput> payloads)
    {
        var valid = new List<PayloadInput>();
        foreach (var payload in payloads)
        {
            if (!IsValidPayload(payload.Type))
            {
                output.ReportDiagnostic(Diagnostic.Create(InvalidPayload, payload.Location));
                continue;
            }

            if (!IsValidId(payload.Id))
            {
                output.ReportDiagnostic(Diagnostic.Create(InvalidPayloadId, payload.Location));
                continue;
            }

            valid.Add(payload);
        }

        foreach (var group in valid.GroupBy(payload => payload.Id).Where(group => group.Count() > 1))
        {
            var first = group.First();
            foreach (var duplicate in group.Skip(1))
            {
                output.ReportDiagnostic(Diagnostic.Create(
                    DuplicatePayloadId,
                    duplicate.Location,
                    duplicate.Id,
                    first.Type.ToDisplayString()));
            }
        }

        var duplicateIds = new HashSet<int>(valid.GroupBy(payload => payload.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key));

        foreach (var payload in valid.Where(payload => !duplicateIds.Contains(payload.Id)))
            output.AddSource(GetPayloadHintName(payload.Type), BuildPayloadSource(payload));
    }

    static void EmitCallbacks(SourceProductionContext output, ImmutableArray<CallbackInput> callbacks)
    {
        if (callbacks.IsDefaultOrEmpty)
            return;

        var valid = new List<CallbackInput>();
        foreach (var callback in callbacks)
        {
            if (!IsValidCallback(callback.Method, callback.PayloadType))
            {
                output.ReportDiagnostic(Diagnostic.Create(InvalidCallback, callback.Location));
                continue;
            }

            if (callback.PayloadType == null || callback.Id == 0)
            {
                output.ReportDiagnostic(Diagnostic.Create(InvalidCallbackPayload, callback.Location));
                continue;
            }

            if (!IsValidId(callback.Id))
            {
                output.ReportDiagnostic(Diagnostic.Create(InvalidCallbackId, callback.Location));
                continue;
            }

            valid.Add(callback);
        }

        if (valid.Count != 0)
            output.AddSource("NetworkManager.Callbacks.g.cs", BuildCallbackSource(valid));
    }

    static bool IsValidPayload(INamedTypeSymbol type)
    {
        if (type == null ||
            type.TypeKind != TypeKind.Class ||
            type.IsStatic ||
            type.Arity != 0 ||
            type.ContainingType != null)
        {
            return false;
        }

        return type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is ClassDeclarationSyntax declaration &&
            declaration.Modifiers.Any(SyntaxKind.PartialKeyword));
    }

    static bool IsValidCallback(IMethodSymbol method, INamedTypeSymbol payloadType)
    {
        return method != null &&
               method.MethodKind == MethodKind.Ordinary &&
               !method.IsAbstract &&
               method.ReturnsVoid &&
               method.Arity == 0 &&
               method.Parameters.Length == 1 &&
               method.Parameters[0].RefKind == RefKind.None &&
               (method.Parameters[0].Type.ToDisplayString() == EVENT_ARGS_TYPE ||
                SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, payloadType)) &&
               method.DeclaredAccessibility != Accessibility.Private &&
               method.ContainingType.TypeKind == TypeKind.Class &&
               method.ContainingType.Arity == 0 &&
               (method.IsStatic || IsSingletonType(method.ContainingType));
    }

    static bool IsSingletonType(INamedTypeSymbol type)
    {
        return type.AllInterfaces.Any(interfaceType =>
            interfaceType.OriginalDefinition.ToDisplayString() == SINGLETON_INTERFACE &&
            interfaceType.TypeArguments.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(interfaceType.TypeArguments[0], type));
    }

    static bool IsValidId(int id) => id > 0 && id != 127;

    static string BuildPayloadSource(PayloadInput payload)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        if (!payload.Type.ContainingNamespace.IsGlobalNamespace)
        {
            builder.Append("namespace ");
            builder.AppendLine(payload.Type.ContainingNamespace.ToDisplayString());
            builder.AppendLine("{");
        }

        builder.Append("    ");
        AppendAccessibility(builder, payload.Type.DeclaredAccessibility);
        builder.Append("partial class ");
        builder.Append(payload.Type.Name);
        builder.AppendLine(" : global::Generated.NetworkPackage");
        builder.AppendLine("    {");
        builder.Append("        public override int Id { get { return ");
        builder.Append(payload.Id);
        builder.AppendLine("; } }");
        builder.AppendLine("    }");

        if (!payload.Type.ContainingNamespace.IsGlobalNamespace)
            builder.AppendLine("}");
        return builder.ToString();
    }

    static string BuildCallbackSource(IReadOnlyList<CallbackInput> callbacks)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("namespace Generated");
        builder.AppendLine("{");
        builder.AppendLine("    public partial class NetworkManager");
        builder.AppendLine("    {");
        builder.AppendLine("        partial void DispatchGeneratedCallbacks(ReceivedPacketEventArgs args, ref bool isKnown)");
        builder.AppendLine("        {");
        builder.AppendLine("            switch (args.PacketId)");
        builder.AppendLine("            {");

        foreach (var group in callbacks.GroupBy(callback => callback.Id).OrderBy(group => group.Key))
        {
            builder.Append("                case ");
            builder.Append(group.Key);
            builder.AppendLine(":");
            builder.AppendLine("                    isKnown = true;");
            foreach (var callback in group.OrderBy(item => item.Method.ToDisplayString(), StringComparer.Ordinal))
                AppendCallback(builder, callback);
            builder.AppendLine("                    return;");
        }

        builder.AppendLine("                default:");
        builder.AppendLine("                    return;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    static void AppendCallback(StringBuilder builder, CallbackInput callback)
    {
        var condition = BuildCallbackCondition(callback);
        if (condition != null)
        {
            builder.Append("                    if (");
            builder.Append(condition);
            builder.AppendLine(")");
            builder.Append("    ");
        }

        builder.Append("                    ");
        if (!callback.Method.IsStatic)
        {
            builder.Append(callback.Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            builder.Append(".Instance");
        }
        else
        {
            builder.Append(callback.Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }
        builder.Append('.');
        builder.Append(callback.Method.Name);
        if (callback.Method.Parameters[0].Type.ToDisplayString() == EVENT_ARGS_TYPE)
        {
            builder.AppendLine("(args);");
        }
        else
        {
            builder.Append("(args.UnWrap<");
            builder.Append(callback.PayloadType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            builder.AppendLine(">());");
        }
    }

    static string BuildFilterCondition(int filter)
    {
        var conditions = new List<string>();
        var fromServer = (filter & 1) != 0;
        var fromClient = (filter & 2) != 0;
        var isServer = (filter & 4) != 0;
        var isClient = (filter & 8) != 0;

        if (fromServer != fromClient)
            conditions.Add(fromServer ? "args.IsFromServer" : "!args.IsFromServer");
        if (isServer != isClient)
            conditions.Add(isServer
                ? "global::Sandbox.ModAPI.MyAPIGateway.Session.IsServer"
                : "!global::Sandbox.ModAPI.MyAPIGateway.Utilities.IsDedicated");

        return conditions.Count == 0 ? null : string.Join(" && ", conditions);
    }

    static string BuildCallbackCondition(CallbackInput callback)
    {
        var conditions = new List<string>();
        var filterCondition = BuildFilterCondition(callback.Filter);
        if (filterCondition != null)
            conditions.Add(filterCondition);

        if (!callback.Method.IsStatic)
        {
            conditions.Add(
                callback.Method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ".Instance != null");
        }

        return conditions.Count == 0 ? null : string.Join(" && ", conditions);
    }

    static void AppendAccessibility(StringBuilder builder, Accessibility accessibility)
    {
        if (accessibility == Accessibility.Public)
            builder.Append("public ");
        else if (accessibility == Accessibility.Internal)
            builder.Append("internal ");
    }

    static string GetPayloadHintName(INamedTypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                   .Replace("global::", string.Empty)
                   .Replace(".", "_") + ".NetworkPayload.g.cs";
    }

    static bool ShouldEmitNetworkManager(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("Sandbox.ModAPI.MyAPIGateway") != null &&
               compilation.GetTypeByMetadataName("ProtoBuf.ProtoContractAttribute") != null &&
               compilation.GetTypeByMetadataName("VRage.Game.ModAPI.IMyPlayer") != null &&
               compilation.GetTypeByMetadataName("VRage.Utils.MyLog") != null;
    }

    static DiagnosticDescriptor Error(string id, string title, string message) =>
        new DiagnosticDescriptor(id, title, message, "AdkAnalyzer", DiagnosticSeverity.Error, true);

    const string ContractSource = @"// <auto-generated/>
namespace Generated
{
    [global::System.Flags]
    public enum NetworkCallbackFilter
    {
        None = 0,
        FromServer = 1,
        FromClient = 2,
        IsServer = 4,
        IsClient = 8
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class NetworkPayloadAttribute : global::System.Attribute
    {
        /// <summary>
        /// Marks a Protobuf network payload between players.
        /// Requires the usage of <see cref=""Generated.NetworkManager""/>
        /// </summary>
        public NetworkPayloadAttribute(int id) { }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class NetworkCallbackAttribute : global::System.Attribute
    {
        /// <summary>
        /// Marks a method called automatically by <see cref=""Generated.NetworkManager""/>
        /// Requires to be Static or a Singleton instance method, 
        /// and to have a single parameter matching either <see cref=""Generated.ReceivedPacketEventArgs""/> or the declared payload type.
        /// </summary>
        public NetworkCallbackAttribute(global::System.Type payloadType, NetworkCallbackFilter filter) { }
    }

    public abstract class NetworkPackage
    {
        public abstract int Id { get; }
    }

    internal interface INetworkDeserializer
    {
        T Deserialize<T>(byte[] data);
    }

    public sealed class ReceivedPacketEventArgs : global::System.EventArgs
    {
        public bool IsResolved { private set; get; }
        public int PacketId { private set; get; }
        public ulong SenderId { private set; get; }
        public bool IsFromServer { private set; get; }

        readonly byte[] _data;
        readonly INetworkDeserializer _deserializer;

        internal ReceivedPacketEventArgs(
            int packetId,
            byte[] data,
            ulong senderId,
            bool isFromServer,
            INetworkDeserializer deserializer)
        {
            PacketId = packetId;
            SenderId = senderId;
            IsFromServer = isFromServer;
            _data = data;
            _deserializer = deserializer;
        }

        public T UnWrap<T>()
        {
            return _deserializer.Deserialize<T>(_data);
        }

        public void SetResolved(bool value)
        {
            IsResolved = value;
        }
    }
}
";

    readonly struct PayloadInput
    {
        public readonly INamedTypeSymbol Type;
        public readonly int Id;
        public readonly Location Location;

        public PayloadInput(INamedTypeSymbol type, int id, Location location)
        {
            Type = type;
            Id = id;
            Location = location;
        }
    }

    readonly struct CallbackInput
    {
        public readonly IMethodSymbol Method;
        public readonly INamedTypeSymbol PayloadType;
        public readonly int Id;
        public readonly int Filter;
        public readonly Location Location;

        public CallbackInput(
            IMethodSymbol method,
            INamedTypeSymbol payloadType,
            int id,
            int filter,
            Location location)
        {
            Method = method;
            PayloadType = payloadType;
            Id = id;
            Filter = filter;
            Location = location;
        }
    }
}
