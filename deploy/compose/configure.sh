#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
umask 077
if [[ -f .env ]]; then
    echo '.env 已存在，保留原有凭据；修改配置请编辑该文件。'
else
    read -r -p 'Docker Hub 镜像（账号/mintapi）：' image
    read -r -p '发布版本（例如 0.1.0）：' version
    read -r -p 'osu! Client ID：' client_id
    read -r -s -p 'osu! Client Secret：' secret; echo
    [[ $image =~ ^[a-z0-9._/-]+$ && $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$ && $client_id =~ ^[1-9][0-9]*$ && -n $secret ]] || exit 1
    # dotenv single quotes prevent interpolation of dollar signs in supplied credentials.
    secret=${secret//\'/\\\'}
    api=$(openssl rand -hex 24)
    database=$(openssl rand -hex 24)
    root=$(openssl rand -hex 24)
    cache=$(openssl rand -hex 24)
    cat > .env <<CONFIG
MINTAPI_IMAGE=$image
MINTAPI_VERSION=$version
MINTAPI_BIND=127.0.0.1
MINTAPI_PORT=8080
MINTAPI_API_KEY=$api
OSU_CLIENT_ID=$client_id
OSU_CLIENT_SECRET='$secret'
MYSQL_PASSWORD=$database
MYSQL_ROOT_PASSWORD=$root
REDIS_PASSWORD=$cache
CONFIG
fi
docker compose pull
docker compose up -d --wait --wait-timeout 180
echo '部署完成：http://127.0.0.1:8080。API Key 保存在 .env；数据库和缓存未发布主机端口。'
