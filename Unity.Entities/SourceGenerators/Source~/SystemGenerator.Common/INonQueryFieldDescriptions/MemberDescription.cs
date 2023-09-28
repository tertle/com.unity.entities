using System;
using System.CodeDom.Compiler;

namespace Unity.Entities.SourceGen.SystemGenerator.Common;

public interface IMemberWriter
{
    public void WriteTo(IndentedTextWriter writer);
}

public interface IMemberDescription
{
    string GeneratedFieldName { get; }
    void AppendMemberDeclaration(IndentedTextWriter w, bool forcePublic = false);
    string GetMemberAssignment();
}

public class MemberDescription : IMemberDescription, IEquatable<MemberDescription>
{
    private readonly IMemberWriter _memberWriter;

    public MemberDescription(IMemberWriter memberWriter) => _memberWriter = memberWriter;

    public string GeneratedFieldName => string.Empty;
    public void AppendMemberDeclaration(IndentedTextWriter w, bool forcePublic = false) => _memberWriter.WriteTo(w);
    public string GetMemberAssignment() => string.Empty;


    public bool Equals(MemberDescription other) => other != null && GetType() == other.GetType();

    public override int GetHashCode() => typeof(MemberDescription).GetHashCode();
}
