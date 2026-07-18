# External Tools Manager（dev.external-tools）

Unity 外部工具管理器：用一份**清单文件**声明项目依赖的平台相关二进制工具和 Python（PyPI）工具，打开工程时自动下载、校验、安装当前平台所需的版本——**二进制本体永远不进入版本控制**。

## 核心思想

- **清单是唯一真相来源**（`tools-manifest.json`，进 git）
- **二进制 / venv 是可再生产物**（`ExternalTools/`，加入 `.gitignore`）
- **本地状态驱动增量同步**（`.installed.json` 扮演锁文件角色，版本不符才下载）
- **引导层最小化**：仅一个静态二进制 uv（版本随本包维护），Python 解释器由 uv 按需自动下载

## 安装

通过 Unity Package Manager 的 git URL 安装：

```
https://github.com/dayongxie/unity-external-tools.git
```

Package Manager → `+` → `Add package from git URL...`

## 快速开始

1. 菜单 **Tools → External Tools → Manager**，点击 **Create Manifest** 生成
   `Assets/ExternalTools/tools-manifest.json` 模板（此文件**需要提交到 git**）；
2. 按项目需求修改清单（见下方说明）；
3. 在项目的 `.gitignore` 中加入：

   ```gitignore
   # 外部工具本体（可再生产物）
   /ExternalTools/
   ```

4. 重新打开工程或点击 **Sync All**，工具自动就绪。

## 清单格式

```json
{
  "registry": "https://your-server.com/tools",
  "pypiIndex": "https://pypi.yourcompany.com/simple",
  "tools": [
    {
      "name": "ffmpeg",
      "type": "binary",
      "version": "7.1",
      "executable": "ffmpeg",
      "platforms": {
        "win-x64":   { "file": "ffmpeg-7.1-win-x64.zip" },
        "osx-arm64": { "file": "ffmpeg-7.1-osx-arm64.tar.gz" }
      }
    },
    {
      "name": "asset-packer",
      "type": "pypi",
      "version": "1.4.0",
      "package": "asset-packer",
      "entryPoint": "asset-packer",
      "python": "3.12"
    }
  ]
}
```

| 字段 | 说明 |
|------|------|
| `registry` | 二进制压缩包的下载根地址 |
| `pypiIndex` | 自建 PyPI 仓库 simple index 地址 |
| `type` | `binary`（压缩包）或 `pypi`（Python 包） |
| `version` | 固定版本号；升级工具 = 改版本号并提交，全员自动同步 |
| `platforms` | 平台标识 → 压缩包文件名。支持 `.zip` / `.tar.gz` / `.tgz`；校验值取自服务端同名 `.sha256` 边车文件 |
| `python` | pypi 工具的 Python 版本约束（如 `3.12`），由 uv 自动解析下载解释器 |

平台标识：`win-x64` / `osx-arm64` / `osx-x64` / `linux-x64`。

## 服务端约定

二进制产物与 SHA256 边车文件同目录存放：

```
tools/ffmpeg-7.1-win-x64.zip
tools/ffmpeg-7.1-win-x64.zip.sha256   # 发布脚本生成
```

发布时无需修改清单——上传产物和边车文件即可；仅在升级版本时改清单中的一行 `version`。

## 业务代码中使用

```csharp
using Dev.ExternalTools.Editor;

string ffmpeg = ToolManager.GetToolPath("ffmpeg");
// → <项目根>/ExternalTools/ffmpeg/7.1/win-x64/ffmpeg.exe

string packer = ToolManager.GetToolPath("asset-packer");
// → <项目根>/ExternalTools/asset-packer/1.4.0/osx-arm64/venv/bin/asset-packer
```

`GetToolPath` 对 binary 和 pypi 工具返回统一的可执行文件路径，`Process.Start` 直接使用。

## 环境变量（凭证与镜像，不进 git）

| 变量 | 用途 |
|------|------|
| `EXTERNALTOOLS_PYPI_INDEX` | 覆盖清单中的 `pypiIndex`，可携带 token：`https://user:TOKEN@pypi.yourcompany.com/simple` |
| `EXTERNALTOOLS_UV_MIRROR` | 覆盖 uv 引导下载地址（默认指向内网镜像） |
| `UV_PYTHON_INSTALL_MIRROR` | uv 下载 Python 解释器的镜像（python-build-standalone） |

## CI 支持

`[InitializeOnLoad]` 在 `-batchmode` 下同样生效，CI 打开工程即自动备齐工具；也可显式调用：

```bash
Unity -batchmode -projectPath . \
  -executeMethod Dev.ExternalTools.Editor.ToolManager.SyncCli -quit
```

## 目录结构（消费工程中）

```
ProjectRoot/
├── Assets/ExternalTools/tools-manifest.json   # 清单（进 git）
└── ExternalTools/                             # 工具本体（.gitignore）
    ├── .installed.json                        # 本地状态（锁文件）
    ├── bootstrap/uv/0.7.12/<platform>/        # 引导层
    ├── ffmpeg/7.1/<platform>/ffmpeg[.exe]
    └── asset-packer/1.4.0/<platform>/venv/    # pypi 工具的独立 venv
```

## 许可证

MIT，见 [LICENSE](LICENSE)。
