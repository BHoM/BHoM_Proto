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

namespace BH.Benchmark.Proto
{
    // Loads the assemblies that the referenced BHoM assemblies depend on from the installed BHoM folder.
    // Registered by Program.Main, and by the benchmark class itself, because BenchmarkDotNet runs each benchmark in a
    // separate process with its own entry point.
    public static class AssemblyResolver
    {
        /***************************************************/

        public static void Register()
        {
            if (m_Registered)
                return;

            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            m_Registered = true;
        }

        /***************************************************/

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string file = Path.Combine(Paths.BHoMAssemblies, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        }

        /***************************************************/

        private static bool m_Registered;

        /***************************************************/
    }
}
