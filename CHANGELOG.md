# Changelog

## [0.1.0] - 2026-07-19

### Added

- 清单驱动（tools-manifest.json）的外部工具管理，清单进版本控制、二进制不进
- `binary` 工具：按平台下载 zip / tar.gz，服务端 `.sha256` 边车文件校验，按版本分目录安装
- `pypi` 工具：uv 创建独立 venv，从自建 PyPI 安装固定版本，Python 解释器由 uv 自动管理
- 引导层最小化：仅 uv 一个静态二进制，版本随包维护，支持环境变量覆盖
- 本地状态文件 `.installed.json` 实现增量同步，启动时零网络开销（无变更时）
- 管理窗口（Tools → External Tools → Manager）：版本对比、单工具同步 / 重装、清单模板创建
- CI 支持：`-executeMethod Dev.ExternalTools.Editor.ToolManager.SyncCli`
