using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Verse;

namespace ModErrorChecker
{
    [StaticConstructorOnStartup]
    public static class ModErrorChecker
    {
        static ModErrorChecker()
        {
            StartChecks();
        }

        private async static void StartChecks()
        {
            await Task.Run(delegate 
            {
                CheckAssemblies();
                CheckXml();
            });
            CheckSounds();
            foreach (var errorMessage in errorMessages)
            {
                Log.Error(errorMessage);
            }
        }

        public static void CheckXml()
        {
            foreach (WorkGiverDef def in DefDatabase<WorkGiverDef>.AllDefs)
            {
                if (def.giverClass is null)
                {
                    LogError(def, "is missing worker class");
                }
            }

            foreach (var def in DefDatabase<RoomRoleDef>.AllDefs)
            {
                if (def.workerClass is null)
                {
                    LogError(def, "doesn't have a specified role class");
                }
            }

            foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefs)
            {
                if (thingDef.thingClass is null)
                {
                    LogError(thingDef, "is missing thing class");
                }
                if (thingDef.IsApparel && !typeof(Apparel).IsAssignableFrom(thingDef.thingClass))
                {
                    LogError(thingDef, $"is set as apparel, but its thing class {thingDef.thingClass} is not Apparel thing class");
                }
                if (thingDef.comps != null)
                {
                    foreach (CompProperties compProps in thingDef.comps)
                    {
                        if (compProps.compClass is null)
                        {
                            LogError(thingDef, "is missing comp class from one of comps");
                        }
                    }

                    //var duplicateTypes = thingDef.comps.Where(x => x.compClass is not null).Select(x => x.compClass).GroupBy(c => c.Name).Where(g => g.Skip(1).Any());
                    //foreach (var group in duplicateTypes)
                    //{
                    //    var type = group.ToList().First();
                    //    LogError(thingDef, "has duplicate comps in type " + thingDef.comps.First(x => x.compClass == type)
                    //        .GetType().Name + ":" + type.Name + ", count: " + group.Count() + ".");
                    //}
                }
                var recipes = thingDef.AllRecipes;
                for (int j = 0; j < recipes.Count; j++)
                {
                    RecipeDef recipeDef = recipes[j];
                    foreach (var product in recipeDef.products)
                    {
                        if (product.thingDef is null)
                        {
                            LogError(recipeDef, "has an empty product, it will error out on map loading.");
                        }
                    }
                }

                //if (thingDef.modExtensions != null)
                //{
                //    var duplicateTypes = thingDef.modExtensions.GroupBy(c => c.GetType().Name).Where(g => g.Skip(1).Any()).SelectMany(c => c);
                //    foreach (var type in duplicateTypes)
                //    {
                //        LogError(thingDef, "has duplicate modExtensions in type " + type.GetType().Name + ".");
                //    }
                //}
            }
        }

        private static void CheckSounds()
        {
            foreach (SoundDef def in DefDatabase<SoundDef>.AllDefs)
            {
                if (def.modContentPack is null || def.modContentPack.IsOfficialMod is false)
                {
                    foreach (Verse.Sound.SubSoundDef subSound in def.subSounds)
                    {
                        foreach (Verse.Sound.AudioGrain grain in subSound.grains)
                        {
                            if (grain.GetResolvedGrains().Any() is false)
                            {
                                LogError(def, "sound is missing resolved grains");
                            }
                        }
                    }
                }
            }
        }

        private static void LogError(Def def, string detail)
        {
            errorMessages.Add(def.defName + " (mod " + (def.modContentPack?.Name ?? "Unknown") + ") " + detail + " and will not work properly.");
        }

        public static bool CanBePatched(this MethodBase mi)
        {
            if (mi.HasMethodBody() && mi.DeclaringType.IsConstructedGenericType is false &&
                mi.IsGenericMethod is false && mi.ContainsGenericParameters is false && mi.IsGenericMethodDefinition is false)
            {
                return true;
            }
            return false;
        }
        public static HashSet<string> assembliesToSkip = new()
        {
            "System", "Cecil", "Multiplayer", "Prepatcher", "HeavyMelee", "0Harmony", "UnityEngine", "mscorlib",
            "ICSharpCode", "Newtonsoft", "ISharpZipLib", "NAudio", "Unity.TextMeshPro", "ModErrorChecker", "NVorbis",
            "com.rlabrecque.steamworks.net", "Assembly-CSharp-firstpass", "CombatAI", "MonoMod"
        };
        private static bool TypeValidator(Type type)
        {
            return !assembliesToSkip.Any(asmName => type.Assembly?.FullName?.Contains(asmName) ?? false);
        }

        public static ConcurrentBag<string> errorMessages = new ConcurrentBag<string>();
        private static void CheckAssemblies()
        {
            HashSet<MethodInfo> methodsToParse = new();
            IEnumerable<Type> types = GenTypes.AllTypes.Where(x => TypeValidator(x));
            foreach (Type type in types)
            {
                try
                {
                    foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                    {
                        if (method.CanBePatched())
                        {
                            methodsToParse.Add(method);
                        }
                    }
                }
                catch 
                {
                }
            }
            foreach (MethodInfo method in methodsToParse)
            {
                try
                {
                    List<CodeInstruction> instructions = PatchProcessor.GetOriginalInstructions(method);
                }
                catch (Exception ex)
                {
                    if (ex is not TypeLoadException)
                    {
                        Assembly methodAssembly = method.DeclaringType.Assembly;
                        ModContentPack mod = LoadedModManager.runningMods.Where(x => x.assemblies.loadedAssemblies.Contains(methodAssembly)).FirstOrDefault();
                        errorMessages.Add("Error in " + mod?.Name + ", assembly name: " + methodAssembly.GetName().Name + ", method: "
                            + method.DeclaringType.Name + ":" + method.Name + ", exception: " + ex);
                    }
                }
            }
        }
    }
}
