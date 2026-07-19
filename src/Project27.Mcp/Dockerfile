# Build from the repo root: docker build -f src/Project27.Mcp/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Project27.Core/ src/Project27.Core/
COPY src/Project27.Storage/ src/Project27.Storage/
COPY src/Project27.Mcp/ src/Project27.Mcp/
RUN dotnet publish src/Project27.Mcp -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG VERSION=dev
WORKDIR /app
COPY --from=build /app .
ENV IMAGE_TAG=$VERSION
# p27-mcp speaks MCP over stdio (docs/spec/14-mcp-server.md) -- there is no
# HTTP port to expose. A container run from this image is only useful with
# stdin/stdout attached to an MCP client (docker run -i ...); running it
# under a Kubernetes Deployment leaves the process idle on stdio with
# nothing to talk to it.
USER $APP_UID
ENTRYPOINT ["dotnet", "Project27.Mcp.dll"]
