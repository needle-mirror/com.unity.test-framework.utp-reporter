using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework.Interfaces;

namespace Unity.TestFramework.UTPReporter
{
    // Reproduces UnityEngine.TestRunner's GetUniqueName / GetFullName / GetAncestorPath, which are
    // internal to that assembly. Keyed the same way so a player's ids agree with the editor's for
    // the same test. Uses only public ITest surface, and never throws: the dispatcher that calls
    // into here rethrows, so a throw would fail the test run rather than drop a message.
    internal static class TestIdentity
    {
        const string k_ChildIndex = "childIndex";

        public static string UniqueName(ITest test)
        {
            if (test == null)
            {
                return string.Empty;
            }

            var id = AncestorPath(test) + FullNameKey(test);
            var index = ChildIndex(test);
            if (index >= 0)
            {
                id += index;
            }

            if (test.IsSuite)
            {
                id += "[suite]";
            }

            return id;
        }

        static string AncestorPath(ITest test)
        {
            var path = string.Empty;
            var parent = test.Parent;
            while (parent != null && parent.Parent != null && !string.IsNullOrEmpty(parent.Name))
            {
                var segment = parent.Name;
                var index = ChildIndex(parent);
                if (index >= 0)
                {
                    segment += index;
                }

                path = segment + "/" + path;
                parent = parent.Parent;
            }

            return path;
        }

        static string FullNameKey(ITest test)
        {
            var typeInfo = test.TypeInfo ?? test.Parent?.TypeInfo ?? FirstChildTypeInfo(test);
            if (typeInfo == null)
            {
                return "[" + test.Name + "]";
            }

            string assemblyId;
            try
            {
                assemblyId = typeInfo.Assembly.GetName().Name;
            }
            catch (Exception)
            {
                return "[" + test.Name + "]";
            }

            if (assemblyId == test.Name)
            {
                return "[" + test.Name + "]";
            }

            return string.Format("[{0}][{1}]", assemblyId, test.FullName);
        }

        static ITypeInfo FirstChildTypeInfo(ITest test)
        {
            var children = test.Tests;
            if (children == null)
            {
                return null;
            }

            for (var i = 0; i < children.Count; i++)
            {
                if (children[i]?.TypeInfo != null)
                {
                    return children[i].TypeInfo;
                }
            }

            return null;
        }

        // -1 when absent. ContainsKey first: NUnit's PropertyBag indexer INSERTS an empty list for a
        // missing key, and a bag that then reports ContainsKey true with nothing in it makes
        // TestExtensions.GetRetryIteration/GetRepeatIteration throw on their .First().
        static int ChildIndex(ITest test)
        {
            var properties = test.Properties;
            if (properties == null || !properties.ContainsKey(k_ChildIndex))
            {
                return -1;
            }

            var values = properties[k_ChildIndex];
            if (values == null || values.Count == 0)
            {
                return -1;
            }

            return values[0] is int index ? index : -1;
        }
    }

    // UnityEngine.TestRunner's DeterministicGuid: SHA256 over salt:key[:iteration], first 16 bytes
    // as a Guid. The salt is a fresh Guid per process, never the process id - the OS reuses the
    // id of an exited process (UTR-1336).
    internal static class SpanIds
    {
        static readonly string k_ProcessSalt = Guid.NewGuid().ToString("N");

        public static string Create(string key, int iteration = 0)
        {
            var input = iteration > 0 ? $"{key}:{iteration}" : (key ?? string.Empty);
            input = $"{k_ProcessSalt}:{input}";
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
                var guidBytes = new byte[16];
                Array.Copy(hash, guidBytes, 16);
                return new Guid(guidBytes).ToString();
            }
        }
    }
}
