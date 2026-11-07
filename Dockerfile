# 서버와 클라 빌드 결과를 한 이미지에 담음. 서버 하나가 클라 파일과 /ws를 함께 내보냄 (기술 설계서 배포)
# 빌드: docker build -t sheep-server --build-arg CLIENT_REF=<Sheep_Client 커밋> .

# 1. 클라 빌드. 브랜치 이름이 아니라 커밋을 받음. 인자가 같으면 Docker가 이 단계를 캐시로 건너뛰어 새 커밋을 받지 않기 때문임
FROM node:26 AS client
ARG CLIENT_REF
WORKDIR /client
RUN git clone https://github.com/min-38/Sheep_Client.git . \
    && git checkout --detach "${CLIENT_REF:?CLIENT_REF에 Sheep_Client 커밋을 넘겨야 함}"
RUN npm ci && npm run build

# 2. 서버 빌드. lockfile과 다른 패키지를 받으려 하면 실패하게 함
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/SheepServer -c Release -o /app -p:RestoreLockedMode=true

# 3. 실행. 런타임만 있는 이미지에 두 빌드 결과만 담음. 서버는 지금 폴더의 wwwroot를 내보내므로 WORKDIR 아래에 둠
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=client /client/dist ./wwwroot
# 이미지에 있는 일반 사용자로 돌림. 포트는 이미지 기본값 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SheepServer.dll"]
