using System;

namespace FasterGameLoading.Tests
{
    internal sealed class MockResourcesAPI : UnityEngine.ResourcesAPI
    {
        protected override UnityEngine.Object[] LoadAll(string path, Type systemTypeInstance)
        {
            return Array.Empty<UnityEngine.Object>();
        }

        protected override UnityEngine.Object Load(string path, Type systemTypeInstance)
        {
            if (systemTypeInstance == typeof(UnityEngine.Shader))
            {
                return (UnityEngine.Shader)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.Shader));
            }
            if (systemTypeInstance == typeof(UnityEngine.Texture2D))
            {
                return (UnityEngine.Texture2D)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(UnityEngine.Texture2D));
            }
            return null;
        }

        protected override UnityEngine.ResourceRequest LoadAsync(string path, Type systemTypeInstance)
        {
            return null;
        }
    }
}
