# Comment service (:5046).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# csproj first so a source-only change reuses the restore layer.
COPY explAInedCommentService.csproj ./
RUN dotnet restore explAInedCommentService.csproj

COPY . .
RUN dotnet publish explAInedCommentService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish ./

# Same port inside the container as on the host, so the upstream URLs other services carry
# (http://localhost:5046) keep meaning the same thing under a 1:1 compose mapping.
ENV ASPNETCORE_HTTP_PORTS=5046
EXPOSE 5046

# Migrations auto-apply at startup (db.Database.Migrate()), so Postgres must be reachable
# before this container starts — no separate migration step.
USER $APP_UID
ENTRYPOINT ["dotnet", "explAInedCommentService.dll"]
