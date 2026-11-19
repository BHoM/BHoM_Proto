using BH.Engine.Adapters.gRPC;
using BH.Engine.Base;
using BH.oM.Adapters.gRPC;
using System.Reflection;

namespace ProtoGeneration
{
    public static class AssemblyLoader
    {
        /***************************************************/

        // Loads every oM assembly in the BHoM folder that is within the organisations and repositories of the config.
        public static List<Assembly> LoadOmAssemblies(ProtoConfig config)
        {
            List<Assembly> result = new List<Assembly>();
            string folder = BH.Engine.Base.Query.BHoMFolder();

            foreach (string file in Directory.GetFiles(folder, "*.dll", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!name.IsOmAssembly())
                    continue;

                try
                {
                    Assembly loaded = Assembly.LoadFrom(file);
                    if (loaded != null && loaded.IsInScope(config))
                        result.Add(loaded);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error loading assembly {name}: {e.Message}");
                }
            }

            return result.OrderBy(a => a.GetName().Name, StringComparer.Ordinal).ToList();
        }

        /***************************************************/
    }
}
