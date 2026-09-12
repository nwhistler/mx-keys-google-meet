namespace Loupedeck.MxKeysGoogleMeetPlugin
{
    using System;

    /// <summary>
    /// The plugin's ClientApplication — deliberately empty, because this is a universal plugin
    /// (HasNoApplication in the manifest). The class still has to exist: the Logi Plugin Service
    /// refuses to load the assembly at all without a {PluginName}Application : ClientApplication
    /// type present, regardless of whether HasNoApplication means it binds nothing. Confirmed
    /// against a real prior incident in another open-source plugin (rshankras/claude-console,
    /// ClaudeConsoleApplication.cs) with the identical symptom: "Cannot load plugin from ...",
    /// "added to disabled plugins list", no exception anywhere, same DLL loads fine in a plain
    /// .NET host. Do not delete this class.
    /// </summary>
    public class MxKeysGoogleMeetApplication : ClientApplication
    {
        public MxKeysGoogleMeetApplication()
        {
        }
    }
}
