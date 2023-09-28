using System;
using System.CodeDom.Compiler;

namespace Unity.Entities.SourceGen.SystemGenerator.Common;

public readonly struct ContainerTypeHandleFieldDescription : IEquatable<ContainerTypeHandleFieldDescription>, IMemberDescription
{
    string ContainerTypeName { get; }
    public string GeneratedFieldName { get; }
    public void AppendMemberDeclaration(IndentedTextWriter w, bool forcePublic = false)
    {
        if (forcePublic)
            w.Write("public ");
        w.Write($"{ContainerTypeName}.TypeHandle {GeneratedFieldName};");
        w.WriteLine();
    }

    public string GetMemberAssignment()
        => $@"{GeneratedFieldName} = new {ContainerTypeName}.TypeHandle(ref state, isReadOnly: false);";

    public ContainerTypeHandleFieldDescription(string containerTypeName)
    {
        ContainerTypeName = containerTypeName;
        GeneratedFieldName = $"__{containerTypeName.Replace(".", "_")}_RW_TypeHandle";
    }

    public bool Equals(ContainerTypeHandleFieldDescription other) => ContainerTypeName == other.ContainerTypeName;
    public override int GetHashCode() => ContainerTypeName != null ? ContainerTypeName.GetHashCode() : 0;
}
