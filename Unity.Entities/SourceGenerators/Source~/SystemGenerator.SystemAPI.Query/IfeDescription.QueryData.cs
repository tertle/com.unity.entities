using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.SystemAPI.Query;

public partial class IfeDescription
{
    struct QueryData
    {
        public string TypeSymbolFullName { get; set; }
        public ITypeSymbol TypeSymbol { get; set; }
        public ITypeSymbol TypeParameterSymbol { get; set; }
        public QueryType QueryType { get; set; }
        public bool IsReadOnly => QueryType is QueryType.RefRO or QueryType.EnabledRefRO or QueryType.ValueTypeComponent or QueryType.UnmanagedSharedComponent or QueryType.ManagedSharedComponent;
        public ITypeSymbol QueriedTypeSymbol => TypeParameterSymbol ?? TypeSymbol;
    }

    private bool TryGetQueryDatas()
    {
#pragma warning disable RS1024
        InitialIterableEnableableTypeSymbols = new HashSet<ITypeSymbol>();
#pragma warning restore RS1024

        InitialIterableEnableableQueryDatas = new List<QueryData>();
        IterableEnableableQueryDatasToBeTreatedAsAllComponents = new List<QueryData>();

        AllIterableQueryDatas = new List<QueryData>();

        foreach (var typeSyntax in QueryCandidate.QueryTypeNodes)
        {
            var typeSymbol = SystemDescription.SemanticModel.GetTypeInfo(typeSyntax).Type;
            var typeParameterSymbol = default(ITypeSymbol);

            var genericNameCandidate = typeSyntax;
            if (typeSyntax is QualifiedNameSyntax qualifiedNameSyntax) // This is the case when people type out their syntax Query<MyNameSpace.MyThing>
                genericNameCandidate = qualifiedNameSyntax.Right;
            if (genericNameCandidate is GenericNameSyntax genericNameSyntax)
            {
                var typeArg = genericNameSyntax.TypeArgumentList.Arguments.Single();
                typeParameterSymbol = SystemDescription.SemanticModel.GetTypeInfo(typeArg).Type;
            }

            var result = TryGetIdiomaticCSharpForEachQueryType(typeSymbol, typeSyntax.GetLocation());

            if (result.QueryType == QueryType.Invalid)
                return false;

            if (result.QueryType == QueryType.ValueTypeComponent)
                IfeCompilerMessages.SGFE009(SystemDescription, typeSymbol.ToFullName(), Location);

            var queryData = new QueryData
            {
                TypeParameterSymbol = typeParameterSymbol,
                TypeSymbol = typeSymbol,
                TypeSymbolFullName = typeSymbol.ToFullName(),
                QueryType = result.QueryType,
            };
            if (result.IsTypeEnableable)
            {
                InitialIterableEnableableQueryDatas.Add(queryData);
                IterableEnableableQueryDatasToBeTreatedAsAllComponents.Add(queryData);

                InitialIterableEnableableTypeSymbols.Add(queryData.QueriedTypeSymbol);

                AllIterableQueryDatas.Add(queryData);
            }
            else
            {
                AllIterableQueryDatas.Add(queryData);

                _iterableNonEnableableTypes.Add(new Common.Query()
                {
                    IsReadOnly = queryData.IsReadOnly,
                    TypeSymbol = queryData.QueriedTypeSymbol,
                    Type = Common.QueryType.All
                });
            }
        }
        return true;

        static bool HasTypeParameter(ITypeSymbol typeArgument)
        {
            if (typeArgument is INamedTypeSymbol namedTypeSymbol)
                foreach (var typeArg in namedTypeSymbol.TypeArguments)
                    if (typeArg is ITypeParameterSymbol || HasTypeParameter(typeArg))
                        return true;

            return false;
        }

        (QueryType QueryType, bool IsTypeEnableable) TryGetIdiomaticCSharpForEachQueryType(ITypeSymbol typeSymbol, Location errorLocation)
        {
            // `typeSymbol` is an error type.  This is usually caused by an ambiguous type.
            // Go ahead and mark the query as invalid and let roslyn report the other error.
            if (typeSymbol is IErrorTypeSymbol)
                return (QueryType.Invalid, false);

            if (typeSymbol.IsAspect())
                return (QueryType.Aspect, false);

            if (typeSymbol.IsSharedComponent())
                return (typeSymbol.IsUnmanagedType ? QueryType.UnmanagedSharedComponent : QueryType.ManagedSharedComponent, false);

            if (typeSymbol.IsComponent())
            {
                if (typeSymbol.InheritsFromType("System.ValueType"))
                    return (typeSymbol.IsZeroSizedComponent() ? QueryType.TagComponent : QueryType.ValueTypeComponent, typeSymbol.IsEnableableComponent());

                return(QueryType.ManagedComponent, false);
            }

            var typeArgument = ((INamedTypeSymbol)typeSymbol).TypeArguments[0];
            if (typeArgument is ITypeParameterSymbol)
            {
                IfeCompilerMessages.SGFE011(SystemDescription, errorLocation);
                return (QueryType.Invalid, false);
            }

            bool isQueryTypeEnableable = false;
            if (typeArgument is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.Arity != 0)
            {
                // If T is itself generic
                if (HasTypeParameter(typeArgument))
                {
                    IfeCompilerMessages.SGFE010(SystemDescription, errorLocation);
                    return (QueryType.Invalid, false);
                }

                // T is not generic
                var componentType = namedTypeSymbol.TypeArguments[0];
                isQueryTypeEnableable = componentType.IsEnableableComponent();
            }

            return typeSymbol.Name switch
            {
                "DynamicBuffer" => (QueryType.DynamicBuffer, false),
                "RefRW" => (QueryType.RefRW, isQueryTypeEnableable),
                "RefRO" => (QueryType.RefRO, isQueryTypeEnableable),
                "EnabledRefRW" => (QueryType.EnabledRefRW, true),
                "EnabledRefRO" => (QueryType.EnabledRefRO, true),
                "UnityEngineComponent" => (QueryType.UnityEngineComponent, false),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}
