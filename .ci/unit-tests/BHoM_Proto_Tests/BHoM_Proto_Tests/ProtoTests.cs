/*
 * This file is part of the Buildings and Habitats object Model (BHoM)
 * Copyright (c) 2015 - 2026, the respective contributors. All rights reserved.
 *
 * Each contributor holds copyright over their respective contributions.
 * The project versioning (Git) records all such contribution source information.
 *
 *
 * The BHoM is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3.0 of the License, or
 * (at your option) any later version.
 *
 * The BHoM is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this code. If not, see <https://www.gnu.org/licenses/lgpl-3.0.html>.
 */

using System.Diagnostics;
using System.Reflection;
using BH.Engine.Adapters.Protobuf;
using BH.Engine.Base;
using BH.oM.Adapters.Protobuf;
using BH.oM.Base;

namespace BH.Test.Proto
{
    public class ProtoTests
    {
        /***************************************************/
        /**** Test Methods - Schema                     ****/
        /***************************************************/

        [Test]
        [Description("Checks that all generated .proto files compile with protoc, which also catches circular imports and naming clashes.")]
        public void ProtoFilesCompile()
        {
            bool success = CompileProtoFiles(out byte[] _, out string errors);
            Assert.That(success, Is.True, errors);
        }

        /***************************************************/

        [Test]
        [Description("Checks that the committed descriptor set is the compiled version of the committed .proto files.")]
        public void CompiledSchemaIsUpToDate()
        {
            Assume.That(CompileProtoFiles(out byte[] compiled, out string errors), Is.True, errors);
            Assert.That(compiled, Is.EqualTo(File.ReadAllBytes(SchemaFile)),
                $"{Path.GetFileName(SchemaFile)} does not match the .proto files. Run the generation again.");
        }

        /***************************************************/

        [Test]
        [Description("Checks that the schema installed in the BHoM resources folder, used when no schema file is given, is the one in this repository.")]
        public void InstalledSchemaMatchesRepository()
        {
            string installed = Path.Combine(BH.Engine.Adapters.Protobuf.Query.SchemaFolder(), Path.GetFileName(SchemaFile));
            Assert.That(File.Exists(installed), Is.True, $"No schema installed at {installed}. Run the generation without --no-install.");
            Assert.That(File.ReadAllBytes(installed), Is.EqualTo(File.ReadAllBytes(SchemaFile)),
                "The installed schema differs from the one in the repository. Run the generation again.");
        }

        /***************************************************/
        /**** Test Methods - Round trips                ****/
        /***************************************************/

        [TestCaseSource(nameof(MessageTypes))]
        [Description("Writes a dummy object of each type to protobuf and reads it back with the schema of this repository, and checks that the result equals the original, ignoring the properties that are not exchanged.")]
        public void RoundTrip(Type type)
        {
            IObject original = Dummy(type);

            BH.Engine.Base.Compute.ClearCurrentEvents();
            byte[] bytes = original.ToProtobuf(SchemaFile);
            Assert.That(bytes, Is.Not.Null, $"Could not write {type.Name} to protobuf. {Events()}");

            IObject result = bytes.FromProtobuf(type, SchemaFile);
            AssertEqual(original, result);
        }

        /***************************************************/

        [TestCaseSource(nameof(MessageTypes))]
        [Description("Writes a dummy object of each type to a google.protobuf.Any and reads it back without giving the type or the schema, which are found from the installed schemas.")]
        public void RoundTripAnyWithInstalledSchema(Type type)
        {
            IObject original = Dummy(type);

            BH.Engine.Base.Compute.ClearCurrentEvents();
            byte[] bytes = original.ToProtobufAny();
            Assert.That(bytes, Is.Not.Null, $"Could not write {type.Name} to protobuf. {Events()}");

            IObject result = bytes.FromProtobufAny();
            AssertEqual(original, result);
        }

        /***************************************************/
        /**** Test Methods - Compiled writer            ****/
        /***************************************************/

        [TestCaseSource(nameof(MessageTypes))]
        [Description("Checks that the compiled writer used by ToProtobuf and ToProtobufAny produces exactly the same bytes as the reflection writer, the readable reference of the encoding.")]
        public void CompiledWriterMatchesReference(Type type)
        {
            IObject original = Dummy(type);

            BH.Engine.Base.Compute.ClearCurrentEvents();
            Assert.That(original.ToProtobuf(SchemaFile), Is.EqualTo(original.ToProtobufReflection(SchemaFile)), $"Plain message of {type.Name}. {Events()}");
            Assert.That(original.ToProtobufAny(SchemaFile), Is.EqualTo(original.ToProtobufAnyReflection(SchemaFile)), $"Any of {type.Name}. {Events()}");
        }

        /***************************************************/

        [TestCaseSource(nameof(MessageTypes))]
        [Description("Checks that the compiled reader used by FromProtobuf and FromProtobufAny reads the same object as the reflection reader, the readable reference of the decoding.")]
        public void CompiledReaderMatchesReference(Type type)
        {
            IObject original = Dummy(type);

            BH.Engine.Base.Compute.ClearCurrentEvents();
            byte[] bytes = original.ToProtobuf(SchemaFile);
            IObject reference = bytes.FromProtobufReflection(type, SchemaFile);
            Assume.That(reference, Is.Not.Null, $"The reference reader could not read {type.Name}. {Events()}");
            AssertEqual(reference, bytes.FromProtobuf(type, SchemaFile));

            byte[] anyBytes = original.ToProtobufAny(SchemaFile);
            AssertEqual(anyBytes.FromProtobufAnyReflection(SchemaFile), anyBytes.FromProtobufAny(SchemaFile));
        }

        /***************************************************/
        /**** Test Data                                 ****/
        /***************************************************/

        public static IEnumerable<Type> MessageTypes()
        {
            LoadAssemblies();
            return BH.Engine.Adapters.Protobuf.Query.TypesInSchema(SchemaFile)
                .Select(ResolveType)
                .Where(t => t != null && t.IsClass && !t.IsAbstract && typeof(IObject).IsAssignableFrom(t));
        }

        /***************************************************/
        /**** Private Helpers - Round trip              ****/
        /***************************************************/

        private static IObject Dummy(Type type)
        {
            IObject original = null;
            try
            {
                Dummies.TryGetValue(type, out original);
            }
            catch (Exception e)
            {
                Assert.Inconclusive($"Could not create the dummy objects: {e.Message}");
            }

            Assume.That(original, Is.Not.Null, $"No dummy {type.Name} could be created.");

            // Safety net: the dummies only use types of the schema to fill interface properties, so this should never trigger.
            List<string> outOfScope = OutOfScopeTypes(original).ToList();
            if (outOfScope.Count > 0)
                Assert.Inconclusive($"The dummy {type.Name} contains types outside the generated scope: {string.Join(", ", outOfScope)}");

            return original;
        }

        /***************************************************/

        // One dummy per type of the schema, created in a single call so that properties typed as interfaces or abstract classes
        // are only filled with implementations that are part of the schema. DummyObject(type) would pick any loaded implementation,
        // including toolkit types outside the generated scope.
        private static Dictionary<Type, IObject> Dummies => m_Dummies ??= CreateDummies();

        private static Dictionary<Type, IObject> CreateDummies()
        {
            Output<List<object>, List<Type>> result = BH.Engine.Test.Compute.DummyObjects(MessageTypes().ToList());
            return result.Item1
                .OfType<IObject>()
                .GroupBy(d => d.GetType())
                .ToDictionary(g => g.Key, g => g.First());
        }

        private static Dictionary<Type, IObject> m_Dummies;

        /***************************************************/

        private static void AssertEqual(IObject original, IObject result)
        {
            string name = original.GetType().Name;
            Assert.That(result, Is.Not.Null, $"Could not read {name} from protobuf. {Events()}");
            Assert.That(result.GetType(), Is.EqualTo(original.GetType()), $"{name} was read back as a {result.GetType().Name}.");

            // Geometry hashing needs Geometry_Engine: compare property by property instead.
            ComparisonConfig comparison = new ComparisonConfig { PropertyExceptions = NotExchanged.ToList(), UseGeometryHash = false };
            Assert.That(result.Hash(comparison), Is.EqualTo(original.Hash(comparison)),
                $"{name} changed in the round trip. {Events()}\nOriginal: {BH.Engine.Serialiser.Convert.ToJson(original)}\nResult:   {BH.Engine.Serialiser.Convert.ToJson(result)}");
        }

        /***************************************************/

        // oM types reachable through the exchanged properties that have no generated definition.
        private static IEnumerable<string> OutOfScopeTypes(object obj)
        {
            HashSet<string> exceptions = new HashSet<string>(NotExchanged);
            HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            HashSet<string> result = new HashSet<string>();
            Stack<object> stack = new Stack<object>(new[] { obj });

            while (stack.Count > 0)
            {
                object current = stack.Pop();
                if (current == null || current is string || current.GetType().IsPrimitive || !visited.Add(current))
                    continue;

                Type type = current.GetType();
                if (current is System.Collections.IDictionary dictionary)
                {
                    foreach (System.Collections.DictionaryEntry entry in dictionary)
                    {
                        stack.Push(entry.Key);
                        stack.Push(entry.Value);
                    }
                    continue;
                }

                if (current is System.Collections.IEnumerable enumerable && !(current is IObject))
                {
                    foreach (object item in enumerable)
                        stack.Push(item);
                    continue;
                }

                if (type.IsGenericType && type.FullName.StartsWith("System.Tuple`"))
                {
                    foreach (PropertyInfo item in type.GetProperties())
                        stack.Push(item.GetValue(current));
                    continue;
                }

                if (!(current is IObject))
                    continue;

                if (!TypesInSchema.Contains(type.FullName))
                    result.Add(type.FullName);

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length == 0 && !exceptions.Contains(property.Name))
                        stack.Push(property.GetValue(current));
                }
            }

            return result;
        }

        /***************************************************/

        private static string Events()
        {
            List<string> messages = BH.Engine.Base.Query.CurrentEvents().Select(e => $"{e.Type}: {e.Message}").Distinct().ToList();
            return messages.Count == 0 ? "" : "Events: " + string.Join(" | ", messages);
        }

        /***************************************************/
        /**** Private Helpers - Schema                  ****/
        /***************************************************/

        private static string RootFolder => AppContext.BaseDirectory.Split(".ci")[0];

        private static ProtoConfig Config => m_Config ??= BH.Engine.Serialiser.Convert.FromJson(File.ReadAllText(Path.Combine(RootFolder, "ProtoConfig.json"))) as ProtoConfig;

        private static string SchemaFile => Path.Combine(RootFolder, $"{Config.RootPackage}.protoset");

        private static HashSet<string> TypesInSchema => m_TypesInSchema ??= new HashSet<string>(BH.Engine.Adapters.Protobuf.Query.TypesInSchema(SchemaFile));

        // Properties not part of the exchange, e.g. Fragments, across all types of the schema.
        private static IEnumerable<string> NotExchanged => m_NotExchanged ??= TypesInSchema
            .Select(ResolveType)
            .Where(t => t != null)
            .SelectMany(t => t.NotExchangedProperties(SchemaFile))
            .Distinct()
            .ToList();

        private static ProtoConfig m_Config;

        private static HashSet<string> m_TypesInSchema;

        private static List<string> m_NotExchanged;

        /***************************************************/

        private static bool CompileProtoFiles(out byte[] descriptorSet, out string errors)
        {
            string protoc = Path.Combine(AppContext.BaseDirectory, "protoc", "protoc.exe");
            string include = Path.Combine(AppContext.BaseDirectory, "protoc", "include");
            string output = Path.Combine(Path.GetTempPath(), "BHoM_Proto_Tests", Guid.NewGuid().ToString(), "schema.protoset");
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            // Same order as the generation: options.proto first, then the other files by path.
            List<string> files = Directory.GetFiles(Path.Combine(RootFolder, Config.RootPackage), "*.proto")
                .Select(f => Path.GetRelativePath(RootFolder, f).Replace('\\', '/'))
                .OrderBy(f => f.EndsWith("/options.proto") ? 0 : 1)
                .ThenBy(f => f, StringComparer.Ordinal)
                .ToList();

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = protoc,
                WorkingDirectory = RootFolder,
                Arguments = $"-I . -I \"{include}\" --include_imports --descriptor_set_out=\"{output}\" {string.Join(" ", files)}",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using Process process = Process.Start(startInfo);
            errors = process.StandardError.ReadToEnd();
            process.WaitForExit();

            descriptorSet = process.ExitCode == 0 ? File.ReadAllBytes(output) : null;
            return process.ExitCode == 0;
        }

        /***************************************************/

        private static void LoadAssemblies()
        {
            foreach (string file in Directory.GetFiles(BH.Engine.Base.Query.BHoMFolder(), "*.dll"))
            {
                if (!Path.GetFileNameWithoutExtension(file).IsOmAssembly())
                    continue;

                try
                {
                    Assembly.LoadFrom(file);
                }
                catch
                {
                    // Assemblies that cannot be loaded in this runtime are out of scope.
                }
            }
        }

        /***************************************************/

        private static Type ResolveType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        /***************************************************/
    }
}
