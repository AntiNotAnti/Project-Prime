# Conversion and upload queue in a separate unprivileged container.
# This image contains no Nintendo game assets and no production map credentials.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS converter-build
WORKDIR /src
COPY . .
RUN dotnet publish tools/pk3-converter/pk3-converter.csproj -c Release \
    -r linux-x64 -p:MphReadServer=true --self-contained true \
    -p:PublishSingleFile=true -p:PublishReadyToRun=false \
    -o /out/converter

FROM node:22-bookworm-slim
RUN groupadd --gid 10001 pk3 && useradd --uid 10001 --gid pk3 --system --home-dir /data pk3
COPY --from=converter-build /out/converter /opt/pk3-converter
COPY tools/pk3-upload/index.mjs tools/pk3-upload/service.mjs tools/pk3-upload/validation.mjs /opt/pk3-upload/
RUN chmod 755 /opt/pk3-converter/pk3-converter && mkdir -p /data && chown pk3:pk3 /data
USER 10001:10001
WORKDIR /opt/pk3-upload
ENV PK3_HOST=0.0.0.0 PK3_PORT=8094 PK3_STORAGE_ROOT=/data PK3_CONVERTER_EXEC=/opt/pk3-converter/pk3-converter
EXPOSE 8094
CMD ["node", "/opt/pk3-upload/index.mjs"]
