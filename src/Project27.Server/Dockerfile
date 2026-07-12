# Build from the repo root: docker build -f src/Project27.Server/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Project27.Core/ src/Project27.Core/
COPY src/Project27.Interop/ src/Project27.Interop/
COPY src/Project27.Storage/ src/Project27.Storage/
COPY src/Project27.Server/ src/Project27.Server/
RUN dotnet publish src/Project27.Server -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
VOLUME /data
ENV Storage__Provider=sqlite Storage__Path=/data/project27-server.db
USER $APP_UID
ENTRYPOINT ["dotnet", "Project27.Server.dll"]
