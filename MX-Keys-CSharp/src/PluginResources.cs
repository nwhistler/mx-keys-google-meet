namespace Loupedeck.MxKeysGoogleMeetPlugin
{
    using System;
    using System.Reflection;

    // Reads embedded image resources (build action "Embedded Resource" in the .csproj).
    internal static class PluginResources
    {
        private static Assembly _assembly;

        public static void Init(Assembly assembly) => PluginResources._assembly = assembly;

        // Finds the first resource file with the specified file name.
        public static String FindFile(String fileName) => PluginResources._assembly.FindFileOrThrow(fileName);

        // Reads an embedded PNG and returns it as a BitmapImage.
        public static BitmapImage ReadImage(String resourceName) => PluginResources._assembly.ReadImage(PluginResources.FindFile(resourceName));
    }
}
