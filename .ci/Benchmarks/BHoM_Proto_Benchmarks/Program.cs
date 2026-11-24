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
using System.IO.Compression;
using System.Reflection;
using System.Text;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using BH.Engine.Adapters.Protobuf;
using BH.Engine.Base;
using BH.oM.Base;
using BH.Test.Proto;

namespace BH.Benchmark.Proto
{
    // Usage (from the repository root):
    //   dotnet run -c Release --project .ci/Benchmarks/BHoM_Proto_Benchmarks -- check                  round trip every data set and compare
    //   dotnet run -c Release --project .ci/Benchmarks/BHoM_Proto_Benchmarks -- sizes                  output sizes, uncompressed and gzip
    //   dotnet run -c Release --project .ci/Benchmarks/BHoM_Proto_Benchmarks -- coldstart protobuf|json  first call in a fresh process
    //   dotnet run -c Release --project .ci/Benchmarks/BHoM_Proto_Benchmarks -- [--quick] [--scale 0.25] [--inprocess] [--affinity FFFF] [BenchmarkDotNet args, e.g. --filter *Bars*]
    // The last form runs the correctness check first, then the BenchmarkDotNet benchmarks. Each benchmark runs in its own
    // process by default, so benchmarks do not affect each other through the heap; --inprocess is faster but less reliable.
    // --quick is for trying options: a quarter of the data (or --scale), 1 warm-up and 5 iterations, in process.
    public static class Program
    {
        /***************************************************/

        public static int Main(string[] args)
        {
            AssemblyResolver.Register();
            return Run(args);
        }

        /***************************************************/

        private static int Run(string[] args)
        {
            string mode = args.FirstOrDefault()?.ToLowerInvariant();
            switch (mode)
            {
                case "check":
                    return Check() ? 0 : 1;
                case "sizes":
                    Console.WriteLine(Sizes());
                    return 0;
                case "coldstart":
                    Console.WriteLine(ColdStart(args.ElementAtOrDefault(1) ?? "protobuf"));
                    return 0;
                default:
                    // --quick: for trying options. A quarter of the data (unless --scale is given), few iterations, in process.
                    bool quick = args.Contains("--quick");
                    string scale = OptionValue(args, "--scale") ?? (quick ? "0.25" : null);
                    if (scale != null)
                        Environment.SetEnvironmentVariable(Models.ScaleVariable, scale); // Inherited by the benchmark processes.

                    if (!Check())
                        return 1;

                    bool inProcess = quick || args.Contains("--inprocess");
                    Job job = inProcess ? Job.Default.WithToolchain(InProcessEmitToolchain.Instance) : Job.Default;
                    job = quick ? job.WithWarmupCount(1).WithIterationCount(5) : job.WithWarmupCount(3).WithIterationCount(15);

                    // On hybrid CPUs (performance + efficiency cores) a benchmark process scheduled on efficiency cores runs
                    // about twice as slow. Pin the processes to the performance cores, e.g. --affinity FFFF for logical
                    // processors 0-15 (the 8 P-cores of an i7-13850HX).
                    string affinity = OptionValue(args, "--affinity");
                    if (affinity != null)
                    {
                        IntPtr mask = (IntPtr)System.Convert.ToInt64(affinity, 16);
                        if (inProcess)
                            Process.GetCurrentProcess().ProcessorAffinity = mask;
                        else
                            job = job.WithAffinity(mask);
                    }

                    IConfig config = DefaultConfig.Instance
                        .AddJob(job)
                        .AddExporter(MarkdownExporter.GitHub, CsvExporter.Default)
                        .WithArtifactsPath(Paths.ArtifactsFolder)
                        // The BHoM assemblies are installed builds: do not refuse to run if one of them was built in Debug.
                        .WithOptions(ConfigOptions.DisableOptimizationsValidator);

                    BenchmarkSwitcher.FromTypes(new[] { typeof(SerialisationBenchmarks) }).Run(BenchmarkArgs(args), config);
                    return 0;
            }
        }

        /***************************************************/

        private static string OptionValue(string[] args, string option)
        {
            int index = Array.IndexOf(args, option);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        /***************************************************/

        // The arguments for BenchmarkDotNet, without the options handled here.
        private static string[] BenchmarkArgs(string[] args)
        {
            List<string> result = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--quick" || args[i] == "--inprocess")
                    continue;
                if (args[i] == "--affinity" || args[i] == "--scale")
                {
                    i++; // Skip the mask too.
                    continue;
                }
                result.Add(args[i]);
            }
            return result.ToArray();
        }

        /***************************************************/
        /**** Check                                     ****/
        /***************************************************/

        // Round trips every object of every data set through protobuf (plain and Any) and checks it is unchanged,
        // so a fast but wrong implementation can never win a benchmark.
        public static bool Check()
        {
            BH.oM.Base.ComparisonConfig comparison = new BH.oM.Base.ComparisonConfig { PropertyExceptions = NotExchanged(), UseGeometryHash = false };
            bool success = true;

            foreach (string name in Models.Names)
            {
                List<IObject> objects = Models.Create(name);
                int failures = 0;
                int referenceMismatches = 0;
                foreach (IObject obj in objects)
                {
                    string expected = obj.Hash(comparison);
                    byte[] plainBytes = obj.ToProtobuf(Paths.SchemaFile);
                    byte[] anyBytes = obj.ToProtobufAny(Paths.SchemaFile);
                    IObject plain = plainBytes.FromProtobuf(obj.GetType(), Paths.SchemaFile);
                    IObject any = anyBytes.FromProtobufAny(Paths.SchemaFile);

                    // The compiled writer must produce the same bytes as the reflection reference writer, and the compiled
                    // reader the same objects as the reflection reference reader.
                    if (!plainBytes.SequenceEqual(obj.ToProtobufReflection(Paths.SchemaFile)) || !anyBytes.SequenceEqual(obj.ToProtobufAnyReflection(Paths.SchemaFile))
                        || plainBytes.FromProtobufReflection(obj.GetType(), Paths.SchemaFile)?.Hash(comparison) != plain?.Hash(comparison)
                        || anyBytes.FromProtobufAnyReflection(Paths.SchemaFile)?.Hash(comparison) != any?.Hash(comparison))
                    {
                        if (referenceMismatches == 0)
                            Console.WriteLine($"  First difference with the reference writer or reader in {name}: {obj.GetType().Name}");
                        referenceMismatches++;
                    }

                    if (plain?.Hash(comparison) != expected || any?.Hash(comparison) != expected)
                    {
                        if (failures == 0)
                        {
                            // Keep the first difference as json, to compare the original with what was read back.
                            Directory.CreateDirectory(Paths.ArtifactsFolder);
                            string original = Path.Combine(Paths.ArtifactsFolder, $"check-{name}-original.json");
                            string result = Path.Combine(Paths.ArtifactsFolder, $"check-{name}-result.json");
                            File.WriteAllText(original, BH.Engine.Serialiser.Convert.ToJson(obj));
                            File.WriteAllText(result, BH.Engine.Serialiser.Convert.ToJson(plain?.Hash(comparison) != expected ? plain : any));
                            Console.WriteLine($"  First difference in {name}: {obj.GetType().Name}. Compare {original} with {result}.");
                            Console.WriteLine($"  Events: {string.Join(" | ", BH.Engine.Base.Query.CurrentEvents().Select(e => e.Message).Distinct())}");
                        }
                        failures++;
                    }
                }

                BH.Engine.Base.Compute.ClearCurrentEvents();
                Console.WriteLine($"Check {name}: {objects.Count - failures}/{objects.Count} objects round trip unchanged, {objects.Count - referenceMismatches}/{objects.Count} identical to the reference writer and reader.");
                success &= failures == 0 && referenceMismatches == 0;
            }

            return success;
        }

        /***************************************************/

        // Properties not part of the exchange, e.g. those typed as .NET types without a protobuf equivalent.
        private static List<string> NotExchanged()
        {
            return BH.Engine.Adapters.Protobuf.Query.TypesInSchema(Paths.SchemaFile)
                .Select(ResolveType)
                .Where(t => t != null)
                .SelectMany(t => t.NotExchangedProperties(Paths.SchemaFile))
                .Distinct()
                .ToList();
        }

        /***************************************************/
        /**** Sizes                                     ****/
        /***************************************************/

        public static string Sizes()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("| Data set | Objects | Protobuf | Protobuf (gzip) | Protobuf Any | Protobuf Any (gzip) | JSON | JSON (gzip) | Protobuf / JSON | Protobuf / JSON (gzip) |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (string name in Models.Names)
            {
                List<IObject> objects = Models.Create(name);
                List<byte[]> protobuf = objects.Select(o => o.ToProtobuf(Paths.SchemaFile)).ToList();
                List<byte[]> any = objects.Select(o => o.ToProtobufAny(Paths.SchemaFile)).ToList();
                List<byte[]> json = objects.Select(o => Encoding.UTF8.GetBytes(BH.Engine.Serialiser.Convert.ToJson(o))).ToList();

                long p = protobuf.Sum(b => (long)b.Length), a = any.Sum(b => (long)b.Length), j = json.Sum(b => (long)b.Length);
                long pz = Gzip(protobuf), az = Gzip(any), jz = Gzip(json);
                sb.AppendLine($"| {name} | {objects.Count:N0} | {Kb(p)} | {Kb(pz)} | {Kb(a)} | {Kb(az)} | {Kb(j)} | {Kb(jz)} | {(double)p / j:P0} | {(double)pz / jz:P0} |");
                BH.Engine.Base.Compute.ClearCurrentEvents();
            }

            sb.AppendLine();
            sb.AppendLine("Sizes are the sum over the objects, each serialised on its own. Gzip is applied to all objects of a data set together.");
            return sb.ToString();
        }

        /***************************************************/

        private static long Gzip(IEnumerable<byte[]> items)
        {
            using MemoryStream output = new MemoryStream();
            using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, true))
            {
                foreach (byte[] item in items)
                    gzip.Write(item, 0, item.Length);
            }
            return output.Length;
        }

        /***************************************************/

        private static string Kb(long bytes) => $"{bytes / 1024.0:N0} KB";

        /***************************************************/
        /**** Cold start                                ****/
        /***************************************************/

        // Time of the first write and read of one Bar in a fresh process, which includes loading the schema
        // and any per-type set-up. Run once per format, each in its own process.
        public static string ColdStart(string format)
        {
            IObject bar = Models.Bars(1, 2).First(); // Building the model is not measured.
            Stopwatch watch = Stopwatch.StartNew();
            double write, read;

            if (format == "json")
            {
                string json = BH.Engine.Serialiser.Convert.ToJson(bar);
                write = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                BH.Engine.Serialiser.Convert.FromJson(json);
                read = watch.Elapsed.TotalMilliseconds;
            }
            else
            {
                byte[] bytes = bar.ToProtobuf(Paths.SchemaFile);
                write = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                bytes.FromProtobuf(bar.GetType(), Paths.SchemaFile);
                read = watch.Elapsed.TotalMilliseconds;
            }

            return $"Cold start ({format}): first write {write:N1} ms, first read {read:N1} ms";
        }

        /***************************************************/
        /**** Assemblies                                ****/
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
