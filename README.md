本项目是基于 [Plain Craft Launcher（PCL）](https://github.com/Meloong-Git/PCL) 的非官方二次开发版本，主要用于个人学习、功能调整及日常使用。

本项目与 PCL 官方、Mojang Studios 及 Microsoft 无关联，所有修改均基于个人使用需求，不代表原项目开发者的立场。

# Plain Craft Launcher

[![Stars](https://img.shields.io/github/stars/Meloong-Git/PCL?style=flat&logo=data:image/svg%2bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZlcnNpb249IjEiIHdpZHRoPSIxNiIgaGVpZ2h0PSIxNiI+PHBhdGggZD0iTTggLjI1YS43NS43NSAwIDAgMSAuNjczLjQxOGwxLjg4MiAzLjgxNSA0LjIxLjYxMmEuNzUuNzUgMCAwIDEgLjQxNiAxLjI3OWwtMy4wNDYgMi45Ny43MTkgNC4xOTJhLjc1MS43NTEgMCAwIDEtMS4wODguNzkxTDggMTIuMzQ3bC0zLjc2NiAxLjk4YS43NS43NSAwIDAgMS0xLjA4OC0uNzlsLjcyLTQuMTk0TC44MTggNi4zNzRhLjc1Ljc1IDAgMCAxIC40MTYtMS4yOGw0LjIxLS42MTFMNy4zMjcuNjY4QS43NS43NSAwIDAgMSA4IC4yNVoiIGZpbGw9IiNlYWM1NGYiLz48L3N2Zz4=&logoSize=auto&label=Stars&labelColor=444444&color=eac54f)](https://github.com/Meloong-Git/PCL/)
[![Issues](https://img.shields.io/github/issues/Meloong-Git/PCL?style=flat&label=Issues&labelColor=444444&color=1F883D)](https://github.com/Meloong-Git/PCL/issues)
[![哔哩哔哩](https://img.shields.io/badge/动态-BiliBili-00A4DB?style=flat&labelColor=444444&logoSize=auto)](https://space.bilibili.com/11343203/dynamic)
[![爱发电](https://img.shields.io/badge/赞助-爱发电-946ce6?style=flat&labelColor=444444&logoSize=auto)](https://meloong.com/afd/a/LTCat)

包含 PCL 的大部分源代码，包括 UI 库、动画模块、下载模块、Minecraft 启动模块等！  
代码并不是实时更新的，它会在每次 PCL 发布更新时同步一次。  
非常感谢社区的大力支持！

### 编译打包

通过执行项目根目录下的 `build.ps1` 来得到最终文件，路径位于 `artifacts/public/<版本号>`，即为本项目仓库 release 中的文件。

但发布者也可以在 `Plain Craft Launcher 2/Modules/ModSecret.vb` 中填入 OAuthClientId 与 CurseForgeAPIKey 后再进行打包，他们分别负责**正版账号登录**与从**CurseForge**获取 mod 等资源，需要发布者自行前往对应平台进行申请。
> 仓库 release 发布的文件不包含这两项 key

### 自动更新

可以在 **设置 - 其他 - 启动器 - 更新源地址** 填写版本信息 JSON 文件的完整 url 地址，然后点击“检查更新”，若地址无效，则会弹出错误提示。  
配置默认为空，填写后每次启动会按所选模式检查更新。

JSON 的可选布尔字段 `critical` 默认是 `false`。标记为 `true` 时，选择“仅在有重大漏洞更新时显示提示”的用户也会收到启动更新提示。

每次打包后会同步生成 update.json，版本信息 JSON 文件的内容示例如下：

```json
{
    "version": "2.99.0.1-fix-download.2",
    "url": "https://example.com/PCL-2.99.0.1-fix-download.2.zip",
    "sha256": "ZIP文件的64位十六进制SHA256",
    "notes": "本次更新说明",
    "critical": false
}
```

各项说明如下：

- version: 打包自动生成
- url: 必填，ZIP 的完整下载地址，记得填写
- sha256: 打包自动生成
- notes: 可选，本次更新说明
- critical: 为 `true` 时，选择“仅在有重大漏洞更新时显示提示”的用户也会收到启动更新提示

可将本文件的可访问完整 url 发送给使用方填写更新源。

### 相关资源
- [PCL 下载](https://meloong.com/pcl)：下载可免费使用的正式版 PCL！
- [PCL 功能投票](https://github.com/Meloong-Git/PCL/discussions/2)：来参加投票吧，开发者会优先处理票数较高的项目！
- [帮助文档库](https://github.com/LTCatt/PCL2Help)：PCL 内置帮助文档的存储库
