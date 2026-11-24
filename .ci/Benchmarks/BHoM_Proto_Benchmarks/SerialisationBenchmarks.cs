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

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BH.Engine.Adapters.Protobuf;
using BH.oM.Base;

namespace BH.Benchmark.Proto
{
    // Writes and reads every object of a data set with the protobuf writer and reader (plain and wrapped in google.protobuf.Any)
    // and with BHoM json, the existing alternative. Each benchmark handles the whole data set, one object at a time.
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class SerialisationBenchmarks
    {
        /***************************************************/
        /**** Parameters                                ****/
        /***************************************************/

        static SerialisationBenchmarks()
        {
            AssemblyResolver.Register();
        }

        /***************************************************/

        [ParamsSource(nameof(DataSets))]
        public string DataSet { get; set; }

        public static IEnumerable<string> DataSets => Models.Names;

        /***************************************************/
        /**** Setup                                     ****/
        /***************************************************/

        [GlobalSetup]
        public void Setup()
        {
            m_Objects = Models.Create(DataSet);
            m_Types = m_Objects.Select(o => o.GetType()).ToList();
            m_Protobuf = m_Objects.Select(o => o.ToProtobuf(Paths.SchemaFile)).ToList();
            m_ProtobufAny = m_Objects.Select(o => o.ToProtobufAny(Paths.SchemaFile)).ToList();
            m_Json = m_Objects.Select(o => BH.Engine.Serialiser.Convert.ToJson(o)).ToList();

            if (m_Protobuf.Any(b => b == null) || m_ProtobufAny.Any(b => b == null) || m_Json.Any(j => j == null))
                throw new InvalidOperationException($"Some objects of {DataSet} could not be serialised.");

            ClearEvents();
        }

        /***************************************************/

        // The engines record warnings and errors in a global list: cleared at the start of every call so it never grows.
        // Not an [IterationCleanup], which would force BenchmarkDotNet to time a single call per iteration: too short to measure reliably.
        private static void ClearEvents()
        {
            BH.Engine.Base.Compute.ClearCurrentEvents();
        }

        /***************************************************/
        /**** Write                                     ****/
        /***************************************************/

        [Benchmark(Baseline = true), BenchmarkCategory("Write")]
        public long Json_Write()
        {
            ClearEvents();
            long length = 0;
            foreach (IObject obj in m_Objects)
                length += BH.Engine.Serialiser.Convert.ToJson(obj).Length;
            return length;
        }

        /***************************************************/

        [Benchmark, BenchmarkCategory("Write")]
        public long Protobuf_Write()
        {
            ClearEvents();
            long length = 0;
            foreach (IObject obj in m_Objects)
                length += obj.ToProtobuf(Paths.SchemaFile).Length;
            return length;
        }

        /***************************************************/

        [Benchmark, BenchmarkCategory("Write")]
        public long ProtobufAny_Write()
        {
            ClearEvents();
            long length = 0;
            foreach (IObject obj in m_Objects)
                length += obj.ToProtobufAny(Paths.SchemaFile).Length;
            return length;
        }

        /***************************************************/
        /**** Read                                      ****/
        /***************************************************/

        [Benchmark(Baseline = true), BenchmarkCategory("Read")]
        public int Json_Read()
        {
            ClearEvents();
            int count = 0;
            foreach (string json in m_Json)
            {
                if (BH.Engine.Serialiser.Convert.FromJson(json) != null)
                    count++;
            }
            return count;
        }

        /***************************************************/

        [Benchmark, BenchmarkCategory("Read")]
        public int Protobuf_Read()
        {
            ClearEvents();
            int count = 0;
            for (int i = 0; i < m_Protobuf.Count; i++)
            {
                if (m_Protobuf[i].FromProtobuf(m_Types[i], Paths.SchemaFile) != null)
                    count++;
            }
            return count;
        }

        /***************************************************/

        [Benchmark, BenchmarkCategory("Read")]
        public int ProtobufAny_Read()
        {
            ClearEvents();
            int count = 0;
            foreach (byte[] bytes in m_ProtobufAny)
            {
                if (bytes.FromProtobufAny(Paths.SchemaFile) != null)
                    count++;
            }
            return count;
        }

        /***************************************************/
        /**** Private Fields                            ****/
        /***************************************************/

        private List<IObject> m_Objects;

        private List<Type> m_Types;

        private List<byte[]> m_Protobuf;

        private List<byte[]> m_ProtobufAny;

        private List<string> m_Json;

        /***************************************************/
    }
}
