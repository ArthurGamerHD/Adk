using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Adk.Analyzer;

[Generator]
public sealed class ApiProviderGenerator : IIncrementalGenerator
{
    const string API_MANAGER_ATTRIBUTE =
        "Generated.APIManagerAttribute";

    const string API_PROVIDER_ATTRIBUTE =
        "Generated.ApiProviderAttribute";

    const string API_METHOD_ATTRIBUTE =
        "Generated.ApiMethodAttribute";

    static readonly DiagnosticDescriptor PartialTypeRequired = Error(
        "ADKAPI001",
        "API type must be partial",
        "API type '{0}' and all of its containing types must be partial");

    static readonly DiagnosticDescriptor InvalidProvider = Error(
        "ADKAPI002",
        "Invalid API provider",
        "API provider '{0}' must be a non-static, non-abstract class without an existing parameterless GetApi method");

    static readonly DiagnosticDescriptor InvalidMethod = Error(
        "ADKAPI003",
        "Invalid API method",
        "API method '{0}' must be an ordinary non-generic method with at most 16 by-value parameters and a by-value return type");

    static readonly DiagnosticDescriptor DuplicateMethodId = Error(
        "ADKAPI004",
        "Duplicate API method id",
        "API provider '{0}' exposes API id '{1}' more than once");

    static readonly DiagnosticDescriptor MissingProvider = Error(
        "ADKAPI005",
        "API method has no provider",
        "API method '{0}' is declared in '{1}', which is not marked with ApiProvider");

    static readonly DiagnosticDescriptor InvalidManager = Error(
        "ADKAPI006",
        "Invalid API manager",
        "API manager '{0}' must be a non-static, non-abstract, non-generic top-level class that can derive from MySessionComponentBase and has no generated lifecycle member conflicts");

    static readonly DiagnosticDescriptor ManagerNeedsApi = Error(
        "ADKAPI007",
        "API manager has no API provider",
        "API manager '{0}' must also be marked with ApiProvider, declare a compatible GetApi method, or select an ApiProvider field/property through APIManager.Provider");

    static readonly DiagnosticDescriptor DuplicateManagerPort = Error(
        "ADKAPI008",
        "Duplicate API manager port",
        "API manager port {0} is already used by '{1}'");

    static readonly DiagnosticDescriptor InvalidClientMirror = Error(
        "ADKAPI009",
        "Invalid API client mirror",
        "API provider '{0}' has an invalid or duplicate client mirror name '{1}'");


    public void Initialize(
        IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static output =>
            output.AddSource(
                "ApiProviderAttributes.g.cs",
                ATTRIBUTE_SOURCE));

        var providers =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                API_PROVIDER_ATTRIBUTE,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) =>
                    attributeContext.TargetSymbol as INamedTypeSymbol)
            .Where(static symbol => symbol != null)
            .Collect();

        var managers =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                API_MANAGER_ATTRIBUTE,
                static (node, _) => node is ClassDeclarationSyntax,
                static (attributeContext, _) =>
                    ReadManager(attributeContext))
            .Where(static manager => manager != null)
            .Collect();

        var methods =
            context.SyntaxProvider.ForAttributeWithMetadataName(
                API_METHOD_ATTRIBUTE,
                static (node, _) => node is MethodDeclarationSyntax,
                static (attributeContext, _) =>
                    ReadMethod(attributeContext))
            .Where(static method => method != null)
            .Collect();

        var targets =
            providers
                .Combine(managers)
                .Combine(methods);

        var generationInput =
            context.CompilationProvider
                .Combine(
                    context.AnalyzerConfigOptionsProvider)
                .Combine(
                    targets);

        context.RegisterSourceOutput(
            generationInput,
            static (sourceContext, input) =>
                Generate(
                    sourceContext,
                    input.Left.Left,
                    GetRootNamespace(
                        input.Left.Right,
                        input.Left.Left),
                    input.Right.Left.Left,
                    input.Right.Left.Right,
                    input.Right.Right));
    }


    static DiagnosticDescriptor Error(
        string id,
        string title,
        string message)
    {
        return new DiagnosticDescriptor(
            id,
            title,
            message,
            "AdkApiProviderGenerator",
            DiagnosticSeverity.Error,
            true);
    }


    static ManagerInput ReadManager(
        GeneratorAttributeSyntaxContext context)
    {
        var type =
            context.TargetSymbol as INamedTypeSymbol;

        if (type == null || context.Attributes.Length == 0)
            return null;

        long port =
            0;

        string provider =
            null;

        AttributeData attribute =
            context.Attributes[0];

        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value != null)
        {
            port =
                Convert.ToInt64(
                    attribute.ConstructorArguments[0].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
        }

        foreach (KeyValuePair<string, TypedConstant> pair in
            attribute.NamedArguments)
        {
            if (pair.Key == "Provider")
                provider = pair.Value.Value as string;
        }

        return new ManagerInput(
            type,
            port,
            provider,
            AttributeLocation(
                attribute,
                type));
    }


    static ApiMethodInput ReadMethod(
        GeneratorAttributeSyntaxContext context)
    {
        var method =
            context.TargetSymbol as IMethodSymbol;

        if (method == null || context.Attributes.Length == 0)
            return null;

        AttributeData attribute =
            context.Attributes[0];

        string id =
            null;

        ITypeSymbol returnType =
            null;

        foreach (TypedConstant argument in
            attribute.ConstructorArguments)
        {
            if (argument.Kind == TypedConstantKind.Type)
            {
                returnType =
                    argument.Value as ITypeSymbol;
            }
            else if (argument.Type?.SpecialType ==
                     SpecialType.System_String)
            {
                id =
                    argument.Value as string;
            }
        }

        foreach (KeyValuePair<string, TypedConstant> pair in
            attribute.NamedArguments)
        {
            if (pair.Key == "Id")
                id = pair.Value.Value as string;

            if (pair.Key == "ReturnType")
                returnType = pair.Value.Value as ITypeSymbol;
        }

        if (id == null)
            id = method.Name;

        return new ApiMethodInput(
            method,
            id,
            returnType,
            AttributeLocation(
                attribute,
                method));
    }


    static Location AttributeLocation(
        AttributeData attribute,
        ISymbol fallback)
    {
        return attribute.ApplicationSyntaxReference
                   ?.GetSyntax()
                   .GetLocation()
               ?? fallback.Locations.FirstOrDefault()
               ?? Location.None;
    }


    static void Generate(
        SourceProductionContext context,
        Compilation compilation,
        string rootNamespace,
        ImmutableArray<INamedTypeSymbol> providerSymbols,
        ImmutableArray<ManagerInput> managerInputs,
        ImmutableArray<ApiMethodInput> methodInputs)
    {
        var providers =
            DistinctTypes(
                providerSymbols);

        var providerSet =
            new HashSet<INamedTypeSymbol>(
                providers,
                SymbolEqualityComparer.Default);

        var validProviders =
            new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default);

        foreach (ApiMethodInput method in methodInputs)
        {
            if (method == null || method.Method == null)
                continue;

            if (!providerSet.Contains(
                method.Method.ContainingType))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        MissingProvider,
                        method.Location,
                        MethodDisplayName(
                            method.Method),
                        TypeDisplayName(
                            method.Method.ContainingType)));
            }
        }

        foreach (INamedTypeSymbol provider in providers)
        {
            if (!AreTypeAndContainersPartial(
                provider))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        PartialTypeRequired,
                        SymbolLocation(
                            provider),
                        TypeDisplayName(
                            provider)));

                continue;
            }

            if (!IsValidProvider(
                provider))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InvalidProvider,
                        SymbolLocation(
                            provider),
                        TypeDisplayName(
                            provider)));

                continue;
            }

            validProviders.Add(
                provider);
        }

        var providerMethods =
            new Dictionary<INamedTypeSymbol, List<ApiMethodInput>>(
                SymbolEqualityComparer.Default);

        foreach (INamedTypeSymbol provider in providers)
        {
            if (!validProviders.Contains(
                provider))
            {
                continue;
            }

            List<ApiMethodInput> validMethods =
                GetValidProviderMethods(
                    context,
                    provider,
                    methodInputs,
                    validProviders);

            providerMethods.Add(
                provider,
                validMethods);

            context.AddSource(
                HintName(
                    provider,
                    "ApiProvider"),
                BuildProviderSource(
                    provider,
                    validMethods));
        }

        GenerateClientMirrors(
            context,
            compilation,
            rootNamespace,
            providers,
            validProviders,
            providerMethods,
            managerInputs);

        GenerateManagers(
            context,
            compilation,
            managerInputs,
            validProviders);
    }


    static IReadOnlyList<INamedTypeSymbol> DistinctTypes(
        ImmutableArray<INamedTypeSymbol> symbols)
    {
        var result =
            new List<INamedTypeSymbol>();

        var seen =
            new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default);

        foreach (INamedTypeSymbol symbol in symbols)
        {
            if (symbol != null && seen.Add(symbol))
                result.Add(symbol);
        }

        result.Sort(static (left, right) =>
            string.CompareOrdinal(
                TypeDisplayName(left),
                TypeDisplayName(right)));

        return result;
    }


    static bool IsValidProvider(
        INamedTypeSymbol provider)
    {
        if (provider.TypeKind != TypeKind.Class ||
            provider.IsStatic ||
            provider.IsAbstract ||
            provider.IsRecord ||
            provider.IsGenericType)
        {
            return false;
        }

        return !provider.GetMembers("GetApi")
            .OfType<IMethodSymbol>()
            .Any(method =>
                method.Parameters.Length == 0 &&
                !method.IsGenericMethod);
    }


    static List<ApiMethodInput> GetValidProviderMethods(
        SourceProductionContext context,
        INamedTypeSymbol provider,
        ImmutableArray<ApiMethodInput> methodInputs,
        HashSet<INamedTypeSymbol> validProviders)
    {
        var methods =
            methodInputs
                .Where(method =>
                    method != null &&
                    SymbolEqualityComparer.Default.Equals(
                        method.Method?.ContainingType,
                        provider))
                .OrderBy(method =>
                    SourcePath(
                        method.Location),
                    StringComparer.Ordinal)
                .ThenBy(method =>
                    SourceSpanStart(
                        method.Location))
                .ToList();

        var result =
            new List<ApiMethodInput>();

        var ids =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (ApiMethodInput method in methods)
        {
            if (!IsValidApiMethod(
                    method,
                    validProviders) ||
                string.IsNullOrWhiteSpace(
                    method.Id))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InvalidMethod,
                        method.Location,
                        MethodDisplayName(
                            method.Method)));

                continue;
            }

            if (!ids.Add(
                method.Id))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DuplicateMethodId,
                        method.Location,
                        TypeDisplayName(
                            provider),
                        method.Id));

                continue;
            }

            result.Add(
                method);
        }

        return result;
    }


    static bool IsValidApiMethod(
        ApiMethodInput input,
        HashSet<INamedTypeSymbol> validProviders)
    {
        IMethodSymbol method =
            input?.Method;

        if (method == null ||
            method.MethodKind != MethodKind.Ordinary ||
            method.IsGenericMethod ||
            method.IsAbstract ||
            method.IsExtern ||
            method.ReturnsByRef ||
            method.ReturnsByRefReadonly ||
            method.ExplicitInterfaceImplementations.Length > 0 ||
            method.Parameters.Length > 16)
        {
            return false;
        }

        if (!IsValidDelegateType(
            method.ReturnType))
        {
            return false;
        }

        foreach (IParameterSymbol parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None ||
                !IsValidDelegateType(
                    parameter.Type))
            {
                return false;
            }
        }

        if (input.ReturnType == null)
            return true;

        var namedReturnType =
            input.ReturnType as INamedTypeSymbol;

        if (namedReturnType != null &&
            validProviders.Contains(
                namedReturnType))
        {
            return IsApiDataType(
                method.ReturnType);
        }

        return IsPrimitiveType(
                   input.ReturnType) &&
               SymbolEqualityComparer.Default.Equals(
                   input.ReturnType,
                   method.ReturnType);
    }


    static bool IsPrimitiveType(
        ITypeSymbol type)
    {
        if (type == null)
            return false;

        return type.TypeKind == TypeKind.Enum ||
               type.SpecialType != SpecialType.None &&
               type.SpecialType != SpecialType.System_Object;
    }


    static bool IsEnumType(
        ITypeSymbol type)
    {
        var namedType =
            type as INamedTypeSymbol;

        return namedType?.TypeKind == TypeKind.Enum &&
               namedType.EnumUnderlyingType != null;
    }


    static ITypeSymbol ApiWireType(
        ITypeSymbol type)
    {
        var namedType =
            type as INamedTypeSymbol;

        return namedType?.TypeKind == TypeKind.Enum
            ? namedType.EnumUnderlyingType ?? type
            : type;
    }


    static bool IsApiDataType(
        ITypeSymbol type)
    {
        var namedType =
            type as INamedTypeSymbol;

        return namedType != null &&
               namedType.Name == "Dictionary" &&
               namedType.Arity == 2 &&
               namedType.ContainingNamespace?.ToDisplayString() ==
                   "System.Collections.Generic" &&
               namedType.TypeArguments[0].SpecialType ==
                   SpecialType.System_String &&
               namedType.TypeArguments[1].ToDisplayString() ==
                   "System.Delegate";
    }


    static bool IsValidDelegateType(
        ITypeSymbol type)
    {
        if (type == null)
            return false;

        if (type.TypeKind == TypeKind.Pointer ||
            type.TypeKind == TypeKind.FunctionPointer)
        {
            return false;
        }

        var namedType =
            type as INamedTypeSymbol;

        return namedType == null ||
               !namedType.IsRefLikeType;
    }


    static string BuildProviderSource(
        INamedTypeSymbol provider,
        IReadOnlyList<ApiMethodInput> methods)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine("// <auto-generated/>");
        AppendNamespaceOpen(
            builder,
            provider);
        AppendTypeDeclarationsOpen(
            builder,
            provider,
            false,
            false);

        builder.AppendLine("        internal global::System.Collections.Generic.Dictionary<string, global::System.Delegate> GetApi()");
        builder.AppendLine("        {");
        builder.AppendLine("            return new global::System.Collections.Generic.Dictionary<string, global::System.Delegate>");
        builder.AppendLine("            {");

        foreach (ApiMethodInput method in methods)
        {
            builder.Append("                { ");
            builder.Append(
                EscapeLiteral(
                    method.Id));
            builder.Append(", ");
            AppendDelegateConstruction(
                builder,
                method.Method);
            builder.AppendLine(" },");
        }

        builder.AppendLine("            };");
        builder.AppendLine("        }");

        AppendTypeDeclarationsClose(
            builder,
            provider);
        AppendNamespaceClose(
            builder,
            provider);

        return builder.ToString();
    }


    static void GenerateClientMirrors(
        SourceProductionContext context,
        Compilation compilation,
        string rootNamespace,
        IReadOnlyList<INamedTypeSymbol> providers,
        HashSet<INamedTypeSymbol> validProviders,
        Dictionary<INamedTypeSymbol, List<ApiMethodInput>> providerMethods,
        ImmutableArray<ManagerInput> managerInputs)
    {
        var mirrors =
            new Dictionary<INamedTypeSymbol, ClientMirrorInput>(
                SymbolEqualityComparer.Default);

        var mirrorNames =
            new HashSet<string>(
                StringComparer.Ordinal);

        var mirrorFileNames =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (INamedTypeSymbol provider in providers)
        {
            if (!validProviders.Contains(
                provider))
            {
                continue;
            }

            ClientMirrorInput mirror =
                ReadClientMirror(
                    provider);

            string fullName =
                mirror.Namespace +
                "." +
                mirror.Name;

            if (!IsValidNamespace(
                    mirror.Namespace) ||
                !SyntaxFacts.IsValidIdentifier(
                    mirror.Name) ||
                compilation.GetTypeByMetadataName(
                    fullName) != null ||
                !mirrorNames.Add(
                    fullName) ||
                !mirrorFileNames.Add(
                    mirror.Name))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InvalidClientMirror,
                        SymbolLocation(
                            provider),
                        TypeDisplayName(
                            provider),
                        fullName));

                continue;
            }

            mirrors.Add(
                provider,
                mirror);
        }

        foreach (KeyValuePair<INamedTypeSymbol, ClientMirrorInput> pair in
            mirrors.OrderBy(
                value => value.Value.FullName,
                StringComparer.Ordinal))
        {
            List<ApiMethodInput> methods;

            if (!providerMethods.TryGetValue(
                pair.Key,
                out methods))
            {
                continue;
            }

            bool nestedMirrorMissing =
                methods.Any(method =>
                    method.ReturnType is INamedTypeSymbol nestedProvider &&
                    validProviders.Contains(
                        nestedProvider) &&
                    !mirrors.ContainsKey(
                        nestedProvider));

            if (nestedMirrorMissing)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InvalidClientMirror,
                        SymbolLocation(
                            pair.Key),
                        TypeDisplayName(
                            pair.Key),
                        pair.Value.FullName));

                continue;
            }

            context.AddSource(
                pair.Value.Name,
                BuildClientMirrorSource(
                    pair.Key,
                    pair.Value,
                    methods,
                    mirrors,
                    validProviders));
        }

        GenerateClientEnums(
            context,
            providerMethods,
            mirrors,
            mirrorFileNames);

        var generatedBootstrappers =
            new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default);

        foreach (ManagerInput manager in managerInputs
            .Where(value => value != null)
            .OrderBy(value => value.Port))
        {
            INamedTypeSymbol selectedProvider =
                ResolveSelectedProvider(
                    manager,
                    validProviders);

            ClientMirrorInput mirror;

            if (selectedProvider == null ||
                !mirrors.TryGetValue(
                    selectedProvider,
                    out mirror) ||
                !generatedBootstrappers.Add(
                    selectedProvider))
            {
                continue;
            }

            string bootstrapperFileName =
                GetClientBootstrapperName(
                    rootNamespace);

            if (!mirrorFileNames.Add(
                bootstrapperFileName))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InvalidClientMirror,
                        manager.Location,
                        TypeDisplayName(
                            selectedProvider),
                        bootstrapperFileName));

                continue;
            }

            context.AddSource(
                bootstrapperFileName,
                BuildClientBootstrapperSource(
                    mirror,
                    bootstrapperFileName,
                    manager.Port));
        }
    }


    static void GenerateClientEnums(
        SourceProductionContext context,
        Dictionary<INamedTypeSymbol, List<ApiMethodInput>> providerMethods,
        Dictionary<INamedTypeSymbol, ClientMirrorInput> mirrors,
        HashSet<string> generatedFileNames)
    {
        var generatedEnums =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (KeyValuePair<INamedTypeSymbol, ClientMirrorInput> pair in
            mirrors.OrderBy(
                value => value.Value.FullName,
                StringComparer.Ordinal))
        {
            List<ApiMethodInput> methods;

            if (!providerMethods.TryGetValue(
                    pair.Key,
                    out methods))
            {
                continue;
            }

            foreach (INamedTypeSymbol enumType in
                GetApiEnumTypes(methods)
                    .OrderBy(
                        TypeDisplayName,
                        StringComparer.Ordinal))
            {
                string fullName =
                    pair.Value.Namespace +
                    "." +
                    enumType.Name;

                if (!generatedEnums.Add(
                        fullName))
                {
                    continue;
                }

                if (!generatedFileNames.Add(
                        enumType.Name))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            InvalidClientMirror,
                            SymbolLocation(
                                enumType),
                            TypeDisplayName(
                                pair.Key),
                            fullName));

                    continue;
                }

                context.AddSource(
                    enumType.Name,
                    BuildClientEnumSource(
                        enumType,
                        pair.Value.Namespace));
            }
        }
    }


    static IReadOnlyList<INamedTypeSymbol> GetApiEnumTypes(
        IReadOnlyList<ApiMethodInput> methods)
    {
        var result =
            new List<INamedTypeSymbol>();

        var seen =
            new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default);

        foreach (ApiMethodInput method in methods)
        {
            AddApiEnumType(
                method.Method.ReturnType,
                seen,
                result);

            foreach (IParameterSymbol parameter in
                method.Method.Parameters)
            {
                AddApiEnumType(
                    parameter.Type,
                    seen,
                    result);
            }
        }

        return result;
    }


    static void AddApiEnumType(
        ITypeSymbol type,
        HashSet<INamedTypeSymbol> seen,
        List<INamedTypeSymbol> result)
    {
        var enumType =
            type as INamedTypeSymbol;

        if (IsEnumType(
                enumType) &&
            seen.Add(
                enumType))
        {
            result.Add(
                enumType);
        }
    }


    static INamedTypeSymbol ResolveSelectedProvider(
        ManagerInput manager,
        HashSet<INamedTypeSymbol> validProviders)
    {
        if (string.IsNullOrWhiteSpace(
            manager.Provider))
        {
            return validProviders.Contains(
                manager.Type)
                ? manager.Type
                : null;
        }

        foreach (ISymbol member in manager.Type.GetMembers(
            manager.Provider))
        {
            var field =
                member as IFieldSymbol;

            if (field != null &&
                !field.IsStatic &&
                field.Type is INamedTypeSymbol fieldType &&
                validProviders.Contains(
                    fieldType))
            {
                return fieldType;
            }

            var property =
                member as IPropertySymbol;

            if (property != null &&
                !property.IsStatic &&
                property.Parameters.Length == 0 &&
                property.GetMethod != null &&
                property.Type is INamedTypeSymbol propertyType &&
                validProviders.Contains(
                    propertyType))
            {
                return propertyType;
            }
        }

        return null;
    }


    static string BuildClientBootstrapperSource(
        ClientMirrorInput mirror,
        string bootstrapperName,
        long managerPort)
    {
        var builder =
            new StringBuilder();

        string mirrorName =
            EscapeIdentifier(
                mirror.Name);

        bootstrapperName =
            EscapeIdentifier(
                bootstrapperName);

        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("using System;");
        builder.AppendLine("using Sandbox.ModAPI;");
        builder.AppendLine("using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;");
        builder.AppendLine();
        builder.Append("namespace ");
        builder.Append(
            mirror.Namespace);
        builder.AppendLine();
        builder.AppendLine("{");
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// Performs a one-shot local mod-message request for the root API.");
        builder.AppendLine("    /// </summary>");
        builder.Append("    public static class ");
        builder.Append(
            bootstrapperName);
        builder.AppendLine();
        builder.AppendLine("    {");
        builder.Append("        public const long REGISTRATION_CHANNEL = ");
        builder.Append(
            LongLiteral(
                managerPort));
        builder.AppendLine(";");
        builder.AppendLine();
        builder.AppendLine("        /// <summary>");
        builder.AppendLine("        /// Returns the root API when the manager replies synchronously; otherwise null.");
        builder.AppendLine("        /// </summary>");
        builder.Append("        public static ");
        builder.Append(
            mirrorName);
        builder.AppendLine(" TryGet(");
        builder.AppendLine("            long replyChannel)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (replyChannel == REGISTRATION_CHANNEL)");
        builder.AppendLine("                return null;");
        builder.AppendLine();
        builder.Append("            ");
        builder.Append(
            mirrorName);
        builder.AppendLine(" result = null;");
        builder.AppendLine("            bool registered = false;");
        builder.AppendLine();
        builder.AppendLine("            Action<object> handler =");
        builder.AppendLine("                delegate(object payload)");
        builder.AppendLine("                {");
        builder.AppendLine("                    ApiData api = payload as ApiData;");
        builder.AppendLine();
        builder.AppendLine("                    if (api != null)");
        builder.AppendLine("                    {");
        builder.Append("                        result = new ");
        builder.Append(
            mirrorName);
        builder.AppendLine("(api);");
        builder.AppendLine("                    }");
        builder.AppendLine("                };");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                MyAPIGateway.Utilities.RegisterMessageHandler(");
        builder.AppendLine("                    replyChannel,");
        builder.AppendLine("                    handler);");
        builder.AppendLine();
        builder.AppendLine("                registered = true;");
        builder.AppendLine();
        builder.AppendLine("                MyAPIGateway.Utilities.SendModMessage(");
        builder.AppendLine("                    REGISTRATION_CHANNEL,");
        builder.AppendLine("                    replyChannel);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                result = null;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                if (registered)");
        builder.AppendLine("                {");
        builder.AppendLine("                    try");
        builder.AppendLine("                    {");
        builder.AppendLine("                        MyAPIGateway.Utilities.UnregisterMessageHandler(");
        builder.AppendLine("                            replyChannel,");
        builder.AppendLine("                            handler);");
        builder.AppendLine("                    }");
        builder.AppendLine("                    catch");
        builder.AppendLine("                    {");
        builder.AppendLine("                    }");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return result;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }


    static string GetClientBootstrapperName(
        string rootNamespace)
    {
        if (string.IsNullOrWhiteSpace(
            rootNamespace))
        {
            return "Client";
        }

        return rootNamespace.Replace(
                   ".",
                   string.Empty) +
               "Client";
    }


    static string GetRootNamespace(
        AnalyzerConfigOptionsProvider optionsProvider,
        Compilation compilation)
    {
        string rootNamespace;

        if (optionsProvider.GlobalOptions.TryGetValue(
                "build_property.RootNamespace",
                out rootNamespace) &&
            !string.IsNullOrWhiteSpace(
                rootNamespace))
        {
            return rootNamespace.Trim();
        }

        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees)
        {
            SyntaxNode root =
                syntaxTree.GetRoot();

            foreach (BaseNamespaceDeclarationSyntax declaration in
                root.DescendantNodes()
                    .OfType<BaseNamespaceDeclarationSyntax>())
            {
                string namespaceName =
                    declaration.Name.ToString();

                int marker =
                    namespaceName.IndexOf(
                        ".Server",
                        StringComparison.Ordinal);

                if (marker > 0 &&
                    IsNamespaceBoundaryMarker(
                        namespaceName,
                        marker,
                        ".Server"))
                {
                    return namespaceName.Substring(
                        0,
                        marker);
                }
            }
        }

        return null;
    }


    static bool IsNamespaceBoundaryMarker(
        string namespaceName,
        int index,
        string marker)
    {
        int end =
            index + marker.Length;

        return end == namespaceName.Length ||
               namespaceName[end] == '.';
    }


    static ClientMirrorInput ReadClientMirror(
        INamedTypeSymbol provider)
    {
        string clientNamespace =
            "Generated.Api";

        string clientName =
            DefaultClientMirrorName(
                provider.Name);

        AttributeData attribute =
            provider.GetAttributes()
                .FirstOrDefault(value =>
                    value.AttributeClass?.ToDisplayString() ==
                        API_PROVIDER_ATTRIBUTE);

        if (attribute != null)
        {
            foreach (KeyValuePair<string, TypedConstant> pair in
                attribute.NamedArguments)
            {
                if (pair.Key == "ClientNamespace")
                {
                    clientNamespace =
                        pair.Value.Value as string;
                }

                if (pair.Key == "ClientName")
                {
                    clientName =
                        pair.Value.Value as string;
                }
            }
        }

        return new ClientMirrorInput(
            provider,
            clientNamespace,
            clientName);
    }


    static string DefaultClientMirrorName(
        string providerName)
    {
        const string serverSuffix =
            "Server";

        if (providerName.EndsWith(
            serverSuffix,
            StringComparison.Ordinal))
        {
            return providerName.Substring(
                0,
                providerName.Length - serverSuffix.Length);
        }

        return providerName.EndsWith(
            "Api",
            StringComparison.Ordinal)
            ? providerName
            : providerName + "Api";
    }


    static bool IsValidNamespace(
        string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(
            namespaceName))
        {
            return false;
        }

        string[] segments =
            namespaceName.Split('.');

        return segments.All(
            SyntaxFacts.IsValidIdentifier);
    }


    static string BuildClientMirrorSource(
        INamedTypeSymbol provider,
        ClientMirrorInput mirror,
        IReadOnlyList<ApiMethodInput> methods,
        Dictionary<INamedTypeSymbol, ClientMirrorInput> mirrors,
        HashSet<INamedTypeSymbol> validProviders)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine("// <auto-generated/>");

        foreach (string importedNamespace in
            GetClientMirrorNamespaces(
                mirror,
                methods,
                mirrors,
                validProviders))
        {
            builder.Append("using ");
            builder.Append(
                importedNamespace);
            builder.AppendLine(";");
        }

        builder.AppendLine("using ApiData = System.Collections.Generic.Dictionary<string, System.Delegate>;");
        builder.AppendLine();
        builder.Append("namespace ");
        builder.Append(
            mirror.Namespace);
        builder.AppendLine();
        builder.AppendLine("{");

        AppendDocumentation(
            builder,
            provider,
            "    ");

        builder.Append("    public sealed class ");
        builder.Append(
            EscapeIdentifier(
                mirror.Name));
        builder.AppendLine();
        builder.AppendLine("    {");

        var fieldNames =
            CreateClientFieldNames(
                methods);

        for (int i = 0;
            i < methods.Count;
            i++)
        {
            builder.Append("        private readonly ");
            AppendDelegateType(
                builder,
                methods[i].Method);
            builder.Append(" ");
            builder.Append(
                fieldNames[i]);
            builder.AppendLine(";");
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.Append("        public ");
        builder.Append(
            EscapeIdentifier(
                mirror.Name));
        builder.AppendLine("(");
        builder.AppendLine("            ApiData api)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (api == null)");
        builder.AppendLine("                throw new ArgumentNullException(\"api\");");

        for (int i = 0;
            i < methods.Count;
            i++)
        {
            builder.AppendLine();
            builder.Append("            ");
            builder.Append(
                fieldNames[i]);
            builder.AppendLine(" =");
            builder.Append("                GetRequired<");
            AppendDelegateType(
                builder,
                methods[i].Method);
            builder.AppendLine(">(");
            builder.AppendLine("                    api,");
            builder.Append("                    ");
            builder.Append(
                EscapeLiteral(
                    methods[i].Id));
            builder.AppendLine(");");
        }

        builder.AppendLine("        }");

        for (int i = 0;
            i < methods.Count;
            i++)
        {
            ApiMethodInput method =
                methods[i];

            builder.AppendLine();
            builder.AppendLine();
            AppendDocumentation(
                builder,
                method.Method,
                "        ");
            builder.Append("        public ");
            AppendClientReturnType(
                builder,
                method,
                mirrors,
                validProviders);
            builder.Append(" ");
            builder.Append(
                EscapeIdentifier(
                    method.Method.Name));
            builder.Append("(");

            if (method.Method.Parameters.Length == 0)
            {
                builder.AppendLine(")");
            }
            else
            {
                AppendClientParameters(
                    builder,
                    method.Method);
            }
            builder.AppendLine("        {");

            var nestedProvider =
                method.ReturnType as INamedTypeSymbol;

            bool returnsNestedApi =
                nestedProvider != null &&
                validProviders.Contains(
                    nestedProvider);

            if (returnsNestedApi)
            {
                builder.AppendLine("            ApiData nestedApi =");
                builder.Append("                ");
                AppendClientInvocation(
                    builder,
                    fieldNames[i],
                    method.Method);
                builder.AppendLine(";");
                builder.AppendLine();
                builder.AppendLine("            return nestedApi == null");
                builder.AppendLine("                ? null");
                builder.Append("                : new ");
                builder.Append(
                    EscapeIdentifier(
                        mirrors[nestedProvider].Name));
                builder.AppendLine("(nestedApi);");
            }
            else if (method.Method.ReturnsVoid)
            {
                builder.Append("            ");
                AppendClientInvocation(
                    builder,
                    fieldNames[i],
                    method.Method);
                builder.AppendLine(";");
            }
            else
            {
                builder.Append("            return ");

                if (IsEnumType(
                    method.Method.ReturnType))
                {
                    builder.Append("(");
                    builder.Append(
                        ClientPublicTypeDisplayName(
                            method.Method.ReturnType));
                    builder.Append(")");
                }

                AppendClientInvocation(
                    builder,
                    fieldNames[i],
                    method.Method);
                builder.AppendLine(";");
            }

            builder.AppendLine("        }");
        }

        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("        private static T GetRequired<T>(");
        builder.AppendLine("            ApiData api,");
        builder.AppendLine("            string id)");
        builder.AppendLine("            where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            Delegate value;");
        builder.AppendLine("            T result = api.TryGetValue(id, out value)");
        builder.AppendLine("                ? value as T");
        builder.AppendLine("                : null;");
        builder.AppendLine();
        builder.AppendLine("            if (result == null)");
        builder.AppendLine("            {");
        builder.Append("                throw new InvalidOperationException(\"API mirror '");
        builder.Append(
            EscapeLogText(
                mirror.FullName));
        builder.AppendLine("' is missing delegate '\" + id + \"'.\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return result;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }


    static string BuildClientEnumSource(
        INamedTypeSymbol enumType,
        string clientNamespace)
    {
        var builder =
            new StringBuilder();

        bool hasFlags =
            enumType.GetAttributes()
                .Any(attribute =>
                    attribute.AttributeClass?.ToDisplayString() ==
                        "System.FlagsAttribute");

        builder.AppendLine("// <auto-generated/>");

        if (hasFlags)
        {
            builder.AppendLine("using System;");
            builder.AppendLine();
        }

        builder.Append("namespace ");
        builder.Append(
            clientNamespace);
        builder.AppendLine();
        builder.AppendLine("{");

        AppendDocumentation(
            builder,
            enumType,
            "    ");

        if (hasFlags)
            builder.AppendLine("    [Flags]");

        builder.Append("    public enum ");
        builder.Append(
            EscapeIdentifier(
                enumType.Name));

        if (enumType.EnumUnderlyingType?.SpecialType !=
            SpecialType.System_Int32)
        {
            builder.Append(" : ");
            builder.Append(
                enumType.EnumUnderlyingType?.ToDisplayString(
                    SymbolDisplayFormat.MinimallyQualifiedFormat));
        }

        builder.AppendLine();
        builder.AppendLine("    {");

        IFieldSymbol[] members =
            enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field =>
                    field.HasConstantValue)
                .ToArray();

        for (int index = 0;
            index < members.Length;
            index++)
        {
            IFieldSymbol member =
                members[index];

            AppendDocumentation(
                builder,
                member,
                "        ");

            builder.Append("        ");
            builder.Append(
                EscapeIdentifier(
                    member.Name));
            builder.Append(" = ");
            builder.Append(
                Convert.ToString(
                    member.ConstantValue,
                    System.Globalization.CultureInfo.InvariantCulture));

            if (index < members.Length - 1)
                builder.Append(",");

            builder.AppendLine();
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");

        return builder.ToString();
    }


    static IReadOnlyList<string> GetClientMirrorNamespaces(
        ClientMirrorInput mirror,
        IReadOnlyList<ApiMethodInput> methods,
        Dictionary<INamedTypeSymbol, ClientMirrorInput> mirrors,
        HashSet<INamedTypeSymbol> validProviders)
    {
        var namespaces =
            new HashSet<string>(
                StringComparer.Ordinal)
            {
                "System"
            };

        foreach (ApiMethodInput method in methods)
        {
            AddClientTypeNamespaces(
                namespaces,
                method.Method.ReturnType);

            foreach (IParameterSymbol parameter in
                method.Method.Parameters)
            {
                AddClientTypeNamespaces(
                    namespaces,
                    parameter.Type);
            }

            var nestedProvider =
                method.ReturnType as INamedTypeSymbol;

            if (nestedProvider != null &&
                validProviders.Contains(
                    nestedProvider))
            {
                namespaces.Add(
                    mirrors[nestedProvider].Namespace);
            }
        }

        namespaces.Remove(
            mirror.Namespace);

        return namespaces
            .OrderBy(value =>
                value.StartsWith(
                    "System",
                    StringComparison.Ordinal)
                    ? 0
                    : 1)
            .ThenBy(
                value => value,
                StringComparer.Ordinal)
            .ToList();
    }


    static void AddClientTypeNamespaces(
        HashSet<string> namespaces,
        ITypeSymbol type)
    {
        if (IsApiDataType(
            type))
        {
            return;
        }

        if (IsEnumType(
            type))
        {
            AddClientTypeNamespaces(
                namespaces,
                ApiWireType(
                    type));

            return;
        }

        var arrayType =
            type as IArrayTypeSymbol;

        if (arrayType != null)
        {
            AddClientTypeNamespaces(
                namespaces,
                arrayType.ElementType);

            return;
        }

        var namedType =
            type as INamedTypeSymbol;

        if (namedType == null)
            return;

        string namespaceName =
            namedType.ContainingNamespace?.IsGlobalNamespace == false
                ? namedType.ContainingNamespace.ToDisplayString()
                : null;

        if (!string.IsNullOrWhiteSpace(
            namespaceName))
        {
            namespaces.Add(
                namespaceName);
        }

        foreach (ITypeSymbol typeArgument in
            namedType.TypeArguments)
        {
            AddClientTypeNamespaces(
                namespaces,
                typeArgument);
        }
    }


    static string ClientWireTypeDisplayName(
        ITypeSymbol type)
    {
        if (IsApiDataType(
            type))
        {
            return "ApiData";
        }

        return ApiWireType(type)?.ToDisplayString(
                   SymbolDisplayFormat.MinimallyQualifiedFormat)
               ?? string.Empty;
    }


    static string ClientPublicTypeDisplayName(
        ITypeSymbol type)
    {
        var enumType =
            type as INamedTypeSymbol;

        if (IsEnumType(
            enumType))
        {
            return EscapeIdentifier(
                enumType.Name);
        }

        return ClientWireTypeDisplayName(
            type);
    }


    static IReadOnlyList<string> CreateClientFieldNames(
        IReadOnlyList<ApiMethodInput> methods)
    {
        var result =
            new List<string>();

        var used =
            new HashSet<string>(
                StringComparer.Ordinal);

        foreach (ApiMethodInput method in methods)
        {
            string methodName =
                method.Method.Name;

            string stem =
                "_" +
                char.ToLowerInvariant(
                    methodName[0]) +
                methodName.Substring(1);

            string candidate =
                stem;

            int suffix =
                2;

            while (!used.Add(
                candidate))
            {
                candidate =
                    stem +
                    suffix;

                suffix++;
            }

            result.Add(
                candidate);
        }

        return result;
    }


    static void AppendDelegateType(
        StringBuilder builder,
        IMethodSymbol method)
    {
        bool returnsVoid =
            method.ReturnsVoid;

        builder.Append(
            returnsVoid
                ? "Action"
                : "Func");

        int argumentCount =
            method.Parameters.Length +
            (returnsVoid ? 0 : 1);

        if (argumentCount == 0)
            return;

        builder.Append("<");

        for (int i = 0;
            i < method.Parameters.Length;
            i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(
                ClientWireTypeDisplayName(
                    method.Parameters[i].Type));
        }

        if (!returnsVoid)
        {
            if (method.Parameters.Length > 0)
                builder.Append(", ");

            builder.Append(
                ClientWireTypeDisplayName(
                    method.ReturnType));
        }

        builder.Append(">");
    }


    static void AppendClientReturnType(
        StringBuilder builder,
        ApiMethodInput method,
        Dictionary<INamedTypeSymbol, ClientMirrorInput> mirrors,
        HashSet<INamedTypeSymbol> validProviders)
    {
        var nestedProvider =
            method.ReturnType as INamedTypeSymbol;

        if (nestedProvider != null &&
            validProviders.Contains(
                nestedProvider))
        {
            builder.Append(
                EscapeIdentifier(
                    mirrors[nestedProvider].Name));

            return;
        }

        builder.Append(
            ClientPublicTypeDisplayName(
                method.Method.ReturnType));
    }


    static void AppendClientParameters(
        StringBuilder builder,
        IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return;

        builder.AppendLine();

        for (int i = 0;
            i < method.Parameters.Length;
            i++)
        {
            IParameterSymbol parameter =
                method.Parameters[i];

            builder.Append("            ");
            builder.Append(
                ClientPublicTypeDisplayName(
                    parameter.Type));
            builder.Append(" ");
            builder.Append(
                EscapeIdentifier(
                    parameter.Name));

            if (i < method.Parameters.Length - 1)
            {
                builder.AppendLine(",");
            }
            else
            {
                builder.AppendLine(")");
            }
        }
    }


    static void AppendClientInvocation(
        StringBuilder builder,
        string fieldName,
        IMethodSymbol method)
    {
        builder.Append(
            fieldName);
        builder.Append("(");

        for (int i = 0;
            i < method.Parameters.Length;
            i++)
        {
            if (i > 0)
                builder.Append(", ");

            IParameterSymbol parameter =
                method.Parameters[i];

            if (IsEnumType(
                parameter.Type))
            {
                builder.Append("(");
                builder.Append(
                    ClientWireTypeDisplayName(
                        parameter.Type));
                builder.Append(")");
            }

            builder.Append(
                EscapeIdentifier(
                    parameter.Name));
        }

        builder.Append(")");
    }


    static void AppendDocumentation(
        StringBuilder builder,
        ISymbol symbol,
        string indentation)
    {
        foreach (SyntaxReference reference in
            symbol.DeclaringSyntaxReferences)
        {
            SyntaxNode declaration =
                reference.GetSyntax();

            string[] leadingLines =
                declaration.GetLeadingTrivia()
                    .ToFullString()
                    .Replace("\r", string.Empty)
                    .Split('\n');

            bool wroteDocumentation =
                false;

            foreach (string leadingLine in leadingLines)
            {
                string trimmedLeadingLine =
                    leadingLine.TrimStart();

                if (!trimmedLeadingLine.StartsWith(
                    "///",
                    StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(indentation);
                builder.AppendLine(
                    trimmedLeadingLine);

                wroteDocumentation =
                    true;
            }

            if (wroteDocumentation)
                return;

            foreach (SyntaxTrivia trivia in
                declaration.GetLeadingTrivia())
            {
                if (!trivia.HasStructure ||
                    !(trivia.GetStructure() is
                        DocumentationCommentTriviaSyntax documentation))
                {
                    continue;
                }

                string[] sourceLines =
                    documentation.ToFullString()
                        .Replace("\r", string.Empty)
                        .Split('\n');

                foreach (string sourceLine in sourceLines)
                {
                    string trimmedSourceLine =
                        sourceLine.TrimStart();

                    if (trimmedSourceLine.Length == 0)
                        continue;

                    builder.Append(indentation);
                    builder.AppendLine(
                        trimmedSourceLine);
                }

                return;
            }
        }

        string xml =
            symbol.GetDocumentationCommentXml(
                null,
                true,
                default);

        if (string.IsNullOrWhiteSpace(
            xml))
        {
            return;
        }

        int openingEnd =
            xml.IndexOf('>');

        int closingStart =
            xml.LastIndexOf(
                "</member>",
                StringComparison.Ordinal);

        if (openingEnd < 0 ||
            closingStart <= openingEnd)
        {
            return;
        }

        string content =
            xml.Substring(
                openingEnd + 1,
                closingStart - openingEnd - 1);

        string[] lines =
            content.Replace("\r", string.Empty)
                .Split('\n');

        foreach (string line in lines)
        {
            string trimmed =
                line.Trim();

            if (trimmed.Length == 0)
                continue;

            builder.Append(indentation);
            builder.Append("/// ");
            builder.AppendLine(trimmed);
        }
    }


    static void AppendDelegateConstruction(
        StringBuilder builder,
        IMethodSymbol method)
    {
        bool returnsVoid =
            method.ReturnsVoid;

        bool requiresEnumAdapter =
            IsEnumType(
                method.ReturnType) ||
            method.Parameters.Any(parameter =>
                IsEnumType(
                    parameter.Type));

        builder.Append("new global::System.");
        builder.Append(
            returnsVoid
                ? "Action"
                : "Func");

        int typeArgumentCount =
            method.Parameters.Length +
            (returnsVoid ? 0 : 1);

        if (typeArgumentCount > 0)
        {
            builder.Append("<");

            for (int i = 0;
                i < method.Parameters.Length;
                i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append(
                    TypeDisplayName(
                        ApiWireType(
                            method.Parameters[i].Type)));
            }

            if (!returnsVoid)
            {
                if (method.Parameters.Length > 0)
                    builder.Append(", ");

                builder.Append(
                    TypeDisplayName(
                        ApiWireType(
                            method.ReturnType)));
            }

            builder.Append(">");
        }

        builder.Append("(");

        if (!requiresEnumAdapter)
        {
            if (!method.IsStatic)
                builder.Append("this.");

            builder.Append(
                EscapeIdentifier(
                    method.Name));
        }
        else
        {
            builder.Append("(");

            for (int i = 0;
                i < method.Parameters.Length;
                i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append("__apiArg");
                builder.Append(i);
            }

            builder.Append(") => ");

            if (IsEnumType(
                method.ReturnType))
            {
                builder.Append("(");
                builder.Append(
                    TypeDisplayName(
                        ApiWireType(
                            method.ReturnType)));
                builder.Append(")");
            }

            if (!method.IsStatic)
                builder.Append("this.");

            builder.Append(
                EscapeIdentifier(
                    method.Name));
            builder.Append("(");

            for (int i = 0;
                i < method.Parameters.Length;
                i++)
            {
                if (i > 0)
                    builder.Append(", ");

                ITypeSymbol parameterType =
                    method.Parameters[i].Type;

                if (IsEnumType(
                    parameterType))
                {
                    builder.Append("(");
                    builder.Append(
                        TypeDisplayName(
                            parameterType));
                    builder.Append(")");
                }

                builder.Append("__apiArg");
                builder.Append(i);
            }

            builder.Append(")");
        }

        builder.Append(")");
    }


    static void GenerateManagers(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ManagerInput> managerInputs,
        HashSet<INamedTypeSymbol> validProviders)
    {
        INamedTypeSymbol sessionBase =
            compilation.GetTypeByMetadataName(
                "VRage.Game.Components.MySessionComponentBase");

        var ports =
            new Dictionary<long, ManagerInput>();

        foreach (ManagerInput manager in managerInputs
            .Where(value => value != null)
            .OrderBy(value => TypeDisplayName(value.Type), StringComparer.Ordinal))
        {
            ManagerInput existing;

            if (ports.TryGetValue(
                manager.Port,
                out existing))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        DuplicateManagerPort,
                        manager.Location,
                        manager.Port,
                        TypeDisplayName(
                            existing.Type)));

                continue;
            }

            ports.Add(
                manager.Port,
                manager);

            if (!AreTypeAndContainersPartial(
                manager.Type))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        PartialTypeRequired,
                        manager.Location,
                        TypeDisplayName(
                            manager.Type)));

                continue;
            }

            bool addSessionBase;

            if (!IsValidManager(
                manager.Type,
                sessionBase,
                out addSessionBase))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InvalidManager,
                        manager.Location,
                        TypeDisplayName(
                            manager.Type)));

                continue;
            }

            string apiExpression;

            if (!TryResolveManagerApiExpression(
                manager,
                validProviders,
                out apiExpression))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        ManagerNeedsApi,
                        manager.Location,
                        TypeDisplayName(
                            manager.Type)));

                continue;
            }

            context.AddSource(
                HintName(
                    manager.Type,
                    "ApiManager"),
                BuildManagerSource(
                    manager.Type,
                    manager.Port,
                    apiExpression,
                    !HasSessionComponentDescriptor(
                        manager.Type),
                    addSessionBase,
                    !HasDeclaredInstanceMethod(
                        manager.Type,
                        "LoadData"),
                    !HasDeclaredInstanceMethod(
                        manager.Type,
                        "UnloadData")));
        }
    }


    static bool TryResolveManagerApiExpression(
        ManagerInput manager,
        HashSet<INamedTypeSymbol> validProviders,
        out string apiExpression)
    {
        apiExpression =
            null;

        if (string.IsNullOrWhiteSpace(
            manager.Provider))
        {
            if (!validProviders.Contains(
                    manager.Type) &&
                !HasCompatibleGetApi(
                    manager.Type))
            {
                return false;
            }

            apiExpression =
                "GetApi()";

            return true;
        }

        ISymbol selectedMember =
            null;

        ITypeSymbol providerType =
            null;

        foreach (ISymbol member in manager.Type.GetMembers(
            manager.Provider))
        {
            var field =
                member as IFieldSymbol;

            if (field != null && !field.IsStatic)
            {
                if (selectedMember != null)
                    return false;

                selectedMember =
                    field;

                providerType =
                    field.Type;

                continue;
            }

            var property =
                member as IPropertySymbol;

            if (property != null &&
                !property.IsStatic &&
                property.Parameters.Length == 0 &&
                property.GetMethod != null)
            {
                if (selectedMember != null)
                    return false;

                selectedMember =
                    property;

                providerType =
                    property.Type;
            }
        }

        var providerNamedType =
            providerType as INamedTypeSymbol;

        if (selectedMember == null ||
            providerNamedType == null ||
            (!validProviders.Contains(
                 providerNamedType) &&
             !HasCompatibleGetApi(
                 providerNamedType)))
        {
            return false;
        }

        apiExpression =
            "this." +
            EscapeIdentifier(
                selectedMember.Name) +
            ".GetApi()";

        return true;
    }


    static bool IsValidManager(
        INamedTypeSymbol manager,
        INamedTypeSymbol sessionBase,
        out bool addSessionBase)
    {
        addSessionBase =
            false;

        if (manager == null ||
            sessionBase == null ||
            manager.TypeKind != TypeKind.Class ||
            manager.IsStatic ||
            manager.IsAbstract ||
            manager.IsRecord ||
            manager.IsGenericType ||
            manager.ContainingType != null ||
            !HasParameterlessInstanceConstructor(
                manager) ||
            HasGeneratedManagerMemberConflict(
                manager))
        {
            return false;
        }

        bool derivesFromSession =
            InheritsFrom(
                manager,
                sessionBase);

        if (derivesFromSession)
            return true;

        if (manager.BaseType == null ||
            manager.BaseType.SpecialType !=
                SpecialType.System_Object)
        {
            return false;
        }

        addSessionBase =
            true;

        return true;
    }


    static bool HasParameterlessInstanceConstructor(
        INamedTypeSymbol manager)
    {
        return manager.InstanceConstructors.Any(constructor =>
            !constructor.IsStatic &&
            constructor.Parameters.Length == 0);
    }


    static bool HasGeneratedManagerMemberConflict(
        INamedTypeSymbol manager)
    {
        string[] generatedMembers =
        {
            "GeneratedApiManagerOnRequest",
            "_generatedApiManagerRegistered",
            "RegisterApiManager",
            "UnregisterApiManager"
        };

        foreach (string memberName in generatedMembers)
        {
            if (manager.GetMembers(memberName).Length > 0)
                return true;
        }

        return false;
    }


    static bool HasDeclaredInstanceMethod(
        INamedTypeSymbol type,
        string name)
    {
        return type.GetMembers(name)
            .OfType<IMethodSymbol>()
            .Any(method => !method.IsStatic);
    }


    static bool HasCompatibleGetApi(
        INamedTypeSymbol manager)
    {
        foreach (IMethodSymbol method in manager.GetMembers("GetApi")
            .OfType<IMethodSymbol>())
        {
            if (method.IsStatic || method.Parameters.Length != 0)
                continue;

            var returnType =
                method.ReturnType as INamedTypeSymbol;

            if (returnType == null ||
                returnType.Name != "Dictionary" ||
                returnType.Arity != 2 ||
                returnType.ContainingNamespace?.ToDisplayString() !=
                    "System.Collections.Generic")
            {
                continue;
            }

            if (returnType.TypeArguments[0].SpecialType !=
                    SpecialType.System_String ||
                returnType.TypeArguments[1].ToDisplayString() !=
                    "System.Delegate")
            {
                continue;
            }

            return true;
        }

        return false;
    }


    static bool InheritsFrom(
        INamedTypeSymbol type,
        INamedTypeSymbol baseType)
    {
        INamedTypeSymbol current =
            type;

        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(
                current,
                baseType))
            {
                return true;
            }

            current =
                current.BaseType;
        }

        return false;
    }


    static bool HasAttribute(
        INamedTypeSymbol type,
        string attributeMetadataName)
    {
        return type.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() ==
                attributeMetadataName);
    }


    static bool HasSessionComponentDescriptor(
        INamedTypeSymbol type)
    {
        return HasAttribute(
                   type,
                   "VRage.Game.Components.MySessionComponentDescriptor") ||
               HasAttribute(
                   type,
                   "VRage.Game.Components.MySessionComponentDescriptorAttribute");
    }


    static string BuildManagerSource(
        INamedTypeSymbol manager,
        long port,
        string apiExpression,
        bool addSessionDescriptor,
        bool addSessionBase,
        bool generateLoadData,
        bool generateUnloadData)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine("// <auto-generated/>");
        AppendNamespaceOpen(
            builder,
            manager);
        AppendTypeDeclarationsOpen(
            builder,
            manager,
            addSessionDescriptor,
            addSessionBase);

        builder.AppendLine("        private bool _generatedApiManagerRegistered;");
        builder.AppendLine();
        builder.AppendLine("        private void RegisterApiManager()");
        builder.AppendLine("        {");
        builder.AppendLine("            if (_generatedApiManagerRegistered)");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.Append("            global::Sandbox.ModAPI.MyAPIGateway.Utilities.RegisterMessageHandler(");
        builder.Append(
            LongLiteral(
                port));
        builder.AppendLine(", GeneratedApiManagerOnRequest);");
        builder.AppendLine("            _generatedApiManagerRegistered = true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private void UnregisterApiManager()");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!_generatedApiManagerRegistered)");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.Append("            global::Sandbox.ModAPI.MyAPIGateway.Utilities.UnregisterMessageHandler(");
        builder.Append(
            LongLiteral(
                port));
        builder.AppendLine(", GeneratedApiManagerOnRequest);");
        builder.AppendLine("            _generatedApiManagerRegistered = false;");
        builder.AppendLine("        }");
        builder.AppendLine();

        if (generateLoadData)
        {
            builder.AppendLine("        public override void LoadData()");
            builder.AppendLine("        {");
            builder.AppendLine("            RegisterApiManager();");
            builder.AppendLine("        }");
            builder.AppendLine();
        }

        if (generateUnloadData)
        {
            builder.AppendLine("        protected override void UnloadData()");
            builder.AppendLine("        {");
            builder.AppendLine("            UnregisterApiManager();");
            builder.AppendLine("        }");
            builder.AppendLine();
        }

        builder.AppendLine("        private void GeneratedApiManagerOnRequest(object payload)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!(payload is long))");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            long replyPort = (long)payload;");
        builder.Append("            if (replyPort == ");
        builder.Append(
            LongLiteral(
                port));
        builder.AppendLine(")");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                var api = ");
        builder.Append(
            apiExpression);
        builder.AppendLine(";");
        builder.AppendLine("                if (api == null)");
        builder.AppendLine("                    throw new global::System.Exception(\"Generated API provider returned null.\");");
        builder.AppendLine();
        builder.AppendLine("                global::Sandbox.ModAPI.MyAPIGateway.Utilities.SendModMessage(");
        builder.AppendLine("                    replyPort,");
        builder.AppendLine("                    new global::System.Collections.Generic.Dictionary<string, global::System.Delegate>(api));");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception error)");
        builder.AppendLine("            {");
        builder.Append("                global::VRage.Utils.MyLog.Default.WriteLineAndConsole(\"[Generated API Manager:");
        builder.Append(
            EscapeLogText(
                TypeDisplayName(
                    manager)));
        builder.AppendLine("] Could not respond to API request: \" + error);");
        builder.AppendLine("            }");
        builder.AppendLine("        }");

        AppendTypeDeclarationsClose(
            builder,
            manager);
        AppendNamespaceClose(
            builder,
            manager);

        return builder.ToString();
    }


    static bool AreTypeAndContainersPartial(
        INamedTypeSymbol type)
    {
        INamedTypeSymbol current =
            type;

        while (current != null)
        {
            bool partial =
                current.DeclaringSyntaxReferences.Length > 0 &&
                current.DeclaringSyntaxReferences.All(reference =>
                {
                    var declaration =
                        reference.GetSyntax() as TypeDeclarationSyntax;

                    return declaration != null &&
                           declaration.Modifiers.Any(
                               SyntaxKind.PartialKeyword);
                });

            if (!partial)
                return false;

            current =
                current.ContainingType;
        }

        return true;
    }


    static void AppendNamespaceOpen(
        StringBuilder builder,
        INamedTypeSymbol type)
    {
        string namespaceName =
            type.ContainingNamespace?.IsGlobalNamespace == false
                ? type.ContainingNamespace.ToDisplayString()
                : null;

        if (string.IsNullOrEmpty(
            namespaceName))
        {
            return;
        }

        builder.Append("namespace ");
        builder.Append(namespaceName);
        builder.AppendLine();
        builder.AppendLine("{");
    }


    static void AppendNamespaceClose(
        StringBuilder builder,
        INamedTypeSymbol type)
    {
        if (type.ContainingNamespace?.IsGlobalNamespace == false)
            builder.AppendLine("}");
    }


    static void AppendTypeDeclarationsOpen(
        StringBuilder builder,
        INamedTypeSymbol type,
        bool addSessionDescriptor,
        bool addSessionBase)
    {
        var types =
            new List<INamedTypeSymbol>();

        INamedTypeSymbol current =
            type;

        while (current != null)
        {
            types.Add(current);
            current = current.ContainingType;
        }

        types.Reverse();

        foreach (INamedTypeSymbol currentType in types)
        {
            bool isTarget =
                SymbolEqualityComparer.Default.Equals(
                    currentType,
                    type);

            if (addSessionDescriptor && isTarget)
            {
                builder.AppendLine("    [global::VRage.Game.Components.MySessionComponentDescriptor(global::VRage.Game.Components.MyUpdateOrder.NoUpdate)]");
            }

            builder.Append("    partial class ");
            builder.Append(
                EscapeIdentifier(
                    currentType.Name));
            AppendTypeParameters(
                builder,
                currentType);

            if (addSessionBase &&
                isTarget)
            {
                builder.Append(" : global::VRage.Game.Components.MySessionComponentBase");
            }

            builder.AppendLine();
            builder.AppendLine("    {");
        }
    }


    static void AppendTypeDeclarationsClose(
        StringBuilder builder,
        INamedTypeSymbol type)
    {
        INamedTypeSymbol current =
            type;

        while (current != null)
        {
            builder.AppendLine("    }");
            current = current.ContainingType;
        }
    }


    static void AppendTypeParameters(
        StringBuilder builder,
        INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0)
            return;

        builder.Append("<");

        for (int i = 0;
            i < type.TypeParameters.Length;
            i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(
                EscapeIdentifier(
                    type.TypeParameters[i].Name));
        }

        builder.Append(">");
    }


    static string HintName(
        INamedTypeSymbol type,
        string suffix)
    {
        return TypeDisplayName(type)
                   .Replace("global::", string.Empty)
                   .Replace("<", "_")
                   .Replace(">", "_")
                   .Replace(",", "_")
                   .Replace(".", "_") +
               "." +
               suffix +
               ".g.cs";
    }


    static string TypeDisplayName(
        ISymbol symbol)
    {
        return symbol?.ToDisplayString(
                   SymbolDisplayFormat.FullyQualifiedFormat)
               ?? string.Empty;
    }


    static string MethodDisplayName(
        IMethodSymbol method)
    {
        return method?.ToDisplayString(
                   SymbolDisplayFormat.CSharpErrorMessageFormat)
               ?? string.Empty;
    }


    static string EscapeIdentifier(
        string identifier)
    {
        if (SyntaxFacts.GetKeywordKind(identifier) !=
                SyntaxKind.None ||
            SyntaxFacts.GetContextualKeywordKind(identifier) !=
                SyntaxKind.None)
        {
            return "@" + identifier;
        }

        return identifier;
    }


    static string EscapeLiteral(
        string value)
    {
        return "\"" +
               value
                   .Replace("\\", "\\\\")
                   .Replace("\"", "\\\"")
                   .Replace("\r", "\\r")
                   .Replace("\n", "\\n") +
               "\"";
    }


    static string EscapeLogText(
        string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", " ")
            .Replace("\n", " ");
    }


    static string LongLiteral(
        long value)
    {
        if (value == long.MinValue)
            return "global::System.Int64.MinValue";

        return value.ToString(
                   System.Globalization.CultureInfo.InvariantCulture) +
               "L";
    }


    static string SourcePath(
        Location location)
    {
        return location == null ||
               !location.IsInSource
            ? string.Empty
            : location.SourceTree?.FilePath ?? string.Empty;
    }


    static int SourceSpanStart(
        Location location)
    {
        return location == null ||
               !location.IsInSource
            ? int.MaxValue
            : location.SourceSpan.Start;
    }


    static Location SymbolLocation(
        ISymbol symbol)
    {
        return symbol?.Locations.FirstOrDefault()
               ?? Location.None;
    }


    sealed class ManagerInput
    {
        public readonly INamedTypeSymbol Type;
        public readonly long Port;
        public readonly string Provider;
        public readonly Location Location;


        public ManagerInput(
            INamedTypeSymbol type,
            long port,
            string provider,
            Location location)
        {
            Type = type;
            Port = port;
            Provider = provider;
            Location = location;
        }
    }


    sealed class ApiMethodInput
    {
        public readonly IMethodSymbol Method;
        public readonly string Id;
        public readonly ITypeSymbol ReturnType;
        public readonly Location Location;


        public ApiMethodInput(
            IMethodSymbol method,
            string id,
            ITypeSymbol returnType,
            Location location)
        {
            Method = method;
            Id = id;
            ReturnType = returnType;
            Location = location;
        }
    }


    sealed class ClientMirrorInput
    {
        public readonly INamedTypeSymbol Provider;
        public readonly string Namespace;
        public readonly string Name;


        public ClientMirrorInput(
            INamedTypeSymbol provider,
            string clientNamespace,
            string name)
        {
            Provider = provider;
            Namespace = clientNamespace;
            Name = name;
        }


        public string FullName
        {
            get { return Namespace + "." + Name; }
        }


        public string FullyQualifiedName
        {
            get { return "global::" + FullName; }
        }
    }


    const string ATTRIBUTE_SOURCE = @"// <auto-generated/>
namespace Generated
{
    /// <summary>
    /// Promotes a partial class to a Space Engineers session component that
    /// listens on the supplied request port. Requests contain a long reply port.
    /// Its selected root provider also receives a basic static client bootstrapper.
    /// If the class already owns LoadData or UnloadData, those methods must call
    /// the generated RegisterApiManager or UnregisterApiManager method.
    /// </summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class APIManagerAttribute : global::System.Attribute
    {
        public APIManagerAttribute(long port)
        {
            Port = port;
        }

        public long Port { get; private set; }

        /// <summary>
        /// Optional instance field or property whose ApiProvider supplies the
        /// root dictionary published on this manager's port. This keeps nested
        /// ApiProviders out of transport discovery. When omitted, the manager
        /// class itself supplies GetApi().
        /// </summary>
        public string Provider { get; set; }
    }

    /// <summary>
    /// Generates an internal Dictionary&lt;string, Delegate&gt; GetApi() method
    /// for the attributed partial class and a public dictionary-backed client
    /// mirror.
    /// </summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    internal sealed class ApiProviderAttribute : global::System.Attribute
    {
        /// <summary>
        /// Namespace for the generated public client mirror.
        /// Defaults to Generated.Api.
        /// </summary>
        public string ClientNamespace { get; set; }

        /// <summary>
        /// Name of the generated public client mirror. By default, Server is
        /// removed from the provider name or Api is appended.
        /// </summary>
        public string ClientName { get; set; }
    }

    /// <summary>
    /// Exposes a method through its containing ApiProvider. The optional id
    /// replaces the method name used as the dictionary key. A Type argument can
    /// repeat a primitive wire return type or name another ApiProvider when the
    /// wire method returns Dictionary&lt;string, Delegate&gt; for a nested API.
    /// </summary>
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    internal sealed class ApiMethodAttribute : global::System.Attribute
    {
        public ApiMethodAttribute()
        {
        }

        public ApiMethodAttribute(string id)
        {
            Id = id;
        }

        public ApiMethodAttribute(global::System.Type returnType)
        {
            ReturnType = returnType;
        }

        public ApiMethodAttribute(string id, global::System.Type returnType)
        {
            Id = id;
            ReturnType = returnType;
        }

        public string Id { get; set; }
        public global::System.Type ReturnType { get; set; }
    }
}";
}
