FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /source
COPY src/ src/
RUN dotnet publish src/Portal.Web/Portal.Web.csproj -c Release -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl openssl ca-certificates && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY docker/entrypoint.sh /entrypoint.sh
USER $APP_UID
EXPOSE 8443
ENTRYPOINT ["/bin/bash", "/entrypoint.sh", "Portal.Web.dll"]
