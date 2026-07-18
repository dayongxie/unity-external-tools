using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Dev.ExternalTools.Editor
{
    /// <summary>
    /// 引导层：管理器运行所需的最小内核，只有 uv 一个静态二进制。
    /// Python 解释器由 uv 按需自动下载（python-build-standalone），无需在此声明。
    /// 引导版本随本包一起版本化，不属于项目清单；可用环境变量做紧急覆盖。
    /// </summary>
    public static class Bootstrap
    {
        public const string UvVersion = "0.7.12";

        static readonly Dictionary<string, string> UvFiles = new Dictionary<string, string>
        {
            ["win-x64"]   = $"uv-{UvVersion}-x86_64-pc-windows-msvc.zip",
            ["osx-arm64"] = $"uv-{UvVersion}-aarch64-apple-darwin.tar.gz",
            ["osx-x64"]   = $"uv-{UvVersion}-x86_64-apple-darwin.tar.gz",
            ["linux-x64"] = $"uv-{UvVersion}-x86_64-unknown-linux-gnu.tar.gz",
        };

        /// <summary>uv 安装包的镜像根地址。默认走内网镜像；可用 EXTERNALTOOLS_UV_MIRROR 覆盖。</summary>
        public static string UvBaseUrl =>
            (Environment.GetEnvironmentVariable("EXTERNALTOOLS_UV_MIRROR")
             ?? "https://your-server.com/tools/bootstrap").TrimEnd('/');

        /// <summary>uv 下载 Python 解释器的镜像（可选），通过 UV_PYTHON_INSTALL_MIRROR 注入。</summary>
        public static string PythonInstallMirror =>
            Environment.GetEnvironmentVariable("UV_PYTHON_INSTALL_MIRROR") ?? "";

        public static string UvDir =>
            Path.Combine(ToolManager.ToolsRoot, "bootstrap", "uv", UvVersion, ToolPlatform.Key);

        public static string UvExePath =>
            Path.Combine(UvDir, ToolPlatform.IsWindows ? "uv.exe" : "uv");

        /// <summary>确保 uv 可用，返回 uv 可执行文件路径。已存在则零网络开销直接返回。</summary>
        public static async Task<string> EnsureUv()
        {
            if (File.Exists(UvExePath)) return UvExePath;

            if (!UvFiles.TryGetValue(ToolPlatform.Key, out string file))
                throw new NotSupportedException($"[ExternalTools] 引导层不支持当前平台 {ToolPlatform.Key}");

            string tmpDir = Path.Combine(ToolManager.ToolsRoot, ".tmp");
            Directory.CreateDirectory(tmpDir);
            string archive = Path.Combine(tmpDir, file);

            Debug.Log($"[ExternalTools] 引导：下载 uv {UvVersion} ({ToolPlatform.Key})");
            await ToolManager.Download($"{UvBaseUrl}/{file}", archive);
            await ToolManager.VerifyWithSidecar($"{UvBaseUrl}/{file}", archive);

            if (Directory.Exists(UvDir)) Directory.Delete(UvDir, true);
            Directory.CreateDirectory(UvDir);
            await ToolManager.ExtractArchive(archive, UvDir);
            File.Delete(archive);
            ToolManager.MakeExecutable(UvDir);

            if (!File.Exists(UvExePath))
                throw new FileNotFoundException($"[ExternalTools] uv 解压后未找到可执行文件: {UvExePath}");

            return UvExePath;
        }
    }
}
