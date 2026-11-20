using BH.oM.Adapters.Protobuf;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace ProtoGeneration
{
    public static class Generate
    {
        /***************************************************/
        /**** Properties                                ****/
        /***************************************************/

        // Root folder of the BHoM_Proto repository.
        public static string Path { get; set; }

        public const string ConfigFileName = "ProtoConfig.json";

        public const string ReportFileName = "GenerationReport.md";

        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        // The generation settings live in the repository, so the scope and the file groups can be changed without changing code.
        public static ProtoConfig LoadConfig()
        {
            string file = System.IO.Path.Combine(Path, ConfigFileName);
            if (!File.Exists(file))
            {
                Console.WriteLine($"No {ConfigFileName} found, writing the default settings.");
                ProtoConfig config = DefaultConfig();
                WriteJson(file, config);
                return config;
            }

            return BH.Engine.Serialiser.Convert.FromJson(File.ReadAllText(file)) as ProtoConfig;
        }

        /***************************************************/

        // The compiled descriptor set of the generated files, e.g. bhom.protoset.
        // It also holds the published field numbers that the next generation must keep.
        public static string SchemaFile(ProtoConfig config)
        {
            return System.IO.Path.Combine(Path, $"{config.RootPackage}.protoset");
        }

        /***************************************************/

        public static void WriteResult(ProtoGenerationResult result, ProtoConfig config)
        {
            // Clean the generated folder so files of removed groups or assemblies disappear.
            string protoFolder = System.IO.Path.Combine(Path, config.RootPackage);
            if (Directory.Exists(protoFolder))
                Directory.Delete(protoFolder, true);

            foreach (ProtoFile file in result.Files)
            {
                string path = System.IO.Path.Combine(Path, file.Path);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                File.WriteAllText(path, file.Content);
            }

            File.WriteAllText(System.IO.Path.Combine(Path, ReportFileName), Report(result));
        }

        /***************************************************/

        // Compiles the generated files with protoc into the descriptor set the BHoM writer and reader load.
        public static bool CompileSchema(ProtoGenerationResult result, ProtoConfig config, out string errors)
        {
            string protoc = System.IO.Path.Combine(AppContext.BaseDirectory, "protoc", "protoc.exe");
            string include = System.IO.Path.Combine(AppContext.BaseDirectory, "protoc", "include");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = protoc,
                WorkingDirectory = Path,
                Arguments = $"-I . -I \"{include}\" --include_imports --descriptor_set_out=\"{SchemaFile(config)}\" {string.Join(" ", result.Files.Select(f => f.Path))}",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using Process process = Process.Start(startInfo);
            errors = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }

        /***************************************************/

        // Copies the descriptor set to the BHoM resources folder, where ToProtobuf and FromProtobuf find it when no schema file is given.
        public static string InstallSchema(ProtoConfig config)
        {
            string folder = BH.Engine.Adapters.Protobuf.Query.SchemaFolder();
            Directory.CreateDirectory(folder);
            string target = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(SchemaFile(config)));
            File.Copy(SchemaFile(config), target, true);
            return target;
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        private static ProtoConfig DefaultConfig()
        {
            return new ProtoConfig
            {
                OrganisationsToInclude = new List<string> { "BHoM" },
                RepositoriesToInclude = new List<string> { "BHoM/BHoM" },
                RootPackage = "bhom",
                ConsumerSideUnions = true,
                // One file per assembly.
                FileGroups = new List<ProtoFileGroup>(),
                SkippedUnionTypes = new List<string>(),
                // Open sets of implementations, spread over many files and toolkits: exchanged as google.protobuf.Any.
                OpenUnionTypes = new List<string> { "BH.oM.Base.IFragment", "BH.oM.Physical.Materials.IMaterialProperties", "BH.oM.Dimensional.IElementM" },
                // Only the implementations in Geometry_oM are exchanged (not e.g. RenderMesh and RenderPoint from Graphics_oM).
                ClosedUnionTypes = new List<string> { "BH.oM.Geometry.IGeometry" }
            };
        }

        /***************************************************/

        // BHoM json, indented so changes are easy to review.
        private static void WriteJson(string file, object obj)
        {
            string json = BH.Engine.Serialiser.Convert.ToJson(obj);
            using JsonDocument document = JsonDocument.Parse(json);
            string indented = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, indented.Replace("\r\n", "\n") + "\n");
        }

        /***************************************************/

        private static string Report(ProtoGenerationResult result)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Generation report");
            sb.AppendLine();
            sb.AppendLine("Generated by `.ci/Generation/ProtoGeneration`. Do not edit by hand.");
            sb.AppendLine();
            sb.AppendLine("## Files");
            sb.AppendLine();
            sb.AppendLine("| File | Assemblies | Imports |");
            sb.AppendLine("|---|---|---|");
            foreach (ProtoFile file in result.Files)
                sb.AppendLine($"| `{file.Path}` | {string.Join(", ", file.Assemblies)} | {string.Join(", ", file.Imports.Select(i => $"`{i}`"))} |");

            sb.AppendLine();
            sb.AppendLine($"Import cycles: {(result.ImportCycles.Count == 0 ? "none" : string.Join("; ", result.ImportCycles.Select(c => string.Join(" <-> ", c))))}");
            sb.AppendLine();

            sb.AppendLine("## Properties typed as interfaces implemented in other files, or set in ProtoConfig.json");
            sb.AppendLine();
            sb.AppendLine("Every other interface or abstract class used as a property type is a typed union in its own file.");
            sb.AppendLine();
            sb.AppendLine("- **Any**: `google.protobuf.Any`, with the `any_of` option naming the interface. Used for `OpenUnionTypes`, and for interfaces without implementations in their own file.");
            sb.AppendLine("- **Closed**: the union of the interface's own file (`ClosedUnionTypes`). Implementations in other files are not exchanged.");
            sb.AppendLine("- **ConsumerSideUnion**: a union defined in the file of the property owner, covering every implementation.");
            sb.AppendLine("- **TypedWithOther**: the union of the interface's own file, with a `google.protobuf.Any other` slot for implementations in other files.");
            sb.AppendLine("- **Skipped**: not exchanged (`SkippedUnionTypes`); the field number is reserved.");
            sb.AppendLine();
            foreach (IGrouping<UnionResolution, PropertyResolution> group in result.Resolutions.GroupBy(r => r.Resolution).OrderBy(g => g.Key))
            {
                sb.AppendLine($"### {group.Key} ({group.Count()})");
                sb.AppendLine();
                foreach (IGrouping<string, PropertyResolution> union in group.GroupBy(r => r.UnionTypeName).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    List<string> properties = union.Select(r => $"{ShortName(r.TypeName)}.{r.PropertyName}").Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
                    string shown = properties.Count > 12 ? string.Join(", ", properties.Take(12)) + $", ... ({properties.Count} in total)" : string.Join(", ", properties);
                    sb.AppendLine($"- `{union.Key}`: {shown}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("Properties typed as attributes, or as .NET types without a protobuf equivalent, are also not exchanged. ");
            sb.AppendLine("Search the .proto files for \"is not exchanged\" to find them: their field numbers are reserved.");

            return sb.ToString().Replace("\r\n", "\n");
        }

        /***************************************************/

        // Type name without namespace and generic assembly details, e.g. BHoMGroup`1 for BH.oM.Base.BHoMGroup`1[[...]].
        private static string ShortName(string fullName)
        {
            int generic = fullName.IndexOf('[');
            string name = generic < 0 ? fullName : fullName.Substring(0, generic);
            return name.Split('.').Last();
        }

        /***************************************************/
    }
}
