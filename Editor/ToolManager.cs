using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Dev.ExternalTools.Editor
{
    /// <summary>
    /// 外部工具管理器核心：打开工程 / 触发菜单时自动同步。
    /// 增量更新：本地状态（.installed.json）与清单版本逐工具对比，不一致才安装。
    /// </summary>
    [InitializeOnLoad]
    public static class ToolManager
    {
        /// <summary>清单在项目中的位置（应提交到版本控制）。</summary>
        public const string ManifestRelativePath = "Assets/ExternalTools/tools-manifest.json";

        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

        static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>工具本体存放目录（项目根下，必须加入 .gitignore）。</summary>
        public static string ToolsRoot => Path.Combine(ProjectRoot, "ExternalTools");

        public static string ManifestPath => Path.Combine(ProjectRoot, ManifestRelativePath);
        static string StatePath => Path.Combine(ToolsRoot, ".installed.json");

        static ToolManager()
        {
            EditorApplication.delayCall += () => _ = Sync();
        }

        [MenuItem("Tools/External Tools/Sync Now")]
        public static void SyncMenu() => _ = Sync(force: true);

        /// <summary>供 CI -executeMethod 调用：Dev.ExternalTools.Editor.ToolManager.SyncCli</summary>
        public static async Task SyncCli() => await Sync(force: false);

        // ---------------------------------------------------------------
        // 清单 / 状态
        // ---------------------------------------------------------------

        public static ToolManifest LoadManifest()
        {
            if (!File.Exists(ManifestPath)) return null;
            return JsonConvert.DeserializeObject<ToolManifest>(File.ReadAllText(ManifestPath));
        }

        internal static Dictionary<string, string> LoadState()
        {
            if (!File.Exists(StatePath)) return new Dictionary<string, string>();
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(StatePath))
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }

        internal static void SaveState(Dictionary<string, string> state)
        {
            Directory.CreateDirectory(ToolsRoot);
            File.WriteAllText(StatePath, JsonConvert.SerializeObject(state, Formatting.Indented));
        }

        // ---------------------------------------------------------------
        // 同步
        // ---------------------------------------------------------------

        /// <summary>对比清单与本地状态，增量安装缺失 / 版本不符的工具。</summary>
        public static async Task Sync(bool force = false)
        {
            var manifest = LoadManifest();
            if (manifest == null) return; // 项目未启用外部工具

            var state = LoadState();

            foreach (var tool in manifest.Tools)
            {
                if (!force
                    && state.TryGetValue(tool.Name, out string installed)
                    && installed == tool.Version)
                    continue;

                bool ok = false;
                try
                {
                    EditorUtility.DisplayProgressBar(
                        "同步外部工具", $"{tool.Name} {tool.Version} ({ToolPlatform.Key})", -1f);
                    ok = tool.IsPypi
                        ? await InstallPypi(manifest, tool)
                        : await InstallBinary(manifest, tool);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ExternalTools] 安装 {tool.Name} 失败: {e.Message}");
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }

                // 只有校验通过才写入状态：坏文件永远不会被业务代码用到，下次启动自动重试
                if (ok)
                {
                    state[tool.Name] = tool.Version;
                    SaveState(state);
                }
            }
        }

        // ---------------------------------------------------------------
        // 安装：binary
        // ---------------------------------------------------------------

        static async Task<bool> InstallBinary(ToolManifest manifest, ToolEntry tool)
        {
            if (tool.Platforms == null || !tool.Platforms.TryGetValue(ToolPlatform.Key, out var art))
            {
                Debug.LogWarning($"[ExternalTools] {tool.Name} 不支持当前平台 {ToolPlatform.Key}");
                return false;
            }

            string url = $"{manifest.Registry.TrimEnd('/')}/{art.File}";
            string tmpDir = Path.Combine(ToolsRoot, ".tmp");
            Directory.CreateDirectory(tmpDir);
            string archive = Path.Combine(tmpDir, art.File);

            await Download(url, archive);
            if (!await VerifyWithSidecar(url, archive))
            {
                File.Delete(archive);
                return false;
            }

            string dest = ToolDir(tool);
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);
            await ExtractArchive(archive, dest);
            File.Delete(archive);
            MakeExecutable(dest);

            Debug.Log($"[ExternalTools] {tool.Name} {tool.Version} ({ToolPlatform.Key}) 安装完成");
            return true;
        }

        // ---------------------------------------------------------------
        // 安装：pypi
        // ---------------------------------------------------------------

        static async Task<bool> InstallPypi(ToolManifest manifest, ToolEntry tool)
        {
            string uv = await Bootstrap.EnsureUv();

            string dest = ToolDir(tool);
            string venv = Path.Combine(dest, "venv");
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            Directory.CreateDirectory(dest);

            var env = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(Bootstrap.PythonInstallMirror))
                env["UV_PYTHON_INSTALL_MIRROR"] = Bootstrap.PythonInstallMirror;

            // 自建 PyPI 的认证通过环境变量注入（如带 token 的 EXTERNALTOOLS_PYPI_INDEX），不进 git
            string indexUrl = Environment.GetEnvironmentVariable("EXTERNALTOOLS_PYPI_INDEX")
                              ?? manifest.PypiIndex;

            string python = string.IsNullOrEmpty(tool.Python) ? "3.12" : tool.Python;
            if (!await Run(uv, $"venv --python {python} \"{venv}\"", env)) return false;

            string package = string.IsNullOrEmpty(tool.Package) ? tool.Name : tool.Package;
            return await Run(uv,
                $"pip install --python \"{VenvPython(venv)}\" --index-url {indexUrl} {package}=={tool.Version}",
                env);
        }

        // ---------------------------------------------------------------
        // 对外 API
        // ---------------------------------------------------------------

        /// <summary>获取某个工具的入口可执行文件路径（binary 或 pypi 通用）。</summary>
        public static string GetToolPath(string toolName)
        {
            var manifest = LoadManifest()
                ?? throw new InvalidOperationException($"[ExternalTools] 未找到清单: {ManifestPath}");
            var tool = manifest.Find(toolName)
                ?? throw new InvalidOperationException($"[ExternalTools] 清单中不存在工具: {toolName}");

            if (tool.IsPypi)
            {
                string exe = string.IsNullOrEmpty(tool.EntryPoint) ? tool.Name : tool.EntryPoint;
                string venv = Path.Combine(ToolDir(tool), "venv");
                return Path.Combine(venv,
                    ToolPlatform.IsWindows ? $"Scripts/{exe}.exe" : $"bin/{exe}");
            }

            string exeName = string.IsNullOrEmpty(tool.Executable) ? tool.Name : tool.Executable;
            return Path.Combine(ToolDir(tool), exeName + ToolPlatform.ExeSuffix);
        }

        internal static string ToolDir(ToolEntry tool) =>
            Path.Combine(ToolsRoot, tool.Name, tool.Version, ToolPlatform.Key);

        static string VenvPython(string venv) =>
            Path.Combine(venv, ToolPlatform.IsWindows ? "Scripts/python.exe" : "bin/python");

        // ---------------------------------------------------------------
        // 基础能力（引导层复用）
        // ---------------------------------------------------------------

        internal static async Task Download(string url, string path)
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(path);
            await resp.Content.CopyToAsync(fs);
        }

        /// <summary>拉取服务端同名 .sha256 边车文件并校验。校验失败抛异常。</summary>
        internal static async Task<bool> VerifyWithSidecar(string url, string file)
        {
            string expected = (await Http.GetStringAsync(url + ".sha256")).Trim().Split(' ')[0];
            using var sha = SHA256.Create();
            await using var fs = File.OpenRead(file);
            string actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            if (actual != expected.ToLowerInvariant())
            {
                Debug.LogError($"[ExternalTools] SHA256 校验失败: {Path.GetFileName(file)}（文件可能损坏）");
                return false;
            }
            return true;
        }

        internal static async Task ExtractArchive(string archive, string dest)
        {
            if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archive, dest);
                return;
            }
            if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
                || archive.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(dest);
                await Run("tar", $"-xzf \"{archive}\" -C \"{dest}\"");
                return;
            }
            throw new NotSupportedException($"[ExternalTools] 不支持的压缩格式: {archive}");
        }

        internal static void MakeExecutable(string dir)
        {
            if (ToolPlatform.IsWindows) return;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo("chmod", $"+x \"{f}\"")
                        { UseShellExecute = false, CreateNoWindow = true });
                    p?.WaitForExit();
                }
                catch { /* 忽略单个文件失败 */ }
            }
        }

        internal static async Task<bool> Run(string exe, string args,
            Dictionary<string, string> env = null)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (env != null)
                foreach (var kv in env)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;

            using var p = Process.Start(psi);
            string stdout = await p.StandardOutput.ReadToEndAsync();
            string stderr = await p.StandardError.ReadToEndAsync();
            await Task.Run(() => p.WaitForExit());

            if (p.ExitCode != 0)
            {
                Debug.LogError($"[ExternalTools] 命令失败 ({p.ExitCode}): {exe} {args}\n{stderr}");
                return false;
            }
            return true;
        }
    }
}
