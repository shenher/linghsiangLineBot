# ===== 階段一：Build =====
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先只複製 csproj 做 restore，可以讓 Docker layer cache 生效——
# 只要 csproj（相依套件）沒變，之後改程式碼重 build 就不用重新下載 NuGet 套件。
COPY src/LineBot.Api/LineBot.Api.csproj src/LineBot.Api/
RUN dotnet restore src/LineBot.Api/LineBot.Api.csproj

COPY src/LineBot.Api/ src/LineBot.Api/
RUN dotnet publish src/LineBot.Api/LineBot.Api.csproj -c Release -o /app/publish

# ===== 階段二：Runtime =====
# 用 aspnet runtime image（不含 SDK），減少最終 image 大小與攻擊面。
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# 憑證與資料目錄：實際內容一律用 docker-compose.yml 的 volume 掛載進來，
# 這裡只是先把目錄建好，避免 Kestrel／SQLite 因為目錄不存在而啟動失敗。
RUN mkdir -p /app/certs /app/data

ENV ASPNETCORE_ENVIRONMENT=Production

# 只開 443：本專案 Program.cs 在非 Development 環境固定用 Kestrel 監聽 443（見憑證設定那段），
# 沒有另外開 80，避免對外暴露一個沒有 TLS 的入口。
EXPOSE 443

ENTRYPOINT ["dotnet", "LineBot.Api.dll"]
