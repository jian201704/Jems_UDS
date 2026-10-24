using System;
using System.IO;
using System.Reflection;

namespace LoadProbe
{
    /// <summary>
    /// Simulates JEMSts LibraryLoader.SearchForLibraries: create a temp AppDomain whose
    /// ApplicationBase = the directory containing the function-library DLLs, then
    /// Load(AssemblyName) + GetTypes(). Proves the 3-DLL split resolves under JEMSts probing.
    /// Usage: LoadProbe.exe <dir-with-DoipFunctionLibrary.dll>
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length < 1 || !File.Exists(args[0]))
            {
                Console.WriteLine("usage: LoadProbe <path to DoipFunctionLibrary.dll>");
                return 2;
            }
            string dllPath = Path.GetFullPath(args[0]);
            string dir = Path.GetDirectoryName(dllPath);

            // Temp domain: app base = this probe's dir (so LoadProbe itself resolves);
            // PrivateBinPath = dir containing UdsFunctionLibrary + deps (mirrors JEMSts probing).
            var setup = new AppDomainSetup
            {
                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                PrivateBinPath = dir,
                ShadowCopyFiles = "false"
            };
            AppDomain domain = AppDomain.CreateDomain("SearchForLibraries", null, setup);
            try
            {
                var probe = (Probe)domain.CreateInstanceAndUnwrap(
                    typeof(Probe).Assembly.FullName, typeof(Probe).FullName);
                string report = probe.Run(dllPath);
                Console.WriteLine("PROBE OK: " + report);
                return 0;
            }
            catch (ReflectionTypeLoadException ex)
            {
                Console.WriteLine("PROBE FAIL (ReflectionTypeLoadException). Missing:");
                foreach (Exception le in ex.LoaderExceptions)
                    Console.WriteLine("  - " + le.Message);
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("PROBE FAIL: " + ex.Message);
                return 1;
            }
            finally
            {
                AppDomain.Unload(domain);
            }
        }
    }

    /// <summary>Runs inside the temp AppDomain (MarshalByRef so it executes there).</summary>
    public class Probe : MarshalByRefObject
    {
        public string Run(string dllPath)
        {
            // Mirrors JEMSts: Assembly.Load(AssemblyName.GetAssemblyName(path)).GetTypes()
            AssemblyName name = AssemblyName.GetAssemblyName(dllPath);
            Assembly asm = Assembly.Load(name);
            Type[] types = asm.GetTypes();

            int testSubclasses = 0;
            int concrete = 0;
            foreach (Type t in types)
            {
                bool isTest = false;
                Type b = t.BaseType;
                while (b != null)
                {
                    if (b.FullName == "JabilTest.Test") { isTest = true; break; }
                    b = b.BaseType;
                }
                if (isTest)
                {
                    testSubclasses++;
                    if (!t.IsAbstract && t.IsClass) concrete++;
                }
            }
            return "types=" + types.Length + " testSubclasses=" + testSubclasses + " concrete=" + concrete;
        }
    }
}
