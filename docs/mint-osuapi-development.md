# MintOsuAPI 本地联调

MintAPI 通过项目引用使用独立的 `mint-osuapi` 仓库。两个仓库需要位于同一父目录：

```text
RiderProjects/
├── MintAPI/
└── mint-osuapi/
```

打开 `MintAPI.sln` 即可同时编辑 API、独立库及其测试。修改独立库后，构建 API 会自动重新编译该库。

在 MintAPI 仓库根目录执行：

```sh
dotnet build MintAPI.sln
dotnet test MintAPI.sln
```

解决方案中的 MintOsuApi 和 MintOsuApi.Tests 均来自独立仓库。原仓库的库与测试副本已删除；保留的契约验证工具也引用独立库，后续库和库测试修改应在 `../mint-osuapi` 中进行。

Docker 构建需要通过命名构建上下文提供独立仓库。在 MintAPI 仓库根目录执行：

```sh
docker buildx build --build-context mint-osuapi=../mint-osuapi \
  -f MintAPI/Dockerfile -t mintapi --load .
```

CI 构建同样需要检出两个相邻仓库。正式切换 NuGet 引用后，可以移除这一目录结构要求。
