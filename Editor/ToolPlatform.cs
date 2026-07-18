using System.Runtime.InteropServices;

namespace Dev.ExternalTools.Editor
{
    /// <summary>当前编辑器平台的标识解析。</summary>
    public static class ToolPlatform
    {
        /// <summary>平台标识：win-x64 / osx-arm64 / osx-x64 / linux-x64。</summary>
        public static string Key
        {
            get
            {
#if UNITY_EDITOR_WIN
                return "win-x64";
#elif UNITY_EDITOR_OSX
                return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                    ? "osx-arm64"
                    : "osx-x64";
#elif UNITY_EDITOR_LINUX
                return "linux-x64";
#else
                return "unknown";
#endif
            }
        }

        public static bool IsWindows => Key == "win-x64";

        public static string ExeSuffix => IsWindows ? ".exe" : "";
    }
}
