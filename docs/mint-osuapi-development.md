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

客户端代码和测试请在 `../mint-osuapi` 修改；MintAPI 控制器和服务请在本仓库修改。

Docker 构建需要通过命名构建上下文提供独立仓库。在 MintAPI 仓库根目录执行：

```sh
docker buildx build --build-context mint-osuapi=../mint-osuapi \
  -f MintAPI/Dockerfile -t mintapi --load .
```

发布构建使用 `deploy/dependencies.json` 指定的客户端提交。准备发布时先将相邻客户端仓库检出到该提交，并确保其工作区干净。
