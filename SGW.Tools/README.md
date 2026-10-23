# SGW.Tools

`SGW.Tools` 存放 SG-WorkStation 自行开发的工具项目，与启动器主体（`PCL.Core/`、`Plain Craft Launcher 2/`）相互独立：

- `SGW.CloudPiercer/`
- `SGW.SaveTimeMachine/`

## 许可证

本目录下由 SG-WorkStation 原创的代码采用 **Creative Commons 署名—非商业性使用—相同方式共享 4.0 国际许可协议**（Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International，简称 CC BY-NC-SA 4.0）。

- 法律文本：[`LICENSE`](LICENSE)
- 协议主页：<https://creativecommons.org/licenses/by-nc-sa/4.0/>
- SPDX 标识：`CC-BY-NC-SA-4.0`

三条许可要素对本目录的含义：

- **非商业性使用（NC）**：只允许"非主要用于商业优势或金钱报酬"的分享与改编，包含本目录的构建产物同样受此限制。
- **相同方式共享（SA）**：修改本目录代码后发布，衍生作品必须以 CC BY-NC-SA 4.0（或同一组要素的更新版本、或 CC 声明的兼容许可证）发布。因此本目录的代码不能并入 `PCL.SGW/`（MIT）或 Apache-2.0 目录——那会让整个文件变成 CC BY-NC-SA 4.0，MIT 与 Apache 的授权覆盖不了这个条件。
- **署名（BY）**：分享时必须给出作者、版权声明、许可声明与协议链接，并说明是否作过修改。

CC 协议不授予专利与商标权（第 2(b)(2) 条），这一点与 Apache-2.0 目录不同；需要专利授权的代码应放在或贡献给 Apache-2.0 目录。

完整的路径—许可映射见仓库根目录 [`LICENSE`](../LICENSE) 第 3 节。

## 署名（再分发时请保留）

> SGW.Tools © 2026 SG-WorkStation，以 CC BY-NC-SA 4.0 授权。
> 来源：<https://github.com/SG-Workstation/PCL-CE-SGW>
> 许可证：<https://creativecommons.org/licenses/by-nc-sa/4.0/>
> 如作修改，请注明"已修改"。

## 建议的文件头

新源文件建议带上这两行，便于机器识别许可：

```csharp
// SPDX-License-Identifier: CC-BY-NC-SA-4.0
// Copyright (c) 2026 SG-WorkStation
```

