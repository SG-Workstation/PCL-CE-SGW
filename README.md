# PCL-CE-SGW
PCL-CE-SGW 是一款基于 PCL-CE 的第三方修改版，由 SG-WorkStation（非营利组织）独立开发与维护

## 许可证

本仓库采用分目录许可，完整的路径映射见 [LICENSE](LICENSE)。

| 路径 | 许可证 |
| --- | --- |
| `PCL.SGW/`（SG-WorkStation 自写代码） | MIT，见 [`PCL.SGW/LICENSE`](PCL.SGW/LICENSE) |
| `SGW.Tools/`（SG-WorkStation 自写工具） | CC BY-NC-SA 4.0（非商业性使用·相同方式共享），见 [`SGW.Tools/LICENSE`](SGW.Tools/LICENSE) |
| `Plain Craft Launcher 2/` | PCL 分发有限许可，见 [`Plain Craft Launcher 2/LICENCE`](<Plain Craft Launcher 2/LICENCE>) |
| 其余文件与目录 | Apache License 2.0 |

修改现有目录中的文件时，保留原许可证头并另加变更声明（例如 `// Modified by SG-WorkStation, 2026.`），该文件的许可证不因此改变。`SGW.Tools/` 的代码不能并入其他目录：相同方式共享条件会要求整个文件按 CC BY-NC-SA 4.0 发布。分发构建时需同时遵守上述各许可证，并保留 `PCL.Core/NOTICE` 中的第三方声明。
