using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using Unity.Cecil.Awesome;
using Unity.Cecil.Awesome.Comparers;

namespace Unity.Entities.CodeGen
{
    public static class TypeReferenceExtensions
    {
        public static TypeDefinition CheckedResolve(this TypeReference typeReference)
        {
            return typeReference.Resolve() ?? throw new ResolutionException(typeReference);
        }

        public static bool TypeReferenceEqualsOrInheritsFrom(this TypeReference ref1, TypeReference ref2)
        {
            if (TypeReferenceEqualityComparer.AreEqual(ref1, ref2))
                return true;

            var def1 = ref1.Resolve();
            if (def1 != null && def1.BaseType != null)
                return def1.BaseType.TypeReferenceEqualsOrInheritsFrom(ref2);

            return false;
        }


        public static TypeReference LaunderTypeRef(TypeReference r_, ModuleDefinition mod)
        {
            TypeDefinition def = r_.Resolve();
            TypeReference result;

            if (r_ is GenericInstanceType git)
            {
                var gt = new GenericInstanceType(LaunderTypeRef(def, mod));
                foreach (var gp in git.GenericParameters)
                {
                    gt.GenericParameters.Add(gp);
                }

                foreach (var ga in git.GenericArguments)
                {
                    gt.GenericArguments.Add(LaunderTypeRef(ga, mod));
                }

                result = gt;

            }
            else
            {
                result = new TypeReference(def.Namespace, def.Name, def.Module, def.Scope, def.IsValueType);
                if (def.DeclaringType != null)
                {
                    result.DeclaringType = LaunderTypeRef(def.DeclaringType, mod);
                }
            }

            return mod.ImportReference(result);
        }

        public static bool TypeImplements(this TypeReference typeReference, TypeReference interfaceType)
        {
            var resolvedType = typeReference.Resolve();
            if (resolvedType == null) return false;

            return resolvedType.Interfaces.Any(i =>
            {
                var interfaceDef = i.InterfaceType.Resolve();
                return interfaceDef != null && TypeReferenceEqualityComparer.AreEqual(interfaceDef, interfaceType);
            });
        }

        public static TypeReference StripRef(this TypeReference tr) => tr is ByReferenceType brt ? brt.ElementType : tr;

        public static bool IsVoid(this TypeReference type)
        {
            return type.MetadataType == MetadataType.Void;
        }

        // Roslyn can decide to start this class with <>c__DisplayClass or just <>c
        // (seems to do the latter with DisplayClasses in value types for some reason)
        public static bool IsDisplayClassCandidate(this TypeReference tr) =>
            tr.Name.StartsWith("<>c");
            
            
        // Roslyn tends to generate local functions with names that contain g__ (and they are always static)
        public static bool IsLocalFunctionCandidate(this MethodDefinition md) =>
            (md.IsStatic && md.Name.Contains("g__"));
    }

    static class MethodReferenceExtensions
    {
        /// <summary>
        /// Generates a closed/specialized MethodReference for the given method and types[]
        /// e.g.
        /// <![CDATA[struct Foo { T Bar<T>(T val) { return default(T); }]]>
        ///
        /// In this case, if one would like a reference to `Foo::int Bar(int val)` this method will construct such a method
        /// reference when provided the open `T Bar(T val)` method reference and the TypeReferences to the types you'd like
        /// specified as generic arguments (in this case a TypeReference to "int" would be passed in).
        /// </summary>
        /// <param name="method"></param>
        /// <param name="types"></param>
        /// <returns></returns>
        public static MethodReference MakeGenericInstanceMethod(this MethodReference method,
            params TypeReference[] types)
        {
            var result = new GenericInstanceMethod(method);
            foreach (var type in types)
                result.GenericArguments.Add(type);
            return result;
        }

        /// <summary>
        /// Allows one to generate reference to a method contained in a generic type which has been closed/specialized.
        /// e.g.
        /// <![CDATA[struct Foo<T> { T Bar(T val) { return default(T); }]]>
        ///
        /// In this case, if one would like a reference to <code><![CDATA[Foo<int>::int Bar(int val)]]></code> this method will construct such a method
        /// reference when provided the open `T Bar(T val)` method reference and the closed declaring TypeReference, Foo&lt;int&gt;.
        /// </summary>
        /// <param name="self"></param>
        /// <param name="closedDeclaringType">See summary above for example. Typically construct this type using `MakeGenericInstanceMethod`</param>
        /// <returns></returns>
        public static MethodReference MakeGenericHostMethod(this MethodReference self, TypeReference closedDeclaringType)
        {
            var reference = new MethodReference(self.Name, self.ReturnType, closedDeclaringType)
            {
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis,
                CallingConvention = self.CallingConvention
            };

            foreach (var parameter in self.Parameters)
            {
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
            }

            foreach (var genericParam in self.GenericParameters)
            {
                reference.GenericParameters.Add(new GenericParameter(genericParam.Name, reference));
            }

            return reference;
        }
    }

    static class ParameterDefinitionExtensions
    {
        internal static bool HasCompilerServicesIsReadOnlyAttribute(this ParameterDefinition p)
        {
            return p.HasCustomAttributes && p.CustomAttributes.Any(c =>
                c.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        }
    }



    static class TypeDefinitionExtensions
    {
        public static bool IsDelegate(this TypeDefinition typeDefinition) =>

            //XXX THIS IS BAD https://jira.unity3d.com/browse/DOTS-10211
            typeDefinition.BaseType?.Name == nameof(MulticastDelegate);

        public static bool IsSystemBase(this TypeDefinition arg)
        {
            var baseTypeRef = arg.BaseType;

            if (baseTypeRef == null)
                return false;

            //XXX THIS IS BAD https://jira.unity3d.com/browse/DOTS-10211
            if (baseTypeRef.Namespace == "Unity.Entities" && baseTypeRef.Name == "SystemBase")
                return true;

            if (baseTypeRef.Name == "Object" && baseTypeRef.Namespace == "System")
                return false;

            if (baseTypeRef.Name == "ValueType" && baseTypeRef.Namespace == "System")
                return false;

            return IsSystemBase(baseTypeRef.Resolve());
        }

        public static bool IsComponentSystem(this TypeDefinition arg)
        {
            while (true)
            {
                var baseTypeRef = arg.BaseType;

                if (arg.Interfaces.Any(typeInterface => typeInterface.InterfaceType.FullName == "Unity.Entities.ISystem"))
                    return true;

                if (baseTypeRef == null) return false;

                ///XXX THIS IS BAD https://jira.unity3d.com/browse/DOTS-10211
                if (baseTypeRef.Namespace == "Unity.Entities" && baseTypeRef.Name == "ComponentSystemBase") return true;

                switch (baseTypeRef.Name)
                {
                    case "Object" when baseTypeRef.Namespace == "System":
                    case "ValueType" when baseTypeRef.Namespace == "System":
                        return false;
                    default:
                        arg = baseTypeRef.Resolve();
                        break;
                }
            }
        }


        public static bool IsUnityEngineObject(this TypeDefinition typeDefinition)
        {
            if (typeDefinition.IsValueType())
                return false;
            //XXX BAD https://jira.unity3d.com/browse/DOTS-10211
            if (typeDefinition.Name == "Object" && typeDefinition.Namespace == "UnityEngine")
                return true;
            if (typeDefinition.BaseType == null)
                return false;
            var baseType = typeDefinition.BaseType.Resolve();
            return IsUnityEngineObject(baseType);
        }



        public static bool IsChildTypeOf(this TypeDefinition typeDefinition, TypeDefinition baseClass)
        {
            while (!TypeReferenceEqualityComparer.AreEqual(baseClass, typeDefinition))
            {
                if (typeDefinition == null || typeDefinition.BaseType == null)
                    return false;
                typeDefinition = typeDefinition.BaseType.Resolve();
            }

            return true;
        }

        public static void MakeTypeInternal(this TypeDefinition typeDefinition)
        {
            if (typeDefinition.IsNested)
            {
                if (!typeDefinition.IsNestedPublic)
                {
                    typeDefinition.IsNestedFamilyOrAssembly = true;
                }
		        MakeTypeInternal(typeDefinition.DeclaringType);
            }
            else if (!typeDefinition.IsPublic)
            {
                typeDefinition.IsNotPublic = true;
            }
        }

        public static void MakeTypePublic(this TypeDefinition typeDefinition)
        {
            if (typeDefinition.IsNested)
            {
                if (!typeDefinition.IsNestedPublic)
                {
                    typeDefinition.IsNestedPublic = true;
                }
            }
            else if (!typeDefinition.IsPublic)
            {
                typeDefinition.IsPublic = true;
            }
        }
    }

    static class ILProcessorExtensions
    {
        public static void EnsurePreviousAndNextAreSet(this MethodBody body)
        {
            for (int i = 0; i != body.Instructions.Count - 1; i++)
            {
                var thisOne = body.Instructions[i];
                var nextOne = body.Instructions[i + 1];
                thisOne.Next = nextOne;
                nextOne.Previous = thisOne;
            }
        }

        public static void InsertAfter(this ILProcessor ilProcessor, Instruction insertAfterThisOne,
            IEnumerable<Instruction> instructions)
        {
            var prev = insertAfterThisOne;
            foreach (var instruction in instructions)
            {
                ilProcessor.InsertAfter(prev, instruction);
                prev = instruction;
            }
        }

        public static void InsertBefore(this ILProcessor ilProcessor, Instruction insertBeforeThisOne,
            IEnumerable<Instruction> instructions)
        {
            foreach (var instruction in instructions)
                ilProcessor.InsertBefore(insertBeforeThisOne, instruction);
        }

        public static void Append(this ILProcessor ilProcessor, IEnumerable<Instruction> instructions)
        {
            foreach (var instruction in instructions)
                ilProcessor.Append(instruction);
        }

        public static void Replace(this ILProcessor ilProcessor, Instruction replaceThisOne,
            IEnumerable<Instruction> withThese)
        {
            replaceThisOne.OpCode = withThese.First().OpCode;
            replaceThisOne.Operand = withThese.First().Operand;

            ilProcessor.InsertAfter(replaceThisOne, withThese.Skip(1).ToArray());
        }
    }

    static class InstructionExtensions
    {
        public static void MakeNOP(this Instruction instruction)
        {
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }

        public static bool IsLoadLocalAddress(this Instruction instruction, out int index)
        {
            index = 0;
            switch (instruction.OpCode.Code)
            {
                case Code.Ldloca:
                case Code.Ldloca_S:
                    index = instruction.Operand is VariableDefinition vd ? vd.Index : (int)instruction.Operand;
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsInvocation(this Instruction instruction, out MethodReference targetMethod)
        {
            var opCode = instruction.OpCode;
            var result = opCode == OpCodes.Call || opCode == OpCodes.Callvirt;
            targetMethod = result ? (MethodReference)instruction.Operand : null;

            return result;
        }

        public static bool IsBranch(this Instruction instruction)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Brtrue:
                case Code.Brtrue_S:
                case Code.Brfalse:
                case Code.Brfalse_S:
                case Code.Br:
                case Code.Br_S:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsLoadLocal(this Instruction instruction, out int index)
        {
            index = 0;
            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc:
                case Code.Ldloc_S:
                    index = instruction.Operand is VariableDefinition vd ? vd.Index : (int)instruction.Operand;
                    return true;
                case Code.Ldloc_0:
                    index = 0;
                    return true;
                case Code.Ldloc_1:
                    index = 1;
                    return true;
                case Code.Ldloc_2:
                    index = 2;
                    return true;
                case Code.Ldloc_3:
                    index = 3;
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsStoreLocal(this Instruction instruction, out int index)
        {
            index = 0;
            switch (instruction.OpCode.Code)
            {
                case Code.Stloc:
                case Code.Stloc_S:
                    if (instruction.Operand is VariableDefinition vd)
                        index = vd.Index;
                    else
                        index = (int)instruction.Operand;
                    return true;
                case Code.Stloc_0:
                    index = 0;
                    return true;
                case Code.Stloc_1:
                    index = 1;
                    return true;
                case Code.Stloc_2:
                    index = 2;
                    return true;
                case Code.Stloc_3:
                    index = 3;
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsLoadArg(this Instruction instruction, out int index)
        {
            index = 0;
            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg: index = (int)instruction.Operand; return true;
                case Code.Ldarg_0: index = 0; return true;
                case Code.Ldarg_1: index = 1; return true;
                case Code.Ldarg_2: index = 2; return true;
                case Code.Ldarg_3: index = 3; return true;
                default:
                    return false;
            }
        }

        public static int GetPushDelta(this Instruction instruction)
        {
            OpCode code = instruction.OpCode;
            switch (code.StackBehaviourPush)
            {
                case StackBehaviour.Push0:
                    return 0;

                case StackBehaviour.Push1:
                case StackBehaviour.Pushi:
                case StackBehaviour.Pushi8:
                case StackBehaviour.Pushr4:
                case StackBehaviour.Pushr8:
                case StackBehaviour.Pushref:
                    return 1;

                case StackBehaviour.Push1_push1:
                    return 2;

                case StackBehaviour.Varpush:
                    if (code.FlowControl == FlowControl.Call)
                    {
                        var method = (IMethodSignature)instruction.Operand;
                        return method.ReturnType.IsVoid() ? 0 : 1;
                    }

                    break;
            }

            throw new ArgumentException(instruction.ToString());
        }

        public static int GetPopDelta(this Instruction instruction)
        {
            OpCode code = instruction.OpCode;
            switch (code.StackBehaviourPop)
            {
                case StackBehaviour.Pop0:
                    return 0;
                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                case StackBehaviour.Pop1:
                    return 1;

                case StackBehaviour.Pop1_pop1:
                case StackBehaviour.Popi_pop1:
                case StackBehaviour.Popi_popi:
                case StackBehaviour.Popi_popi8:
                case StackBehaviour.Popi_popr4:
                case StackBehaviour.Popi_popr8:
                case StackBehaviour.Popref_pop1:
                case StackBehaviour.Popref_popi:
                    return 2;

                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref:
                    return 3;

                case StackBehaviour.Varpop:
                    if (code.FlowControl == FlowControl.Call)
                    {
                        var method = (IMethodSignature)instruction.Operand;
                        int count = method.Parameters.Count;
                        if (method.HasThis && OpCodes.Newobj.Value != code.Value)
                            ++count;

                        return count;
                    }

                    break;
            }

            throw new ArgumentException(instruction.ToString());
        }

        public static bool IsLoadFieldOrLoadFieldAddress(this Instruction instruction) => (instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldflda);
        public static bool IsLoadField(this Instruction instruction) => (instruction.OpCode == OpCodes.Ldfld || instruction.OpCode == OpCodes.Ldflda);
        public static bool IsLoadStaticField(this Instruction instruction) => (instruction.OpCode == OpCodes.Ldsfld || instruction.OpCode == OpCodes.Ldsflda);
        public static bool IsStoreField(this Instruction instruction) => (instruction.OpCode == OpCodes.Stfld);

        public static bool IsLoadConstantInt(this Instruction instruction, out int intValue)
        {
            var opCode = instruction.OpCode;

            if (opCode == OpCodes.Ldc_I4)
                intValue = (int)instruction.Operand;
            else if (opCode == OpCodes.Ldc_I4_0)
                intValue = 0;
            else if (opCode == OpCodes.Ldc_I4_1)
                intValue = 1;
            else if (opCode == OpCodes.Ldc_I4_2)
                intValue = 2;
            else if (opCode == OpCodes.Ldc_I4_3)
                intValue = 3;
            else if (opCode == OpCodes.Ldc_I4_4)
                intValue = 4;
            else if (opCode == OpCodes.Ldc_I4_5)
                intValue = 5;
            else if (opCode == OpCodes.Ldc_I4_6)
                intValue = 6;
            else if (opCode == OpCodes.Ldc_I4_7)
                intValue = 7;
            else if (opCode == OpCodes.Ldc_I4_8)
                intValue = 8;
            else
            {
                intValue = 0;
                return false;
            }

            return true;
        }
    }
}
