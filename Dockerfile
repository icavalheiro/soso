FROM node:24-alpine AS web
WORKDIR /web
COPY src/Soso.Web/package*.json ./
RUN npm ci
COPY src/Soso.Web/ ./
COPY logo.jpg ./public/logo.jpg
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Soso.Api/Soso.Api.csproj src/Soso.Api/
RUN dotnet restore src/Soso.Api/Soso.Api.csproj
COPY src/Soso.Api/ src/Soso.Api/
RUN dotnet publish src/Soso.Api/Soso.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false
COPY --from=web /web/dist /out/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app/data
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080 DataPath=/app/data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=20s --retries=3 CMD ["dotnet", "Soso.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Soso.Api.dll"]