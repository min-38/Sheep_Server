# 서버 빌드 결과만 담음. 클라는 itch.io에 따로 올려 서버는 /ws만 내보냄 (기술 설계서 배포)
# 빌드: docker build -t tqtsts-server .

# 1. 서버 빌드. lockfile과 다른 패키지를 받으려 하면 실패하게 함
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SheepServer -c Release -o /app -p:RestoreLockedMode=true

# 2. 실행. 런타임만 있는 이미지에 빌드 결과만 담음
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# 이미지에 있는 일반 사용자로 돌림. 포트는 이미지 기본값 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SheepServer.dll"]
