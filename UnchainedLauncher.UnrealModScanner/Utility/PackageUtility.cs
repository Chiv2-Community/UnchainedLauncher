using CUE4Parse.UE4.Assets;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.UObject;

namespace UnchainedLauncher.UnrealModScanner.Utility {
    public class PackageUtility {
        /// <summary>
        /// Returns the CDOs of all classes defined in this package.
        /// </summary>
        public static IEnumerable<UObject> GetClassDefaultObjects(IPackage package) =>
            package.ExportsLazy
                .Select(export => export.Value)
                .OfType<UClass>()
                .Select(uClass => uClass.ClassDefaultObject.Load())
                .OfType<UObject>();
        
        public static T GetSingleCDO<T>(IPackage package) where T : UObject
        {
            try 
            {
                return GetClassDefaultObjects(package).OfType<T>().Single();
            }
            catch (InvalidOperationException) 
            {
                throw new InvalidOperationException(
                    $"Package '{package.Name}' was expected to have exactly one CDO of type {typeof(T).Name}.");
            }
        }

        public static UObject GetSingleCDO(IPackage package)
        {
            try 
            {
                return GetClassDefaultObjects(package).Single();
            }
            catch (InvalidOperationException) 
            {
                throw new InvalidOperationException(
                    $"Package '{package.Name}' is expected to have exactly one Class Default Object, but found {GetClassDefaultObjects(package).Count()}.");
            }
        }
        
        /// <summary>
        /// Returns the CDOs of all classes defined in this package.
        /// </summary>
        public static IEnumerable<UClass> GetClassExports(IPackage package) =>
            package.ExportsLazy
                .Select(export => export.Value)
                .OfType<UClass>();

        public static UClass GetSingleClassExport(IPackage package)
        {
            try 
            {
                return GetClassExports(package).Single();
            }
            catch (InvalidOperationException) 
            {
                throw new InvalidOperationException(
                    $"Package '{package.Name}' is expected to have exactly one UClass export, but found {GetClassExports(package).Count()}.");
            }
        }
    }
}