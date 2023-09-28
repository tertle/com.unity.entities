using System;
using System.CodeDom.Compiler;
using Microsoft.CodeAnalysis;
using Unity.Entities.SourceGen.Common;

namespace Unity.Entities.SourceGen.SystemGenerator.Common;

public readonly struct TypeHandleFieldDescription : IEquatable<TypeHandleFieldDescription>, IMemberDescription
{
    public enum TypeHandleSource
    {
        Aspect,
        Component,
        SharedComponent,
        BufferElement
    }
    ITypeSymbol TypeSymbol { get; }
    bool IsReadOnly { get; }

    TypeHandleSource Source { get; } // I'm sure we know the type at the call site,
    // lets either split this into four classes
    // or see if we can find a way to
    // not have a bazillion INonQueryFieldDescription
    public string GeneratedFieldName { get; }

    public void AppendMemberDeclaration(IndentedTextWriter w, bool forcePublic = false)
    {
        switch (Source)
        {
            case TypeHandleSource.Aspect:
                if (IsReadOnly)
                    w.Write("[global::Unity.Collections.ReadOnly] ");
                if (forcePublic)
                    w.Write("public ");
                w.Write($"{TypeSymbol.ToFullName()}.TypeHandle {GeneratedFieldName};");
                break;
            case TypeHandleSource.BufferElement:
                if (IsReadOnly)
                    w.Write("[global::Unity.Collections.ReadOnly] ");
                if (forcePublic)
                    w.Write("public ");
                w.Write($"Unity.Entities.BufferTypeHandle<{TypeSymbol.ToFullName()}> {GeneratedFieldName};");
                break;
            case TypeHandleSource.Component:
                if (IsReadOnly)
                    w.Write("[global::Unity.Collections.ReadOnly] ");
                if (forcePublic)
                    w.Write("public ");
                w.Write($"Unity.Entities.ComponentTypeHandle<{TypeSymbol.ToFullName()}> {GeneratedFieldName};");
                break;
            default:
                if (IsReadOnly)
                    w.Write("[global::Unity.Collections.ReadOnly] ");
                if (forcePublic)
                    w.Write("public ");
                w.Write($"Unity.Entities.SharedComponentTypeHandle<{TypeSymbol.ToFullName()}> {GeneratedFieldName};");
                break;
        }
        w.WriteLine();
    }

    public string GetMemberAssignment() => Source switch
    {
        TypeHandleSource.Aspect => $"{GeneratedFieldName} = new {TypeSymbol.ToFullName()}.TypeHandle(ref state);",
        TypeHandleSource.BufferElement => $"{GeneratedFieldName} = state.GetBufferTypeHandle<{TypeSymbol.ToFullName()}>({(IsReadOnly ? "true" : "false")});",
        TypeHandleSource.Component =>
            TypeSymbol.IsReferenceType
                ? $"{GeneratedFieldName} = state.EntityManager.GetComponentTypeHandle<{TypeSymbol.ToFullName()}>({(IsReadOnly ? "true" : "false")});"
                : $"{GeneratedFieldName} = state.GetComponentTypeHandle<{TypeSymbol.ToFullName()}>({(IsReadOnly ? "true" : "false")});",
        _ => $"{GeneratedFieldName} = state.GetSharedComponentTypeHandle<{TypeSymbol.ToFullName()}>();"
    };

    public TypeHandleFieldDescription(ITypeSymbol typeSymbol, bool isReadOnly)
    {
        TypeSymbol = typeSymbol;
        IsReadOnly = isReadOnly;

        var typeSymbolValidIdentifier = TypeSymbol.ToValidIdentifier();
        if (TypeSymbol.IsAspect())
        {
            GeneratedFieldName = $"__{typeSymbolValidIdentifier}_{(IsReadOnly ? "RO" : "RW")}_AspectTypeHandle";
            Source = TypeHandleSource.Aspect;
        }
        else if (typeSymbol.InheritsFromInterface("Unity.Entities.IBufferElementData"))
        {
            GeneratedFieldName = $"__{typeSymbolValidIdentifier}_{(IsReadOnly ? "RO" : "RW")}_BufferTypeHandle";
            Source = TypeHandleSource.BufferElement;
        }
        else if (typeSymbol.IsSharedComponent())
        {
            GeneratedFieldName = $"__{typeSymbolValidIdentifier}_SharedComponentTypeHandle";
            Source = TypeHandleSource.SharedComponent;
        }
        else
        {
            GeneratedFieldName = $"__{typeSymbolValidIdentifier}_{(IsReadOnly ? "RO" : "RW")}_ComponentTypeHandle";
            Source = TypeHandleSource.Component;
        }
    }

    public bool Equals(TypeHandleFieldDescription other) =>
        SymbolEqualityComparer.Default.Equals(TypeSymbol, other.TypeSymbol) && IsReadOnly == other.IsReadOnly && Source == other.Source;

    public override int GetHashCode()
    {
        unchecked
        {
            var hashCode =  TypeSymbol != null ? SymbolEqualityComparer.Default.GetHashCode(TypeSymbol) : 0;
            hashCode = (hashCode * 397) ^ IsReadOnly.GetHashCode();
            hashCode = (hashCode * 397) ^ (int)Source;
            return hashCode;
        }
    }
}
