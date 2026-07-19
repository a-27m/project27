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
# stdio by default (docker run -i ... for a locally-attached MCP client); the Helm chart
# overrides P27_MCP_TRANSPORT=http to run this as a network-reachable Deployment instead
# (docs/spec/14-mcp-server.md "HTTP transport").
EXPOSE 5241
USER $APP_UID
ENTRYPOINT ["dotnet", "Project27.Mcp.dll"]
