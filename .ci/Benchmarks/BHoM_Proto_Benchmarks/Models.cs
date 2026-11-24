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

using BH.oM.Base;
using BH.oM.Geometry;
using BH.oM.Structure.Constraints;
using BH.oM.Structure.Elements;
using BH.oM.Structure.Fragments;
using BH.oM.Structure.MaterialFragments;
using BH.oM.Structure.SectionProperties;
using BH.oM.Structure.SurfaceProperties;

namespace BH.Benchmark.Proto
{
    // The data sets of the benchmark, built by hand with a fixed seed so every run serialises exactly the same objects.
    public static class Models
    {
        /***************************************************/
        /**** Public Methods                            ****/
        /***************************************************/

        public static readonly string[] Names = { "Geometry", "Bars", "Panels", "CustomData", "Fragments" };

        // Fraction of the full data set sizes, e.g. 0.25 for quick runs while trying options. Read from an environment
        // variable so the benchmark processes started by BenchmarkDotNet use the same value.
        public const string ScaleVariable = "BHOM_PROTO_BENCHMARK_SCALE";

        public static double Scale => double.TryParse(Environment.GetEnvironmentVariable(ScaleVariable), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double scale) && scale > 0 ? scale : 1;

        public static List<IObject> Create(string name)
        {
            switch (name)
            {
                case "Geometry": return Geometry();
                case "Bars": return Bars(Scaled(2000), Scaled(500)).Cast<IObject>().ToList();
                case "Panels": return Panels(Scaled(200));
                case "CustomData": return CustomData(Scaled(500));
                case "Fragments": return Fragments(Scaled(1000));
                default: throw new ArgumentException($"Unknown data set {name}. Known: {string.Join(", ", Names)}.");
            }
        }

        /***************************************************/
        /**** Data sets                                 ****/
        /***************************************************/

        // Small, flat, high-volume messages: scalar fields and lists of points.
        public static List<IObject> Geometry()
        {
            Random random = new Random(42);
            List<IObject> result = new List<IObject>();

            for (int i = 0; i < Scaled(10000); i++)
                result.Add(RandomPoint(random));

            for (int i = 0; i < Scaled(2000); i++)
                result.Add(new Line { Start = RandomPoint(random), End = RandomPoint(random) });

            for (int i = 0; i < Scaled(200); i++)
                result.Add(new Polyline { ControlPoints = Enumerable.Range(0, 100).Select(_ => RandomPoint(random)).ToList() });

            for (int i = 0; i < Scaled(50); i++)
            {
                int count = 20, degree = 3;
                result.Add(new NurbsCurve
                {
                    ControlPoints = Enumerable.Range(0, count).Select(_ => RandomPoint(random)).ToList(),
                    Weights = Enumerable.Range(0, count).Select(_ => 0.5 + random.NextDouble()).ToList(),
                    Knots = Enumerable.Range(0, count + degree - 1).Select(k => (double)Math.Clamp(k - degree + 1, 0, count - degree)).ToList()
                });
            }

            return result;
        }

        /***************************************************/

        // Bars sharing nodes, with supports, releases, steel I sections and a steel material: nested messages, unions
        // (ISectionProperty, IMaterialFragment, IProfile, ICurve) and immutable types (SteelSection, ISectionProfile).
        public static List<Bar> Bars(int barCount, int nodeCount)
        {
            Random random = new Random(42);
            Steel steel = new Steel { Name = "S355", Density = 7850, PoissonsRatio = 0.3, YoungsModulus = 210e9, ThermalExpansionCoeff = 1.2e-5, YieldStress = 355e6, UltimateStress = 470e6 };

            List<SteelSection> sections = new List<SteelSection>
            {
                BH.Engine.Structure.Create.SteelISection(0.300, 0.0071, 0.150, 0.0107, 0.015, 0, steel, "IPE300"),
                BH.Engine.Structure.Create.SteelISection(0.400, 0.0086, 0.180, 0.0135, 0.021, 0, steel, "IPE400"),
                BH.Engine.Structure.Create.SteelISection(0.500, 0.0102, 0.200, 0.0160, 0.021, 0, steel, "IPE500"),
                BH.Engine.Structure.Create.SteelISection(0.300, 0.0110, 0.300, 0.0190, 0.027, 0, steel, "HEB300"),
                BH.Engine.Structure.Create.SteelISection(0.200, 0.0090, 0.200, 0.0150, 0.018, 0, steel, "HEB200"),
            };

            Constraint6DOF pinned = new Constraint6DOF { Name = "Pinned", TranslationX = DOFType.Fixed, TranslationY = DOFType.Fixed, TranslationZ = DOFType.Fixed };
            Constraint6DOF fixedSupport = new Constraint6DOF { Name = "Fixed", TranslationX = DOFType.Fixed, TranslationY = DOFType.Fixed, TranslationZ = DOFType.Fixed, RotationX = DOFType.Fixed, RotationY = DOFType.Fixed, RotationZ = DOFType.Fixed };
            Constraint6DOF free = new Constraint6DOF { Name = "Free" };
            BarRelease fixFix = new BarRelease { Name = "FixFix", StartRelease = fixedSupport, EndRelease = fixedSupport };
            BarRelease pinPin = new BarRelease { Name = "PinPin", StartRelease = pinned, EndRelease = pinned };

            List<Node> nodes = Enumerable.Range(0, nodeCount).Select(i => new Node
            {
                Name = $"N{i}",
                Position = RandomPoint(random),
                Support = i % 25 == 0 ? (i % 50 == 0 ? fixedSupport : pinned) : null
            }).ToList();

            return Enumerable.Range(0, barCount).Select(i => new Bar
            {
                Name = $"B{i}",
                Start = nodes[random.Next(nodeCount)],
                End = nodes[random.Next(nodeCount)],
                SectionProperty = sections[i % sections.Count],
                OrientationAngle = random.NextDouble() * Math.PI,
                Release = i % 3 == 0 ? pinPin : fixFix,
                Tags = new HashSet<string> { "Benchmark", i % 2 == 0 ? "Beam" : "Column" }
            }).ToList();
        }

        /***************************************************/

        // Panels with polyline outlines, one rectangular opening each and a concrete surface property: collections of messages
        // and ICurve unions inside edges.
        public static List<IObject> Panels(int count)
        {
            Random random = new Random(42);
            Concrete concrete = new Concrete { Name = "C32/40", Density = 2400, PoissonsRatio = 0.2, YoungsModulus = 33e9, CylinderStrength = 32e6, CubeStrength = 40e6 };
            ConstantThickness slab = new ConstantThickness { Name = "Slab 250", Thickness = 0.25, Material = concrete };

            List<IObject> result = new List<IObject>();
            for (int i = 0; i < count; i++)
            {
                double x = random.NextDouble() * 100, y = random.NextDouble() * 100, z = Math.Floor(random.NextDouble() * 10) * 3.5;
                double w = 5 + random.NextDouble() * 10, d = 5 + random.NextDouble() * 10;
                Polyline outline = Rectangle(x, y, z, w, d);
                Polyline opening = Rectangle(x + w / 3, y + d / 3, z, w / 4, d / 4);
                Panel panel = BH.Engine.Structure.Create.Panel(outline, new List<ICurve> { opening }, slab, null, $"P{i}");
                result.Add(panel);
            }
            return result;
        }

        /***************************************************/

        // Bars with ~10 CustomData entries each: strings, numbers, booleans, lists, a nested dictionary and a BHoM object,
        // i.e. google.protobuf.Any holding wrappers, Struct, ListValue and messages.
        public static List<IObject> CustomData(int count)
        {
            Random random = new Random(7);
            List<Bar> bars = Bars(count, Math.Max(2, count / 4));
            foreach (Bar bar in bars)
            {
                bar.CustomData = new Dictionary<string, object>
                {
                    ["Id"] = RandomGuid(random).ToString(),
                    ["Mark"] = $"M-{random.Next(1000)}",
                    ["Utilisation"] = random.NextDouble(),
                    ["Storey"] = random.Next(20),
                    ["IsPrimary"] = random.Next(2) == 0,
                    ["Loads"] = Enumerable.Range(0, 5).Select(_ => random.NextDouble() * 100).Cast<object>().ToList(),
                    ["Comments"] = new List<object> { "Checked", "Revised" },
                    ["Properties"] = new Dictionary<string, object> { ["Fire"] = "R60", ["Exposure"] = "XC1", ["Cover"] = 0.03 },
                    ["Origin"] = RandomPoint(random),
                    ["Revision"] = (double)random.Next(10)
                };
            }
            return bars.Cast<IObject>().ToList();
        }

        /***************************************************/

        // Bars with 2 fragments each: repeated google.protobuf.Any fragments.
        public static List<IObject> Fragments(int count)
        {
            Random random = new Random(11);
            List<Bar> bars = Bars(count, Math.Max(2, count / 4));
            foreach (Bar bar in bars)
            {
                bar.Fragments.Add(new SectionModifier { Area = 1, Iy = 0.5 + random.NextDouble() / 2, Iz = 0.5 + random.NextDouble() / 2, J = 1, Asy = 1, Asz = 1 });
                bar.Fragments.Add(new ConnectionAllowance { Allowance = random.NextDouble() * 0.1, Name = "Connections" });
            }
            return bars.Cast<IObject>().ToList();
        }

        /***************************************************/
        /**** Private Methods                           ****/
        /***************************************************/

        private static Point RandomPoint(Random random)
        {
            return new Point { X = random.NextDouble() * 100, Y = random.NextDouble() * 100, Z = random.NextDouble() * 30 };
        }

        /***************************************************/

        private static int Scaled(int count)
        {
            return Math.Max(2, (int)Math.Round(count * Scale));
        }

        /***************************************************/

        private static Guid RandomGuid(Random random)
        {
            byte[] bytes = new byte[16];
            random.NextBytes(bytes);
            return new Guid(bytes);
        }

        /***************************************************/

        private static Polyline Rectangle(double x, double y, double z, double width, double depth)
        {
            return new Polyline
            {
                ControlPoints = new List<Point>
                {
                    new Point { X = x, Y = y, Z = z },
                    new Point { X = x + width, Y = y, Z = z },
                    new Point { X = x + width, Y = y + depth, Z = z },
                    new Point { X = x, Y = y + depth, Z = z },
                    new Point { X = x, Y = y, Z = z },
                }
            };
        }

        /***************************************************/
    }
}
