using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class TypeLoader
{
    /// <summary>
    /// Return a list of all types that are T subclasses
    /// </summary>
    public static List<Type> GetSubclassesOf<T>()
    {
        var baseType = typeof(T);

        // Get all assemblies in the current AppDomain
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        var subclasses = new List<Type>();

        foreach (var assembly in assemblies)
        {
            try
            {
                // Find all types that are subclasses of the given type and not abstract
                var types = assembly.GetTypes()
                    .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract);

                subclasses.AddRange(types);
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Log loader exceptions if any
                foreach (var loaderException in ex.LoaderExceptions)
                {
                    Console.WriteLine(loaderException.Message);
                }
            }
        }

        return subclasses;
    }

    /// <summary>
    /// Return a list of all types implementing T
    /// </summary>
    public static List<Type> GetTypesImplementing<T>()
    {
        var interfaceType = typeof(T);

        if (!interfaceType.IsInterface)
            throw new ArgumentException($"{interfaceType.FullName} is not an interface.");

        List<Type> types = new();

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            try
            {
                // Find all types that implement the interface and are concrete (not abstract)
                types.AddRange(assembly.GetTypes()
                    .Where(t => interfaceType.IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                    .ToList());
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Log loader exceptions if any
                foreach (var loaderException in ex.LoaderExceptions)
                {
                    Console.WriteLine(loaderException.Message);
                }
            }
        }
        return types;
    }
}
