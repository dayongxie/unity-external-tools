using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Dev.ExternalTools.Editor
{
    /// <summary>
    /// 工具依赖清单（tools-manifest.json）的数据模型。
    /// 清单是唯一的真相来源：进版本控制；二进制与 venv 是可再生产物，不进版本控制。
    /// </summary>
    [Serializable]
    public class ToolManifest
    {
        /// <summary>二进制工具的下载根地址。</summary>
        [JsonProperty("registry")] public string Registry = "";

        /// <summary>自建 PyPI 仓库地址（simple index）。</summary>
        [JsonProperty("pypiIndex")] public string PypiIndex = "";

        [JsonProperty("tools")] public List<ToolEntry> Tools = new List<ToolEntry>();

        public ToolEntry Find(string name) =>
            Tools.Find(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    [Serializable]
    public class ToolEntry
    {
        /// <summary>工具名，业务代码通过它查询路径。</summary>
        [JsonProperty("name")] public string Name;

        /// <summary>"binary"（压缩包）或 "pypi"（Python 包）。</summary>
        [JsonProperty("type")] public string Type = "binary";

        /// <summary>固定版本号。升级工具 = 改这里并提交，全员自动同步。</summary>
        [JsonProperty("version")] public string Version;

        // ---- binary 专属 ----

        /// <summary>各平台压缩包信息。键为平台标识：win-x64 / osx-arm64 / osx-x64 / linux-x64。</summary>
        [JsonProperty("platforms")] public Dictionary<string, PlatformArtifact> Platforms;

        /// <summary>压缩包内可执行文件名（不含扩展名），默认同 Name。</summary>
        [JsonProperty("executable")] public string Executable;

        // ---- pypi 专属 ----

        /// <summary>PyPI 包名，默认同 Name。</summary>
        [JsonProperty("package")] public string Package;

        /// <summary>venv 中的入口命令名，默认同 Name。</summary>
        [JsonProperty("entryPoint")] public string EntryPoint;

        /// <summary>Python 版本约束（如 "3.12"），由 uv 自动解析并下载对应解释器。</summary>
        [JsonProperty("python")] public string Python = "3.12";

        [JsonIgnore] public bool IsPypi =>
            string.Equals(Type, "pypi", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public class PlatformArtifact
    {
        /// <summary>压缩包文件名（.zip / .tar.gz / .tgz）。SHA256 校验值从服务端同名 .sha256 边车文件获取。</summary>
        [JsonProperty("file")] public string File;
    }
}
