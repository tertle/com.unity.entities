#if !BL_ENTITIES_CUSTOM
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Unity.Entities.Editor
{
    [InitializeOnLoad]
    class EditorInitialization
    {
        static readonly string k_CustomDefine = "BL_ENTITIES_CUSTOM";

        static EditorInitialization()
        {
            var fromBuildTargetGroup = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var definesStr = PlayerSettings.GetScriptingDefineSymbols(fromBuildTargetGroup);
            var defines = definesStr.Split(';').ToList();
            defines.Add(k_CustomDefine);
            PlayerSettings.SetScriptingDefineSymbols(fromBuildTargetGroup, string.Join(";", defines.ToArray()));
        }
    }
}
#endif