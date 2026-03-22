FROM node:22-bookworm-slim AS frontend-build
WORKDIR /src

RUN corepack enable

COPY pnpm-lock.yaml ./
COPY apps/frontend/package.json ./apps/frontend/package.json
RUN pnpm install --frozen-lockfile --dir apps/frontend

COPY apps/frontend ./apps/frontend
RUN pnpm --dir apps/frontend build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src

COPY apps/backend/backend.csproj ./apps/backend/backend.csproj
RUN dotnet restore ./apps/backend/backend.csproj

COPY apps/backend ./apps/backend
COPY --from=frontend-build /src/apps/frontend/dist ./apps/backend/wwwroot

RUN dotnet publish ./apps/backend/backend.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

COPY --from=backend-build /app/publish ./

ENTRYPOINT ["dotnet", "backend.dll"]
