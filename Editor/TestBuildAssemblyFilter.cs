using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace Unity.TestFramework.UTPReporter.Editor
{
    // Include UTPReporter only in test builds due to dependency on com.unity.test-framework
    // which is only available in builds with test assemblies
    internal class TestBuildAssemblyFilter : IFilterBuildAssemblies
    {
        private const string UTPReporterAssemblyName = "Unity.TestFramework.UTPReporter";

        public int callbackOrder { get; }

        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            if ((buildOptions & BuildOptions.IncludeTestAssemblies) != 0)
            {
                return assemblies;
            }

            return assemblies.Where(x => !x.Contains(UTPReporterAssemblyName)).ToArray();
        }
    }
}