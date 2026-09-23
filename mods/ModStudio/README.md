# ModStudio 模组工坊

游戏内模组创作工作台（M0）+ 本机 Agent HTTP API（M1）。文档见 [`docs/mods/ModStudio/`](../../docs/mods/ModStudio/)。

## 构建

```bat
dotnet test mods/ModStudio.Tests/ModStudio.Tests.csproj
dotnet build mods/ModStudio/ModStudio.csproj
```

成功后生成 `mods/ModStudio/bin/ModStudio.scmod`，并尽量复制到 `Survivalcraft.Windows/bin/Debug/Mods/`。

## 本地测试

1. 启动游戏，主菜单右下角点「模组工坊」
2. 新建 → 编译 → 打包 → 安装 → 确认重载
3. Agent：工坊底栏复制 Token，访问 `http://127.0.0.1:17865/health`

```bat
curl http://127.0.0.1:17865/health
curl -H "Authorization: Bearer <token>" http://127.0.0.1:17865/v1/mods
```

## 发布

只分发 `ModStudio.scmod`。适配 API `1.9.3.1`。
