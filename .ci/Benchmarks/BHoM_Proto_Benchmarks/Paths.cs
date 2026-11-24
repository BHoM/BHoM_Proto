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

using BH.oM.Adapters.Protobuf;

namespace BH.Benchmark.Proto
{
    public static class Paths
    {
        /***************************************************/

        // Root folder of the BHoM_Proto repository: the folder containing the .ci folder this program is built in.
        public static string RootFolder => AppContext.BaseDirectory.Split(".ci")[0];

        // The schema of this repository, passed explicitly so results do not depend on what is installed.
        public static string SchemaFile => m_SchemaFile ??= Path.Combine(RootFolder, $"{Config.RootPackage}.protoset");

        public static ProtoConfig Config => m_Config ??= BH.Engine.Serialiser.Convert.FromJson(File.ReadAllText(Path.Combine(RootFolder, "ProtoConfig.json"))) as ProtoConfig;

        public static string BHoMAssemblies => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "BHoM", "Assemblies");

        public static string ArtifactsFolder => Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts");

        /***************************************************/

        private static string m_SchemaFile;

        private static ProtoConfig m_Config;

        /***************************************************/
    }
}
