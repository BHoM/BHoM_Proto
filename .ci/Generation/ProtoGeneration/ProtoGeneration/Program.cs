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

using System.Reflection;
using BH.oM.Adapters.Protobuf;
using ProtoGeneration;

// The repository root is the folder containing the .ci folder this program is built in.
string rootFolder = args.FirstOrDefault(a => !a.StartsWith("--")) ?? AppContext.BaseDirectory.Split(".ci")[0];
Console.WriteLine($"Root folder: {rootFolder}");
Generate.Path = rootFolder;

ProtoConfig config = Generate.LoadConfig();
if (config == null)
{
    Console.WriteLine($"Could not read {Generate.ConfigFileName}.");
    return 1;
}

Console.WriteLine("Loading BHoM assemblies...");
List<Assembly> assemblies = AssemblyLoader.LoadOmAssemblies(config);
Console.WriteLine($"Found {assemblies.Count} assemblies in scope: {string.Join(", ", assemblies.Select(a => a.GetName().Name))}");

// The descriptor set of the previous generation holds the field numbers that must be kept.
string schemaFile = Generate.SchemaFile(config);
string existingSchema = File.Exists(schemaFile) ? schemaFile : "";
Console.WriteLine(existingSchema == "" ? "No existing schema: assigning all field numbers from scratch." : $"Keeping the field numbers of {schemaFile}.");

ProtoGenerationResult result = BH.Engine.Adapters.Protobuf.Compute.GenerateProtoFiles(assemblies, config, existingSchema);
if (result == null)
{
    Console.WriteLine("Generation failed: " + string.Join(" | ", BH.Engine.Base.Query.CurrentEvents().Select(e => e.Message)));
    return 1;
}

if (result.ImportCycles.Count > 0)
{
    foreach (List<string> cycle in result.ImportCycles)
        Console.WriteLine($"Import cycle: {string.Join(" <-> ", cycle)}");
    return 1;
}

Generate.WriteResult(result, config);
Console.WriteLine($"Wrote {result.Files.Count} files: {string.Join(", ", result.Files.Select(f => f.Path))}");

if (!Generate.CompileSchema(result, config, out string errors))
{
    Console.WriteLine($"protoc could not compile the generated files:\n{errors}");
    return 1;
}
Console.WriteLine($"Compiled the schema to {schemaFile}");

// Pass --no-install to only write the files in the repository, e.g. on a CI machine.
if (!args.Contains("--no-install"))
    Console.WriteLine($"Installed the schema to {Generate.InstallSchema(config)}");

return 0;
