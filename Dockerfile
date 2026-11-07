FROM node:24-alpine AS web
WORKDIR /src/apps/web
COPY apps/web/package*.json ./
RUN npm ci
COPY apps/web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/SukoonDesk.Api/SukoonDesk.Api.csproj src/SukoonDesk.Api/
RUN dotnet restore src/SukoonDesk.Api/SukoonDesk.Api.csproj
COPY src/SukoonDesk.Api/ src/SukoonDesk.Api/
RUN dotnet publish src/SukoonDesk.Api/SukoonDesk.Api.csproj -c Release -o /app/publish --no-restore
COPY --from=web /src/apps/web/dist /app/publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=api /app/publish ./
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "SukoonDesk.Api.dll"]
